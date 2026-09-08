package automation

import (
	"context"
	"encoding/json"
	"flag"
	"fmt"
	"io"
	"strconv"
	"time"
)

const (
	mergePackageReleasePRCommandName            = "merge-package-release-pr"
	mergePackageReleasePRComponent              = "unity-package"
	defaultMergePackageReleasePRTimeoutMinutes  = 45
	defaultMergePackageReleasePRIntervalSeconds = 30
)

type mergePackageReleasePRConfig struct {
	repository                string
	baseBranch                string
	dispatcherTag             string
	requireNoOpenDispatcherPR bool
	waitForMergeable          bool
	workflows                 []string
	timeout                   time.Duration
	interval                  time.Duration
}

type mergePackageReleasePRDeps struct {
	now       func() time.Time
	sleep     func(context.Context, time.Duration) error
	runOutput func(context.Context, string, ...string) (string, error)
}

type mergePackageReleasePullRequest struct {
	Number      int    `json:"number"`
	HeadRefName string `json:"headRefName"`
	HeadRefOID  string `json:"headRefOid"`
	IsDraft     bool   `json:"isDraft"`
}

type mergePackageReleasePRRun struct {
	DatabaseID int64  `json:"databaseId"`
	Status     string `json:"status"`
	Conclusion string `json:"conclusion"`
	HeadSHA    string `json:"headSha"`
}

// RunMergePackageReleasePR merges the open Unity package release pull request
// once its head records the dispatcher release that was just published. It is
// called straight after the pin stamp because that is the first moment a
// package release commit can point at the current dispatcher.
func RunMergePackageReleasePR(ctx context.Context, stdout io.Writer, stderr io.Writer, args []string) int {
	return RunMergePackageReleasePRWithDeps(ctx, stdout, stderr, args, defaultMergePackageReleasePRDeps())
}

func RunMergePackageReleasePRWithDeps(
	ctx context.Context,
	stdout io.Writer,
	stderr io.Writer,
	args []string,
	deps mergePackageReleasePRDeps,
) int {
	config, err := parseMergePackageReleasePRFlags(args)
	if err != nil {
		writeMergePackageReleasePRLine(stderr, err)
		return 1
	}
	config, err = resolveMergePackageReleasePRDispatcherTag(ctx, stdout, config, deps)
	if err != nil {
		writeMergePackageReleasePRLine(stderr, err)
		return 1
	}
	return waitAndMergePackageReleasePR(ctx, stdout, stderr, config, deps)
}

// resolveMergePackageReleasePRDispatcherTag fills in the dispatcher tag from
// the base branch tip when the caller names none. The release-please path has
// no published tag to pass, so the tag the base branch already releases is what
// the package pin has to record for the package to ship the current dispatcher.
func resolveMergePackageReleasePRDispatcherTag(
	ctx context.Context,
	stdout io.Writer,
	config mergePackageReleasePRConfig,
	deps mergePackageReleasePRDeps,
) (mergePackageReleasePRConfig, error) {
	if config.dispatcherTag != "" {
		return config, nil
	}
	dispatcherTag, err := dispatcherReleaseTagFromManifestAtRef(ctx, deps.runOutput, config.repository, config.baseBranch)
	if err != nil {
		return config, err
	}
	config.dispatcherTag = dispatcherTag
	writeMergePackageReleasePRLine(stdout, fmt.Sprintf(
		"Resolved dispatcher release tag %s from the %s release manifest.", dispatcherTag, config.baseBranch))
	return config, nil
}

func defaultMergePackageReleasePRDeps() mergePackageReleasePRDeps {
	return mergePackageReleasePRDeps{
		now:       releasePRCheckNow,
		sleep:     releasePRCheckSleep,
		runOutput: runReleasePRCheckCommandOutput,
	}
}

