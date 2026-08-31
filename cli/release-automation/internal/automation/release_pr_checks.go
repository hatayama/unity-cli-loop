package automation

import (
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	"io"
	"os"
	"os/exec"
	"strconv"
	"strings"
	"time"
)

const (
	// GITHUB_TOKEN-created release PRs never trigger pull_request workflows on
	// their own, so every workflow that produces a required status check must be
	// dispatched here: build-cli, test-unity-package, and test-windows-installers
	// come from build-and-test.yml, while Compile Check (Linux) comes from
	// unity-compile-check-and-test-runner.yml.
	defaultReleasePRCheckWorkflows             = "build-and-test.yml,unity-compile-check-and-test-runner.yml"
	defaultReleasePRCheckLookupAttempts        = 30
	defaultReleasePRCheckLookupIntervalSeconds = 10
	defaultReleasePRCheckWatchIntervalSeconds  = 10
)

var (
	releasePRCheckNow   = time.Now
	releasePRCheckSleep = func(ctx context.Context, duration time.Duration) error {
		timer := time.NewTimer(duration)
		defer timer.Stop()

		select {
		case <-ctx.Done():
			return ctx.Err()
		case <-timer.C:
			return nil
		}
	}
)

type releasePRCheckConfig struct {
	repository            string
	targetBranch          string
	workflows             []string
	lookupAttempts        int
	lookupIntervalSeconds int
	watchIntervalSeconds  int
}

type releasePRCheckDeps struct {
	now       func() time.Time
	sleep     func(context.Context, time.Duration) error
	runOutput func(context.Context, string, ...string) (string, error)
}

type releasePullRequest struct {
	Number      int    `json:"number"`
	HeadRefName string `json:"headRefName"`
	HeadRefOID  string `json:"headRefOid"`
	Title       string `json:"title"`
	URL         string `json:"url"`
}

type releasePullRequestBody struct {
	Body string `json:"body"`
}

func RunReleasePleasePRChecks(ctx context.Context, stdout io.Writer, stderr io.Writer) int {
	return runReleasePleasePRChecksWithDeps(ctx, stdout, stderr, defaultReleasePRCheckDeps())
}

func defaultReleasePRCheckDeps() releasePRCheckDeps {
	return releasePRCheckDeps{
		now:       releasePRCheckNow,
		sleep:     releasePRCheckSleep,
		runOutput: runReleasePRCheckCommandOutput,
	}
}

func runReleasePleasePRChecksWithDeps(ctx context.Context, stdout io.Writer, stderr io.Writer, deps releasePRCheckDeps) int {
	config, err := releasePRCheckConfigFromEnvironment()
	if err != nil {
		writeReleasePRCheckLine(stderr, err)
		return 1
	}

	releasePR, found, err := findReleasePRCheckPullRequestWithRetry(ctx, config, deps)
	if err != nil {
		writeReleasePRCheckLine(stderr, err)
		return 1
	}
	if !found {
		writeReleasePRCheckLine(stdout, "No pending release-please PR found for "+config.targetBranch+".")
		return 0
	}

	if code := prepareReleasePRForChecks(ctx, stdout, stderr, config, releasePR, deps); code != 0 {
		return code
	}
	checkedHeadSHA, code := dispatchAndWatchReleasePRCheckWorkflows(ctx, stdout, stderr, config, releasePR, deps)
	if code != 0 {
		return code
	}
	return finalizeReleasePRChecks(ctx, stdout, stderr, config, releasePR, checkedHeadSHA, deps)
}

// prepareReleasePRForChecks clarifies the release PR body and marks it draft
// before workflows run. Why a helper: find/dispatch/finalize are separate
// stages, and leaving draft setup inline kept the orchestrator over cyclop.
func prepareReleasePRForChecks(
	ctx context.Context,
	stdout io.Writer,
	stderr io.Writer,
	config releasePRCheckConfig,
	releasePR releasePullRequest,
	deps releasePRCheckDeps,
) int {
	bodyChanged, err := clarifyReleasePRCheckBody(ctx, config, releasePR, deps)
	if err != nil {
		writeReleasePRCheckLine(stderr, err)
		return 1
	}
	if bodyChanged {
		writeReleasePRCheckLine(stdout, fmt.Sprintf("Updated release PR #%d body to clarify release component labels.", releasePR.Number))
	}

	err = markReleasePRCheckDraft(ctx, config, releasePR, deps)
	if err != nil {
		writeReleasePRCheckLine(stderr, err)
		return 1
	}
	writeReleasePRCheckLine(stdout, fmt.Sprintf("Marked release PR #%d as draft while checks run.", releasePR.Number))
	return 0
}

