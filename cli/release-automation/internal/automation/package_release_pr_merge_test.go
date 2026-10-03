package automation

import (
	"bytes"
	"context"
	"encoding/base64"
	"errors"
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
	// stateJSONSequence, when set, answers successive re-reads in order and
	// repeats its last entry, so a test can walk a merge that settles late.
	stateJSONSequence []string
}

type mergePackageReleasePRStub struct {
	polls          []mergePackageReleasePRPoll
	pollIndex      int
	activePoll     mergePackageReleasePRPoll
	stateReadIndex int
	commandLog     []string
}

func (stub *mergePackageReleasePRStub) nextStateJSON() string {
	sequence := stub.activePoll.stateJSONSequence
	if len(sequence) == 0 {
		return stub.activePoll.stateJSON
	}
	index := stub.stateReadIndex
	if index >= len(sequence) {
		index = len(sequence) - 1
	}
	stub.stateReadIndex++
	return sequence[index]
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
		return stub.nextStateJSON(), true, nil
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

// Verifies a failed merge is a success rather than a failed job when the pull request is already merged: the command reports it and exits 0.
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
	assertReleasePRCheckLogContains(t, stdout, "PR #2002 is merged even though gh pr merge reported an error")
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

// Verifies a merge GitHub reports as failed while it is still settling is a success once the pull request reaches MERGED on a later re-read.
func TestMergePackageReleasePRWaitsForAMergeThatSettlesAfterReportingAnError(t *testing.T) {
	exitCode, stdout, stderr, stub := runMergePackageReleasePRCase(t, []mergePackageReleasePRPoll{
		{
			prListJSON:      packageReleasePRListJSON("package123", false),
			pinnedTagsByRef: packageReleasePRPinAt("package123", "dispatcher-v3.4.0"),
			runs:            packageReleasePRRunsAt("package123"),
			failMerge:       true,
			stateJSONSequence: []string{
				`{"state":"OPEN","isDraft":false,"headRefOid":"package123"}`,
				`{"state":"MERGED","isDraft":false,"headRefOid":"package123"}`,
			},
		},
	})

	if exitCode != 0 {
		t.Fatalf("expected exit code 0, got %d\nstderr: %s", exitCode, stderr)
	}
	assertReleasePRCheckLogContains(t, stdout, "PR #2002 is merged even though gh pr merge reported an error")
	assertMergePackageReleasePRMergeCount(t, stub, "package123", 1)
	assertMergePackageReleasePRStateReadCount(t, stub, 2)
}

// Verifies a merge that never settles fails with the original merge error after a bounded number of re-reads, instead of polling forever or reporting success.
func TestMergePackageReleasePRFailsWhenAFailedMergeNeverSettles(t *testing.T) {
	exitCode, stdout, stderr, stub := runMergePackageReleasePRCase(t, []mergePackageReleasePRPoll{
		{
			prListJSON:      packageReleasePRListJSON("package123", false),
			pinnedTagsByRef: packageReleasePRPinAt("package123", "dispatcher-v3.4.0"),
			runs:            packageReleasePRRunsAt("package123"),
			failMerge:       true,
			stateJSON:       `{"state":"OPEN","isDraft":false,"headRefOid":"package123"}`,
		},
	})

	if exitCode != 1 {
		t.Fatalf("expected exit code 1, got %d\nstdout: %s", exitCode, stdout)
	}
	assertReleasePRCheckLogContains(t, stderr, "gh pr merge failed")
	assertMergePackageReleasePRMergeCount(t, stub, "package123", 1)
	assertMergePackageReleasePRStateReadCount(t, stub, 1+mergePackageReleasePRSettleAttempts)
}

// Verifies a failed merge whose pull request moved to another head fails at once: waiting cannot turn a merge pinned to the old head into a release.
func TestMergePackageReleasePRDoesNotWaitWhenTheHeadMovedAfterAFailedMerge(t *testing.T) {
	exitCode, stdout, stderr, stub := runMergePackageReleasePRCase(t, []mergePackageReleasePRPoll{
		{
			prListJSON:      packageReleasePRListJSON("package123", false),
			pinnedTagsByRef: packageReleasePRPinAt("package123", "dispatcher-v3.4.0"),
			runs:            packageReleasePRRunsAt("package123"),
			failMerge:       true,
			stateJSON:       `{"state":"OPEN","isDraft":false,"headRefOid":"package456"}`,
		},
	})

	if exitCode != 1 {
		t.Fatalf("expected exit code 1, got %d\nstdout: %s", exitCode, stdout)
	}
	assertReleasePRCheckLogContains(t, stderr, "gh pr merge failed")
	assertMergePackageReleasePRStateReadCount(t, stub, 1)
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

func assertMergePackageReleasePRStateReadCount(t *testing.T, stub *mergePackageReleasePRStub, expected int) {
	t.Helper()
	count := 0
	for _, commandLine := range stub.commandLog {
		if strings.HasPrefix(commandLine, "gh pr view ") {
			count++
		}
	}
	if count != expected {
		t.Fatalf("expected %d pull request state reads, got %d: %v", expected, count, stub.commandLog)
	}
}

// mergePackageReleasePROverride replaces the stubbed answer for every command line it matches,
// so a test can fail one gh call while the rest of the pass answers normally.
type mergePackageReleasePROverride struct {
	matches func(commandLine string) bool
	output  string
	err     error
}

// runMergePackageReleasePRWithOverride runs one command against polls with the override applied and,
// when sleepErr is set, a sleep that fails instead of advancing the clock.
func runMergePackageReleasePRWithOverride(
	t *testing.T,
	polls []mergePackageReleasePRPoll,
	extraArgs []string,
	override mergePackageReleasePROverride,
	sleepErr error,
) (int, string, string, *mergePackageReleasePRStub) {
	t.Helper()
	stub := &mergePackageReleasePRStub{polls: polls}
	now := time.Date(2026, 9, 8, 1, 0, 0, 0, time.UTC)
	deps := mergePackageReleasePRDeps{
		now: func() time.Time { return now },
		sleep: func(ctx context.Context, duration time.Duration) error {
			if sleepErr != nil {
				return sleepErr
			}
			now = now.Add(duration)
			return ctx.Err()
		},
		runOutput: func(ctx context.Context, name string, args ...string) (string, error) {
			commandLine := strings.Join(append([]string{name}, args...), " ")
			if override.matches != nil && override.matches(commandLine) {
				stub.commandLog = append(stub.commandLog, commandLine)
				return override.output, override.err
			}
			return stub.runOutput(ctx, name, args...)
		},
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

// greenDraftPackageReleasePRPoll is a draft pull request whose head pins dispatcher-v3.4.0 and has passed every required workflow.
func greenDraftPackageReleasePRPoll() mergePackageReleasePRPoll {
	return mergePackageReleasePRPoll{
		prListJSON:      packageReleasePRListJSON("package123", true),
		pinnedTagsByRef: packageReleasePRPinAt("package123", "dispatcher-v3.4.0"),
		runs:            packageReleasePRRunsAt("package123"),
		manifestByRef:   map[string]string{"main": `{"cli/dispatcher":"3.4.0"}`},
	}
}

func mergePackageReleasePRCommandHasPrefix(prefix string) func(string) bool {
	return func(commandLine string) bool { return strings.HasPrefix(commandLine, prefix) }
}

func mergePackageReleasePRCommandContains(fragment string) func(string) bool {
	return func(commandLine string) bool { return strings.Contains(commandLine, fragment) }
}

func base64Of(value string) string {
	return base64.StdEncoding.EncodeToString([]byte(value))
}

// Verifies every failing or malformed gh read before the merge fails the command with that read's own error and never merges.
func TestMergePackageReleasePRFailsOnEachUnusableRead(t *testing.T) {
	pinPath := "contents/" + unityPackageCliPinFile
	cases := []struct {
		name     string
		args     []string
		override mergePackageReleasePROverride
		wantErr  string
	}{
		{"release PR list", nil, mergePackageReleasePROverride{matches: mergePackageReleasePRCommandContains("autorelease: pending"), err: errors.New("release PR list failed")}, "release PR list failed"},
		{"release PR list JSON", nil, mergePackageReleasePROverride{matches: mergePackageReleasePRCommandContains("autorelease: pending"), output: "{"}, "failed to parse release PR list"},
		{"release PR without head", nil, mergePackageReleasePROverride{matches: mergePackageReleasePRCommandContains("autorelease: pending"), output: `[{"number":2002,"headRefName":"` + packageReleasePRHeadBranch + `","headRefOid":""}]`}, "release PR #2002 has no head SHA"},
		{"dispatcher PR list", []string{"--require-no-open-dispatcher-pr"}, mergePackageReleasePROverride{matches: mergePackageReleasePRCommandContains("--components--dispatcher"), err: errors.New("dispatcher PR list failed")}, "dispatcher PR list failed"},
		{"dispatcher PR list JSON", []string{"--require-no-open-dispatcher-pr"}, mergePackageReleasePROverride{matches: mergePackageReleasePRCommandContains("--components--dispatcher"), output: "{"}, "failed to parse dispatcher release PR list"},
		{"pin read", nil, mergePackageReleasePROverride{matches: mergePackageReleasePRCommandContains(pinPath), err: errors.New("pin read failed")}, "pin read failed"},
		{"pin encoding", nil, mergePackageReleasePROverride{matches: mergePackageReleasePRCommandContains(pinPath), output: "not base64!"}, "failed to decode " + unityPackageCliPinFile + " at package123"},
		{"pin JSON", nil, mergePackageReleasePROverride{matches: mergePackageReleasePRCommandContains(pinPath), output: base64Of("{")}, "failed to parse " + unityPackageCliPinFile + " at package123"},
		{"pin without tag", nil, mergePackageReleasePROverride{matches: mergePackageReleasePRCommandContains(pinPath), output: base64Of(`{}`)}, unityPackageCliPinFile + " at package123 has no dispatcherReleaseTag"},
		{"workflow runs", nil, mergePackageReleasePROverride{matches: mergePackageReleasePRCommandHasPrefix("gh run list "), err: errors.New("workflow runs failed")}, "workflow runs failed"},
		{"workflow runs JSON", nil, mergePackageReleasePROverride{matches: mergePackageReleasePRCommandHasPrefix("gh run list "), output: "{"}, "failed to parse " + packageReleasePRWorkflows()[0] + " workflow runs"},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			args := append([]string{"--dispatcher-tag", "dispatcher-v3.4.0"}, testCase.args...)

			exitCode, _, stderr, stub := runMergePackageReleasePRWithOverride(
				t, []mergePackageReleasePRPoll{greenDraftPackageReleasePRPoll()}, args, testCase.override, nil)

			if exitCode != 1 {
				t.Fatalf("expected exit code 1, got %d", exitCode)
			}
			assertReleasePRCheckLogContains(t, stderr, testCase.wantErr)
			assertMergePackageReleasePRNeverMerged(t, stub)
		})
	}
}

// Verifies an unreadable, unparsable, or padded release manifest fails the tag resolution with its own error when no tag is given.
func TestMergePackageReleasePRFailsOnUnusableManifest(t *testing.T) {
	manifestPath := "contents/" + releasePleaseManifestRelativePath
	cases := []struct {
		name     string
		override mergePackageReleasePROverride
		wantErr  string
	}{
		{"manifest read", mergePackageReleasePROverride{matches: mergePackageReleasePRCommandContains(manifestPath), err: errors.New("manifest read failed")}, "manifest read failed"},
		{"manifest JSON", mergePackageReleasePROverride{matches: mergePackageReleasePRCommandContains(manifestPath), output: base64Of("{")}, "failed to parse " + releasePleaseManifestRelativePath + " at main"},
		{"padded version", mergePackageReleasePROverride{matches: mergePackageReleasePRCommandContains(manifestPath), output: base64Of(`{"cli/dispatcher":" 3.4.0"}`)}, `releases "cli/dispatcher" as " 3.4.0", which is not a bare version`},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			exitCode, _, stderr, stub := runMergePackageReleasePRWithOverride(
				t, []mergePackageReleasePRPoll{greenDraftPackageReleasePRPoll()}, nil, testCase.override, nil)

			if exitCode != 1 {
				t.Fatalf("expected exit code 1, got %d", exitCode)
			}
			assertReleasePRCheckLogContains(t, stderr, testCase.wantErr)
			assertMergePackageReleasePRNeverMerged(t, stub)
		})
	}
}