func parseMergePackageReleasePRFlags(args []string) (mergePackageReleasePRConfig, error) {
	flagSet := flag.NewFlagSet(mergePackageReleasePRCommandName, flag.ContinueOnError)
	repository := flagSet.String("repo", "", "GitHub repository that owns the release pull requests")
	baseBranch := flagSet.String("base-branch", "main", "base branch of the release pull requests")
	dispatcherTag := flagSet.String("dispatcher-tag", "", "dispatcher release tag the package pin must record (default: the tag the base branch manifest releases)")
	requireNoOpenDispatcherPR := flagSet.Bool("require-no-open-dispatcher-pr", false, "refuse to merge while a dispatcher release pull request is open")
	noWait := flagSet.Bool("no-wait", false, "decide once and leave the pull request draft instead of polling")
	timeoutMinutes := flagSet.Int("timeout-minutes", defaultMergePackageReleasePRTimeoutMinutes, "how long to wait for the pull request to become mergeable")
	intervalSeconds := flagSet.Int("interval-seconds", defaultMergePackageReleasePRIntervalSeconds, "how long to wait between polls")
	err := flagSet.Parse(args)
	if err != nil {
		return mergePackageReleasePRConfig{}, err
	}

	if *repository == "" {
		return mergePackageReleasePRConfig{}, fmt.Errorf("%s: --repo is required", mergePackageReleasePRCommandName)
	}
	if *baseBranch == "" {
		return mergePackageReleasePRConfig{}, fmt.Errorf("%s: --base-branch must not be empty", mergePackageReleasePRCommandName)
	}
	if *timeoutMinutes <= 0 || *intervalSeconds <= 0 {
		return mergePackageReleasePRConfig{}, fmt.Errorf("%s: --timeout-minutes and --interval-seconds must be positive", mergePackageReleasePRCommandName)
	}

	workflows, err := releasePRCheckWorkflowsFromEnvironment()
	if err != nil {
		return mergePackageReleasePRConfig{}, err
	}

	return mergePackageReleasePRConfig{
		repository:                *repository,
		baseBranch:                *baseBranch,
		dispatcherTag:             *dispatcherTag,
		requireNoOpenDispatcherPR: *requireNoOpenDispatcherPR,
		waitForMergeable:          !*noWait,
		workflows:                 workflows,
		timeout:                   time.Duration(*timeoutMinutes) * time.Minute,
		interval:                  time.Duration(*intervalSeconds) * time.Second,
	}, nil
}

func waitAndMergePackageReleasePR(
	ctx context.Context,
	stdout io.Writer,
	stderr io.Writer,
	config mergePackageReleasePRConfig,
	deps mergePackageReleasePRDeps,
) int {
	deadline := deps.now().Add(config.timeout)
	for {
		settled, exitCode := attemptMergePackageReleasePR(ctx, stdout, stderr, config, deps)
		if settled {
			return exitCode
		}
		// The release-please path runs on every push to the base branch, so a
		// pull request that is not mergeable now is reconsidered by the next
		// push rather than being waited out inside one workflow run.
		if !config.waitForMergeable {
			writeMergePackageReleasePRLine(stdout, "The Unity package release pull request is not mergeable yet; leaving it draft.")
			return 0
		}
		if !deps.now().Before(deadline) {
			writeMergePackageReleasePRLine(stderr, fmt.Errorf(
				"%s: waiting for the Unity package release pull request to record %s timed out; merge the pull request by hand once its checks pass",
				mergePackageReleasePRCommandName, config.dispatcherTag))
			return 1
		}
		err := deps.sleep(ctx, config.interval)
		if err != nil {
			writeMergePackageReleasePRLine(stderr, err)
			return 1
		}
	}
}

