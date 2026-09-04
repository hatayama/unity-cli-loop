package automation

import (
	"bytes"
	"context"
	"strings"
	"testing"
	"time"
)

func TestDispatchPullRequestCheckWorkflowsUsesTheGivenHeadRef(t *testing.T) {
	// Verifies each required workflow is dispatched on exactly the head ref passed in and the log names the pull request.
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
		context.Background(), &stdout, "example/repository", "release-please--branches--main",
		"release PR #7: https://example.test/pr/7",
		[]string{"build-and-test.yml", "unity-compile-check-and-test-runner.yml"}, deps)
	if err != nil {
		t.Fatalf("dispatchPullRequestCheckWorkflows failed: %v", err)
	}

	wanted := []string{
		"gh workflow run build-and-test.yml --repo example/repository --ref release-please--branches--main",
		"gh workflow run unity-compile-check-and-test-runner.yml --repo example/repository --ref release-please--branches--main",
	}
	for index, wantedCommand := range wanted {
		if index >= len(commands) || commands[index] != wantedCommand {
			t.Fatalf("expected command %q, got %v", wantedCommand, commands)
		}
	}
	if !strings.Contains(stdout.String(), "Dispatching build-and-test.yml for release PR #7: https://example.test/pr/7") {
		t.Fatalf("expected the dispatch log to name the pull request, got %q", stdout.String())
	}
}