// Verifies a timeout without a named tag describes the manifest it waited on rather than an empty tag.
func TestMergePackageReleasePRTimeoutWithoutTagNamesTheManifest(t *testing.T) {
	stalePoll := greenDraftPackageReleasePRPoll()
	stalePoll.pinnedTagsByRef = packageReleasePRPinAt("package123", "dispatcher-v3.3.1")

	exitCode, _, stderr, stub := runMergePackageReleasePRWithOverride(t, []mergePackageReleasePRPoll{stalePoll}, nil, mergePackageReleasePROverride{}, nil)

	if exitCode != 1 {
		t.Fatalf("expected exit code 1, got %d", exitCode)
	}
	assertReleasePRCheckLogContains(t, stderr, "to record the dispatcher release the main manifest publishes timed out")
	assertMergePackageReleasePRNeverMerged(t, stub)
}

// Verifies an interrupted wait between passes fails with the sleep error instead of polling again.
func TestMergePackageReleasePRFailsWhenTheWaitIsInterrupted(t *testing.T) {
	stalePoll := greenDraftPackageReleasePRPoll()
	stalePoll.pinnedTagsByRef = packageReleasePRPinAt("package123", "dispatcher-v3.3.1")

	exitCode, _, stderr, stub := runMergePackageReleasePRWithOverride(
		t, []mergePackageReleasePRPoll{stalePoll}, []string{"--dispatcher-tag", "dispatcher-v3.4.0"}, mergePackageReleasePROverride{}, errors.New("wait interrupted"))

	if exitCode != 1 {
		t.Fatalf("expected exit code 1, got %d", exitCode)
	}
	assertReleasePRCheckLogContains(t, stderr, "wait interrupted")
	assertMergePackageReleasePRListCount(t, stub, 1)
}

