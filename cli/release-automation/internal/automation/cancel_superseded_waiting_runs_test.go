package automation

import (
	"bytes"
	"context"
	"fmt"
	"strings"
	"testing"
)

// Verifies that only waiting runs with a databaseId older than the current run are cancelled,
// while the current run itself and any newer waiting run are left untouched.
func TestCancelSupersededWaitingRunsCancelsOlderRunsOnly(t *testing.T) {
	commandLog := []string{}
	deps := cancelSupersededWaitingRunsDeps{
		runOutput: func(ctx context.Context, name string, args ...string) (string, error) {
			commandLine := strings.Join(append([]string{name}, args...), " ")
			commandLog = append(commandLog, commandLine)
			if commandLine == "gh run list --repo owner/repository --workflow native-cli-publish.yml --branch v3-beta --status waiting --json databaseId,createdAt --limit 50" {
				return `[{"databaseId":100,"createdAt":"2026-07-20T01:00:00Z"},{"databaseId":200,"createdAt":"2026-07-20T02:00:00Z"},{"databaseId":300,"createdAt":"2026-07-20T03:00:00Z"}]`, nil
			}
			if commandLine == "gh run cancel 100 --repo owner/repository" || commandLine == "gh run cancel 200 --repo owner/repository" {
				return "", nil
			}
			return "", fmt.Errorf("unexpected command: %s", commandLine)
		},
	}
	config := cancelSupersededWaitingRunsConfig{
		repository:   "owner/repository",
		workflow:     "native-cli-publish.yml",
		branch:       "v3-beta",
		currentRunID: 300,
	}
	stdout := bytes.Buffer{}
	stderr := bytes.Buffer{}

	exitCode := runCancelSupersededWaitingRunsWithDeps(context.Background(), &stdout, &stderr, config, deps)

	if exitCode != 0 {
		t.Fatalf("expected exit code 0, got %d\nstderr: %s", exitCode, stderr.String())
	}
	commandLogText := strings.Join(commandLog, "\n")
	assertCancelSupersededWaitingRunsLogContainsLine(t, commandLogText, "gh run cancel 100 --repo owner/repository")
	assertCancelSupersededWaitingRunsLogContainsLine(t, commandLogText, "gh run cancel 200 --repo owner/repository")
	assertCancelSupersededWaitingRunsLogDoesNotContain(t, commandLogText, "gh run cancel 300")
}

// Verifies that no cancel command is issued when there are no waiting runs.
func TestCancelSupersededWaitingRunsSkipsCancelWhenNoWaitingRuns(t *testing.T) {
	commandLog := []string{}
	deps := cancelSupersededWaitingRunsDeps{
		runOutput: func(ctx context.Context, name string, args ...string) (string, error) {
			commandLine := strings.Join(append([]string{name}, args...), " ")
			commandLog = append(commandLog, commandLine)
			if commandLine == "gh run list --repo owner/repository --workflow native-cli-publish.yml --branch v3-beta --status waiting --json databaseId,createdAt --limit 50" {
				return `[]`, nil
			}
			return "", fmt.Errorf("unexpected command: %s", commandLine)
		},
	}
	config := cancelSupersededWaitingRunsConfig{
		repository:   "owner/repository",
		workflow:     "native-cli-publish.yml",
		branch:       "v3-beta",
		currentRunID: 300,
	}
	stdout := bytes.Buffer{}
	stderr := bytes.Buffer{}

	exitCode := runCancelSupersededWaitingRunsWithDeps(context.Background(), &stdout, &stderr, config, deps)

	if exitCode != 0 {
		t.Fatalf("expected exit code 0, got %d\nstderr: %s", exitCode, stderr.String())
	}
	commandLogText := strings.Join(commandLog, "\n")
	assertCancelSupersededWaitingRunsLogDoesNotContain(t, commandLogText, "gh run cancel")
}

