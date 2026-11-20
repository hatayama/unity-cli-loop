package projectrunner

import (
	"context"
	"encoding/json"
	"reflect"
	"strings"
	"testing"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
)

const (
	hotReloadDialogStatusMessageBox = `{"IsBusy":false,"HasEditorState":true,"IsCompiling":false,"IsUpdating":false,` +
		`"SecondsSinceLastMainThreadTick":12,"OpenDialogs":[{"Title":"Scene Has Been Modified",` +
		`"Message":"Do you want to save the changes?","Buttons":["Save","Don't Save","Cancel"]}]}`
	hotReloadDialogStatusWindowWithoutButtons = `{"IsBusy":false,"HasEditorState":true,"IsCompiling":false,"IsUpdating":false,` +
		`"SecondsSinceLastMainThreadTick":12,"OpenDialogs":[{"Title":"Build Settings","Message":"","Buttons":[]}]}`
	hotReloadDialogStatusBusyWithMessageBox = `{"IsBusy":true,"RunningToolName":"compile","RunningToolElapsedSeconds":11,` +
		`"RunningToolPhase":"WaitingForMainThread","HasEditorState":true,"IsCompiling":false,"IsUpdating":false,` +
		`"SecondsSinceLastMainThreadTick":12,"OpenDialogs":[{"Title":"Scene Has Been Modified",` +
		`"Message":"Do you want to save the changes?","Buttons":["Save","Don't Save","Cancel"]}]}`
	hotReloadDialogRunAgainLine = "After the dialog is closed, run uloop hot-reload again."
)

func hotReloadMessageBoxDialogs() []statusDialog {
	return []statusDialog{{
		Title:   "Scene Has Been Modified",
		Message: "Do you want to save the changes?",
		Buttons: []string{"Save", "Don't Save", "Cancel"},
	}}
}

// hotReloadErrorEnvelope reads the error envelope that follows the waiting line on stderr.
func hotReloadErrorEnvelope(t *testing.T, stderr string) clierrors.CLIErrorEnvelope {
	t.Helper()
	start := strings.Index(stderr, "{\n")
	if start < 0 {
		t.Fatalf("stderr carries no error envelope: %q", stderr)
	}
	var envelope clierrors.CLIErrorEnvelope
	if err := json.Unmarshal([]byte(stderr[start:]), &envelope); err != nil {
		t.Fatalf("stderr envelope is not valid JSON: %v\n%s", err, stderr)
	}
	return envelope
}

// assertBlockedByDialogError checks the envelope names the dialogs and is safe to retry.
func assertBlockedByDialogError(t *testing.T, stderr string, wantDialogs []statusDialog) clierrors.CLIError {
	t.Helper()
	cliError := hotReloadErrorEnvelope(t, stderr).Error
	if cliError.ErrorCode != clierrors.ErrorCodeBlockedByDialog {
		t.Fatalf("ErrorCode = %q, want %q\nstderr=%s", cliError.ErrorCode, clierrors.ErrorCodeBlockedByDialog, stderr)
	}
	if !cliError.SafeToRetry || !cliError.Retryable {
		t.Fatalf("SafeToRetry = %v, Retryable = %v, want both true", cliError.SafeToRetry, cliError.Retryable)
	}
	if !strings.HasPrefix(cliError.Message, blockedByDialogMessage(wantDialogs)) ||
		!strings.Contains(cliError.Message, "uloop hot-reload stopped waiting") {
		t.Fatalf("Message = %q, want the status sentence and the stopped-waiting sentence", cliError.Message)
	}
	encoded, err := json.Marshal(cliError.Details["Dialogs"])
	if err != nil {
		t.Fatalf("Details.Dialogs is not encodable: %v", err)
	}
	var gotDialogs []statusDialog
	if err := json.Unmarshal(encoded, &gotDialogs); err != nil || !reflect.DeepEqual(gotDialogs, wantDialogs) {
		t.Fatalf("Details.Dialogs = %s, want %#v", encoded, wantDialogs)
	}
	if len(cliError.NextActions) == 0 || cliError.NextActions[len(cliError.NextActions)-1] != hotReloadDialogRunAgainLine {
		t.Fatalf("NextActions = %#v, want the last line %q", cliError.NextActions, hotReloadDialogRunAgainLine)
	}
	return cliError
}

func assertPressGuidance(t *testing.T, nextActions []string) {
	t.Helper()
	if !strings.Contains(nextActions[0], "uloop press-dialog-button") {
		t.Fatalf("NextActions[0] = %q, want the press-dialog-button guidance", nextActions[0])
	}
}

func assertAskTheUserGuidance(t *testing.T, nextActions []string) {
	t.Helper()
	if !strings.Contains(nextActions[0], "ask them to answer it in Unity") || strings.Contains(strings.Join(nextActions, " "), "press-dialog-button") {
		t.Fatalf("NextActions = %#v, want the ask-the-user guidance without a press", nextActions)
	}
}

// assertSettleWaitStoppedOnDialog checks the settle wait ended with the first answer on stdout and
// no second apply.
func assertSettleWaitStoppedOnDialog(t *testing.T, run editorReadyRetryRun) {
	t.Helper()
	if run.code != 1 {
		t.Fatalf("exit code = %d, want 1\nstderr=%s", run.code, run.stderr)
	}
	if applies := run.hotReloadRequests(); len(applies) != 1 {
		t.Fatalf("hot-reload requests = %d, want only the first\nstderr=%s", len(applies), run.stderr)
	}
	if fields := run.stdoutFields(t); fields["Message"] != "refused" {
		t.Fatalf("stdout must be the first answer: %s", run.stdout)
	}
	if run.compileCalls != 0 {
		t.Fatalf("compile calls = %d, want 0", run.compileCalls)
	}
}