// dispatchAndWatchReleasePRCheckWorkflows dispatches each required workflow
// and waits for the same head SHA. Why reject mixed heads: a release-please
// force-push between dispatches would otherwise let a mixed set of green runs
// mark the PR ready.
func dispatchAndWatchReleasePRCheckWorkflows(
	ctx context.Context,
	stdout io.Writer,
	stderr io.Writer,
	config releasePRCheckConfig,
	releasePR releasePullRequest,
	deps releasePRCheckDeps,
) (string, int) {
	dispatchedAt := deps.now().UTC().Truncate(time.Second)
	for _, workflow := range config.workflows {
		writeReleasePRCheckLine(stdout, fmt.Sprintf("Dispatching %s for release PR #%d: %s", workflow, releasePR.Number, releasePR.URL))
		err := dispatchReleasePRCheckWorkflow(ctx, config, workflow, releasePR, deps)
		if err != nil {
			writeReleasePRCheckLine(stderr, err)
			return "", 1
		}
	}

	checkedHeadSHA := ""
	checkedHeadWorkflow := ""
	for _, workflow := range config.workflows {
		run, err := findDispatchedReleasePRCheckRun(ctx, config, workflow, releasePR, dispatchedAt, deps)
		if err != nil {
			writeReleasePRCheckLine(stderr, err)
			return "", 1
		}

		writeReleasePRCheckLine(stdout, fmt.Sprintf("Watching %s run %d for release PR #%d.", workflow, run.DatabaseID, releasePR.Number))
		err = watchReleasePRCheckRun(ctx, config, run.DatabaseID, deps)
		if err != nil {
			writeReleasePRCheckLine(stderr, err)
			return "", 1
		}

		if checkedHeadSHA == "" {
			checkedHeadSHA = run.HeadSHA
			checkedHeadWorkflow = workflow
			continue
		}
		if run.HeadSHA != checkedHeadSHA {
			writeReleasePRCheckLine(stderr, fmt.Errorf(
				"release PR #%d checks ran on different heads: %s checked %s but %s checked %s",
				releasePR.Number, checkedHeadWorkflow, checkedHeadSHA, workflow, run.HeadSHA))
			return "", 1
		}
	}
	return checkedHeadSHA, 0
}

func finalizeReleasePRChecks(
	ctx context.Context,
	stdout io.Writer,
	stderr io.Writer,
	config releasePRCheckConfig,
	releasePR releasePullRequest,
	checkedHeadSHA string,
	deps releasePRCheckDeps,
) int {
	err := verifyReleasePRCheckHeadMatchesRun(ctx, config, releasePR, checkedHeadSHA, deps)
	if err != nil {
		writeReleasePRCheckLine(stderr, err)
		return 1
	}

	err = markReleasePRCheckReady(ctx, config, releasePR, deps)
	if err != nil {
		writeReleasePRCheckLine(stderr, err)
		return 1
	}
	writeReleasePRCheckLine(stdout, fmt.Sprintf("Marked release PR #%d as ready after checks passed.", releasePR.Number))
	return 0
}

func releasePRCheckConfigFromEnvironment() (releasePRCheckConfig, error) {
	repository := os.Getenv("GITHUB_REPOSITORY")
	if repository == "" {
		return releasePRCheckConfig{}, fmt.Errorf("GITHUB_REPOSITORY is required")
	}
	targetBranch := os.Getenv("TARGET_BRANCH")
	if targetBranch == "" {
		return releasePRCheckConfig{}, fmt.Errorf("TARGET_BRANCH is required")
	}

	workflows, err := releasePRCheckWorkflowsFromEnvironment()
	if err != nil {
		return releasePRCheckConfig{}, err
	}

	lookupAttempts, err := releasePRCheckPositiveIntFromEnvironment("RELEASE_PR_CHECK_LOOKUP_ATTEMPTS", defaultReleasePRCheckLookupAttempts)
	if err != nil {
		return releasePRCheckConfig{}, err
	}
	lookupIntervalSeconds, err := releasePRCheckPositiveIntFromEnvironment("RELEASE_PR_CHECK_LOOKUP_INTERVAL_SECONDS", defaultReleasePRCheckLookupIntervalSeconds)
	if err != nil {
		return releasePRCheckConfig{}, err
	}
	watchIntervalSeconds, err := releasePRCheckPositiveIntFromEnvironment("RELEASE_PR_CHECK_WATCH_INTERVAL_SECONDS", defaultReleasePRCheckWatchIntervalSeconds)
	if err != nil {
		return releasePRCheckConfig{}, err
	}

	return releasePRCheckConfig{
		repository:            repository,
		targetBranch:          targetBranch,
		workflows:             workflows,
		lookupAttempts:        lookupAttempts,
		lookupIntervalSeconds: lookupIntervalSeconds,
		watchIntervalSeconds:  watchIntervalSeconds,
	}, nil
}

