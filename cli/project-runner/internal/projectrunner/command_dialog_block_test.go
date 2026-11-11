package projectrunner

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"reflect"
	"strings"
	"testing"
	"time"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

const (
	commandDialogBlockCommand   = "get-logs"
	commandDialogRunAgainLine   = "After the dialog is closed, run the command again."
	commandDialogMainThreadData = `{"type":"main_thread_blocked","message":"blocked","runningToolName":"%s","stallSeconds":30.4}`
	// The answer of an Editor that reports the dialog itself (Unity 6000.7 and later), so a scripted
	// run finds it without looking in Unity's windows.
	commandDialogStatusWithDialog = `{"IsBusy":false,"HasEditorState":true,"IsCompiling":false,"IsUpdating":false,` +
		`"SecondsSinceLastMainThreadTick":31,"OpenDialogs":[{"Title":"Player 2 unexpectedly stopped",` +
		`"Message":"Do you want to restart it?","Buttons":["No","Yes"]}]}`
)

func commandDialogPlayerStoppedDialog() statusDialog {
	return statusDialog{
		Title:   "Player 2 unexpectedly stopped",
		Message: "Do you want to restart it?",
		Buttons: []string{"No", "Yes"},
	}
}

func commandDialogMainThreadBlockedRPCError(command string) *unityipc.RPCError {
	return &unityipc.RPCError{
		Code:    -32603,
		Message: "blocked",
		Data:    json.RawMessage(strings.Replace(commandDialogMainThreadData, "%s", command, 1)),
	}
}

func commandDialogMainThreadBlockedRPCErrorJSON(command string) string {
	return `{"code":-32603,"message":"blocked","data":` + strings.Replace(commandDialogMainThreadData, "%s", command, 1) + `}`
}

// commandDialogProbe stands in for the status probe and counts how often it ran.
type commandDialogProbe struct {
	report statusReport
	err    error
	calls  int
}

func (probe *commandDialogProbe) deps(canPress bool) dialogWatchDeps {
	return dialogWatchDeps{
		probeEditorStatus: func(context.Context, unityipc.Connection) (statusReport, error) {
			probe.calls++
			return probe.report, probe.err
		},
		canPressDialogs: canPress,
		now:             time.Now,
	}
}

func blockedByDialogStatusReport(dialogs ...statusDialog) statusReport {
	report := newStatusReport(statusStateBlockedByDialog, "A modal dialog is open in Unity.")
	report.Dialogs = dialogs
	return report
}

func classifyCommandDialogError(err error) clierrors.CLIError {
	return clierrors.ClassifyError(err, clierrors.ErrorContext{ProjectRoot: "<PROJECT_ROOT>", Command: commandDialogBlockCommand})
}

func explainCommandDialogBlock(err error, deps dialogWatchDeps) error {
	return explainBlockedByDialog(context.Background(), unreachableConnection("<PROJECT_ROOT>"), commandDialogBlockCommand, err, deps)
}

func assertCommandBlockedByDialog(t *testing.T, cliError clierrors.CLIError, wantTitle string) {
	t.Helper()
	if cliError.ErrorCode != clierrors.ErrorCodeBlockedByDialog || cliError.Phase != clierrors.ErrorPhaseResponseWaiting {
		t.Fatalf("ErrorCode = %q, Phase = %q, want BLOCKED_BY_DIALOG in response_waiting", cliError.ErrorCode, cliError.Phase)
	}
	wantPrefix := "'" + commandDialogBlockCommand + "' was not run: A modal dialog \"" + wantTitle + "\" is open in Unity"
	if !strings.HasPrefix(cliError.Message, wantPrefix) {
		t.Fatalf("Message = %q, want prefix %q", cliError.Message, wantPrefix)
	}
	if !cliError.Retryable || !cliError.SafeToRetry {
		t.Fatalf("Retryable = %v, SafeToRetry = %v, want both true", cliError.Retryable, cliError.SafeToRetry)
	}
	if last := cliError.NextActions[len(cliError.NextActions)-1]; last != commandDialogRunAgainLine {
		t.Fatalf("last NextAction = %q, want %q", last, commandDialogRunAgainLine)
	}
}

