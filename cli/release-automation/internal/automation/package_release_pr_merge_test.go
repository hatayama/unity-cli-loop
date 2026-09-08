package automation

import (
	"bytes"
	"context"
	"encoding/base64"
	"fmt"
	"strings"
	"testing"
	"time"
)

const (
	packageReleasePRHeadBranch = "release-please--branches--main--components--unity-package"
	packageReleasePRNumber     = 2002
)

// mergePackageReleasePRPoll describes what the stubbed gh reports on one pass of
// the wait loop, so a test can walk the pull request through the states it
// really passes: stale pin, rebased head with no checks yet, draft, and green.
type mergePackageReleasePRPoll struct {
	prListJSON string
	// pinnedTagsByRef answers the contents API per ref, so a test can tell a
	// pin read at the pull request head from one read at any other ref.
	pinnedTagsByRef map[string]string
	runs            map[string]string
	// openDispatcherPRListJSON answers the listing that asks whether a
	// dispatcher release is still pending.
	openDispatcherPRListJSON string
	// manifestByRef answers the contents API for the release manifest, so a
	// test can pin the dispatcher version the base branch tip releases.
	manifestByRef map[string]string
	// failReady and failMerge make the two writes fail the way they do when
	// another run reached the pull request first.
	failReady bool
	failMerge bool
	// stateJSON answers the re-read that follows a failed write.
	stateJSON string
}

type mergePackageReleasePRStub struct {
	polls      []mergePackageReleasePRPoll
	pollIndex  int
	activePoll mergePackageReleasePRPoll
	commandLog []string
}

func (stub *mergePackageReleasePRStub) nextPoll() mergePackageReleasePRPoll {
	index := stub.pollIndex
	if index >= len(stub.polls) {
		index = len(stub.polls) - 1
	}
	stub.pollIndex++
	return stub.polls[index]
}

func (stub *mergePackageReleasePRStub) runOutput(ctx context.Context, name string, args ...string) (string, error) {
	commandLine := strings.Join(append([]string{name}, args...), " ")
	stub.commandLog = append(stub.commandLog, commandLine)

	output, answered, err := stub.answerPullRequestCommand(commandLine)
	if answered {
		return output, err
	}
	return stub.answerContentCommand(commandLine)
}

// answerPullRequestCommand covers the pull request reads and writes. answered
// is false for anything it does not own, so the caller falls through to the
// content and run reads.
func (stub *mergePackageReleasePRStub) answerPullRequestCommand(commandLine string) (string, bool, error) {
	switch {
	case strings.Contains(commandLine, "--components--dispatcher"):
		// The dispatcher listing is a gate question inside one pass, so unlike
		// the package listing it must not advance the stubbed state.
		if stub.activePoll.openDispatcherPRListJSON == "" {
			return "[]", true, nil
		}
		return stub.activePoll.openDispatcherPRListJSON, true, nil
	case strings.HasPrefix(commandLine, "gh pr list "):
		// The listing opens each pass, so it is what advances the stubbed state;
		// the rest of the pass must keep reading the same poll.
		stub.activePoll = stub.nextPoll()
		return stub.activePoll.prListJSON, true, nil
	case strings.HasPrefix(commandLine, "gh pr ready "):
		if stub.activePoll.failReady {
			return "", true, fmt.Errorf("gh pr ready failed")
		}
		return "", true, nil
	case strings.HasPrefix(commandLine, "gh pr view "):
		return stub.activePoll.stateJSON, true, nil
	case strings.Contains(commandLine, " --match-head-commit "):
		if stub.activePoll.failMerge {
			return "", true, fmt.Errorf("gh pr merge failed")
		}
		return "", true, nil
	}
	return "", false, nil
}

