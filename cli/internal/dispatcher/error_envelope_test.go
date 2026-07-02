package dispatcher

import (
	"errors"
	"strings"
	"testing"

	"github.com/hatayama/unity-cli-loop/cli/internal/clicore"
)

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
