package cli

import (
	"errors"
	"strings"
	"testing"

	"github.com/hatayama/unity-cli-loop/cli/internal/clicore"
)

// Tests that explicit boolean values are returned as structured CLI errors.
func TestBuildToolParamsReturnsStructuredBooleanValueError(t *testing.T) {
	tool := clicore.ToolDefinition{
		Name: "sample-tool",
		InputSchema: clicore.InputSchema{
			Properties: map[string]clicore.ToolProperty{
				"Enabled": {Type: "boolean"},
			},
		},
	}

	_, _, err := buildToolParams([]string{"--enabled", "true"}, tool)
	if err == nil {
		t.Fatal("expected argument error")
	}

	var argumentErr *clicore.ArgumentError
	if !errors.As(err, &argumentErr) {
		t.Fatalf("expected argumentError, got %T", err)
	}
	cliErr := argumentErr.ToCLIError(clicore.ErrorContext{ProjectRoot: "/tmp/MyProject", Command: "sample-tool"})
	if cliErr.ErrorCode != clicore.ErrorCodeInvalidArgument {
		t.Fatalf("error code mismatch: %#v", cliErr)
	}
	if cliErr.Details["ExpectedType"] != "flag" {
		t.Fatalf("details mismatch: %#v", cliErr.Details)
	}
}

func TestClassifyLaunchStartupTimeoutError(t *testing.T) {
	// Verifies launch startup timeouts do not look like generic reachability or package failures.
	cliErr := clicore.ClassifyError(
		launchStartupTimeoutError{
			projectRoot: "/tmp/MyProject",
			cause:       errors.New("timed out waiting for Unity tool readiness"),
		},
		clicore.ErrorContext{ProjectRoot: "/tmp/MyProject", Command: clicore.LaunchCommandName},
	)

	if cliErr.ErrorCode != clicore.ErrorCodeUnityStartupTimeout {
		t.Fatalf("error code mismatch: %#v", cliErr)
	}
	if cliErr.Message != "Unity is running, but the Editor did not finish startup before the launch timeout." {
		t.Fatalf("message mismatch: %#v", cliErr)
	}
	for _, action := range cliErr.NextActions {
		lowerAction := strings.ToLower(action)
		if strings.Contains(lowerAction, "package") || strings.Contains(lowerAction, "uloop launch") {
			t.Fatalf("next action should avoid package guesses and launch retry guidance: %#v", cliErr.NextActions)
		}
	}
	if cliErr.Details["TimeoutSeconds"] != 600 {
		t.Fatalf("timeout details mismatch: %#v", cliErr.Details)
	}
}

func TestClassifyLaunchProcessExitTimeoutError(t *testing.T) {
	// Verifies restart and quit process-exit timeouts are structured as retryable launch failures.
	cliErr := clicore.ClassifyError(
		launchProcessExitTimeoutError{
			projectRoot: "/tmp/MyProject",
			pid:         123,
			timeout:     launchProcessExitTimeout,
		},
		clicore.ErrorContext{ProjectRoot: "/tmp/MyProject", Command: clicore.LaunchCommandName},
	)

	if cliErr.ErrorCode != clicore.ErrorCodeUnityProcessExitTimeout {
		t.Fatalf("error code mismatch: %#v", cliErr)
	}
	if !cliErr.Retryable || !cliErr.SafeToRetry {
		t.Fatalf("process exit timeout should be retryable: %#v", cliErr)
	}
	if cliErr.Details["Pid"] != 123 {
		t.Fatalf("pid details mismatch: %#v", cliErr.Details)
	}
}

func TestClassifyInstallUnsupportedOS(t *testing.T) {
	// Verifies install platform guards are reported as invalid command input.
	// The bootstrap command wraps the raw error with wrapUnsupportedPlatformError
	// before classification, matching how install.go feeds errors to clicore.ClassifyError.
	cliErr := clicore.ClassifyError(
		wrapUnsupportedPlatformError(errors.New(installUnsupportedOSMessage)),
		clicore.ErrorContext{Command: "install"},
	)

	if cliErr.ErrorCode != clicore.ErrorCodeInvalidArgument {
		t.Fatalf("error code mismatch: %#v", cliErr)
	}
	if cliErr.Phase != clicore.ErrorPhaseExecution {
		t.Fatalf("phase mismatch: %#v", cliErr)
	}
	expectedAction := "Run `uloop install` on Windows."
	if len(cliErr.NextActions) == 0 || cliErr.NextActions[0] != expectedAction {
		t.Fatalf("next actions mismatch: %#v", cliErr.NextActions)
	}
}

// Tests that compile wait timeout guidance teaches the caller to verify Editor
// responsiveness instead of assuming a freeze, because agents have terminated
// whole sessions after misreading this timeout as a frozen Editor.
func TestCompileWaitTimeoutError(t *testing.T) {
	cliErr := compileWaitTimeoutError("/tmp/MyProject")

	if cliErr.ErrorCode != clicore.ErrorCodeCompileWaitTimeout {
		t.Fatalf("error code mismatch: %#v", cliErr)
	}
	if !cliErr.Retryable || !cliErr.SafeToRetry {
		t.Fatalf("retry flags mismatch: %#v", cliErr)
	}
	if cliErr.ProjectRoot != "/tmp/MyProject" {
		t.Fatalf("project root mismatch: %#v", cliErr)
	}
	expectedMessage := "Compile status wait timed out after 180000ms. This does not mean the Unity Editor is frozen; the compile may simply still be running."
	if cliErr.Message != expectedMessage {
		t.Fatalf("message mismatch: %#v", cliErr.Message)
	}
	expectedActions := []string{
		"Run a light command such as `uloop get-logs --max-count 1` to check whether Unity is responsive before treating this as a freeze.",
		"If Unity responds, retry `uloop compile`; the previous compile likely finished in the meantime.",
		"Only if Unity does not respond to any command, restart it with `uloop launch -r`.",
	}
	if len(cliErr.NextActions) != len(expectedActions) {
		t.Fatalf("next actions mismatch: %#v", cliErr.NextActions)
	}
	for i, expected := range expectedActions {
		if cliErr.NextActions[i] != expected {
			t.Fatalf("next action %d mismatch: %#v", i, cliErr.NextActions)
		}
	}
}