// Verifies that a cancel failure on one run (e.g. it was approved between listing and cancelling)
// is reported as a warning while the remaining older runs are still cancelled, and the process exits 0.
func TestCancelSupersededWaitingRunsContinuesAfterCancelFailure(t *testing.T) {
	commandLog := []string{}
	deps := cancelSupersededWaitingRunsDeps{
		runOutput: func(ctx context.Context, name string, args ...string) (string, error) {
			commandLine := strings.Join(append([]string{name}, args...), " ")
			commandLog = append(commandLog, commandLine)
			if commandLine == "gh run list --repo owner/repository --workflow native-cli-publish.yml --branch v3-beta --status waiting --json databaseId,createdAt --limit 50" {
				return `[{"databaseId":100,"createdAt":"2026-07-20T01:00:00Z"},{"databaseId":200,"createdAt":"2026-07-20T02:00:00Z"}]`, nil
			}
			if commandLine == "gh run cancel 100 --repo owner/repository" {
				return "", fmt.Errorf("run has already been approved")
			}
			if commandLine == "gh run cancel 200 --repo owner/repository" {
				return "", nil
			}
			return "", fmt.Errorf("unexpected command: %s", commandLine)
		},
	}
	config := cancelSupersededWaitingRunsConfig{
		repository:   "owner/repository",
		workflow:     "native-cli-publish.yml",
		branch:       "v3-beta",
		currentRunID: 300,
	}
	stdout := bytes.Buffer{}
	stderr := bytes.Buffer{}

	exitCode := runCancelSupersededWaitingRunsWithDeps(context.Background(), &stdout, &stderr, config, deps)

	if exitCode != 0 {
		t.Fatalf("expected exit code 0 even when an individual cancel fails, got %d", exitCode)
	}
	assertCancelSupersededWaitingRunsLogContains(t, stderr.String(), "100")
	commandLogText := strings.Join(commandLog, "\n")
	assertCancelSupersededWaitingRunsLogContainsLine(t, commandLogText, "gh run cancel 200 --repo owner/repository")
}

// Verifies that a gh run list failure aborts with a non-zero exit code instead of being swallowed.
func TestCancelSupersededWaitingRunsFailsWhenRunListFails(t *testing.T) {
	deps := cancelSupersededWaitingRunsDeps{
		runOutput: func(ctx context.Context, name string, args ...string) (string, error) {
			return "", fmt.Errorf("gh: authentication failed")
		},
	}
	config := cancelSupersededWaitingRunsConfig{
		repository:   "owner/repository",
		workflow:     "native-cli-publish.yml",
		branch:       "v3-beta",
		currentRunID: 300,
	}
	stdout := bytes.Buffer{}
	stderr := bytes.Buffer{}

	exitCode := runCancelSupersededWaitingRunsWithDeps(context.Background(), &stdout, &stderr, config, deps)

	if exitCode != 1 {
		t.Fatalf("expected exit code 1 when gh run list fails, got %d", exitCode)
	}
	assertCancelSupersededWaitingRunsLogContains(t, stderr.String(), "authentication failed")
}

// Verifies that each required flag (--repo, --workflow, --branch, --current-run-id) is enforced,
// and that the command errors instead of running with a partially specified configuration.
func TestCancelSupersededWaitingRunsRequiresFlags(t *testing.T) {
	testCases := []struct {
		name string
		args []string
	}{
		{name: "missing repo", args: []string{"--workflow", "native-cli-publish.yml", "--branch", "v3-beta", "--current-run-id", "300"}},
		{name: "missing workflow", args: []string{"--repo", "owner/repository", "--branch", "v3-beta", "--current-run-id", "300"}},
		{name: "missing branch", args: []string{"--repo", "owner/repository", "--workflow", "native-cli-publish.yml", "--current-run-id", "300"}},
		{name: "missing current-run-id", args: []string{"--repo", "owner/repository", "--workflow", "native-cli-publish.yml", "--branch", "v3-beta"}},
	}

	for _, testCase := range testCases {
		t.Run(testCase.name, func(t *testing.T) {
			_, err := parseCancelSupersededWaitingRunsFlags(testCase.args)
			if err == nil {
				t.Fatalf("expected an error for %s, got none", testCase.name)
			}
		})
	}
}

func assertCancelSupersededWaitingRunsLogContains(t *testing.T, actual string, expected string) {
	t.Helper()
	if !strings.Contains(actual, expected) {
		t.Fatalf("expected log to contain %q, got:\n%s", expected, actual)
	}
}

func assertCancelSupersededWaitingRunsLogDoesNotContain(t *testing.T, actual string, unexpected string) {
	t.Helper()
	if strings.Contains(actual, unexpected) {
		t.Fatalf("expected log not to contain %q, got:\n%s", unexpected, actual)
	}
}

func assertCancelSupersededWaitingRunsLogContainsLine(t *testing.T, actual string, expected string) {
	t.Helper()
	for _, line := range strings.Split(actual, "\n") {
		if strings.TrimSuffix(line, "\r") == expected {
			return
		}
	}
	t.Fatalf("expected log to contain line %q, got:\n%s", expected, actual)
}