// answerContentCommand covers the contents API reads and the workflow run
// listing, and rejects anything the command is not expected to run.
func (stub *mergePackageReleasePRStub) answerContentCommand(commandLine string) (string, error) {
	switch {
	case strings.Contains(commandLine, releasePleaseManifestRelativePath):
		ref := mergePackageReleasePRRequestedRef(commandLine)
		manifest, known := stub.manifestAt(ref)
		if !known {
			return "", fmt.Errorf("no stubbed manifest for ref %q", ref)
		}
		return base64.StdEncoding.EncodeToString([]byte(manifest)) + "\n", nil
	case strings.HasPrefix(commandLine, "gh api "):
		ref := mergePackageReleasePRRequestedRef(commandLine)
		pinnedTag, known := stub.activePoll.pinnedTagsByRef[ref]
		if !known {
			return "", fmt.Errorf("no stubbed pin for ref %q", ref)
		}
		pin := fmt.Sprintf(`{"dispatcherReleaseTag":%q}`, pinnedTag)
		return base64.StdEncoding.EncodeToString([]byte(pin)) + "\n", nil
	case strings.HasPrefix(commandLine, "gh run list "):
		for workflow, runsJSON := range stub.activePoll.runs {
			if strings.Contains(commandLine, workflow) {
				return runsJSON, nil
			}
		}
		return "[]", nil
	}
	return "", fmt.Errorf("unexpected command: %s", commandLine)
}

// manifestAt answers the release manifest read of the pass that is open, so a
// test can change what the base branch releases between passes.
func (stub *mergePackageReleasePRStub) manifestAt(ref string) (string, bool) {
	manifest, known := stub.activePoll.manifestByRef[ref]
	return manifest, known
}

func mergePackageReleasePRRequestedRef(commandLine string) string {
	_, ref, found := strings.Cut(commandLine, "?ref=")
	if !found {
		return ""
	}
	return strings.Fields(ref)[0]
}

func runMergePackageReleasePRCase(t *testing.T, polls []mergePackageReleasePRPoll) (int, string, string, *mergePackageReleasePRStub) {
	t.Helper()
	return runMergePackageReleasePRCaseWithArgs(t, polls, []string{"--dispatcher-tag", "dispatcher-v3.4.0"})
}

func runMergePackageReleasePRCaseWithArgs(
	t *testing.T,
	polls []mergePackageReleasePRPoll,
	extraArgs []string,
) (int, string, string, *mergePackageReleasePRStub) {
	t.Helper()

	stub := &mergePackageReleasePRStub{polls: polls}
	now := time.Date(2026, 9, 8, 1, 0, 0, 0, time.UTC)
	deps := mergePackageReleasePRDeps{
		now: func() time.Time {
			return now
		},
		sleep: func(ctx context.Context, duration time.Duration) error {
			// Advancing the clock here is what lets the timeout case finish
			// without waiting in real time.
			now = now.Add(duration)
			return ctx.Err()
		},
		runOutput: stub.runOutput,
	}

	stdout := bytes.Buffer{}
	stderr := bytes.Buffer{}
	args := append([]string{
		"--repo", "owner/repository",
		"--base-branch", "main",
		"--timeout-minutes", "45",
		"--interval-seconds", "30",
	}, extraArgs...)
	exitCode := RunMergePackageReleasePRWithDeps(context.Background(), &stdout, &stderr, args, deps)
	return exitCode, stdout.String(), stderr.String(), stub
}

func packageReleasePRListJSON(headSHA string, isDraft bool) string {
	return fmt.Sprintf(
		`[{"number":%d,"headRefName":%q,"headRefOid":%q,"isDraft":%t}]`,
		packageReleasePRNumber, packageReleasePRHeadBranch, headSHA, isDraft)
}

func packageReleasePRWorkflows() []string {
	return strings.Split(defaultReleasePRCheckWorkflows, ",")
}

func packageReleasePRRun(headSHA string, status string, conclusion string) string {
	return fmt.Sprintf(`[{"databaseId":1,"status":%q,"conclusion":%q,"headSha":%q}]`, status, conclusion, headSHA)
}