// Verifies unknown flags, a missing repository, an empty base branch, non-positive timings, and an empty workflow list are each rejected by their own message.
func TestParseMergePackageReleasePRFlagsRejectsInvalidInput(t *testing.T) {
	cases := []struct {
		name      string
		args      []string
		workflows string
		wantErr   string
	}{
		{"unknown flag", []string{"--unknown"}, "", "flag provided but not defined"},
		{"missing repo", []string{"--base-branch", "main"}, "", "--repo is required"},
		{"empty base branch", []string{"--repo", "owner/repository", "--base-branch", ""}, "", "--base-branch must not be empty"},
		{"zero timeout", []string{"--repo", "owner/repository", "--timeout-minutes", "0"}, "", "--timeout-minutes and --interval-seconds must be positive"},
		{"zero interval", []string{"--repo", "owner/repository", "--interval-seconds", "0"}, "", "--timeout-minutes and --interval-seconds must be positive"},
		{"empty workflow list", []string{"--repo", "owner/repository"}, " , ", "RELEASE_PR_CHECK_WORKFLOWS must list at least one workflow"},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			t.Setenv("RELEASE_PR_CHECK_WORKFLOWS", testCase.workflows)

			_, err := parseMergePackageReleasePRFlags(testCase.args)

			if err == nil || !strings.Contains(err.Error(), testCase.wantErr) {
				t.Fatalf("expected error containing %q, got %v", testCase.wantErr, err)
			}
		})
	}
}

// Verifies the exported command reports a flag error before it runs any command.
func TestRunMergePackageReleasePRReportsUnknownFlag(t *testing.T) {
	stdout := bytes.Buffer{}
	stderr := bytes.Buffer{}

	exitCode := RunMergePackageReleasePR(context.Background(), &stdout, &stderr, []string{"--unknown"})

	if exitCode != 1 || !strings.Contains(stderr.String(), "flag provided but not defined") {
		t.Fatalf("exit code = %d, stderr = %q", exitCode, stderr.String())
	}
}
