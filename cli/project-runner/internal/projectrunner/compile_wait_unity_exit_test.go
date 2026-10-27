package projectrunner

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"syscall"
	"testing"
	"time"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

// exitedEditorCompileDeps answers every status query as a server nobody listens on, finds no Editor
// process, and moves the wait's clock a second on every reading so lookups are spaced without sleeping.
func exitedEditorCompileDeps() compileWaitDeps {
	clock := time.Unix(1_700_000_000, 0)
	deps := compileWaitTestDeps(func(context.Context, unityipc.Connection, string) (compileStatusResponse, error) {
		return compileStatusResponse{}, &unityipc.ConnectionAttemptError{Cause: syscall.ECONNREFUSED}
	})
	deps.findRunningUnityProcess = noUnityProcessLookup
	deps.now = func() time.Time {
		clock = clock.Add(time.Second)
		return clock
	}
	return deps
}

func assertUnityExitedDuringCommand(t *testing.T, err error) {
	t.Helper()
	var notRunning clierrors.UnityNotRunningError
	if !errors.As(err, &notRunning) {
		t.Fatalf("err = %v, want UnityNotRunningError", err)
	}
	if !notRunning.ExitedDuringCommand {
		t.Fatalf("expected the exit to be reported as during the command, got %#v", notRunning)
	}
}

// Verifies a fresh compile's status wait ends with UnityNotRunningError once the Editor is confirmed
// gone, instead of polling a server that cannot come back for the whole compile timeout.
func TestWaitForCompileCompletionStopsOnceUnityHasExited(t *testing.T) {
	_, completed, _, err := waitForCompileCompletionWithDeps(context.Background(), compileCompletionOptions{
		connection: compileWaitTestConnection(t),
		requestID:  "compile_unity_exited",
		// Long enough that only the exit watch, never the timeout, can end the wait first.
		timeout:      5 * time.Second,
		pollInterval: time.Millisecond,
	}, exitedEditorCompileDeps())

	if completed {
		t.Fatal("a compile whose Editor exited cannot complete")
	}
	assertUnityExitedDuringCommand(t, err)
}

// Verifies the attach wait stops the same way when the Editor running the earlier compile is gone.
func TestWaitForAttachedCompileCompletionStopsOnceUnityHasExited(t *testing.T) {
	_, outcome, _, err := waitForAttachedCompileCompletion(context.Background(), compileCompletionOptions{
		connection: compileWaitTestConnection(t),
		requestID:  "compile_attach_unity_exited",
		// Long enough that only the exit watch, never the timeout, can end the wait first.
		timeout:      5 * time.Second,
		pollInterval: time.Millisecond,
	}, exitedEditorCompileDeps())

	if outcome != attachWaitFailed {
		t.Fatalf("outcome = %v, want attachWaitFailed", outcome)
	}
	assertUnityExitedDuringCommand(t, err)
}

// Verifies uloop compile prints the not-running envelope when the Editor exits during the domain reload
// wait, so the caller is told to launch Unity rather than to wait.
func TestRunCompileReportsUnityNotRunningWhenTheEditorExitsDuringTheWait(t *testing.T) {
	deps := exitedEditorCompileDeps()
	deps.freshWaitPollInterval = time.Millisecond
	deps.sendCompile = func(
		context.Context,
		unityipc.Connection,
		string,
		map[string]any,
		unityipc.ProgressFunc,
		time.Duration,
	) (unityipc.UnitySendOutcome, error) {
		return unityipc.UnitySendOutcome{RequestDispatched: true, RequestAccepted: true}, errors.New("read: EOF")
	}
	var stdout, stderr bytes.Buffer

	code := runCompileWithDomainReloadWaitWithDeps(
		context.Background(),
		compileWaitTestConnection(t),
		map[string]any{},
		&stdout,
		&stderr,
		deps,
	)

	if code != 1 || stdout.Len() != 0 {
		t.Fatalf("code = %d, stdout = %q, want 1 and nothing", code, stdout.String())
	}
	jsonStart := bytes.IndexByte(stderr.Bytes(), '{')
	if jsonStart < 0 {
		t.Fatalf("stderr has no JSON envelope: %s", stderr.String())
	}
	var envelope clierrors.CLIErrorEnvelope
	if err := json.Unmarshal(stderr.Bytes()[jsonStart:], &envelope); err != nil {
		t.Fatalf("decoding stderr failed: %v\n%s", err, stderr.String())
	}
	if envelope.Error.ErrorCode != clierrors.ErrorCodeUnityNotRunning {
		t.Fatalf("ErrorCode = %q, want %q\n%s", envelope.Error.ErrorCode, clierrors.ErrorCodeUnityNotRunning, stderr.String())
	}
}
