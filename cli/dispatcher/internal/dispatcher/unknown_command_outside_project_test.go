package dispatcher

import (
	"bytes"
	"context"
	"encoding/json"
	"strings"
	"testing"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
)

// runDispatcherOutsideProject runs the dispatcher from an empty directory with no Unity project
// above or below it, and returns the exit code and stderr.
func runDispatcherOutsideProject(t *testing.T, args []string) (int, string) {
	t.Helper()
	t.Chdir(t.TempDir())
	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := RunDispatcher(context.Background(), args, &stdout, &stderr)
	if stdout.Len() != 0 {
		t.Fatalf("expected no stdout, got:\n%s", stdout.String())
	}
	return code, stderr.String()
}

// decodeErrorEnvelope reads the error a failed command wrote to stderr.
func decodeErrorEnvelope(t *testing.T, stderr string) clierrors.CLIError {
	t.Helper()
	var envelope clierrors.CLIErrorEnvelope
	if err := json.Unmarshal([]byte(stderr), &envelope); err != nil {
		t.Fatalf("stderr is not an error envelope: %v\n%s", err, stderr)
	}
	return envelope.Error
}

// projectNotFoundEnvelope is the error the dispatcher wrote for every failed project lookup
// before unknown commands were told apart.
func projectNotFoundEnvelope() string {
	var expected bytes.Buffer
	clierrors.WriteClassifiedError(&expected, clierrors.ProjectNotFoundError{}, clierrors.ErrorContext{})
	return expected.String()
}

// Verifies a mistyped command outside any Unity project is reported as an unknown command that
// suggests the built-in command, not as a missing project.
func TestMistypedCommandOutsideProjectReportsUnknownCommand(t *testing.T) {
	code, stderr := runDispatcherOutsideProject(t, []string{"upadte"})

	if code != 1 {
		t.Fatalf("code = %d, want 1\n%s", code, stderr)
	}
	cliError := decodeErrorEnvelope(t, stderr)
	if cliError.ErrorCode != clierrors.ErrorCodeUnknownCommand {
		t.Fatalf("ErrorCode = %s, want %s\n%s", cliError.ErrorCode, clierrors.ErrorCodeUnknownCommand, stderr)
	}
	if cliError.Message != "Unknown command: upadte" {
		t.Fatalf("Message = %q\n%s", cliError.Message, stderr)
	}
	suggestions, ok := cliError.Details["SuggestedCommands"].([]any)
	if !ok || len(suggestions) == 0 || suggestions[0] != clicore.UpdateCommandName {
		t.Fatalf("SuggestedCommands must start with %q: %#v", clicore.UpdateCommandName, cliError.Details)
	}
}

// Verifies the unknown-command error outside a project still tells a user who meant a project
// tool how to reach the project, since without one only built-in commands are known.
func TestUnknownCommandOutsideProjectPointsToTheProject(t *testing.T) {
	code, stderr := runDispatcherOutsideProject(t, []string{"some-project-only-tool"})

	if code != 1 {
		t.Fatalf("code = %d, want 1\n%s", code, stderr)
	}
	cliError := decodeErrorEnvelope(t, stderr)
	if cliError.ErrorCode != clierrors.ErrorCodeUnknownCommand {
		t.Fatalf("ErrorCode = %s, want %s\n%s", cliError.ErrorCode, clierrors.ErrorCodeUnknownCommand, stderr)
	}
	nextActions := strings.Join(cliError.NextActions, "\n")
	for _, expected := range []string{
		"`some-project-only-tool` is a tool of your Unity project, run the command from inside a Unity project.",
		"Pass `--project-path <path>` when targeting another Unity project.",
	} {
		if !strings.Contains(nextActions, expected) {
			t.Fatalf("NextActions missing %q:\n%s", expected, nextActions)
		}
	}
}

// Verifies a built-in tool run outside a project still reports the missing project unchanged,
// because the command exists and only the project is missing.
func TestBuiltInToolOutsideProjectKeepsProjectNotFound(t *testing.T) {
	code, stderr := runDispatcherOutsideProject(t, []string{"get-logs"})

	if code != 1 {
		t.Fatalf("code = %d, want 1\n%s", code, stderr)
	}
	if stderr != projectNotFoundEnvelope() {
		t.Fatalf("stderr changed for a built-in tool:\n%s", stderr)
	}
}

// Verifies a runner-owned native command run outside a project still reports the missing project
// unchanged; it is not in the default tool catalog, so only the native command table knows it.
func TestRunnerOwnedCommandOutsideProjectKeepsProjectNotFound(t *testing.T) {
	code, stderr := runDispatcherOutsideProject(t, []string{clicore.StatusCommandName})

	if code != 1 {
		t.Fatalf("code = %d, want 1\n%s", code, stderr)
	}
	if stderr != projectNotFoundEnvelope() {
		t.Fatalf("stderr changed for a runner-owned command:\n%s", stderr)
	}
}

// Verifies a project lookup that failed for another reason than a missing project keeps its own
// error even for an unknown command name.
func TestOtherProjectResolutionErrorsKeepTheirEnvelope(t *testing.T) {
	err := clierrors.MultipleProjectsFoundError{SearchRoot: "/search-root"}

	got := projectResolutionError(err, "upadte", clierrors.ErrorContext{})

	want := clierrors.ClassifyError(err, clierrors.ErrorContext{})
	if got.ErrorCode != want.ErrorCode || got.Message != want.Message ||
		strings.Join(got.NextActions, "\n") != strings.Join(want.NextActions, "\n") || got.Details != nil {
		t.Fatalf("multiple-projects error changed:\ngot  %#v\nwant %#v", got, want)
	}
}
