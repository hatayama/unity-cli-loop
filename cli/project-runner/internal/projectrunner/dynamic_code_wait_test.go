package projectrunner

import (
	"bufio"
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"strings"
	"testing"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

// startDynamicCodeDroppingServer reads one execute-dynamic-code request and closes the connection
// without a word, so the request was sent but never accepted.
func startDynamicCodeDroppingServer(t *testing.T, projectRoot string) unityipc.Connection {
	t.Helper()
	listener := newLoopbackIpcListener(t)
	go func() {
		conn, err := listener.Accept()
		if err != nil {
			return
		}
		defer func() { _ = conn.Close() }()
		_, _ = unityipc.Read(bufio.NewReader(conn))
	}()
	return unityipc.Connection{
		Endpoint:    unityipc.Endpoint{Network: listener.Addr().Network(), Address: listener.Addr().String()},
		ProjectRoot: projectRoot,
	}
}

// dynamicCodeReloadRun is what one execute-dynamic-code run with the reload wait printed.
type dynamicCodeReloadRun struct {
	code     int
	stdout   string
	cliError clierrors.CLIError
}

func runDynamicCodeReloadWaitForTest(t *testing.T, connection unityipc.Connection, waitErr error) dynamicCodeReloadRun {
	t.Helper()
	var stdout, stderr bytes.Buffer
	waits := 0
	// Why no dialog look: these runs are about the report after the wait, and an empty watch makes the
	// look after a failed wait find nothing, so that failure is reported unchanged.
	deps := dynamicCodeWaitDeps{
		waitForToolReadiness: func(context.Context, string, clicore.DialogFinder) error {
			waits++
			return waitErr
		},
		dialogWatch: dialogWatchDeps{},
	}

	code := runExecuteDynamicCodeWithDomainReloadWaitWithDeps(
		context.Background(),
		connection,
		map[string]any{"Code": "UnityEditor.EditorUtility.RequestScriptReload(); return 0;", clicore.DomainReloadWaitParam: true},
		&stdout,
		&stderr,
		deps,
	)

	if waits != 1 {
		t.Fatalf("readiness waits = %d, want 1\nstderr: %s", waits, stderr.String())
	}
	var envelope clierrors.CLIErrorEnvelope
	if err := json.Unmarshal([]byte(fromFirstBrace(stderr.String())), &envelope); err != nil {
		t.Fatalf("stderr has no JSON error envelope: %v\n%s", err, stderr.String())
	}
	return dynamicCodeReloadRun{code: code, stdout: stdout.String(), cliError: envelope.Error}
}

// fromFirstBrace returns the output from its first "{" on, dropping the progress lines a spinner may
// print before the envelope.
func fromFirstBrace(output string) string {
	index := strings.Index(output, "{")
	if index < 0 {
		return output
	}
	return output[index:]
}

// Verifies a snippet whose connection dropped after Unity accepted it, and after which the Editor came
// back, is reported as a lost answer: still exit code 1 and UNITY_DISCONNECTED_AFTER_ACCEPT, but not
// safe to retry, with no advice to retry, with the advice to check the effect first and not to rerun a
// snippet that reloads scripts, and with the cause kept (#1).
func TestExecuteDynamicCodeReportsAnAnswerLostToAReloadWithoutAdvisingARetry(t *testing.T) {
	useRunningUnityProcess(t)
	server := startFakeUnityServer(t, writeFakeUnityProject(t), clicore.ExecuteDynamicCodeCommandName, dynamicCodeAcceptedThenLostResponse)

	run := runDynamicCodeReloadWaitForTest(t, server.connection, nil)

	cliError := run.cliError
	if run.code != 1 || run.stdout != "" {
		t.Fatalf("exit code = %d, stdout = %q, want 1 and nothing", run.code, run.stdout)
	}
	if cliError.ErrorCode != clierrors.ErrorCodeUnityDisconnectedAfterAccept || cliError.Phase != clierrors.ErrorPhaseResponseWaiting ||
		!cliError.Retryable || cliError.SafeToRetry {
		t.Fatalf("ErrorCode = %q, Phase = %q, Retryable = %v, SafeToRetry = %v, want %s, response_waiting, true, false",
			cliError.ErrorCode, cliError.Phase, cliError.Retryable, cliError.SafeToRetry, clierrors.ErrorCodeUnityDisconnectedAfterAccept)
	}
	wantMessage := "Unity reloaded its scripts or restarted its server before it answered, and is ready again. " +
		"The snippet was sent and may have run; its result is not available."
	if cliError.Message != wantMessage {
		t.Fatalf("Message = %q, want %q", cliError.Message, wantMessage)
	}
	steps := strings.Join(cliError.NextActions, "\n")
	if strings.Contains(steps, "Retry after") {
		t.Fatalf("NextActions = %q, want no advice to retry", cliError.NextActions)
	}
	if !strings.Contains(steps, "uloop get-logs") || !strings.Contains(steps, "RequestScriptReload") {
		t.Fatalf("NextActions = %q, want the check before a rerun and the note on snippets that reload scripts", cliError.NextActions)
	}
	if cause, _ := cliError.Details["Cause"].(string); cause == "" {
		t.Fatalf("Details.Cause = %#v, want the lost connection", cliError.Details["Cause"])
	}
}

// Verifies a snippet whose connection dropped before Unity accepted it is reported as before, even
// after the Editor came back (#2).
func TestExecuteDynamicCodeKeepsTheReportOfAConnectionLostBeforeAcceptance(t *testing.T) {
	useRunningUnityProcess(t)
	projectRoot := writeFakeUnityProject(t)

	run := runDynamicCodeReloadWaitForTest(t, startDynamicCodeDroppingServer(t, projectRoot), nil)

	cliError := run.cliError
	if run.code != 1 || cliError.ErrorCode != "UNITY_DISCONNECTED_AFTER_DISPATCH" {
		t.Fatalf("exit code = %d, ErrorCode = %q, want 1 and UNITY_DISCONNECTED_AFTER_DISPATCH", run.code, cliError.ErrorCode)
	}
	if cliError.Message != "Unity disconnected after the CLI dispatched the request." ||
		!strings.Contains(strings.Join(cliError.NextActions, "\n"), "Retry after Unity finishes compiling") {
		t.Fatalf("Message = %q, NextActions = %q, want the report as before", cliError.Message, cliError.NextActions)
	}
}

// Verifies a snippet whose connection dropped after Unity accepted it, and whose wait for the Editor
// failed, reports the wait's failure as before (#3).
func TestExecuteDynamicCodeReportsAFailedReloadWaitAsBefore(t *testing.T) {
	useRunningUnityProcess(t)
	server := startFakeUnityServer(t, writeFakeUnityProject(t), clicore.ExecuteDynamicCodeCommandName, dynamicCodeAcceptedThenLostResponse)
	waitErr := clierrors.UnityServerNotRespondingError{ProjectRoot: "<PROJECT_ROOT>", Cause: errors.New("connection refused")}

	run := runDynamicCodeReloadWaitForTest(t, server.connection, waitErr)

	want := clierrors.ClassifyError(waitErr, clierrors.ErrorContext{Command: clicore.ExecuteDynamicCodeCommandName})
	if run.code != 1 || run.cliError.ErrorCode != want.ErrorCode || run.cliError.Message != want.Message {
		t.Fatalf("exit code = %d, error = %q %q, want 1 and %q %q",
			run.code, run.cliError.ErrorCode, run.cliError.Message, want.ErrorCode, want.Message)
	}
}
