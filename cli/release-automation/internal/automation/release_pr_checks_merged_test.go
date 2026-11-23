package automation

import (
	"bytes"
	"context"
	"fmt"
	"strconv"
	"strings"
	"testing"
	"time"
)

const (
	releasePRMergeRaceDispatcherPR = `{"number":2001,"labels":[{"name":"autorelease: pending"}],"headRefName":"release-please--branches--main--components--dispatcher","headRefOid":"dispatcher123","title":"chore(main): release dispatcher 3.6.0","url":"https://example.test/pr/2001"}`
	releasePRMergeRaceRunnerPR     = `{"number":2003,"labels":[{"name":"autorelease: pending"}],"headRefName":"release-please--branches--main--components--uloop-project-runner","headRefOid":"dispatcher123","title":"chore(main): release uloop-project-runner 3.6.0","url":"https://example.test/pr/2003"}`

	releasePRMergeRaceMergedState = `{"state":"MERGED","mergedAt":"2026-11-16T15:01:00Z"}`
	releasePRMergeRaceClosedState = `{"state":"CLOSED","mergedAt":null}`
	releasePRMergeRaceOpenState   = `{"state":"OPEN","mergedAt":null}`

	releasePRMergeRaceUndoClosedError = "GraphQL: Pull request https://example.test/pr/%d is closed. Only draft pull requests can be marked as ready for review"
)

// releasePRMergeRaceStub answers gh like the per-component stubs above, but lets a test say
// which release PRs a person merged or closed while the job ran: the draft step can fail for a
// PR that is no longer open, later listings can drop a PR, and the state view says what
// happened to it.
type releasePRMergeRaceStub struct {
	firstPRList  string
	laterPRList  string
	undoFailures map[int]bool
	// readyFailures fails the final gh pr ready call, which has no --undo flag.
	readyFailures map[int]bool
	states        map[int]string
	failStates    map[int]bool
	// failOn fails every command line starting with it, standing in for a dispatch, run
	// lookup, or watch that breaks once the PR's head branch is deleted by the merge.
	failOn      string
	prListCalls int
	commandLog  []string
}

func (stub *releasePRMergeRaceStub) run(_ context.Context, name string, args ...string) (string, error) {
	commandLine := strings.Join(append([]string{name}, args...), " ")
	stub.commandLog = append(stub.commandLog, commandLine)
	fields := strings.Fields(commandLine)
	if stub.failOn != "" && strings.HasPrefix(commandLine, stub.failOn) {
		return "", fmt.Errorf("%s failed", stub.failOn)
	}

	switch {
	case strings.HasPrefix(commandLine, "gh pr list "):
		stub.prListCalls++
		if stub.prListCalls > 1 {
			return stub.laterPRList, nil
		}
		return stub.firstPRList, nil
	case strings.HasPrefix(commandLine, "gh pr ready ") && strings.HasSuffix(commandLine, "--undo"):
		number := releasePRMergeRaceNumber(fields)
		if stub.undoFailures[number] {
			return "", fmt.Errorf("gh pr ready %d --undo failed: exit status 1\n"+releasePRMergeRaceUndoClosedError, number, number)
		}
		return "", nil
	case strings.HasPrefix(commandLine, "gh pr ready ") && stub.readyFailures[releasePRMergeRaceNumber(fields)]:
		return "", fmt.Errorf("gh pr ready %d failed: exit status 1", releasePRMergeRaceNumber(fields))
	case strings.HasPrefix(commandLine, "gh pr view ") && strings.HasSuffix(commandLine, "--json state,mergedAt"):
		number := releasePRMergeRaceNumber(fields)
		if stub.failStates[number] {
			return "", fmt.Errorf("gh pr view %d failed", number)
		}
		return stub.states[number], nil
	case strings.HasPrefix(commandLine, "gh pr ready "),
		strings.HasPrefix(commandLine, "gh workflow run "),
		strings.HasPrefix(commandLine, "gh run watch "):
		return "", nil
	case strings.HasPrefix(commandLine, "gh run list "):
		runID := multiReleasePRCheckRunID(commandLine)
		return `[{"databaseId":` + runID + `,"headSha":"` + multiReleasePRCheckHeadSHA(commandLine) +
			`","createdAt":"2026-09-08T01:00:01Z","status":"queued","conclusion":"","url":"https://example.test/run/` + runID + `"}]`, nil
	}
	return "", fmt.Errorf("unexpected command: %s", commandLine)
}