// Verifies a command Unity did not run because its main thread was blocked is reported as blocked by
// the dialog the status probe found: the dialog's title, the press-dialog-button guidance, the dialogs
// and the original stall details, and safe to retry.
func TestExplainBlockedByDialogReportsTheDialogThatKeptTheCommandFromRunning(t *testing.T) {
	dialog := commandDialogPlayerStoppedDialog()
	probe := &commandDialogProbe{report: blockedByDialogStatusReport(dialog)}

	cliError := classifyCommandDialogError(explainCommandDialogBlock(commandDialogMainThreadBlockedRPCError(commandDialogBlockCommand), probe.deps(true)))

	assertCommandBlockedByDialog(t, cliError, dialog.Title)
	if !strings.Contains(cliError.NextActions[0], "uloop press-dialog-button") {
		t.Fatalf("first NextAction = %q, want the press-dialog-button guidance", cliError.NextActions[0])
	}
	if !reflect.DeepEqual(cliError.Details["Dialogs"], []statusDialog{dialog}) {
		t.Fatalf("Details.Dialogs = %#v, want the found dialog", cliError.Details["Dialogs"])
	}
	if cliError.Details["StallSeconds"] != 30.4 || cliError.Details["RunningToolName"] != commandDialogBlockCommand {
		t.Fatalf("Details = %#v, want the original StallSeconds and RunningToolName", cliError.Details)
	}
}

// Verifies a dialog without buttons, or a system where uloop cannot press one, points at the user
// instead of press-dialog-button.
func TestExplainBlockedByDialogAsksTheUserWhenNoButtonCanBePressed(t *testing.T) {
	cases := []struct {
		name     string
		dialog   statusDialog
		canPress bool
	}{
		{name: "dialog without buttons", dialog: statusDialog{Title: "Build Settings"}, canPress: true},
		{name: "system where uloop cannot press", dialog: commandDialogPlayerStoppedDialog(), canPress: false},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			probe := &commandDialogProbe{report: blockedByDialogStatusReport(testCase.dialog)}

			cliError := classifyCommandDialogError(explainCommandDialogBlock(commandDialogMainThreadBlockedRPCError(commandDialogBlockCommand), probe.deps(testCase.canPress)))

			assertCommandBlockedByDialog(t, cliError, testCase.dialog.Title)
			if !strings.HasPrefix(cliError.NextActions[0], "uloop cannot press a button in this dialog") {
				t.Fatalf("first NextAction = %q, want the ask-the-user guidance", cliError.NextActions[0])
			}
		})
	}
}

// Verifies the guidance follows the frontmost dialog when several are open.
func TestExplainBlockedByDialogGuidesByTheFrontmostDialog(t *testing.T) {
	frontmost := commandDialogPlayerStoppedDialog()
	behind := statusDialog{Title: "Build Settings"}
	probe := &commandDialogProbe{report: blockedByDialogStatusReport(frontmost, behind)}

	cliError := classifyCommandDialogError(explainCommandDialogBlock(commandDialogMainThreadBlockedRPCError(commandDialogBlockCommand), probe.deps(true)))

	if !strings.HasPrefix(cliError.Message, "'"+commandDialogBlockCommand+"' was not run: 2 modal dialogs are open in Unity") {
		t.Fatalf("Message = %q, want the two-dialog sentence", cliError.Message)
	}
	if !strings.Contains(cliError.NextActions[0], "uloop press-dialog-button") {
		t.Fatalf("first NextAction = %q, want the press guidance of the frontmost dialog", cliError.NextActions[0])
	}
}