func releasePRCheckWorkflowsFromEnvironment() ([]string, error) {
	value := os.Getenv("RELEASE_PR_CHECK_WORKFLOWS")
	if value == "" {
		value = defaultReleasePRCheckWorkflows
	}

	workflows := []string{}
	for _, workflow := range strings.Split(value, ",") {
		workflow = strings.TrimSpace(workflow)
		if workflow == "" {
			continue
		}
		workflows = append(workflows, workflow)
	}
	if len(workflows) == 0 {
		return nil, fmt.Errorf("RELEASE_PR_CHECK_WORKFLOWS must list at least one workflow")
	}
	return workflows, nil
}

func releasePRCheckPositiveIntFromEnvironment(name string, defaultValue int) (int, error) {
	value := os.Getenv(name)
	if value == "" {
		return defaultValue, nil
	}
	parsedValue, err := strconv.Atoi(value)
	if err != nil || parsedValue <= 0 {
		return 0, fmt.Errorf("%s must be a positive integer", name)
	}
	return parsedValue, nil
}

func findReleasePRCheckPullRequest(ctx context.Context, config releasePRCheckConfig, deps releasePRCheckDeps) (releasePullRequest, bool, error) {
	output, err := deps.runOutput(
		ctx,
		"gh",
		"pr",
		"list",
		"--repo",
		config.repository,
		"--state",
		"open",
		"--base",
		config.targetBranch,
		"--label",
		"autorelease: pending",
		"--json",
		"number,headRefName,headRefOid,title,url",
	)
	if err != nil {
		return releasePullRequest{}, false, err
	}

	releasePRs := []releasePullRequest{}
	err = json.Unmarshal([]byte(output), &releasePRs)
	if err != nil {
		return releasePullRequest{}, false, fmt.Errorf("failed to parse release PR list: %w", err)
	}

	matchingPRs := []releasePullRequest{}
	for _, releasePR := range releasePRs {
		if releasePRCheckMatches(releasePR, config.targetBranch) {
			matchingPRs = append(matchingPRs, releasePR)
		}
	}

	if len(matchingPRs) == 0 {
		return releasePullRequest{}, false, nil
	}
	if len(matchingPRs) > 1 {
		return releasePullRequest{}, false, fmt.Errorf("expected one pending release-please PR for %s, found %d", config.targetBranch, len(matchingPRs))
	}
	if matchingPRs[0].HeadRefOID == "" {
		return releasePullRequest{}, false, fmt.Errorf("release PR #%d has no head SHA", matchingPRs[0].Number)
	}
	return matchingPRs[0], true, nil
}

func findReleasePRCheckPullRequestWithRetry(ctx context.Context, config releasePRCheckConfig, deps releasePRCheckDeps) (releasePullRequest, bool, error) {
	for attempt := 0; attempt < config.lookupAttempts; attempt++ {
		releasePR, found, err := findReleasePRCheckPullRequest(ctx, config, deps)
		if err != nil || found {
			return releasePR, found, err
		}
		if attempt+1 < config.lookupAttempts {
			err = deps.sleep(ctx, time.Duration(config.lookupIntervalSeconds)*time.Second)
			if err != nil {
				return releasePullRequest{}, false, err
			}
		}
	}
	return releasePullRequest{}, false, nil
}

func releasePRCheckMatches(releasePR releasePullRequest, targetBranch string) bool {
	releasePRBranch := "release-please--branches--" + targetBranch
	if releasePR.HeadRefName != releasePRBranch && !strings.HasPrefix(releasePR.HeadRefName, releasePRBranch+"--components--") {
		return false
	}
	return releasePRCheckTitleMatches(releasePR.Title)
}