func releasePRMergeRaceNumber(fields []string) int {
	number, _ := strconv.Atoi(fields[3])
	return number
}

func runReleasePRMergeRaceStub(t *testing.T, stub *releasePRMergeRaceStub) (int, string, string) {
	t.Helper()
	t.Setenv("GITHUB_REPOSITORY", "owner/repository")
	t.Setenv("TARGET_BRANCH", "main")
	t.Setenv("RELEASE_PR_CHECK_WORKFLOWS", "")
	t.Setenv("RELEASE_PR_CHECK_LOOKUP_ATTEMPTS", "1")
	t.Setenv("RELEASE_PR_CHECK_LOOKUP_INTERVAL_SECONDS", "")
	t.Setenv("RELEASE_PR_CHECK_WATCH_INTERVAL_SECONDS", "1")
	deps := releasePRCheckDeps{
		now:       func() time.Time { return time.Date(2026, 9, 8, 1, 0, 0, 0, time.UTC) },
		sleep:     func(ctx context.Context, _ time.Duration) error { return ctx.Err() },
		runOutput: stub.run,
	}
	stdout := bytes.Buffer{}
	stderr := bytes.Buffer{}
	exitCode := runReleasePleasePRChecksWithDeps(context.Background(), &stdout, &stderr, deps)
	return exitCode, stdout.String(), stderr.String()
}

// Verifies that a release PR merged before the job reaches it is skipped with a log line instead of failing at the draft step, and the job still finishes the next release PR and exits 0.
func TestReleasePRChecksSkipPullRequestMergedBeforeDraft(t *testing.T) {
	stub := releasePRMergeRaceStub{
		firstPRList:  `[` + releasePRMergeRaceDispatcherPR + `,` + releasePRMergeRaceRunnerPR + `]`,
		laterPRList:  `[` + releasePRMergeRaceRunnerPR + `]`,
		undoFailures: map[int]bool{2001: true},
		states:       map[int]string{2001: releasePRMergeRaceMergedState},
	}

	exitCode, stdout, stderr := runReleasePRMergeRaceStub(t, &stub)

	if exitCode != 0 {
		t.Fatalf("expected exit code 0, got %d\nstdout: %s\nstderr: %s", exitCode, stdout, stderr)
	}
	assertReleasePRCheckLogContains(t, stdout, "Skipping release PR #2001: it was merged")
	assertReleasePRCheckLogContains(t, stdout, "Marked release PR #2003 as ready after checks passed.")
	assertReleasePRCheckLogDoesNotContain(t, stderr, "Release PR checks failed")
	commandLogText := strings.Join(stub.commandLog, "\n")
	assertReleasePRCheckLogDoesNotContain(t, commandLogText, "--components--dispatcher")
}

// Verifies that a release PR merged while its checks are watched is skipped with a log line instead of failing as no longer pending, and is not marked ready.
func TestReleasePRChecksSkipPullRequestMergedDuringChecks(t *testing.T) {
	stub := releasePRMergeRaceStub{
		firstPRList: `[` + releasePRMergeRaceDispatcherPR + `]`,
		laterPRList: `[]`,
		states:      map[int]string{2001: releasePRMergeRaceMergedState},
	}

	exitCode, stdout, stderr := runReleasePRMergeRaceStub(t, &stub)

	if exitCode != 0 {
		t.Fatalf("expected exit code 0, got %d\nstdout: %s\nstderr: %s", exitCode, stdout, stderr)
	}
	assertReleasePRCheckLogContains(t, stdout, "Skipping release PR #2001: it was merged")
	assertReleasePRCheckLogDoesNotContain(t, stderr, "no longer pending")
	commandLogText := strings.Join(stub.commandLog, "\n")
	assertReleasePRCheckLogDoesNotContainLine(t, commandLogText, "gh pr ready 2001 --repo owner/repository")
}