// Verifies the settle wait stops at once when the Editor reports a message box, reports it as
// BLOCKED_BY_DIALOG with the press guidance, and never sends the second apply.
func TestRunHotReloadStopsTheSettleWaitWhenTheEditorReportsADialog(t *testing.T) {
	options := fastEditorReadyWaitOptions()
	options.dialogWatch.canPressDialogs = true
	useFastEditorReadyWait(t, options)
	run := runScriptedHotReload(t, context.Background(), t.TempDir(), map[string]any{},
		[]scriptedIPCStep{
			{method: hotReloadCommandName, result: editorReadyRetryFirstRefused},
			{method: editorStatusBridgeCommandName, result: hotReloadDialogStatusMessageBox},
		},
		editorReadyRetryNoCompile())

	assertSettleWaitStoppedOnDialog(t, run)
	cliError := assertBlockedByDialogError(t, run.stderr, hotReloadMessageBoxDialogs())
	assertPressGuidance(t, cliError.NextActions)
}

// Verifies the settle wait also stops when the dialog appears only after a few compiling answers.
func TestRunHotReloadStopsTheSettleWaitWhenADialogOpensWhileCompiling(t *testing.T) {
	options := fastEditorReadyWaitOptions()
	options.dialogWatch.canPressDialogs = true
	useFastEditorReadyWait(t, options)
	run := runScriptedHotReload(t, context.Background(), t.TempDir(), map[string]any{},
		[]scriptedIPCStep{
			{method: hotReloadCommandName, result: editorReadyRetryFirstRefused},
			{method: editorStatusBridgeCommandName, result: editorReadyRetryStatusCompiling},
			{method: editorStatusBridgeCommandName, result: editorReadyRetryStatusCompiling},
			{method: editorStatusBridgeCommandName, result: hotReloadDialogStatusMessageBox},
		},
		editorReadyRetryNoCompile())

	assertSettleWaitStoppedOnDialog(t, run)
	cliError := assertBlockedByDialogError(t, run.stderr, hotReloadMessageBoxDialogs())
	assertPressGuidance(t, cliError.NextActions)
}

// Verifies a dialog without buttons stops the settle wait with the ask-the-user guidance, since
// there is no button to press.
func TestRunHotReloadStopsTheSettleWaitWithAskTheUserGuidanceForADialogWithoutButtons(t *testing.T) {
	options := fastEditorReadyWaitOptions()
	options.dialogWatch.canPressDialogs = true
	useFastEditorReadyWait(t, options)
	run := runScriptedHotReload(t, context.Background(), t.TempDir(), map[string]any{},
		[]scriptedIPCStep{
			{method: hotReloadCommandName, result: editorReadyRetryFirstRefused},
			{method: editorStatusBridgeCommandName, result: hotReloadDialogStatusWindowWithoutButtons},
		},
		editorReadyRetryNoCompile())

	assertSettleWaitStoppedOnDialog(t, run)
	cliError := assertBlockedByDialogError(t, run.stderr,
		[]statusDialog{{Title: "Build Settings", Message: "", Buttons: []string{}}})
	assertAskTheUserGuidance(t, cliError.NextActions)
}

// Verifies the busy wait stops at once when the Editor reports a dialog: stdout stays empty,
// stderr carries BLOCKED_BY_DIALOG, and the request is not sent again.
func TestRunHotReloadStopsTheBusyWaitWhenTheEditorReportsADialog(t *testing.T) {
	options := fastEditorReadyWaitOptions()
	options.dialogWatch.canPressDialogs = true
	useFastEditorReadyWait(t, options)
	run := runScriptedHotReload(t, context.Background(), t.TempDir(), busyWaitParams(),
		[]scriptedIPCStep{
			{method: hotReloadCommandName, rpcError: busyWaitRunningCompile},
			{method: editorStatusBridgeCommandName, result: hotReloadDialogStatusBusyWithMessageBox},
		},
		editorReadyRetryNoCompile())

	assertFailedWithEmptyStdout(t, run, clierrors.ErrorCodeBlockedByDialog)
	assertHotReloadRequestsUnchanged(t, run, 1)
	cliError := assertBlockedByDialogError(t, run.stderr, hotReloadMessageBoxDialogs())
	assertPressGuidance(t, cliError.NextActions)
}

// Verifies the error asks the user to answer the dialog where uloop cannot press a button, even
// when the dialog has buttons.
func TestHotReloadBlockedByDialogErrorAsksTheUserWhereUloopCannotPress(t *testing.T) {
	cliError := hotReloadWaitBlockedByDialog(hotReloadMessageBoxDialogs(), false).
		ToCLIError(clierrors.ErrorContext{Command: hotReloadCommandName})

	assertAskTheUserGuidance(t, cliError.NextActions)
	if last := cliError.NextActions[len(cliError.NextActions)-1]; last != hotReloadDialogRunAgainLine {
		t.Fatalf("last NextActions line = %q, want %q", last, hotReloadDialogRunAgainLine)
	}
}
