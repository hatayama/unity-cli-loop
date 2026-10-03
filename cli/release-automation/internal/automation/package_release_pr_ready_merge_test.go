package automation

import (
	"errors"
	"testing"
)

// Verifies how a failed draft lift is resolved: an unreadable or unparsable re-read fails with both errors, and a pull request another run already merged succeeds without merging again.
func TestMergePackageReleasePRResolvesAFailedReady(t *testing.T) {
	cases := []struct {
		name       string
		stateJSON  string
		override   mergePackageReleasePROverride
		wantExit   int
		wantStdout string
		wantStderr []string
	}{
		{
			name:       "state read",
			override:   mergePackageReleasePROverride{matches: mergePackageReleasePRCommandHasPrefix("gh pr view "), err: errors.New("state read failed")},
			wantExit:   1,
			wantStderr: []string{"gh pr ready failed", "state read failed"},
		},
		{
			name:       "state JSON",
			stateJSON:  "{",
			wantExit:   1,
			wantStderr: []string{"gh pr ready failed", "failed to parse the state of PR #2002"},
		},
		{
			name:       "merged by another run",
			stateJSON:  `{"state":"MERGED","isDraft":false,"headRefOid":"package123"}`,
			wantExit:   0,
			wantStdout: "Unity package release PR #2002 was already merged by another run; nothing left to do.",
		},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			poll := greenDraftPackageReleasePRPoll()
			poll.failReady = true
			poll.stateJSON = testCase.stateJSON

			exitCode, stdout, stderr, stub := runMergePackageReleasePRWithOverride(
				t, []mergePackageReleasePRPoll{poll}, []string{"--dispatcher-tag", "dispatcher-v3.4.0"}, testCase.override, nil)

			if exitCode != testCase.wantExit {
				t.Fatalf("expected exit code %d, got %d\nstdout: %s\nstderr: %s", testCase.wantExit, exitCode, stdout, stderr)
			}
			if testCase.wantStdout != "" {
				assertReleasePRCheckLogContains(t, stdout, testCase.wantStdout)
			}
			for _, want := range testCase.wantStderr {
				assertReleasePRCheckLogContains(t, stderr, want)
			}
			assertMergePackageReleasePRNeverMerged(t, stub)
		})
	}
}

// Verifies a failed merge whose re-read cannot be made, or whose settle wait is interrupted, fails with the merge error and the cause.
func TestMergePackageReleasePRFailsWhenAFailedMergeCannotBeResolved(t *testing.T) {
	cases := []struct {
		name     string
		override mergePackageReleasePROverride
		sleepErr error
		wantErr  string
	}{
		{"state read", mergePackageReleasePROverride{matches: mergePackageReleasePRCommandHasPrefix("gh pr view "), err: errors.New("state read failed")}, nil, "state read failed"},
		{"settle wait", mergePackageReleasePROverride{}, errors.New("settle wait interrupted"), "settle wait interrupted"},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			poll := greenDraftPackageReleasePRPoll()
			poll.prListJSON = packageReleasePRListJSON("package123", false)
			poll.failMerge = true
			poll.stateJSON = `{"state":"OPEN","isDraft":false,"headRefOid":"package123"}`

			exitCode, _, stderr, _ := runMergePackageReleasePRWithOverride(
				t, []mergePackageReleasePRPoll{poll}, []string{"--dispatcher-tag", "dispatcher-v3.4.0"}, testCase.override, testCase.sleepErr)

			if exitCode != 1 {
				t.Fatalf("expected exit code 1, got %d", exitCode)
			}
			assertReleasePRCheckLogContains(t, stderr, "gh pr merge failed")
			assertReleasePRCheckLogContains(t, stderr, testCase.wantErr)
		})
	}
}
