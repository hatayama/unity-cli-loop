package automation

import (
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	"io"
	"os"
	"os/exec"
	"regexp"
	"strconv"
	"strings"
	"time"
)

const (
	defaultReleasePRCheckWorkflow              = "build-and-test.yml"
	defaultReleasePRCheckLookupAttempts        = 30
	defaultReleasePRCheckLookupIntervalSeconds = 10
	defaultReleasePRCheckWatchIntervalSeconds  = 10
)

var (
	releasePRCheckNow   = time.Now
	releasePRCheckSleep = time.Sleep

	releasePRCheckPlainUnityPackageSummary = regexp.MustCompile(`<details><summary>((?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)(?:-[A-Za-z0-9][A-Za-z0-9.-]*)?)</summary>`)
)

type releasePRCheckConfig struct {
	repository            string
	targetBranch          string
	workflow              string
	lookupAttempts        int
	lookupIntervalSeconds int
	watchIntervalSeconds  int
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

type releaseWorkflowRun struct {
	DatabaseID int64  `json:"databaseId"`
	HeadSHA    string `json:"headSha"`
	CreatedAt  string `json:"createdAt"`
}

func RunReleasePleasePRChecks(ctx context.Context, stdout io.Writer, stderr io.Writer) int {
	config, err := releasePRCheckConfigFromEnvironment()
	if err != nil {
		writeReleasePRCheckLine(stderr, err)
		return 1
	}

	releasePR, found, err := findReleasePRCheckPullRequestWithRetry(ctx, config)
	if err != nil {
		writeReleasePRCheckLine(stderr, err)
		return 1
	}
	if !found {
		writeReleasePRCheckLine(stdout, "No pending release-please PR found for "+config.targetBranch+".")
		return 0
	}

	bodyChanged, err := clarifyReleasePRCheckBody(ctx, config, releasePR)
	if err != nil {
		writeReleasePRCheckLine(stderr, err)
		return 1
	}
	if bodyChanged {
		writeReleasePRCheckLine(stdout, fmt.Sprintf("Updated release PR #%d body to label the Unity package summary.", releasePR.Number))
	}

	err = markReleasePRCheckDraft(ctx, config, releasePR)
	if err != nil {
		writeReleasePRCheckLine(stderr, err)
		return 1
	}
	writeReleasePRCheckLine(stdout, fmt.Sprintf("Marked release PR #%d as draft while checks run.", releasePR.Number))

	dispatchedAt := releasePRCheckNow().UTC().Truncate(time.Second)
	writeReleasePRCheckLine(stdout, fmt.Sprintf("Dispatching %s for release PR #%d: %s", config.workflow, releasePR.Number, releasePR.URL))
	err = dispatchReleasePRCheckWorkflow(ctx, config, releasePR)
	if err != nil {
		writeReleasePRCheckLine(stderr, err)
		return 1
	}

	run, err := findDispatchedReleasePRCheckRun(ctx, config, releasePR, dispatchedAt)
	if err != nil {
		writeReleasePRCheckLine(stderr, err)
		return 1
	}

	writeReleasePRCheckLine(stdout, fmt.Sprintf("Watching %s run %d for release PR #%d.", config.workflow, run.DatabaseID, releasePR.Number))
	err = watchReleasePRCheckRun(ctx, config, run.DatabaseID)
	if err != nil {
		writeReleasePRCheckLine(stderr, err)
		return 1
	}

	err = verifyReleasePRCheckHeadMatchesRun(ctx, config, releasePR, run.HeadSHA)
	if err != nil {
		writeReleasePRCheckLine(stderr, err)
		return 1
	}

	err = markReleasePRCheckReady(ctx, config, releasePR)
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

	workflow := os.Getenv("RELEASE_PR_CHECK_WORKFLOW")
	if workflow == "" {
		workflow = defaultReleasePRCheckWorkflow
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
		workflow:              workflow,
		lookupAttempts:        lookupAttempts,
		lookupIntervalSeconds: lookupIntervalSeconds,
		watchIntervalSeconds:  watchIntervalSeconds,
	}, nil
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

func findReleasePRCheckPullRequest(ctx context.Context, config releasePRCheckConfig) (releasePullRequest, bool, error) {
	output, err := runReleasePRCheckOutput(
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

func findReleasePRCheckPullRequestWithRetry(ctx context.Context, config releasePRCheckConfig) (releasePullRequest, bool, error) {
	for attempt := 0; attempt < config.lookupAttempts; attempt++ {
		releasePR, found, err := findReleasePRCheckPullRequest(ctx, config)
		if err != nil || found {
			return releasePR, found, err
		}
		if attempt+1 < config.lookupAttempts {
			releasePRCheckSleep(time.Duration(config.lookupIntervalSeconds) * time.Second)
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

func markReleasePRCheckDraft(ctx context.Context, config releasePRCheckConfig, releasePR releasePullRequest) error {
	_, err := runReleasePRCheckOutput(ctx, "gh", "pr", "ready", strconv.Itoa(releasePR.Number), "--repo", config.repository, "--undo")
	return err
}

func markReleasePRCheckReady(ctx context.Context, config releasePRCheckConfig, releasePR releasePullRequest) error {
	_, err := runReleasePRCheckOutput(ctx, "gh", "pr", "ready", strconv.Itoa(releasePR.Number), "--repo", config.repository)
	return err
}

func dispatchReleasePRCheckWorkflow(ctx context.Context, config releasePRCheckConfig, releasePR releasePullRequest) error {
	_, err := runReleasePRCheckOutput(ctx, "gh", "workflow", "run", config.workflow, "--repo", config.repository, "--ref", releasePR.HeadRefName)
	return err
}

func clarifyReleasePRCheckBody(ctx context.Context, config releasePRCheckConfig, releasePR releasePullRequest) (bool, error) {
	output, err := runReleasePRCheckOutput(ctx, "gh", "pr", "view", strconv.Itoa(releasePR.Number), "--repo", config.repository, "--json", "body")
	if err != nil {
		return false, err
	}

	prBody := releasePullRequestBody{}
	err = json.Unmarshal([]byte(output), &prBody)
	if err != nil {
		return false, fmt.Errorf("failed to parse release PR body: %w", err)
	}

	clarifiedBody, changed := clarifyReleasePRCheckUnityPackageSummary(prBody.Body)
	if !changed {
		return false, nil
	}

	bodyFile, cleanup, err := writeReleasePRCheckBodyFile(clarifiedBody)
	if err != nil {
		return false, err
	}
	defer cleanup()

	_, err = runReleasePRCheckOutput(ctx, "gh", "pr", "edit", strconv.Itoa(releasePR.Number), "--repo", config.repository, "--body-file", bodyFile)
	if err != nil {
		return false, err
	}
	return true, nil
}

func clarifyReleasePRCheckUnityPackageSummary(body string) (string, bool) {
	if strings.Contains(body, "<details><summary>unity-package: ") {
		return body, false
	}

	matches := releasePRCheckPlainUnityPackageSummary.FindStringSubmatchIndex(body)
	if matches == nil {
		return body, false
	}

	version := body[matches[2]:matches[3]]
	replacement := "<details><summary>unity-package: " + version + "</summary>"
	return body[:matches[0]] + replacement + body[matches[1]:], true
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

func findDispatchedReleasePRCheckRun(
	ctx context.Context,
	config releasePRCheckConfig,
	releasePR releasePullRequest,
	dispatchedAt time.Time,
) (releaseWorkflowRun, error) {
	for attempt := 0; attempt < config.lookupAttempts; attempt++ {
		run, found, err := latestReleasePRCheckRun(ctx, config, releasePR, dispatchedAt)
		if err != nil {
			return releaseWorkflowRun{}, err
		}
		if found {
			return run, nil
		}
		if attempt+1 < config.lookupAttempts {
			releasePRCheckSleep(time.Duration(config.lookupIntervalSeconds) * time.Second)
		}
	}
	return releaseWorkflowRun{}, fmt.Errorf("could not find dispatched %s workflow run for %s", config.workflow, releasePR.HeadRefOID)
}

func latestReleasePRCheckRun(
	ctx context.Context,
	config releasePRCheckConfig,
	releasePR releasePullRequest,
	dispatchedAt time.Time,
) (releaseWorkflowRun, bool, error) {
	output, err := runReleasePRCheckOutput(
		ctx,
		"gh",
		"run",
		"list",
		"--repo",
		config.repository,
		"--workflow",
		config.workflow,
		"--branch",
		releasePR.HeadRefName,
		"--event",
		"workflow_dispatch",
		"--json",
		"databaseId,status,conclusion,headSha,createdAt,url",
		"--limit",
		"20",
	)
	if err != nil {
		return releaseWorkflowRun{}, false, err
	}

	runs := []releaseWorkflowRun{}
	err = json.Unmarshal([]byte(output), &runs)
	if err != nil {
		return releaseWorkflowRun{}, false, fmt.Errorf("failed to parse workflow runs: %w", err)
	}

	var latestRun releaseWorkflowRun
	var latestCreatedAt time.Time
	found := false
	for _, run := range runs {
		createdAt, err := time.Parse(time.RFC3339, run.CreatedAt)
		if err != nil {
			return releaseWorkflowRun{}, false, fmt.Errorf("failed to parse workflow run createdAt %q: %w", run.CreatedAt, err)
		}
		if createdAt.Before(dispatchedAt) {
			continue
		}
		if !found || createdAt.After(latestCreatedAt) {
			latestRun = run
			latestCreatedAt = createdAt
			found = true
		}
	}
	if !found {
		return releaseWorkflowRun{}, false, nil
	}
	return latestRun, true, nil
}

func watchReleasePRCheckRun(ctx context.Context, config releasePRCheckConfig, runID int64) error {
	_, err := runReleasePRCheckOutput(
		ctx,
		"gh",
		"run",
		"watch",
		strconv.FormatInt(runID, 10),
		"--repo",
		config.repository,
		"--exit-status",
		"--compact",
		"--interval",
		strconv.Itoa(config.watchIntervalSeconds),
	)
	return err
}

func verifyReleasePRCheckHeadMatchesRun(
	ctx context.Context,
	config releasePRCheckConfig,
	releasePR releasePullRequest,
	checkedHeadSHA string,
) error {
	currentReleasePR, found, err := findReleasePRCheckPullRequest(ctx, config)
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

func runReleasePRCheckOutput(ctx context.Context, name string, args ...string) (string, error) {
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