// packageReleasePRRunsAt reports a completed successful run of every required
// workflow for one head SHA.
func packageReleasePRRunsAt(headSHA string) map[string]string {
	runs := map[string]string{}
	for _, workflow := range packageReleasePRWorkflows() {
		runs[workflow] = packageReleasePRRun(headSHA, "completed", "success")
	}
	return runs
}

func packageReleasePRPinAt(headSHA string, pinnedTag string) map[string]string {
	return map[string]string{headSHA: pinnedTag}
}

// Verifies the merge waits for the rebased pin and for the rebased head's own check runs, then lifts the draft and merges exactly once against that head — reading the pin, the runs, and the merge target from the same head SHA.
func TestMergePackageReleasePRReadiesDraftPullRequestOnceChecksPass(t *testing.T) {
	exitCode, stdout, stderr, stub := runMergePackageReleasePRCase(t, []mergePackageReleasePRPoll{
		{
			prListJSON:      packageReleasePRListJSON("stale123", true),
			pinnedTagsByRef: packageReleasePRPinAt("stale123", "dispatcher-v3.3.1"),
			runs:            packageReleasePRRunsAt("stale123"),
		},
		{
			prListJSON:      packageReleasePRListJSON("rebased456", true),
			pinnedTagsByRef: packageReleasePRPinAt("rebased456", "dispatcher-v3.4.0"),
			runs:            map[string]string{},
		},
		{
			prListJSON:      packageReleasePRListJSON("rebased456", true),
			pinnedTagsByRef: packageReleasePRPinAt("rebased456", "dispatcher-v3.4.0"),
			runs:            packageReleasePRRunsAt("rebased456"),
		},
	})

	if exitCode != 0 {
		t.Fatalf("expected exit code 0, got %d\nstderr: %s", exitCode, stderr)
	}
	assertReleasePRCheckLogContains(t, stdout, "still pins dispatcher-v3.3.1")
	assertReleasePRCheckLogContains(t, stdout, "has not finished for this head")
	assertReleasePRCheckLogContains(t, stdout, "Marked Unity package release PR #2002 ready before merging it.")
	assertReleasePRCheckLogContains(t, stdout, "Merged Unity package release PR #2002 at rebased456; it pins dispatcher-v3.4.0.")

	commandLogText := strings.Join(stub.commandLog, "\n")
	assertReleasePRCheckLogContains(t, commandLogText, "?ref=stale123")
	assertReleasePRCheckLogContains(t, commandLogText, "?ref=rebased456")
	assertMergePackageReleasePRReadyPrecedesMerge(t, stub)
	assertMergePackageReleasePRMergeCount(t, stub, "rebased456", 1)
}

// Verifies a pull request that is already out of draft is merged without a redundant gh pr ready, which would fail on it.
func TestMergePackageReleasePRMergesReadyPullRequestWithoutReadying(t *testing.T) {
	exitCode, stdout, stderr, stub := runMergePackageReleasePRCase(t, []mergePackageReleasePRPoll{
		{
			prListJSON:      packageReleasePRListJSON("package123", false),
			pinnedTagsByRef: packageReleasePRPinAt("package123", "dispatcher-v3.4.0"),
			runs:            packageReleasePRRunsAt("package123"),
		},
	})

	if exitCode != 0 {
		t.Fatalf("expected exit code 0, got %d\nstderr: %s", exitCode, stderr)
	}
	assertReleasePRCheckLogDoesNotContain(t, stdout, "ready before merging it")
	assertReleasePRCheckLogDoesNotContain(t, strings.Join(stub.commandLog, "\n"), "gh pr ready")
	assertMergePackageReleasePRMergeCount(t, stub, "package123", 1)
}