func releasePRCheckTitleMatches(title string) bool {
	if title == "chore: release" || strings.HasPrefix(title, "chore: release ") {
		return true
	}
	if !strings.HasPrefix(title, "chore(") {
		return false
	}
	closeIndex := strings.Index(title, "):")
	if closeIndex == -1 {
		return false
	}
	rest := strings.TrimSpace(title[closeIndex+2:])
	return rest == "release" || strings.HasPrefix(rest, "release ")
}

func markReleasePRCheckDraft(ctx context.Context, config releasePRCheckConfig, releasePR releasePullRequest, deps releasePRCheckDeps) error {
	_, err := deps.runOutput(ctx, "gh", "pr", "ready", strconv.Itoa(releasePR.Number), "--repo", config.repository, "--undo")
	return err
}

func markReleasePRCheckReady(ctx context.Context, config releasePRCheckConfig, releasePR releasePullRequest, deps releasePRCheckDeps) error {
	_, err := deps.runOutput(ctx, "gh", "pr", "ready", strconv.Itoa(releasePR.Number), "--repo", config.repository)
	return err
}

func clarifyReleasePRCheckBody(ctx context.Context, config releasePRCheckConfig, releasePR releasePullRequest, deps releasePRCheckDeps) (bool, error) {
	output, err := deps.runOutput(ctx, "gh", "pr", "view", strconv.Itoa(releasePR.Number), "--repo", config.repository, "--json", "body")
	if err != nil {
		return false, err
	}

	prBody := releasePullRequestBody{}
	err = json.Unmarshal([]byte(output), &prBody)
	if err != nil {
		return false, fmt.Errorf("failed to parse release PR body: %w", err)
	}

	clarifiedBody, changed := clarifyReleasePRCheckComponentLabels(prBody.Body)
	if !changed {
		return false, nil
	}

	bodyFile, cleanup, err := writeReleasePRCheckBodyFile(clarifiedBody)
	if err != nil {
		return false, err
	}
	defer cleanup()

	_, err = deps.runOutput(ctx, "gh", "pr", "edit", strconv.Itoa(releasePR.Number), "--repo", config.repository, "--body-file", bodyFile)
	if err != nil {
		return false, err
	}
	return true, nil
}

func writeReleasePRCheckBodyFile(body string) (string, func(), error) {
	bodyFile, err := os.CreateTemp("", "uloop-release-pr-body-*.md")
	if err != nil {
		return "", func() {}, fmt.Errorf("failed to create release PR body file: %w", err)
	}

	cleanup := func() { _ = os.Remove(bodyFile.Name()) }
	_, writeErr := bodyFile.WriteString(body)
	closeErr := bodyFile.Close()
	if writeErr != nil {
		cleanup()
		return "", func() {}, fmt.Errorf("failed to write release PR body file: %w", writeErr)
	}
	if closeErr != nil {
		cleanup()
		return "", func() {}, fmt.Errorf("failed to close release PR body file: %w", closeErr)
	}
	return bodyFile.Name(), cleanup, nil
}

func verifyReleasePRCheckHeadMatchesRun(
	ctx context.Context,
	config releasePRCheckConfig,
	releasePR releasePullRequest,
	checkedHeadSHA string,
	deps releasePRCheckDeps,
) error {
	currentReleasePR, found, err := findReleasePRCheckPullRequest(ctx, config, deps)
	if err != nil {
		return err
	}
	if !found {
		return fmt.Errorf("release PR #%d is no longer pending before marking ready", releasePR.Number)
	}
	if currentReleasePR.Number != releasePR.Number {
		return fmt.Errorf("pending release PR changed from #%d to #%d before marking ready", releasePR.Number, currentReleasePR.Number)
	}
	if currentReleasePR.HeadRefOID != checkedHeadSHA {
		return fmt.Errorf("release PR #%d head changed from %s to %s before marking ready", releasePR.Number, checkedHeadSHA, currentReleasePR.HeadRefOID)
	}
	return nil
}

func runReleasePRCheckCommandOutput(ctx context.Context, name string, args ...string) (string, error) {
	command := exec.CommandContext(ctx, name, args...)
	stdout := bytes.Buffer{}
	stderr := bytes.Buffer{}
	command.Stdout = &stdout
	command.Stderr = &stderr
	err := command.Run()
	if err != nil {
		return "", fmt.Errorf("%s %s failed: %w\n%s%s", name, strings.Join(args, " "), err, stderr.String(), stdout.String())
	}
	return stdout.String(), nil
}

func writeReleasePRCheckLine(writer io.Writer, values ...any) {
	// CI status output failures cannot be recovered after the command outcome is known.
	_, _ = fmt.Fprintln(writer, values...)
}