// attemptMergePackageReleasePR runs one pass of the wait loop. settled is false
// while the pull request is still expected to reach a mergeable state, so the
// caller keeps polling until the deadline.
func attemptMergePackageReleasePR(
	ctx context.Context,
	stdout io.Writer,
	stderr io.Writer,
	config mergePackageReleasePRConfig,
	deps mergePackageReleasePRDeps,
) (settled bool, exitCode int) {
	releasePR, found, err := findPackageReleasePullRequest(ctx, config, deps)
	if err != nil {
		writeMergePackageReleasePRLine(stderr, err)
		return true, 1
	}
	if !found {
		writeMergePackageReleasePRLine(stdout, "No open Unity package release pull request; nothing to merge.")
		return true, 0
	}

	if config.requireNoOpenDispatcherPR {
		dispatcherPRIsOpen, err := openDispatcherReleasePullRequestExists(ctx, deps.runOutput, config.repository, config.baseBranch)
		if err != nil {
			writeMergePackageReleasePRLine(stderr, err)
			return true, 1
		}
		// Waiting here cannot help: the dispatcher release has to be merged,
		// published and stamped, which takes far longer than this command runs.
		if dispatcherPRIsOpen {
			writeMergePackageReleasePRLine(stdout, fmt.Sprintf(
				"PR #%d stays draft: a dispatcher release pull request is open; the dispatcher must be released and stamped first.",
				releasePR.Number))
			return true, 0
		}
	}

	pinnedTag, err := packageReleasePullRequestPinnedTag(ctx, config, releasePR, deps)
	if err != nil {
		writeMergePackageReleasePRLine(stderr, err)
		return true, 1
	}
	if pinnedTag != config.dispatcherTag {
		writeMergePackageReleasePRLine(stdout, fmt.Sprintf(
			"PR #%d head %s still pins %s; waiting for release-please to rebase it onto the stamp.",
			releasePR.Number, releasePR.HeadRefOID, pinnedTag))
		return false, 0
	}
	// The draft flag is not consulted as evidence: it exists to keep people from
	// merging this pull request, not to prove its checks ran. What proves that
	// is a successful run of every required workflow for this exact head SHA,
	// held to that head by --match-head-commit at the merge.
	checksPassed, exitCode := packageReleasePullRequestChecksPassed(ctx, stdout, stderr, config, releasePR, deps)
	if exitCode != 0 {
		return true, exitCode
	}
	if !checksPassed {
		return false, 0
	}

	if releasePR.IsDraft {
		err = markPackageReleasePRReady(ctx, config, releasePR, deps)
		if err != nil {
			writeMergePackageReleasePRLine(stderr, err)
			return true, 1
		}
		writeMergePackageReleasePRLine(stdout, fmt.Sprintf(
			"Marked Unity package release PR #%d ready before merging it.", releasePR.Number))
	}

	err = squashMergePackageReleasePR(ctx, config, releasePR, deps)
	if err != nil {
		writeMergePackageReleasePRLine(stderr, err)
		return true, 1
	}
	writeMergePackageReleasePRLine(stdout, fmt.Sprintf(
		"Merged Unity package release PR #%d at %s; it pins %s.", releasePR.Number, releasePR.HeadRefOID, pinnedTag))
	return true, 0
}

func findPackageReleasePullRequest(
	ctx context.Context,
	config mergePackageReleasePRConfig,
	deps mergePackageReleasePRDeps,
) (mergePackageReleasePullRequest, bool, error) {
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
		config.baseBranch,
		"--label",
		"autorelease: pending",
		"--json",
		"number,headRefName,headRefOid,isDraft",
	)
	if err != nil {
		return mergePackageReleasePullRequest{}, false, err
	}

	releasePRs := []mergePackageReleasePullRequest{}
	err = json.Unmarshal([]byte(output), &releasePRs)
	if err != nil {
		return mergePackageReleasePullRequest{}, false, fmt.Errorf("%s: failed to parse release PR list: %w", mergePackageReleasePRCommandName, err)
	}

	headBranch := "release-please--branches--" + config.baseBranch + "--components--" + mergePackageReleasePRComponent
	matchingPRs := []mergePackageReleasePullRequest{}
	for _, releasePR := range releasePRs {
		if releasePR.HeadRefName == headBranch {
			matchingPRs = append(matchingPRs, releasePR)
		}
	}

	if len(matchingPRs) == 0 {
		return mergePackageReleasePullRequest{}, false, nil
	}
	if len(matchingPRs) > 1 {
		return mergePackageReleasePullRequest{}, false, fmt.Errorf(
			"%s: expected one open Unity package release pull request, found %d", mergePackageReleasePRCommandName, len(matchingPRs))
	}
	if matchingPRs[0].HeadRefOID == "" {
		return mergePackageReleasePullRequest{}, false, fmt.Errorf(
			"%s: release PR #%d has no head SHA", mergePackageReleasePRCommandName, matchingPRs[0].Number)
	}
	return matchingPRs[0], true, nil
}

// packageReleasePullRequestPinnedTag reads the pin at the pull request head
// rather than in the checkout: the merge decision is about the commit that will
// carry the package tag, not about the workspace this command runs in.
func packageReleasePullRequestPinnedTag(
	ctx context.Context,
	config mergePackageReleasePRConfig,
	releasePR mergePackageReleasePullRequest,
	deps mergePackageReleasePRDeps,
) (string, error) {
	decoded, err := githubFileContentAtRef(
		ctx, deps.runOutput, config.repository, unityPackageCliPinFile, releasePR.HeadRefOID)
	if err != nil {
		return "", err
	}

	pin := packagePinConsistencyPin{}
	err = json.Unmarshal(decoded, &pin)
	if err != nil {
		return "", fmt.Errorf("%s: failed to parse %s at %s: %w",
			mergePackageReleasePRCommandName, unityPackageCliPinFile, releasePR.HeadRefOID, err)
	}
	if pin.DispatcherReleaseTag == "" {
		return "", fmt.Errorf("%s: %s at %s has no dispatcherReleaseTag",
			mergePackageReleasePRCommandName, unityPackageCliPinFile, releasePR.HeadRefOID)
	}
	return pin.DispatcherReleaseTag, nil
}

