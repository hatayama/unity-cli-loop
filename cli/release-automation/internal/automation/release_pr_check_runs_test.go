package automation

import (
	"context"
	"errors"
	"strings"
	"testing"
	"time"
)

var releasePRCheckRunDispatchedAt = time.Date(2026, 9, 8, 1, 0, 0, 0, time.UTC)

// releasePRCheckRunLookup answers successive gh run list calls in order and records how long each sleep asked to wait.
type releasePRCheckRunLookup struct {
	answers  []string
	err      error
	sleepErr error
	calls    []string
	sleeps   []time.Duration
}

func (lookup *releasePRCheckRunLookup) deps() releasePRCheckDeps {
	return releasePRCheckDeps{
		sleep: func(_ context.Context, duration time.Duration) error {
			lookup.sleeps = append(lookup.sleeps, duration)
			return lookup.sleepErr
		},
		runOutput: func(_ context.Context, name string, args ...string) (string, error) {
			lookup.calls = append(lookup.calls, strings.Join(append([]string{name}, args...), " "))
			if lookup.err != nil {
				return "", lookup.err
			}
			index := len(lookup.calls) - 1
			if index >= len(lookup.answers) {
				index = len(lookup.answers) - 1
			}
			return lookup.answers[index], nil
		},
	}
}

func findReleasePRCheckRunForTest(lookup *releasePRCheckRunLookup, attempts int) (releaseWorkflowRun, error) {
	config := releasePRCheckConfig{repository: "owner/repository", lookupAttempts: attempts, lookupIntervalSeconds: 7}
	releasePR := releasePullRequest{HeadRefName: "release-branch", HeadRefOID: "head123"}
	return findDispatchedReleasePRCheckRun(context.Background(), config, "ci.yml", releasePR, releasePRCheckRunDispatchedAt, lookup.deps())
}

// Verifies the newest run created at or after the dispatch is chosen even when gh lists it before an older one, and runs created before the dispatch are ignored.
func TestFindDispatchedReleasePRCheckRunPicksTheNewestRunSinceDispatch(t *testing.T) {
	lookup := &releasePRCheckRunLookup{answers: []string{`[
		{"databaseId":1,"headSha":"head123","createdAt":"2026-09-08T00:59:59Z"},
		{"databaseId":3,"headSha":"head123","createdAt":"2026-09-08T01:00:09Z"},
		{"databaseId":2,"headSha":"head123","createdAt":"2026-09-08T01:00:05Z"}
	]`}}

	run, err := findReleasePRCheckRunForTest(lookup, 1)
	if err != nil {
		t.Fatalf("findDispatchedReleasePRCheckRun failed: %v", err)
	}
	if run.DatabaseID != 3 {
		t.Fatalf("expected run 3, got %+v", run)
	}
	wantCall := "gh run list --repo owner/repository --workflow ci.yml --branch release-branch --event workflow_dispatch"
	if !strings.HasPrefix(lookup.calls[0], wantCall) {
		t.Fatalf("call = %q, want prefix %q", lookup.calls[0], wantCall)
	}
}

// Verifies a lookup that only sees runs from before the dispatch waits the lookup interval, retries, and returns the run that appears.
func TestFindDispatchedReleasePRCheckRunRetriesUntilTheRunAppears(t *testing.T) {
	lookup := &releasePRCheckRunLookup{answers: []string{
		`[{"databaseId":1,"headSha":"head123","createdAt":"2026-09-08T00:59:59Z"}]`,
		`[{"databaseId":4,"headSha":"head123","createdAt":"2026-09-08T01:00:01Z"}]`,
	}}

	run, err := findReleasePRCheckRunForTest(lookup, 2)
	if err != nil {
		t.Fatalf("findDispatchedReleasePRCheckRun failed: %v", err)
	}
	if run.DatabaseID != 4 || len(lookup.calls) != 2 {
		t.Fatalf("run = %+v after %d calls", run, len(lookup.calls))
	}
	if len(lookup.sleeps) != 1 || lookup.sleeps[0] != 7*time.Second {
		t.Fatalf("sleeps = %v, want one 7s wait", lookup.sleeps)
	}
}

// Verifies a failing gh call, an unparsable listing, an invalid createdAt, an interrupted wait, and running out of attempts each fail with their own error.
func TestFindDispatchedReleasePRCheckRunReportsFailures(t *testing.T) {
	onlyOlderRun := `[{"databaseId":1,"headSha":"head123","createdAt":"2026-09-08T00:59:59Z"}]`
	cases := []struct {
		name     string
		lookup   releasePRCheckRunLookup
		attempts int
		wantErr  string
	}{
		{"gh failure", releasePRCheckRunLookup{err: errors.New("gh run list failed")}, 1, "gh run list failed"},
		{"listing JSON", releasePRCheckRunLookup{answers: []string{"{"}}, 1, "failed to parse workflow runs"},
		{"createdAt", releasePRCheckRunLookup{answers: []string{`[{"databaseId":1,"createdAt":"yesterday"}]`}}, 1, `failed to parse workflow run createdAt "yesterday"`},
		{"interrupted wait", releasePRCheckRunLookup{answers: []string{onlyOlderRun}, sleepErr: errors.New("lookup wait interrupted")}, 2, "lookup wait interrupted"},
		{"attempts exhausted", releasePRCheckRunLookup{answers: []string{onlyOlderRun}}, 1, "could not find dispatched ci.yml workflow run for head123"},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			lookup := testCase.lookup

			_, err := findReleasePRCheckRunForTest(&lookup, testCase.attempts)

			if err == nil || !strings.Contains(err.Error(), testCase.wantErr) {
				t.Fatalf("expected error containing %q, got %v", testCase.wantErr, err)
			}
		})
	}
}