// Verifies the release-please path leaves the pull request draft while a dispatcher release is still pending, and settles at once rather than polling for a state only a later workflow can reach.
func TestMergePackageReleasePRLeavesDraftWhileDispatcherReleaseIsPending(t *testing.T) {
	exitCode, stdout, stderr, stub := runMergePackageReleasePRCaseWithArgs(t, []mergePackageReleasePRPoll{
		{
			prListJSON:               packageReleasePRListJSON("package123", true),
			pinnedTagsByRef:          packageReleasePRPinAt("package123", "dispatcher-v3.4.0"),
			runs:                     packageReleasePRRunsAt("package123"),
			openDispatcherPRListJSON: `[{"number":2001}]`,
		},
	}, []string{"--dispatcher-tag", "dispatcher-v3.4.0", "--require-no-open-dispatcher-pr"})

	if exitCode != 0 {
		t.Fatalf("expected exit code 0, got %d\nstderr: %s", exitCode, stderr)
	}
	assertReleasePRCheckLogContains(t, stdout, "PR #2002 stays draft: a dispatcher release pull request is open")
	commandLogText := strings.Join(stub.commandLog, "\n")
	assertReleasePRCheckLogDoesNotContain(t, commandLogText, "gh pr ready")
	assertMergePackageReleasePRNeverMerged(t, stub)
	assertMergePackageReleasePRListCount(t, stub, 2)
}

// Verifies the post-publish path still merges while the next cycle's dispatcher release pull request is open, because that release is not the one this package pin records.
func TestMergePackageReleasePRMergesWithOpenDispatcherPRWhenGateIsOff(t *testing.T) {
	exitCode, _, stderr, stub := runMergePackageReleasePRCase(t, []mergePackageReleasePRPoll{
		{
			prListJSON:               packageReleasePRListJSON("package123", true),
			pinnedTagsByRef:          packageReleasePRPinAt("package123", "dispatcher-v3.4.0"),
			runs:                     packageReleasePRRunsAt("package123"),
			openDispatcherPRListJSON: `[{"number":2001}]`,
		},
	})

	if exitCode != 0 {
		t.Fatalf("expected exit code 0, got %d\nstderr: %s", exitCode, stderr)
	}
	assertMergePackageReleasePRMergeCount(t, stub, "package123", 1)
}

// Verifies an omitted --dispatcher-tag is resolved from the base branch release manifest and compared against the head pin.
func TestMergePackageReleasePRResolvesDispatcherTagFromManifest(t *testing.T) {
	exitCode, stdout, stderr, stub := runMergePackageReleasePRCaseWithArgs(t, []mergePackageReleasePRPoll{
		{
			prListJSON:      packageReleasePRListJSON("package123", true),
			pinnedTagsByRef: packageReleasePRPinAt("package123", "dispatcher-v3.5.0"),
			runs:            packageReleasePRRunsAt("package123"),
			manifestByRef:   map[string]string{"main": `{"Packages/src":"3.6.0","cli/dispatcher":"3.5.0"}`},
		},
	}, nil)

	if exitCode != 0 {
		t.Fatalf("expected exit code 0, got %d\nstderr: %s", exitCode, stderr)
	}
	assertReleasePRCheckLogContains(t, stdout, "Resolved dispatcher release tag dispatcher-v3.5.0 from the main release manifest.")
	assertMergePackageReleasePRMergeCount(t, stub, "package123", 1)
}

// Verifies a base branch manifest without a dispatcher version stops the command instead of merging against a guessed tag.
func TestMergePackageReleasePRFailsWhenManifestHasNoDispatcherVersion(t *testing.T) {
	exitCode, _, stderr, stub := runMergePackageReleasePRCaseWithArgs(t, []mergePackageReleasePRPoll{
		{
			prListJSON:      packageReleasePRListJSON("package123", true),
			pinnedTagsByRef: packageReleasePRPinAt("package123", "dispatcher-v3.5.0"),
			runs:            packageReleasePRRunsAt("package123"),
			manifestByRef:   map[string]string{"main": `{"Packages/src":"3.6.0"}`},
		},
	}, nil)

	if exitCode != 1 {
		t.Fatalf("expected exit code 1, got %d", exitCode)
	}
	assertReleasePRCheckLogContains(t, stderr, `has no "cli/dispatcher" version`)
	assertMergePackageReleasePRNeverMerged(t, stub)
}

