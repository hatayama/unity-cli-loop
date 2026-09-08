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
	pinnedTag  string
	runs       map[string]string
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
		pin := fmt.Sprintf(`{"dispatcherReleaseTag":%q}`, stub.activePoll.pinnedTag)
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

func packageReleasePRListJSON(isDraft bool) string {
	return fmt.Sprintf(
		`[{"number":2002,"headRefName":%q,"headRefOid":"package123","isDraft":%t}]`,
		packageReleasePRHeadBranch, isDraft)
}

func packageReleasePRSuccessfulRuns() map[string]string {
	runs := map[string]string{}
	for _, workflow := range strings.Split(defaultReleasePRCheckWorkflows, ",") {
		runs[workflow] = `[{"databaseId":1,"status":"completed","conclusion":"success","headSha":"package123"}]`
	}
	return runs
}

// Verifies the merge waits for the rebased pin, for the draft window, and for this head's own check runs, then merges exactly once against the head it verified.
func TestMergePackageReleasePRWaitsForPinDraftAndChecks(t *testing.T) {
	greenRuns := packageReleasePRSuccessfulRuns()
	exitCode, stdout, stderr, stub := runMergePackageReleasePRCase(t, []mergePackageReleasePRPoll{
		{prListJSON: packageReleasePRListJSON(false), pinnedTag: "dispatcher-v3.3.1", runs: greenRuns},
		{prListJSON: packageReleasePRListJSON(false), pinnedTag: "dispatcher-v3.4.0", runs: map[string]string{}},
		{prListJSON: packageReleasePRListJSON(true), pinnedTag: "dispatcher-v3.4.0", runs: greenRuns},
		{prListJSON: packageReleasePRListJSON(false), pinnedTag: "dispatcher-v3.4.0", runs: greenRuns},
	})

	if exitCode != 0 {
		t.Fatalf("expected exit code 0, got %d\nstderr: %s", exitCode, stderr)
	}
	assertReleasePRCheckLogContains(t, stdout, "still pins dispatcher-v3.3.1")
	assertReleasePRCheckLogContains(t, stdout, "has not finished for this head")
	assertReleasePRCheckLogContains(t, stdout, "is draft while release checks run")
	assertReleasePRCheckLogContains(t, stdout, "Merged Unity package release PR #2002 at package123; it pins dispatcher-v3.4.0.")

	mergeCount := 0
	for _, commandLine := range stub.commandLog {
		if strings.Contains(commandLine, "--match-head-commit package123") {
			mergeCount++
		}
	}
	if mergeCount != 1 {
		t.Fatalf("expected exactly one merge against the verified head, got %d\n%s", mergeCount, strings.Join(stub.commandLog, "\n"))
	}
}

// Verifies a run that completed on an older head does not count as this head's check result.
func TestMergePackageReleasePRIgnoresRunsFromAnotherHead(t *testing.T) {
	staleRuns := map[string]string{}
	for _, workflow := range strings.Split(defaultReleasePRCheckWorkflows, ",") {
		staleRuns[workflow] = `[{"databaseId":1,"status":"completed","conclusion":"success","headSha":"older456"}]`
	}
	exitCode, stdout, _, stub := runMergePackageReleasePRCase(t, []mergePackageReleasePRPoll{
		{prListJSON: packageReleasePRListJSON(false), pinnedTag: "dispatcher-v3.4.0", runs: staleRuns},
	})

	if exitCode != 1 {
		t.Fatalf("expected the wait to time out with exit code 1, got %d", exitCode)
	}
	assertReleasePRCheckLogContains(t, stdout, "has not finished for this head")
	assertReleasePRCheckLogDoesNotContain(t, strings.Join(stub.commandLog, "\n"), "--match-head-commit")
}

// Verifies no open Unity package release pull request is a success with nothing merged.
func TestMergePackageReleasePRSkipsWhenNoPullRequestIsOpen(t *testing.T) {
	exitCode, stdout, stderr, stub := runMergePackageReleasePRCase(t, []mergePackageReleasePRPoll{
		{prListJSON: "[]", pinnedTag: "dispatcher-v3.4.0"},
	})

	if exitCode != 0 {
		t.Fatalf("expected exit code 0, got %d\nstderr: %s", exitCode, stderr)
	}
	assertReleasePRCheckLogContains(t, stdout, "No open Unity package release pull request; nothing to merge.")
	assertReleasePRCheckLogDoesNotContain(t, strings.Join(stub.commandLog, "\n"), "--match-head-commit")
}

// Verifies a pull request that never gets the new pin times out instead of merging a stale head.
func TestMergePackageReleasePRTimesOutWhilePinStaysStale(t *testing.T) {
	exitCode, _, stderr, stub := runMergePackageReleasePRCase(t, []mergePackageReleasePRPoll{
		{prListJSON: packageReleasePRListJSON(false), pinnedTag: "dispatcher-v3.3.1", runs: packageReleasePRSuccessfulRuns()},
	})

	if exitCode != 1 {
		t.Fatalf("expected exit code 1, got %d", exitCode)
	}
	assertReleasePRCheckLogContains(t, stderr, "timed out")
	assertReleasePRCheckLogDoesNotContain(t, strings.Join(stub.commandLog, "\n"), "--match-head-commit")
}

// Verifies a failed check on the verified head stops the automation for a human instead of being waited out.
func TestMergePackageReleasePRFailsWhenChecksFail(t *testing.T) {
	failedRuns := map[string]string{}
	for _, workflow := range strings.Split(defaultReleasePRCheckWorkflows, ",") {
		failedRuns[workflow] = `[{"databaseId":1,"status":"completed","conclusion":"failure","headSha":"package123"}]`
	}
	exitCode, _, stderr, stub := runMergePackageReleasePRCase(t, []mergePackageReleasePRPoll{
		{prListJSON: packageReleasePRListJSON(false), pinnedTag: "dispatcher-v3.4.0", runs: failedRuns},
	})

	if exitCode != 1 {
		t.Fatalf("expected exit code 1, got %d", exitCode)
	}
	assertReleasePRCheckLogContains(t, stderr, "concluded failure; not merging.")
	assertReleasePRCheckLogDoesNotContain(t, strings.Join(stub.commandLog, "\n"), "--match-head-commit")
}

// Verifies more than one matching release pull request stops the automation rather than guessing which one to merge.
func TestMergePackageReleasePRFailsWhenSeveralPullRequestsMatch(t *testing.T) {
	prListJSON := fmt.Sprintf(
		`[{"number":2002,"headRefName":%q,"headRefOid":"package123","isDraft":false},`+
			`{"number":2003,"headRefName":%q,"headRefOid":"package456","isDraft":false}]`,
		packageReleasePRHeadBranch, packageReleasePRHeadBranch)

	exitCode, _, stderr, stub := runMergePackageReleasePRCase(t, []mergePackageReleasePRPoll{
		{prListJSON: prListJSON, pinnedTag: "dispatcher-v3.4.0", runs: packageReleasePRSuccessfulRuns()},
	})

	if exitCode != 1 {
		t.Fatalf("expected exit code 1, got %d", exitCode)
	}
	assertReleasePRCheckLogContains(t, stderr, "found 2")
	assertReleasePRCheckLogDoesNotContain(t, strings.Join(stub.commandLog, "\n"), "--match-head-commit")
}
