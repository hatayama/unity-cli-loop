package clierrors

import (
	"bytes"
	"encoding/json"
	"errors"
	"io"
	"strings"
	"testing"

	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

const unityNotRunningTestEditorLog = "/home/user/Library/Logs/Unity/Editor.log"

// Verifies a command that never reached Unity reports a closed Editor as a condition retrying cannot
// clear, with launching Unity as the first step and no crash guidance.
func TestUnityNotRunningErrorBeforeTheCommandReachedUnity(t *testing.T) {
	err := UnityNotRunningError{
		ProjectRoot: "/projects/sample",
		Endpoint:    "/tmp/uloop/sample.sock",
		Cause:       errors.New("dial unix /tmp/uloop/sample.sock: connect: no such file or directory"),
	}

	cliErr := ClassifyError(err, ErrorContext{Command: "get-logs"})

	if cliErr.ErrorCode != ErrorCodeUnityNotRunning || cliErr.Phase != ErrorPhaseConnection {
		t.Fatalf("unexpected classification: %#v", cliErr)
	}
	if cliErr.Retryable {
		t.Fatal("retrying cannot start Unity, so the error must not be Retryable")
	}
	if !cliErr.SafeToRetry {
		t.Fatal("nothing reached Unity, so running the command again must be SafeToRetry")
	}
	if cliErr.Message != "No Unity Editor is running for this project." {
		t.Fatalf("unexpected message: %q", cliErr.Message)
	}
	if len(cliErr.NextActions) != 1 || !strings.Contains(cliErr.NextActions[0], "`uloop launch`") {
		t.Fatalf("expected launch as the only next action, got %#v", cliErr.NextActions)
	}
	if cliErr.ProjectRoot != "/projects/sample" {
		t.Fatalf("expected the error's project root, got %q", cliErr.ProjectRoot)
	}
	if cliErr.Details["Cause"] != err.Cause.Error() || cliErr.Details["Endpoint"] != err.Endpoint {
		t.Fatalf("expected the cause and endpoint in Details, got %#v", cliErr.Details)
	}
	if _, ok := cliErr.Details["EditorLog"]; ok {
		t.Fatalf("a clean exit must not point at the Editor log, got %#v", cliErr.Details)
	}
}

// Verifies a lockfile left behind is reported as an abnormal exit without asserting a crash, and that
// the Editor's own log is named before launch because the next Editor start moves or replaces it.
func TestUnityNotRunningErrorPointsAtTheEditorLogWhenTheLockfileWasLeftBehind(t *testing.T) {
	err := UnityNotRunningError{
		ProjectRoot:        "/projects/sample",
		LockfileLeftBehind: true,
		EditorLogPath:      unityNotRunningTestEditorLog,
	}

	cliErr := err.ToCLIError(ErrorContext{Command: "get-logs"})

	if !strings.Contains(cliErr.Message, "did not shut down normally") ||
		!strings.Contains(cliErr.Message, "may have crashed") {
		t.Fatalf("expected an abnormal-exit message, got %q", cliErr.Message)
	}
	if len(cliErr.NextActions) != 2 {
		t.Fatalf("expected the log step and the launch step, got %#v", cliErr.NextActions)
	}
	if !strings.Contains(cliErr.NextActions[0], unityNotRunningTestEditorLog) ||
		!strings.Contains(cliErr.NextActions[0], "next Editor start") {
		t.Fatalf("expected the Editor log step first, got %q", cliErr.NextActions[0])
	}
	if !strings.Contains(cliErr.NextActions[1], "`uloop launch`") {
		t.Fatalf("expected launch after the log step, got %q", cliErr.NextActions[1])
	}
	if cliErr.Details["EditorLog"] != unityNotRunningTestEditorLog {
		t.Fatalf("expected the Editor log path in Details, got %#v", cliErr.Details)
	}
}

// Verifies no Editor log step appears when no log at the default location is this Editor's, since
// pointing at another Editor's log would send the caller to the wrong reason for the exit.
func TestUnityNotRunningErrorOmitsTheEditorLogStepWithoutTheEditorsLog(t *testing.T) {
	err := UnityNotRunningError{LockfileLeftBehind: true}

	cliErr := err.ToCLIError(ErrorContext{Command: "get-logs"})

	if len(cliErr.NextActions) != 1 || !strings.Contains(cliErr.NextActions[0], "`uloop launch`") {
		t.Fatalf("expected the launch step only, got %#v", cliErr.NextActions)
	}
	if _, ok := cliErr.Details["EditorLog"]; ok {
		t.Fatalf("no log path may appear in Details, got %#v", cliErr.Details)
	}
}

// Verifies an Editor that went away while a mutating command was running warns that the command may
// have partly run, and marks the command unsafe to repeat.
func TestUnityNotRunningErrorAfterUnityExitedDuringAMutatingCommand(t *testing.T) {
	err := UnityNotRunningError{ExitedDuringCommand: true}

	cliErr := err.ToCLIError(ErrorContext{Command: "compile"})

	if cliErr.Phase != ErrorPhaseResponseWaiting {
		t.Fatalf("expected the response-waiting phase, got %q", cliErr.Phase)
	}
	if cliErr.SafeToRetry {
		t.Fatal("compile may have partly run, so it must not be SafeToRetry")
	}
	if !strings.HasPrefix(cliErr.Message, "Unity exited while this command was running") {
		t.Fatalf("expected the exit-during-command message, got %q", cliErr.Message)
	}
	if len(cliErr.NextActions) != 2 || !strings.Contains(cliErr.NextActions[0], "partly run") {
		t.Fatalf("expected the partial-run warning before launch, got %#v", cliErr.NextActions)
	}
}

// Verifies a read-only command that was running when Unity went away stays safe to repeat and gets no
// partial-run warning.
func TestUnityNotRunningErrorAfterUnityExitedDuringAReadOnlyCommand(t *testing.T) {
	err := UnityNotRunningError{ExitedDuringCommand: true}

	cliErr := err.ToCLIError(ErrorContext{Command: "get-logs"})

	if !cliErr.SafeToRetry {
		t.Fatal("get-logs cannot double-apply, so it must stay SafeToRetry")
	}
	if len(cliErr.NextActions) != 1 {
		t.Fatalf("expected only the launch step, got %#v", cliErr.NextActions)
	}
}

// Verifies the error never reads as a dropped connection, even when its cause is one: callers that see a
// disconnect start waiting for a domain reload that cannot come.
func TestUnityNotRunningErrorDoesNotLookLikeADisconnect(t *testing.T) {
	err := UnityNotRunningError{ExitedDuringCommand: true, Cause: io.EOF}

	if IsTransportDisconnectError(err) {
		t.Fatalf("UnityNotRunningError must not be classified as a disconnect: %v", err)
	}
	if IsUnityServerGoneError(err) {
		t.Fatalf("UnityNotRunningError must not be classified as a server that may come back: %v", err)
	}
}

// Verifies the tool failure writer prints the not-running envelope for a dispatched request instead of
// the disconnect envelope that tells the caller to wait for a reload.
func TestWriteToolFailureReportsUnityNotRunningForADispatchedRequest(t *testing.T) {
	var output bytes.Buffer
	err := UnityNotRunningError{ExitedDuringCommand: true, Cause: io.EOF}

	WriteToolFailure(
		&output,
		err,
		unityipc.UnitySendOutcome{RequestDispatched: true, RequestAccepted: true},
		ErrorContext{Command: "run-tests"},
	)

	var envelope CLIErrorEnvelope
	if decodeErr := json.Unmarshal(output.Bytes(), &envelope); decodeErr != nil {
		t.Fatalf("decoding the envelope failed: %v\n%s", decodeErr, output.String())
	}
	if envelope.Error.ErrorCode != ErrorCodeUnityNotRunning {
		t.Fatalf("expected %s, got %#v", ErrorCodeUnityNotRunning, envelope.Error)
	}
}

// Verifies an Editor that exited while launch waited for it is reported as a startup that did not
// finish: launch stays safe to run again, and no step tells the caller to run launch and then launch.
func TestUnityNotRunningErrorAfterUnityExitedDuringLaunch(t *testing.T) {
	err := UnityNotRunningError{ExitedDuringCommand: true, LockfileLeftBehind: true, EditorLogPath: unityNotRunningTestEditorLog}

	cliErr := err.ToCLIError(ErrorContext{Command: "launch"})

	if !cliErr.SafeToRetry {
		t.Fatal("running launch again cannot double-apply anything, so it must stay SafeToRetry")
	}
	if !strings.HasPrefix(cliErr.Message, "Unity exited before it finished starting") ||
		!strings.Contains(cliErr.Message, "may have crashed") {
		t.Fatalf("expected the startup-exit message with the abnormal-exit note, got %q", cliErr.Message)
	}
	if len(cliErr.NextActions) != 2 {
		t.Fatalf("expected the Editor log step and the launch step, got %#v", cliErr.NextActions)
	}
	if !strings.Contains(cliErr.NextActions[0], unityNotRunningTestEditorLog) {
		t.Fatalf("expected the Editor log step first, got %q", cliErr.NextActions[0])
	}
	if cliErr.NextActions[1] != "Run `uloop launch` again." {
		t.Fatalf("expected launch to be run again, got %q", cliErr.NextActions[1])
	}
}