// Verifies --no-wait decides once on a stale pin and exits without polling, so the release-please run does not hold a runner open.
func TestMergePackageReleasePRWithNoWaitLeavesStalePinDraftAfterOnePass(t *testing.T) {
	exitCode, stdout, stderr, stub := runMergePackageReleasePRCaseWithArgs(t, []mergePackageReleasePRPoll{
		{
			prListJSON:      packageReleasePRListJSON("package123", true),
			pinnedTagsByRef: packageReleasePRPinAt("package123", "dispatcher-v3.3.1"),
			runs:            packageReleasePRRunsAt("package123"),
		},
	}, []string{"--dispatcher-tag", "dispatcher-v3.4.0", "--no-wait"})

	if exitCode != 0 {
		t.Fatalf("expected exit code 0, got %d\nstderr: %s", exitCode, stderr)
	}
	assertReleasePRCheckLogContains(t, stdout, "is not mergeable yet; leaving it draft.")
	assertMergePackageReleasePRNeverMerged(t, stub)
	assertMergePackageReleasePRListCount(t, stub, 1)
}

// Verifies losing the merge race to the other automated path is a success rather than a failed job: a merge that fails against an already merged pull request reports it and exits 0.
func TestMergePackageReleasePRAcceptsAPullRequestAnotherRunMerged(t *testing.T) {
	exitCode, stdout, stderr, stub := runMergePackageReleasePRCase(t, []mergePackageReleasePRPoll{
		{
			prListJSON:      packageReleasePRListJSON("package123", false),
			pinnedTagsByRef: packageReleasePRPinAt("package123", "dispatcher-v3.4.0"),
			runs:            packageReleasePRRunsAt("package123"),
			failMerge:       true,
			stateJSON:       `{"state":"MERGED","isDraft":false}`,
		},
	})

	if exitCode != 0 {
		t.Fatalf("expected exit code 0, got %d\nstderr: %s", exitCode, stderr)
	}
	assertReleasePRCheckLogContains(t, stdout, "PR #2002 was already merged by another run")
	assertMergePackageReleasePRMergeCount(t, stub, "package123", 1)
}

// Verifies a gh pr ready that the other path won is absorbed and the merge still runs, so the release is not left half-done.
func TestMergePackageReleasePRContinuesWhenAnotherRunLiftedTheDraft(t *testing.T) {
	exitCode, stdout, stderr, stub := runMergePackageReleasePRCase(t, []mergePackageReleasePRPoll{
		{
			prListJSON:      packageReleasePRListJSON("package123", true),
			pinnedTagsByRef: packageReleasePRPinAt("package123", "dispatcher-v3.4.0"),
			runs:            packageReleasePRRunsAt("package123"),
			failReady:       true,
			stateJSON:       `{"state":"OPEN","isDraft":false}`,
		},
	})

	if exitCode != 0 {
		t.Fatalf("expected exit code 0, got %d\nstderr: %s", exitCode, stderr)
	}
	assertReleasePRCheckLogContains(t, stdout, "PR #2002 is already out of draft; continuing to merge it.")
	assertMergePackageReleasePRMergeCount(t, stub, "package123", 1)
}