// Verifies the original main_thread_blocked error is kept unchanged, byte for byte in its JSON, whenever
// the probe does not report a dialog: none found, the probe failed, or the Editor is in another state.
func TestExplainBlockedByDialogKeepsTheOriginalErrorWithoutADialog(t *testing.T) {
	dialogCheckFailed := newStatusReport(statusStateMainThreadBlocked, "blocked")
	dialogCheckFailed.DialogCheckError = "osascript failed"
	cases := []struct {
		name  string
		probe *commandDialogProbe
	}{
		{name: "BlockedByDialog without dialogs", probe: &commandDialogProbe{report: blockedByDialogStatusReport()}},
		{name: "probe failed", probe: &commandDialogProbe{err: errors.New("status probe failed")}},
		{name: "main thread blocked without a dialog", probe: &commandDialogProbe{report: newStatusReport(statusStateMainThreadBlocked, "blocked")}},
		{name: "dialog check failed", probe: &commandDialogProbe{report: dialogCheckFailed}},
		{name: "Unity not running", probe: &commandDialogProbe{report: newStatusReport(statusStateNotRunning, "not running")}},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			original := commandDialogMainThreadBlockedRPCError(commandDialogBlockCommand)

			got := explainCommandDialogBlock(original, testCase.probe.deps(true))

			if got != error(original) {
				t.Fatalf("error = %#v, want the original error", got)
			}
			assertSameErrorJSON(t, got, original)
		})
	}
}

func assertSameErrorJSON(t *testing.T, got error, want error) {
	t.Helper()
	var gotJSON bytes.Buffer
	var wantJSON bytes.Buffer
	context := clierrors.ErrorContext{ProjectRoot: "<PROJECT_ROOT>", Command: commandDialogBlockCommand}
	clierrors.WriteClassifiedError(&gotJSON, got, context)
	clierrors.WriteClassifiedError(&wantJSON, want, context)
	if gotJSON.String() != wantJSON.String() {
		t.Fatalf("error JSON changed:\n%s\nwant:\n%s", gotJSON.String(), wantJSON.String())
	}
}

// Verifies the status probe never runs for an error other than main_thread_blocked, so the busy resend
// loop and connection failures do not start a dialog look.
func TestExplainBlockedByDialogDoesNotProbeForOtherErrors(t *testing.T) {
	cases := []struct {
		name string
		err  error
	}{
		{name: "busy", err: &unityipc.RPCError{Code: -32603, Message: "busy", Data: json.RawMessage(`{"type":"server_busy"}`)}},
		{name: "other RPC error", err: &unityipc.RPCError{Code: -32603, Message: "tool exploded in Unity"}},
		{name: "connection error", err: errors.New("connection refused")},
		{name: "no error", err: nil},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			probe := &commandDialogProbe{report: blockedByDialogStatusReport(commandDialogPlayerStoppedDialog())}

			got := explainCommandDialogBlock(testCase.err, probe.deps(true))

			if probe.calls != 0 {
				t.Fatalf("status probe ran %d times, want 0", probe.calls)
			}
			if got != testCase.err {
				t.Fatalf("error = %#v, want it unchanged", got)
			}
		})
	}
}

// Verifies callers that recover by the error's type still find Unity's RPC error inside the dialog error.
func TestExplainBlockedByDialogKeepsTheRPCErrorReachable(t *testing.T) {
	original := commandDialogMainThreadBlockedRPCError(commandDialogBlockCommand)
	probe := &commandDialogProbe{report: blockedByDialogStatusReport(commandDialogPlayerStoppedDialog())}

	got := explainCommandDialogBlock(original, probe.deps(true))

	var rpcErr *unityipc.RPCError
	if !errors.As(got, &rpcErr) || rpcErr != original {
		t.Fatalf("errors.As found %#v, want the original RPC error", rpcErr)
	}
}