// packageReleasePullRequestChecksPassed requires a completed successful run of
// every required workflow for this exact head SHA. Trusting "not draft" alone
// would merge during the window between release-please moving the head and the
// check automation drafting the pull request again, when no check has run for
// the new head at all.
func packageReleasePullRequestChecksPassed(
	ctx context.Context,
	stdout io.Writer,
	stderr io.Writer,
	config mergePackageReleasePRConfig,
	releasePR mergePackageReleasePullRequest,
	deps mergePackageReleasePRDeps,
) (passed bool, exitCode int) {
	for _, workflow := range config.workflows {
		run, found, err := packageReleasePullRequestRun(ctx, config, workflow, releasePR, deps)
		if err != nil {
			writeMergePackageReleasePRLine(stderr, err)
			return false, 1
		}
		if !found || run.Status != "completed" {
			writeMergePackageReleasePRLine(stdout, fmt.Sprintf(
				"PR #%d head %s: %s has not finished for this head; waiting.",
				releasePR.Number, releasePR.HeadRefOID, workflow))
			return false, 0
		}
		if run.Conclusion != "success" {
			writeMergePackageReleasePRLine(stderr, fmt.Errorf(
				"%s: PR #%d head %s: %s concluded %s; not merging",
				mergePackageReleasePRCommandName, releasePR.Number, releasePR.HeadRefOID, workflow, run.Conclusion))
			return false, 1
		}
	}
	return true, 0
}

func packageReleasePullRequestRun(
	ctx context.Context,
	config mergePackageReleasePRConfig,
	workflow string,
	releasePR mergePackageReleasePullRequest,
	deps mergePackageReleasePRDeps,
) (mergePackageReleasePRRun, bool, error) {
	output, err := deps.runOutput(
		ctx,
		"gh",
		"run",
		"list",
		"--repo",
		config.repository,
		"--workflow",
		workflow,
		"--branch",
		releasePR.HeadRefName,
		"--event",
		"workflow_dispatch",
		"--json",
		"databaseId,status,conclusion,headSha",
		"--limit",
		"20",
	)
	if err != nil {
		return mergePackageReleasePRRun{}, false, err
	}

	runs := []mergePackageReleasePRRun{}
	err = json.Unmarshal([]byte(output), &runs)
	if err != nil {
		return mergePackageReleasePRRun{}, false, fmt.Errorf(
			"%s: failed to parse %s workflow runs: %w", mergePackageReleasePRCommandName, workflow, err)
	}

	for _, run := range runs {
		if run.HeadSHA == releasePR.HeadRefOID {
			return run, true, nil
		}
	}
	return mergePackageReleasePRRun{}, false, nil
}

// markPackageReleasePRReady lifts the draft state the release PR check
// automation leaves in place. Only draft pull requests are readied, because
// gh pr ready fails on one that is already ready.
func markPackageReleasePRReady(
	ctx context.Context,
	config mergePackageReleasePRConfig,
	releasePR mergePackageReleasePullRequest,
	deps mergePackageReleasePRDeps,
) error {
	_, err := deps.runOutput(ctx, "gh", "pr", "ready", strconv.Itoa(releasePR.Number), "--repo", config.repository)
	return err
}

// squashMergePackageReleasePR pins the merge to the head this command
// verified, so a head moved between the check and the merge fails instead of
// releasing an unchecked commit.
func squashMergePackageReleasePR(
	ctx context.Context,
	config mergePackageReleasePRConfig,
	releasePR mergePackageReleasePullRequest,
	deps mergePackageReleasePRDeps,
) error {
	_, err := deps.runOutput(
		ctx,
		"gh",
		"pr",
		"merge",
		strconv.Itoa(releasePR.Number),
		"--repo",
		config.repository,
		"--squash",
		"--match-head-commit",
		releasePR.HeadRefOID,
	)
	return err
}

func writeMergePackageReleasePRLine(writer io.Writer, values ...any) {
	// CI status output failures cannot be recovered after the command outcome is known.
	_, _ = fmt.Fprintln(writer, values...)
}