// Verifies a merge that fails against a pull request nothing else merged is a failure: reporting success there would leave the package release unmade with a green job.
func TestMergePackageReleasePRFailsWhenTheMergeFailsAndThePullRequestIsStillOpen(t *testing.T) {
	exitCode, stdout, stderr, _ := runMergePackageReleasePRCase(t, []mergePackageReleasePRPoll{
		{
			prListJSON:      packageReleasePRListJSON("package123", false),
			pinnedTagsByRef: packageReleasePRPinAt("package123", "dispatcher-v3.4.0"),
			runs:            packageReleasePRRunsAt("package123"),
			failMerge:       true,
			stateJSON:       `{"state":"OPEN","isDraft":false}`,
		},
	})

	if exitCode != 1 {
		t.Fatalf("expected exit code 1, got %d\nstdout: %s", exitCode, stdout)
	}
	assertReleasePRCheckLogContains(t, stderr, "gh pr merge failed")
	assertReleasePRCheckLogDoesNotContain(t, stdout, "already merged by another run")
}

// Verifies a failed write that no concurrent run explains still fails the command, so a real permission or ruleset error is not swallowed as a lost race.
func TestMergePackageReleasePRFailsWhenAFailedWriteIsNotARace(t *testing.T) {
	exitCode, _, stderr, stub := runMergePackageReleasePRCase(t, []mergePackageReleasePRPoll{
		{
			prListJSON:      packageReleasePRListJSON("package123", true),
			pinnedTagsByRef: packageReleasePRPinAt("package123", "dispatcher-v3.4.0"),
			runs:            packageReleasePRRunsAt("package123"),
			failReady:       true,
			stateJSON:       `{"state":"OPEN","isDraft":true}`,
		},
	})

	if exitCode != 1 {
		t.Fatalf("expected exit code 1, got %d", exitCode)
	}
	assertReleasePRCheckLogContains(t, stderr, "gh pr ready failed")
	assertMergePackageReleasePRNeverMerged(t, stub)
}

// Verifies the release manifest is never read before the open-dispatcher gate: reading it first would pair a tag from the pre-merge manifest with an already empty open list, which together read as "nothing pending" for a head that still pins the old dispatcher.
func TestMergePackageReleasePRReadsTheManifestOnlyAfterTheDispatcherGate(t *testing.T) {
	exitCode, stdout, stderr, stub := runMergePackageReleasePRCaseWithArgs(t, []mergePackageReleasePRPoll{
		{
			prListJSON:               packageReleasePRListJSON("package123", true),
			pinnedTagsByRef:          packageReleasePRPinAt("package123", "dispatcher-v3.4.0"),
			runs:                     packageReleasePRRunsAt("package123"),
			manifestByRef:            map[string]string{"main": `{"cli/dispatcher":"3.4.0"}`},
			openDispatcherPRListJSON: `[{"number":2001}]`,
		},
	}, []string{"--require-no-open-dispatcher-pr"})

	if exitCode != 0 {
		t.Fatalf("expected exit code 0, got %d\nstderr: %s", exitCode, stderr)
	}
	assertReleasePRCheckLogContains(t, stdout, "PR #2002 stays draft: a dispatcher release pull request is open")
	commandLogText := strings.Join(stub.commandLog, "\n")
	assertReleasePRCheckLogDoesNotContain(t, commandLogText, releasePleaseManifestRelativePath)
	assertReleasePRCheckLogDoesNotContain(t, stdout, "Resolved dispatcher release tag")
	assertMergePackageReleasePRNeverMerged(t, stub)
}

// Verifies a run that completed on an older head does not count as this head's check result.
func TestMergePackageReleasePRIgnoresRunsFromAnotherHead(t *testing.T) {
	exitCode, stdout, _, stub := runMergePackageReleasePRCase(t, []mergePackageReleasePRPoll{
		{
			prListJSON:      packageReleasePRListJSON("package123", false),
			pinnedTagsByRef: packageReleasePRPinAt("package123", "dispatcher-v3.4.0"),
			runs:            packageReleasePRRunsAt("older456"),
		},
	})

	if exitCode != 1 {
		t.Fatalf("expected the wait to time out with exit code 1, got %d", exitCode)
	}
	assertReleasePRCheckLogContains(t, stdout, "has not finished for this head")
	assertMergePackageReleasePRNeverMerged(t, stub)
}