// Verifies that a release PR that is closed without merging, still open, or unreadable stays an error at both the draft step and the pre-ready lookup, because none of those is a race a later run resolves.
func TestReleasePRChecksFailWhenPullRequestIsGoneButNotMerged(t *testing.T) {
	cases := []struct {
		name    string
		stub    releasePRMergeRaceStub
		wantErr string
	}{
		{"draft closed unmerged", releasePRMergeRaceStub{
			firstPRList: `[` + releasePRMergeRaceDispatcherPR + `]`, undoFailures: map[int]bool{2001: true},
			states: map[int]string{2001: releasePRMergeRaceClosedState},
		}, "--undo failed"},
		{"draft still open", releasePRMergeRaceStub{
			firstPRList: `[` + releasePRMergeRaceDispatcherPR + `]`, undoFailures: map[int]bool{2001: true},
			states: map[int]string{2001: releasePRMergeRaceOpenState},
		}, "--undo failed"},
		{"draft state unreadable", releasePRMergeRaceStub{
			firstPRList: `[` + releasePRMergeRaceDispatcherPR + `]`, undoFailures: map[int]bool{2001: true},
			failStates: map[int]bool{2001: true},
		}, "--undo failed"},
		{"ready closed unmerged", releasePRMergeRaceStub{
			firstPRList: `[` + releasePRMergeRaceDispatcherPR + `]`, laterPRList: `[]`,
			states: map[int]string{2001: releasePRMergeRaceClosedState},
		}, "release PR #2001 is no longer pending before marking ready"},
		{"ready still open", releasePRMergeRaceStub{
			firstPRList: `[` + releasePRMergeRaceDispatcherPR + `]`, laterPRList: `[]`,
			states: map[int]string{2001: releasePRMergeRaceOpenState},
		}, "release PR #2001 is no longer pending before marking ready"},
		{"ready state unreadable", releasePRMergeRaceStub{
			firstPRList: `[` + releasePRMergeRaceDispatcherPR + `]`, laterPRList: `[]`,
			failStates: map[int]bool{2001: true},
		}, "release PR #2001 is no longer pending before marking ready"},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			stub := testCase.stub

			exitCode, stdout, stderr := runReleasePRMergeRaceStub(t, &stub)

			if exitCode != 1 {
				t.Fatalf("expected exit code 1, got %d\nstdout: %s\nstderr: %s", exitCode, stdout, stderr)
			}
			assertReleasePRCheckLogContains(t, stderr, testCase.wantErr)
			assertReleasePRCheckLogContains(t, stderr, "Release PR checks failed for #2001")
			assertReleasePRCheckLogDoesNotContain(t, stdout, "Skipping release PR #2001")
		})
	}
}

// Verifies the run from the issue: two release PRs where the first is merged while its checks run and the second passes, so the job skips the first, marks the second ready, and exits 0.
func TestReleasePRChecksContinueAfterPullRequestMergedMidRun(t *testing.T) {
	stub := releasePRMergeRaceStub{
		firstPRList: `[` + releasePRMergeRaceDispatcherPR + `,` + releasePRMergeRaceRunnerPR + `]`,
		laterPRList: `[` + releasePRMergeRaceRunnerPR + `]`,
		states:      map[int]string{2001: releasePRMergeRaceMergedState},
	}

	exitCode, stdout, stderr := runReleasePRMergeRaceStub(t, &stub)

	if exitCode != 0 {
		t.Fatalf("expected exit code 0, got %d\nstdout: %s\nstderr: %s", exitCode, stdout, stderr)
	}
	assertReleasePRCheckLogContains(t, stdout, "Skipping release PR #2001: it was merged")
	assertReleasePRCheckLogContains(t, stdout, "Marked release PR #2003 as ready after checks passed.")
	assertReleasePRCheckLogDoesNotContain(t, stderr, "Release PR checks failed")
	commandLogText := strings.Join(stub.commandLog, "\n")
	assertReleasePRCheckLogDoesNotContainLine(t, commandLogText, "gh pr ready 2001 --repo owner/repository")
	assertReleasePRCheckLogContainsLine(t, commandLogText, "gh pr ready 2003 --repo owner/repository")
}

