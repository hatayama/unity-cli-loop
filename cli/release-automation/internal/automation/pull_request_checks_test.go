package automation

import (
	"bytes"
	"context"
	"strings"
	"testing"
	"time"
)

func TestDispatchPullRequestCheckWorkflowsUsesTheGivenHeadRef(t *testing.T) {
	// Verifies each required workflow is dispatched on the head ref passed in, not on a release-please branch name.
	commands := []string{}
	deps := releasePRCheckDeps{
		now:   func() time.Time { return time.Unix(0, 0).UTC() },
		sleep: func(context.Context, time.Duration) error { return nil },
		runOutput: func(_ context.Context, name string, args ...string) (string, error) {
			commands = append(commands, name+" "+strings.Join(args, " "))
			return "", nil
		},
	}
	stdout := bytes.Buffer{}

	_, err := dispatchPullRequestCheckWorkflows(
		context.Background(), &stdout, "example/repository", "chore/dispatcher-pin-dispatcher-v3.0.1",
		"dispatcher pin PR #7: https://example.test/pr/7",
		[]string{"build-and-test.yml", "unity-compile-check-and-test-runner.yml"}, deps)
	if err != nil {
		t.Fatalf("dispatchPullRequestCheckWorkflows failed: %v", err)
	}

	wanted := []string{
		"gh workflow run build-and-test.yml --repo example/repository --ref chore/dispatcher-pin-dispatcher-v3.0.1",
		"gh workflow run unity-compile-check-and-test-runner.yml --repo example/repository --ref chore/dispatcher-pin-dispatcher-v3.0.1",
	}
	for index, wantedCommand := range wanted {
		if index >= len(commands) || commands[index] != wantedCommand {
			t.Fatalf("expected command %q, got %v", wantedCommand, commands)
		}
	}
	if !strings.Contains(stdout.String(), "Dispatching build-and-test.yml for dispatcher pin PR #7: https://example.test/pr/7") {
		t.Fatalf("expected the dispatch log to name the pull request, got %q", stdout.String())
	}
}

func TestDispatchPullRequestChecksForHeadRequiresARepositoryAndHeadRef(t *testing.T) {
	// Verifies the exported entry point refuses to dispatch without both a repository and a head ref.
	err := DispatchPullRequestChecksForHead(context.Background(), &bytes.Buffer{}, "", "some-branch", "")
	if err == nil {
		t.Fatal("expected a missing repository to fail")
	}
	err = DispatchPullRequestChecksForHead(context.Background(), &bytes.Buffer{}, "example/repository", "", "")
	if err == nil {
		t.Fatal("expected a missing head ref to fail")
	}
}