// Verifies no open Unity package release pull request is a success with nothing merged.
func TestMergePackageReleasePRSkipsWhenNoPullRequestIsOpen(t *testing.T) {
	exitCode, stdout, stderr, stub := runMergePackageReleasePRCase(t, []mergePackageReleasePRPoll{
		{prListJSON: "[]"},
	})

	if exitCode != 0 {
		t.Fatalf("expected exit code 0, got %d\nstderr: %s", exitCode, stderr)
	}
	assertReleasePRCheckLogContains(t, stdout, "No open Unity package release pull request; nothing to merge.")
	assertMergePackageReleasePRNeverMerged(t, stub)
}

// Verifies a pull request that never gets the new pin times out instead of merging a stale head.
func TestMergePackageReleasePRTimesOutWhilePinStaysStale(t *testing.T) {
	exitCode, _, stderr, stub := runMergePackageReleasePRCase(t, []mergePackageReleasePRPoll{
		{
			prListJSON:      packageReleasePRListJSON("package123", false),
			pinnedTagsByRef: packageReleasePRPinAt("package123", "dispatcher-v3.3.1"),
			runs:            packageReleasePRRunsAt("package123"),
		},
	})

	if exitCode != 1 {
		t.Fatalf("expected exit code 1, got %d", exitCode)
	}
	assertReleasePRCheckLogContains(t, stderr, "timed out")
	assertMergePackageReleasePRNeverMerged(t, stub)
}

// Verifies a failed check on the verified head stops the automation for a human instead of being waited out.
func TestMergePackageReleasePRFailsWhenChecksFail(t *testing.T) {
	failedRuns := map[string]string{}
	for _, workflow := range packageReleasePRWorkflows() {
		failedRuns[workflow] = packageReleasePRRun("package123", "completed", "failure")
	}
	exitCode, _, stderr, stub := runMergePackageReleasePRCase(t, []mergePackageReleasePRPoll{
		{
			prListJSON:      packageReleasePRListJSON("package123", false),
			pinnedTagsByRef: packageReleasePRPinAt("package123", "dispatcher-v3.4.0"),
			runs:            failedRuns,
		},
	})

	if exitCode != 1 {
		t.Fatalf("expected exit code 1, got %d", exitCode)
	}
	assertReleasePRCheckLogContains(t, stderr, "concluded failure; not merging")
	assertMergePackageReleasePRNeverMerged(t, stub)
}

// Verifies one green workflow is not enough: a second required workflow that has not finished for this head keeps the merge waiting.
func TestMergePackageReleasePRWaitsWhenOneRequiredWorkflowHasNotFinished(t *testing.T) {
	workflows := packageReleasePRWorkflows()
	partialRuns := map[string]string{
		workflows[0]: packageReleasePRRun("package123", "completed", "success"),
		workflows[1]: packageReleasePRRun("package123", "in_progress", ""),
	}
	exitCode, stdout, stderr, stub := runMergePackageReleasePRCase(t, []mergePackageReleasePRPoll{
		{
			prListJSON:      packageReleasePRListJSON("package123", false),
			pinnedTagsByRef: packageReleasePRPinAt("package123", "dispatcher-v3.4.0"),
			runs:            partialRuns,
		},
	})

	if exitCode != 1 {
		t.Fatalf("expected the wait to time out with exit code 1, got %d", exitCode)
	}
	assertReleasePRCheckLogContains(t, stdout, workflows[1]+" has not finished for this head")
	assertReleasePRCheckLogContains(t, stderr, "timed out")
	assertMergePackageReleasePRNeverMerged(t, stub)
}