// Verifies that a release PR merged in the gap between the pre-ready lookup and gh pr ready is skipped with a log line, while the same ready failure on a PR that is still open stays an error.
func TestReleasePRChecksSkipPullRequestMergedBeforeReady(t *testing.T) {
	cases := []struct {
		name         string
		state        string
		wantExitCode int
	}{
		{"merged", releasePRMergeRaceMergedState, 0},
		{"still open", releasePRMergeRaceOpenState, 1},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			stub := releasePRMergeRaceStub{
				firstPRList:   `[` + releasePRMergeRaceDispatcherPR + `]`,
				laterPRList:   `[` + releasePRMergeRaceDispatcherPR + `]`,
				readyFailures: map[int]bool{2001: true},
				states:        map[int]string{2001: testCase.state},
			}

			exitCode, stdout, stderr := runReleasePRMergeRaceStub(t, &stub)

			if exitCode != testCase.wantExitCode {
				t.Fatalf("expected exit code %d, got %d\nstdout: %s\nstderr: %s", testCase.wantExitCode, exitCode, stdout, stderr)
			}
			if testCase.wantExitCode == 0 {
				assertReleasePRCheckLogContains(t, stdout, "Skipping release PR #2001: it was merged")
				assertReleasePRCheckLogDoesNotContain(t, stderr, "gh pr ready 2001 failed")
				return
			}
			assertReleasePRCheckLogContains(t, stderr, "gh pr ready 2001 failed")
			assertReleasePRCheckLogDoesNotContain(t, stdout, "Skipping release PR #2001")
		})
	}
}

// Verifies that a failed workflow dispatch, run lookup, or run watch is skipped when the release PR was merged meanwhile, and stays an error when the PR is still open.
func TestReleasePRChecksSkipDispatchFailureOnMergedPullRequest(t *testing.T) {
	steps := []string{"gh workflow run", "gh run list", "gh run watch"}
	states := []struct {
		name         string
		state        string
		wantExitCode int
	}{
		{"merged", releasePRMergeRaceMergedState, 0},
		{"still open", releasePRMergeRaceOpenState, 1},
	}
	for _, step := range steps {
		for _, testCase := range states {
			t.Run(step+" "+testCase.name, func(t *testing.T) {
				stub := releasePRMergeRaceStub{
					firstPRList: `[` + releasePRMergeRaceDispatcherPR + `]`,
					failOn:      step,
					states:      map[int]string{2001: testCase.state},
				}

				exitCode, stdout, stderr := runReleasePRMergeRaceStub(t, &stub)

				if exitCode != testCase.wantExitCode {
					t.Fatalf("expected exit code %d, got %d\nstdout: %s\nstderr: %s", testCase.wantExitCode, exitCode, stdout, stderr)
				}
				assertReleasePRCheckLogDoesNotContain(t, stdout, "as ready after checks passed")
				if testCase.wantExitCode == 0 {
					assertReleasePRCheckLogContains(t, stdout, "Skipping release PR #2001: it was merged")
					assertReleasePRCheckLogDoesNotContain(t, stderr, step+" failed")
					return
				}
				assertReleasePRCheckLogContains(t, stderr, step+" failed")
				assertReleasePRCheckLogDoesNotContain(t, stdout, "Skipping release PR #2001")
			})
		}
	}
}