// Verifies every path a command takes writes BLOCKED_BY_DIALOG when Unity answers main_thread_blocked
// and the Editor reports a dialog, not only the plain tool path.
func TestRunToolReportsBlockedByDialogOnEveryPath(t *testing.T) {
	cases := []struct {
		name    string
		command string
		params  map[string]any
	}{
		{name: "plain tool", command: commandDialogBlockCommand, params: map[string]any{}},
		{name: "execute-dynamic-code", command: clicore.ExecuteDynamicCodeCommandName, params: map[string]any{clicore.DomainReloadWaitParam: true}},
		{name: "compile", command: clicore.CompileCommandName, params: map[string]any{}},
		{name: "control-play-mode", command: controlPlayModeCommandName, params: map[string]any{controlPlayModeActionParam: "Play"}},
		{name: "run-tests waiting for the domain reload", command: clicore.RunTestsCommandName, params: map[string]any{
			runTestsRespectEnterPlayModeSettingsParam: true,
			runTestsTestModeParam:                     "PlayMode",
		}},
		{name: "hot-reload", command: hotReloadCommandName, params: map[string]any{}},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			useRunningUnityProcess(t)
			listener := newLoopbackIpcListener(t)
			steps := []scriptedIPCStep{
				{method: testCase.command, rpcError: commandDialogMainThreadBlockedRPCErrorJSON(testCase.command)},
				{method: editorStatusBridgeCommandName, result: commandDialogStatusWithDialog},
			}
			requests := make(chan scriptedIPCRequest, len(steps)+1)
			serverErr := make(chan error, len(steps)+1)
			go serveScriptedIPCResponses(listener, steps, requests, serverErr)
			connection := unityipc.Connection{
				Endpoint:    unityipc.Endpoint{Network: listener.Addr().Network(), Address: listener.Addr().String()},
				ProjectRoot: writeFakeUnityProject(t),
			}
			var stdout bytes.Buffer
			var stderr bytes.Buffer

			code := runTool(context.Background(), connection, testCase.command, testCase.params, &stdout, &stderr)

			select {
			case err := <-serverErr:
				t.Fatalf("fake Unity server failed: %v", err)
			default:
			}
			if code != 1 {
				t.Fatalf("exit code = %d, want 1\nstderr=%s", code, stderr.String())
			}
			cliError := hotReloadErrorEnvelope(t, stderr.String()).Error
			if cliError.ErrorCode != clierrors.ErrorCodeBlockedByDialog {
				t.Fatalf("ErrorCode = %q, want %q\nstderr=%s", cliError.ErrorCode, clierrors.ErrorCodeBlockedByDialog, stderr.String())
			}
		})
	}
}

// Verifies a dialog whose title contains words the transport checks look for in error text (such as
// "EOF") does not make the dialog error read as a lost connection, which would hide the dialog and
// send compile and the other waiting paths into their disconnect waits.
func TestBlockedByDialogErrorDoesNotReadAsADisconnect(t *testing.T) {
	dialog := statusDialog{Title: "Unexpected EOF while reading the scene", Buttons: []string{"OK"}}
	cases := []struct {
		name string
		err  error
	}{
		{name: "command not run", err: explainCommandDialogBlock(
			commandDialogMainThreadBlockedRPCError(commandDialogBlockCommand),
			(&commandDialogProbe{report: blockedByDialogStatusReport(dialog)}).deps(true))},
		{name: "hot-reload wait", err: hotReloadWaitBlockedByDialog([]statusDialog{dialog}, true)},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			if clierrors.IsTransportDisconnectError(testCase.err) {
				t.Fatalf("the dialog error reads as a disconnect: %q", testCase.err.Error())
			}
			var stderr bytes.Buffer
			clierrors.WriteToolFailure(&stderr, testCase.err, unityipc.UnitySendOutcome{RequestDispatched: true, RequestAccepted: true},
				clierrors.ErrorContext{ProjectRoot: "<PROJECT_ROOT>", Command: commandDialogBlockCommand})
			cliError := hotReloadErrorEnvelope(t, stderr.String()).Error
			if cliError.ErrorCode != clierrors.ErrorCodeBlockedByDialog || !strings.Contains(cliError.Message, dialog.Title) {
				t.Fatalf("ErrorCode = %q, Message = %q, want BLOCKED_BY_DIALOG naming the dialog", cliError.ErrorCode, cliError.Message)
			}
		})
	}
}