// Verifies one green workflow is not enough: a second required workflow that failed for this head stops the automation.
func TestMergePackageReleasePRFailsWhenOnlyTheSecondRequiredWorkflowFails(t *testing.T) {
	workflows := packageReleasePRWorkflows()
	mixedRuns := map[string]string{
		workflows[0]: packageReleasePRRun("package123", "completed", "success"),
		workflows[1]: packageReleasePRRun("package123", "completed", "failure"),
	}
	exitCode, _, stderr, stub := runMergePackageReleasePRCase(t, []mergePackageReleasePRPoll{
		{
			prListJSON:      packageReleasePRListJSON("package123", false),
			pinnedTagsByRef: packageReleasePRPinAt("package123", "dispatcher-v3.4.0"),
			runs:            mixedRuns,
		},
	})

	if exitCode != 1 {
		t.Fatalf("expected exit code 1, got %d", exitCode)
	}
	assertReleasePRCheckLogContains(t, stderr, workflows[1]+" concluded failure; not merging")
	assertMergePackageReleasePRNeverMerged(t, stub)
}

// Verifies more than one matching release pull request stops the automation rather than guessing which one to merge.
func TestMergePackageReleasePRFailsWhenSeveralPullRequestsMatch(t *testing.T) {
	prListJSON := fmt.Sprintf(
		`[{"number":2002,"headRefName":%q,"headRefOid":"package123","isDraft":false},`+
			`{"number":2003,"headRefName":%q,"headRefOid":"package456","isDraft":false}]`,
		packageReleasePRHeadBranch, packageReleasePRHeadBranch)

	exitCode, _, stderr, stub := runMergePackageReleasePRCase(t, []mergePackageReleasePRPoll{
		{prListJSON: prListJSON},
	})

	if exitCode != 1 {
		t.Fatalf("expected exit code 1, got %d", exitCode)
	}
	assertReleasePRCheckLogContains(t, stderr, "found 2")
	assertMergePackageReleasePRNeverMerged(t, stub)
}

// assertMergePackageReleasePRMergeCount counts merges of the package release
// pull request against one head. The pull request number is part of the match:
// a merge command naming another pull request must not read as this one.
func assertMergePackageReleasePRMergeCount(t *testing.T, stub *mergePackageReleasePRStub, headSHA string, expected int) {
	t.Helper()
	expectedMerge := fmt.Sprintf(
		"gh pr merge %d --repo owner/repository --squash --match-head-commit %s", packageReleasePRNumber, headSHA)
	mergeCount := 0
	for _, commandLine := range stub.commandLog {
		if commandLine == expectedMerge {
			mergeCount++
		}
	}
	if mergeCount != expected {
		t.Fatalf("expected %d merges matching %q, got %d\n%s",
			expected, expectedMerge, mergeCount, strings.Join(stub.commandLog, "\n"))
	}
}

func assertMergePackageReleasePRNeverMerged(t *testing.T, stub *mergePackageReleasePRStub) {
	t.Helper()
	assertReleasePRCheckLogDoesNotContain(t, strings.Join(stub.commandLog, "\n"), "--match-head-commit")
}

func assertMergePackageReleasePRListCount(t *testing.T, stub *mergePackageReleasePRStub, expected int) {
	t.Helper()
	listCount := 0
	for _, commandLine := range stub.commandLog {
		if strings.HasPrefix(commandLine, "gh pr list ") {
			listCount++
		}
	}
	if listCount != expected {
		t.Fatalf("expected %d pull request listings, got %d\n%s", expected, listCount, strings.Join(stub.commandLog, "\n"))
	}
}

func assertMergePackageReleasePRReadyPrecedesMerge(t *testing.T, stub *mergePackageReleasePRStub) {
	t.Helper()
	readyIndex := -1
	for index, commandLine := range stub.commandLog {
		if strings.HasPrefix(commandLine, "gh pr ready ") {
			readyIndex = index
		}
		if strings.Contains(commandLine, "--match-head-commit") {
			if readyIndex == -1 || readyIndex > index {
				t.Fatalf("expected gh pr ready before the merge\n%s", strings.Join(stub.commandLog, "\n"))
			}
			return
		}
	}
	t.Fatalf("expected a merge command\n%s", strings.Join(stub.commandLog, "\n"))
}
