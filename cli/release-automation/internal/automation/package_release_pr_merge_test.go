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

const packageReleasePRHeadBranch = "release-please--branches--main--components--unity-package"

// mergePackageReleasePRPoll describes what the stubbed gh reports on one pass of
// the wait loop, so a test can walk the pull request through the states it
// really passes: stale pin, rebased head with no checks yet, draft, and green.
type mergePackageReleasePRPoll struct {
	prListJSON string
	// pinnedTagsByRef answers the contents API per ref, so a test can tell a
	// pin read at the pull request head from one read at any other ref.
	pinnedTagsByRef map[string]string
	runs            map[string]string
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

	switch {
	case strings.HasPrefix(commandLine, "gh pr list "):
		// The listing opens each pass, so it is what advances the stubbed state;
		// the rest of the pass must keep reading the same poll.
		stub.activePoll = stub.nextPoll()
		return stub.activePoll.prListJSON, nil
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
	case strings.Contains(commandLine, " --match-head-commit "):
		return "", nil
	}
	return "", fmt.Errorf("unexpected command: %s", commandLine)
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
	exitCode := RunMergePackageReleasePRWithDeps(context.Background(), &stdout, &stderr, []string{
		"--repo", "owner/repository",
		"--base-branch", "main",
		"--dispatcher-tag", "dispatcher-v3.4.0",
		"--timeout-minutes", "45",
		"--interval-seconds", "30",
	}, deps)
	return exitCode, stdout.String(), stderr.String(), stub
}

func packageReleasePRListJSON(headSHA string, isDraft bool) string {
	return fmt.Sprintf(
		`[{"number":2002,"headRefName":%q,"headRefOid":%q,"isDraft":%t}]`,
		packageReleasePRHeadBranch, headSHA, isDraft)
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

// Verifies the merge waits for the rebased pin, for the draft window, and for the rebased head's own check runs, then merges exactly once against that head — reading the pin, the runs, and the merge target from the same head SHA.
func TestMergePackageReleasePRWaitsForPinDraftAndChecks(t *testing.T) {
	exitCode, stdout, stderr, stub := runMergePackageReleasePRCase(t, []mergePackageReleasePRPoll{
		{
			prListJSON:      packageReleasePRListJSON("stale123", false),
			pinnedTagsByRef: packageReleasePRPinAt("stale123", "dispatcher-v3.3.1"),
			runs:            packageReleasePRRunsAt("stale123"),
		},
		{
			prListJSON:      packageReleasePRListJSON("rebased456", false),
			pinnedTagsByRef: packageReleasePRPinAt("rebased456", "dispatcher-v3.4.0"),
			runs:            map[string]string{},
		},
		{
			prListJSON:      packageReleasePRListJSON("rebased456", true),
			pinnedTagsByRef: packageReleasePRPinAt("rebased456", "dispatcher-v3.4.0"),
			runs:            packageReleasePRRunsAt("rebased456"),
		},
		{
			prListJSON:      packageReleasePRListJSON("rebased456", false),
			pinnedTagsByRef: packageReleasePRPinAt("rebased456", "dispatcher-v3.4.0"),
			runs:            packageReleasePRRunsAt("rebased456"),
		},
	})

	if exitCode != 0 {
		t.Fatalf("expected exit code 0, got %d\nstderr: %s", exitCode, stderr)
	}
	assertReleasePRCheckLogContains(t, stdout, "still pins dispatcher-v3.3.1")
	assertReleasePRCheckLogContains(t, stdout, "has not finished for this head")
	assertReleasePRCheckLogContains(t, stdout, "is draft while release checks run")
	assertReleasePRCheckLogContains(t, stdout, "Merged Unity package release PR #2002 at rebased456; it pins dispatcher-v3.4.0.")

	commandLogText := strings.Join(stub.commandLog, "\n")
	assertReleasePRCheckLogContains(t, commandLogText, "?ref=stale123")
	assertReleasePRCheckLogContains(t, commandLogText, "?ref=rebased456")
	assertMergePackageReleasePRMergeCount(t, stub, "rebased456", 1)
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

func assertMergePackageReleasePRMergeCount(t *testing.T, stub *mergePackageReleasePRStub, headSHA string, expected int) {
	t.Helper()
	mergeCount := 0
	for _, commandLine := range stub.commandLog {
		if strings.Contains(commandLine, "--match-head-commit "+headSHA) {
			mergeCount++
		}
	}
	if mergeCount != expected {
		t.Fatalf("expected %d merges against %s, got %d\n%s", expected, headSHA, mergeCount, strings.Join(stub.commandLog, "\n"))
	}
}

func assertMergePackageReleasePRNeverMerged(t *testing.T, stub *mergePackageReleasePRStub) {
	t.Helper()
	assertReleasePRCheckLogDoesNotContain(t, strings.Join(stub.commandLog, "\n"), "--match-head-commit")
}
