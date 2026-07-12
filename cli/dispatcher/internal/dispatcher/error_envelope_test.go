package dispatcher

import (
	"encoding/json"
	"errors"
	"strings"
	"testing"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

func TestClassifyLaunchStartupTimeoutError(t *testing.T) {
	// Verifies launch startup timeouts do not look like generic reachability or package failures.
	cliErr := clierrors.ClassifyError(
		launchStartupTimeoutError{
			projectRoot: "/tmp/MyProject",
			cause:       errors.New("timed out waiting for Unity tool readiness"),
		},
		clierrors.ErrorContext{ProjectRoot: "/tmp/MyProject", Command: clicore.LaunchCommandName},
	)

	if cliErr.ErrorCode != clierrors.ErrorCodeUnityStartupTimeout {
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
	cliErr := clierrors.ClassifyError(
		launchProcessExitTimeoutError{
			projectRoot: "/tmp/MyProject",
			pid:         123,
			timeout:     launchProcessExitTimeout,
		},
		clierrors.ErrorContext{ProjectRoot: "/tmp/MyProject", Command: clicore.LaunchCommandName},
	)

	if cliErr.ErrorCode != clierrors.ErrorCodeUnityProcessExitTimeout {
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
	// before classification, matching how install.go feeds errors to clierrors.ClassifyError.
	cliErr := clierrors.ClassifyError(
		wrapUnsupportedPlatformError(errors.New(installUnsupportedOSMessage)),
		clierrors.ErrorContext{Command: "install"},
	)

	if cliErr.ErrorCode != clierrors.ErrorCodeInvalidArgument {
		t.Fatalf("error code mismatch: %#v", cliErr)
	}
	if cliErr.Phase != clierrors.ErrorPhaseExecution {
		t.Fatalf("phase mismatch: %#v", cliErr)
	}
	expectedAction := "Run `uloop install` on macOS or Windows."
	if len(cliErr.NextActions) == 0 || cliErr.NextActions[0] != expectedAction {
		t.Fatalf("next actions mismatch: %#v", cliErr.NextActions)
	}
}

func TestClassifyServerBusyRPCError_WhenCompiling_IncludesEditorActivity(t *testing.T) {
	// Verifies dispatcher-side busy classification surfaces compile-specific editor activity guidance.
	cliErr := clierrors.ClassifyError(
		&unityipc.RPCError{
			Code:    -32603,
			Message: "Unity is busy running 'unity-compile'.",
			Data: json.RawMessage(
				`{"type":"server_busy","runningToolName":"unity-compile","requestedToolName":"get-logs","isCompiling":true}`),
		},
		clierrors.ErrorContext{ProjectRoot: "/tmp/MyProject", Command: "get-logs"},
	)

	editorActivity, ok := cliErr.Details["EditorActivity"].(map[string]any)
	if !ok {
		t.Fatalf("editor activity missing: %#v", cliErr.Details)
	}
	if editorActivity["isCompiling"] != true {
		t.Fatalf("isCompiling mismatch: %#v", editorActivity)
	}
	if cliErr.NextActions[0] != "Unity is compiling scripts; wait for compilation to finish before retrying." {
		t.Fatalf("next actions mismatch: %#v", cliErr.NextActions)
	}
}
