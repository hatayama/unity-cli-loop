package projectrunner

import (
	"context"
	"encoding/json"
	"errors"
	"reflect"
	"testing"
)

var statusTestBlockedByDialogNextActions = []any{
	"Read the dialog's Title, Message, and Buttons, then press the button that fits what the user asked for: uloop press-dialog-button --label \"<button>\".",
	"Ask the user first when no button clearly fits or the choice would discard work (such as \"Don't Save\").",
	"After the dialog is closed, run uloop status again.",
}

var statusTestUnanswerableDialogNextActions = []any{
	"uloop cannot press a button in this dialog. Show the user its Title and Message and ask them to answer it in Unity.",
	"After the dialog is closed, run uloop status again.",
}

var statusTestSaveSceneDialog = statusDialog{
	Title:   "Save scene?",
	Message: "Your changes will be lost.",
	Buttons: []string{"Save", "Don't Save", "Cancel"},
}

const statusTestSaveSceneMessage = `A modal dialog "Save scene?" is open in Unity; its main thread waits until it is closed.`

// Verifies a dialog the Editor reports makes an otherwise idle Editor BlockedByDialog, with the
// dialog's content, without a second probe or a look at Unity's windows.
func TestRunStatusCommandReportsBlockedByDialogFromEditor(t *testing.T) {
	projectRoot := writeFakeUnityProject(t)
	server := startFakeUnityResultServer(t, projectRoot, editorStatusBridgeCommandName, editorStatusResultJSON(t, map[string]any{
		"OpenDialogs": editorDialogsJSON(statusTestSaveSceneDialog),
	}))
	inspector := &fakeDialogInspector{}

	run := runStatusForTest(context.Background(), server.connection, nil, withDialogInspector(statusTestDeps(t), inspector))

	report := assertStatusReport(t, run, "BlockedByDialog")
	assertStatusFields(t, report, append(append([]string{}, statusTestEditorStateFields...), "Dialogs")...)
	assertStatusMessage(t, report, statusTestSaveSceneMessage)
	assertStatusNextActions(t, report, statusTestBlockedByDialogNextActions)
	assertStatusDialogs(t, run, []statusDialog{statusTestSaveSceneDialog})
	assertStatusRequestCount(t, server, 1)
	assertDialogInspections(t, inspector, 0)
}

// Verifies a dialog the Editor reports wins over a running command: the state is BlockedByDialog,
// the running command is still shown, and uloop cancel is not offered.
func TestRunStatusCommandReportsBlockedByDialogWhileBusy(t *testing.T) {
	projectRoot := writeFakeUnityProject(t)
	server := startFakeUnityResultServer(t, projectRoot, editorStatusBridgeCommandName, editorStatusResultJSON(t,
		busyEditorStatusOverrides(0.1, editorDialogsJSON(statusTestSaveSceneDialog))))
	inspector := &fakeDialogInspector{}

	run := runStatusForTest(context.Background(), server.connection, nil, withDialogInspector(statusTestDeps(t), inspector))

	assertBusyBlockedByDialogReport(t, run, []statusDialog{statusTestSaveSceneDialog})
	assertStatusRequestCount(t, server, 1)
	assertDialogInspections(t, inspector, 0)
}

// Verifies a dialog the Editor reports while a command runs and the main thread has stalled is
// reported from the Editor alone: one probe, and no look at Unity's windows.
func TestRunStatusCommandReportsEditorDialogWhileBusyAndStalled(t *testing.T) {
	projectRoot := writeFakeUnityProject(t)
	server := startFakeUnityResultServer(t, projectRoot, editorStatusBridgeCommandName, editorStatusResultJSON(t,
		busyEditorStatusOverrides(30, editorDialogsJSON(statusTestSaveSceneDialog))))
	inspector := &fakeDialogInspector{}

	run := runStatusForTest(context.Background(), server.connection, nil, withDialogInspector(statusTestDeps(t), inspector))

	assertBusyBlockedByDialogReport(t, run, []statusDialog{statusTestSaveSceneDialog})
	assertStatusRequestCount(t, server, 1)
	assertDialogInspections(t, inspector, 0)
}

// Verifies a dialog the Editor reports with a stalled main thread is BlockedByDialog at once,
// without the second probe a blocked main thread would get.
func TestRunStatusCommandReportsEditorDialogWithoutReprobe(t *testing.T) {
	projectRoot := writeFakeUnityProject(t)
	server := startFakeUnityResultServer(t, projectRoot, editorStatusBridgeCommandName, editorStatusResultJSON(t, map[string]any{
		"SecondsSinceLastMainThreadTick": 12.0,
		"OpenDialogs":                    editorDialogsJSON(statusTestSaveSceneDialog),
	}))
	inspector := &fakeDialogInspector{}

	run := runStatusForTest(context.Background(), server.connection, nil, withDialogInspector(statusTestDeps(t), inspector))

	report := assertStatusReport(t, run, "BlockedByDialog")
	assertStatusValue(t, report, "SecondsSinceLastMainThreadTick", 12.0)
	assertStatusDialogs(t, run, []statusDialog{statusTestSaveSceneDialog})
	assertStatusRequestCount(t, server, 1)
	assertDialogInspections(t, inspector, 0)
}

// Verifies a dialog the Editor reports only in the second answer is taken from that answer,
// without a look at Unity's windows.
func TestRunStatusCommandReportsEditorDialogFromSecondProbe(t *testing.T) {
	projectRoot := writeFakeUnityProject(t)
	server := startFakeEditorStatusSequenceServer(t, projectRoot,
		editorStatusRPCResponse(editorStatusResultJSON(t, map[string]any{"SecondsSinceLastMainThreadTick": 30})),
		editorStatusRPCResponse(editorStatusResultJSON(t, map[string]any{
			"SecondsSinceLastMainThreadTick": 31,
			"OpenDialogs":                    editorDialogsJSON(statusTestSaveSceneDialog),
		})),
	)
	inspector := &fakeDialogInspector{}

	run := runStatusForTest(context.Background(), server.connection, nil, withDialogInspector(statusTestDeps(t), inspector))

	assertStatusReport(t, run, "BlockedByDialog")
	assertStatusDialogs(t, run, []statusDialog{statusTestSaveSceneDialog})
	assertStatusRequestCount(t, server, 2)
	assertDialogInspections(t, inspector, 0)
}

// Verifies a dialog the Editor reports wins over an Editor that has not recorded its state yet;
// only the stall seconds and the dialogs are printed.
func TestRunStatusCommandReportsBlockedByDialogBeforeStarting(t *testing.T) {
	projectRoot := writeFakeUnityProject(t)
	server := startFakeUnityResultServer(t, projectRoot, editorStatusBridgeCommandName, editorStatusResultJSON(t, map[string]any{
		"HasEditorState": false,
		"OpenDialogs":    editorDialogsJSON(statusTestSaveSceneDialog),
	}))
	inspector := &fakeDialogInspector{}

	run := runStatusForTest(context.Background(), server.connection, nil, withDialogInspector(statusTestDeps(t), inspector))

	report := assertStatusReport(t, run, "BlockedByDialog")
	assertStatusFields(t, report, "SecondsSinceLastMainThreadTick", "Dialogs")
	assertStatusDialogs(t, run, []statusDialog{statusTestSaveSceneDialog})
	assertDialogInspections(t, inspector, 0)
}

// Verifies every dialog the Editor reports is printed in order, and the message counts them.
func TestRunStatusCommandReportsEveryOpenDialog(t *testing.T) {
	projectRoot := writeFakeUnityProject(t)
	second := statusDialog{Title: "Script Updating Consent", Message: "Update the API?", Buttons: []string{"Go Ahead", "No Thanks"}}
	server := startFakeUnityResultServer(t, projectRoot, editorStatusBridgeCommandName, editorStatusResultJSON(t, map[string]any{
		"OpenDialogs": editorDialogsJSON(statusTestSaveSceneDialog, second),
	}))
	inspector := &fakeDialogInspector{}

	run := runStatusForTest(context.Background(), server.connection, nil, withDialogInspector(statusTestDeps(t), inspector))

	report := assertStatusReport(t, run, "BlockedByDialog")
	assertStatusMessage(t, report, "2 modal dialogs are open in Unity; its main thread waits until they are closed.")
	assertStatusDialogs(t, run, []statusDialog{statusTestSaveSceneDialog, second})
	assertDialogInspections(t, inspector, 0)
}

// Verifies a running command whose main thread still runs is Busy without a look at Unity's windows.
func TestRunStatusCommandDoesNotInspectWhileBusyAndTicking(t *testing.T) {
	projectRoot := writeFakeUnityProject(t)
	server := startFakeUnityResultServer(t, projectRoot, editorStatusBridgeCommandName, editorStatusResultJSON(t,
		busyEditorStatusOverrides(0.1, []any{})))
	inspector := &fakeDialogInspector{dialogs: []statusDialog{statusTestSaveSceneDialog}}

	run := runStatusForTest(context.Background(), server.connection, nil, withDialogInspector(statusTestDeps(t), inspector))

	assertStatusReport(t, run, "Busy")
	assertDialogInspections(t, inspector, 0)
}

// Verifies a dialog found in Unity's windows while a command runs and the main thread has stalled
// turns Busy into BlockedByDialog, keeping the running command and leaving out uloop cancel.
func TestRunStatusCommandReportsInspectedDialogWhileBusy(t *testing.T) {
	projectRoot := writeFakeUnityProject(t)
	server := startFakeUnityResultServer(t, projectRoot, editorStatusBridgeCommandName, editorStatusResultJSON(t,
		busyEditorStatusOverrides(30, []any{})))
	inspector := &fakeDialogInspector{dialogs: []statusDialog{statusTestSaveSceneDialog}}

	run := runStatusForTest(context.Background(), server.connection, nil, withDialogInspector(statusTestDeps(t), inspector))

	assertBusyBlockedByDialogReport(t, run, []statusDialog{statusTestSaveSceneDialog})
	assertStatusRequestCount(t, server, 1)
	assertDialogInspections(t, inspector, 1)
}

// Verifies a stalled running command stays Busy when Unity's windows show no dialog.
func TestRunStatusCommandKeepsBusyWhenNoDialogIsFound(t *testing.T) {
	projectRoot := writeFakeUnityProject(t)
	server := startFakeUnityResultServer(t, projectRoot, editorStatusBridgeCommandName, editorStatusResultJSON(t,
		busyEditorStatusOverrides(30, []any{})))
	inspector := &fakeDialogInspector{dialogs: []statusDialog{}}

	run := runStatusForTest(context.Background(), server.connection, nil, withDialogInspector(statusTestDeps(t), inspector))

	report := assertStatusReport(t, run, "Busy")
	assertStatusFields(t, report, append(append([]string{}, statusTestEditorStateFields...), statusTestRunningToolFields...)...)
	assertDialogInspections(t, inspector, 1)
}

// Verifies a stalled running command stays Busy when Unity's windows cannot be read, with the
// reason and how to grant Accessibility access added.
func TestRunStatusCommandReportsDialogCheckErrorWhileBusy(t *testing.T) {
	projectRoot := writeFakeUnityProject(t)
	server := startFakeUnityResultServer(t, projectRoot, editorStatusBridgeCommandName, editorStatusResultJSON(t,
		busyEditorStatusOverrides(30, []any{})))
	inspector := &fakeDialogInspector{err: errors.New("osascript is not allowed assistive access")}

	run := runStatusForTest(context.Background(), server.connection, nil, withDialogInspector(statusTestDeps(t), inspector))

	report := assertStatusReport(t, run, "Busy")
	assertStatusFields(t, report,
		append(append(append([]string{}, statusTestEditorStateFields...), statusTestRunningToolFields...), "DialogCheckError")...)
	assertDialogCheckError(t, report, "osascript is not allowed assistive access")
	assertDialogInspections(t, inspector, 1)
}

// Verifies a dialog found in Unity's windows turns a blocked main thread into BlockedByDialog
// after the second probe.
func TestRunStatusCommandReportsInspectedDialogWhenMainThreadIsBlocked(t *testing.T) {
	projectRoot := writeFakeUnityProject(t)
	server := startBlockedMainThreadStatusServer(t, projectRoot, editorStatusResultJSON(t, map[string]any{
		"SecondsSinceLastMainThreadTick": 31,
	}))
	found := statusDialog{Title: "Spike pick", Message: "", Buttons: []string{}}
	inspector := &fakeDialogInspector{dialogs: []statusDialog{found}}

	run := runStatusForTest(context.Background(), server.connection, nil, withDialogInspector(statusTestDeps(t), inspector))

	report := assertStatusReport(t, run, "BlockedByDialog")
	assertStatusFields(t, report, append(append([]string{}, statusTestEditorStateFields...), "Dialogs")...)
	assertStatusMessage(t, report, `A modal dialog "Spike pick" is open in Unity; its main thread waits until it is closed.`)
	assertStatusDialogs(t, run, []statusDialog{found})
	assertStatusRequestCount(t, server, 2)
	assertDialogInspections(t, inspector, 1)
}

// Verifies a blocked main thread stays MainThreadBlocked when Unity's windows show no dialog.
func TestRunStatusCommandKeepsMainThreadBlockedWhenNoDialogIsFound(t *testing.T) {
	projectRoot := writeFakeUnityProject(t)
	server := startBlockedMainThreadStatusServer(t, projectRoot, editorStatusResultJSON(t, map[string]any{
		"SecondsSinceLastMainThreadTick": 31,
	}))
	inspector := &fakeDialogInspector{dialogs: []statusDialog{}}

	run := runStatusForTest(context.Background(), server.connection, nil, withDialogInspector(statusTestDeps(t), inspector))

	report := assertStatusReport(t, run, "MainThreadBlocked")
	assertStatusFields(t, report, statusTestEditorStateFields...)
	assertDialogInspections(t, inspector, 1)
}

// Verifies a blocked main thread stays MainThreadBlocked when Unity's windows cannot be read,
// with the reason and how to grant Accessibility access added.
func TestRunStatusCommandReportsDialogCheckErrorWhenMainThreadIsBlocked(t *testing.T) {
	projectRoot := writeFakeUnityProject(t)
	server := startBlockedMainThreadStatusServer(t, projectRoot, editorStatusResultJSON(t, map[string]any{
		"SecondsSinceLastMainThreadTick": 31,
	}))
	inspector := &fakeDialogInspector{err: errors.New("osascript did not answer within 5s")}

	run := runStatusForTest(context.Background(), server.connection, nil, withDialogInspector(statusTestDeps(t), inspector))

	report := assertStatusReport(t, run, "MainThreadBlocked")
	assertStatusFields(t, report, append(append([]string{}, statusTestEditorStateFields...), "DialogCheckError")...)
	assertDialogCheckError(t, report, "osascript did not answer within 5s")
	assertDialogInspections(t, inspector, 1)
}

// Verifies an Editor whose package predates OpenDialogs still gets a look at Unity's windows when
// its main thread is blocked.
func TestRunStatusCommandInspectsWhenEditorDoesNotReportDialogs(t *testing.T) {
	projectRoot := writeFakeUnityProject(t)
	server := startBlockedMainThreadStatusServer(t, projectRoot, editorStatusResultJSON(t, map[string]any{
		"SecondsSinceLastMainThreadTick": 31,
		"OpenDialogs":                    omittedEditorStatusField{},
	}))
	inspector := &fakeDialogInspector{dialogs: []statusDialog{statusTestSaveSceneDialog}}

	run := runStatusForTest(context.Background(), server.connection, nil, withDialogInspector(statusTestDeps(t), inspector))

	assertStatusReport(t, run, "BlockedByDialog")
	assertStatusDialogs(t, run, []statusDialog{statusTestSaveSceneDialog})
	assertDialogInspections(t, inspector, 1)
}

// Verifies a dialog found in Unity's windows while Unity runs without its server, as with the
// Safe Mode prompt at startup, is BlockedByDialog and keeps the process and connection fields.
func TestRunStatusCommandReportsInspectedDialogWhenServerIsUnavailable(t *testing.T) {
	projectRoot := writeFakeUnityProject(t)
	inspector := &fakeDialogInspector{dialogs: []statusDialog{statusTestSaveSceneDialog}}
	deps := withDialogInspector(statusTestDeps(t), inspector)
	deps.isUnityProcessRunning = fakeUnityProcessLookup(t, projectRoot, true, nil)

	run := runStatusForTest(context.Background(), unreachableConnection(projectRoot), nil, deps)

	report := assertStatusReport(t, run, "BlockedByDialog")
	assertStatusFields(t, report, "ConnectionError", "UnityProcessRunning", "Dialogs")
	assertStatusValue(t, report, "UnityProcessRunning", true)
	assertStatusMessage(t, report, statusTestSaveSceneMessage)
	assertStatusNextActions(t, report, statusTestBlockedByDialogNextActions)
	assertStatusDialogs(t, run, []statusDialog{statusTestSaveSceneDialog})
	assertDialogInspections(t, inspector, 1)
}

// Verifies Unity running without its server stays ServerUnavailable when its windows show no dialog.
func TestRunStatusCommandKeepsServerUnavailableWhenNoDialogIsFound(t *testing.T) {
	projectRoot := writeFakeUnityProject(t)
	inspector := &fakeDialogInspector{dialogs: []statusDialog{}}
	deps := withDialogInspector(statusTestDeps(t), inspector)
	deps.isUnityProcessRunning = fakeUnityProcessLookup(t, projectRoot, true, nil)

	run := runStatusForTest(context.Background(), unreachableConnection(projectRoot), nil, deps)

	report := assertStatusReport(t, run, "ServerUnavailable")
	assertStatusFields(t, report, "ConnectionError", "UnityProcessRunning")
	assertDialogInspections(t, inspector, 1)
}

// Verifies Unity running without its server stays ServerUnavailable when its windows cannot be
// read, with the reason and the hint added.
func TestRunStatusCommandReportsDialogCheckErrorWhenServerIsUnavailable(t *testing.T) {
	projectRoot := writeFakeUnityProject(t)
	inspector := &fakeDialogInspector{err: errors.New("osascript is not allowed assistive access")}
	deps := withDialogInspector(statusTestDeps(t), inspector)
	deps.isUnityProcessRunning = fakeUnityProcessLookup(t, projectRoot, true, nil)

	run := runStatusForTest(context.Background(), unreachableConnection(projectRoot), nil, deps)

	report := assertStatusReport(t, run, "ServerUnavailable")
	assertStatusFields(t, report, "ConnectionError", "UnityProcessRunning", "DialogCheckError")
	assertDialogCheckError(t, report, "osascript is not allowed assistive access")
	assertDialogInspections(t, inspector, 1)
}

// Verifies a closed Editor is NotRunning without a look for dialogs.
func TestRunStatusCommandDoesNotInspectWhenUnityIsNotRunning(t *testing.T) {
	projectRoot := writeFakeUnityProject(t)
	inspector := &fakeDialogInspector{dialogs: []statusDialog{statusTestSaveSceneDialog}}
	deps := withDialogInspector(statusTestDeps(t), inspector)
	deps.isUnityProcessRunning = fakeUnityProcessLookup(t, projectRoot, false, nil)

	run := runStatusForTest(context.Background(), unreachableConnection(projectRoot), nil, deps)

	assertStatusReport(t, run, "NotRunning")
	assertDialogInspections(t, inspector, 0)
}

// fakeDialogInspector returns fixed dialogs or an error and counts how often status looked.
type fakeDialogInspector struct {
	dialogs []statusDialog
	err     error
	calls   int
}

func (inspector *fakeDialogInspector) inspect(context.Context, string) ([]statusDialog, error) {
	inspector.calls++
	return inspector.dialogs, inspector.err
}

func withDialogInspector(deps statusCommandDeps, inspector *fakeDialogInspector) statusCommandDeps {
	deps.inspectDialogs = inspector.inspect
	return deps
}

func assertDialogInspections(t *testing.T, inspector *fakeDialogInspector, want int) {
	t.Helper()
	if inspector.calls != want {
		t.Fatalf("dialog inspections = %d, want %d", inspector.calls, want)
	}
}

// startBlockedMainThreadStatusServer answers a blocked first probe and then the given second answer.
func startBlockedMainThreadStatusServer(t *testing.T, projectRoot string, secondResult string) fakeUnityServer {
	t.Helper()
	return startFakeEditorStatusSequenceServer(t, projectRoot,
		editorStatusRPCResponse(editorStatusResultJSON(t, map[string]any{"SecondsSinceLastMainThreadTick": 30})),
		editorStatusRPCResponse(secondResult),
	)
}

// Verifies a dialog the Editor reports is not offered to uloop press-dialog-button where uloop
// cannot press buttons; the user is asked to answer it instead.
func TestRunStatusCommandAsksTheUserForAnEditorDialogWhereUloopCannotPress(t *testing.T) {
	projectRoot := writeFakeUnityProject(t)
	server := startFakeUnityResultServer(t, projectRoot, editorStatusBridgeCommandName, editorStatusResultJSON(t, map[string]any{
		"OpenDialogs": editorDialogsJSON(statusTestSaveSceneDialog),
	}))
	deps := withDialogInspector(statusTestDeps(t), &fakeDialogInspector{})
	deps.canAnswerDialogs = false

	run := runStatusForTest(context.Background(), server.connection, nil, deps)

	report := assertStatusReport(t, run, "BlockedByDialog")
	assertStatusNextActions(t, report, statusTestUnanswerableDialogNextActions)
}

// Verifies a dialog found in Unity's windows behind a blocked main thread is not offered to
// uloop press-dialog-button where uloop cannot press buttons.
func TestRunStatusCommandAsksTheUserForAnInspectedDialogWhereUloopCannotPress(t *testing.T) {
	projectRoot := writeFakeUnityProject(t)
	server := startBlockedMainThreadStatusServer(t, projectRoot, editorStatusResultJSON(t, map[string]any{
		"SecondsSinceLastMainThreadTick": 31,
		"OpenDialogs":                    omittedEditorStatusField{},
	}))
	deps := withDialogInspector(statusTestDeps(t), &fakeDialogInspector{dialogs: []statusDialog{statusTestSaveSceneDialog}})
	deps.canAnswerDialogs = false

	run := runStatusForTest(context.Background(), server.connection, nil, deps)

	report := assertStatusReport(t, run, "BlockedByDialog")
	assertStatusNextActions(t, report, statusTestUnanswerableDialogNextActions)
}

// Verifies a dialog found in Unity's windows while its server is unavailable is not offered to
// uloop press-dialog-button where uloop cannot press buttons.
func TestRunStatusCommandAsksTheUserForADialogBeforeTheServerWhereUloopCannotPress(t *testing.T) {
	projectRoot := writeFakeUnityProject(t)
	deps := withDialogInspector(statusTestDeps(t), &fakeDialogInspector{dialogs: []statusDialog{statusTestSaveSceneDialog}})
	deps.isUnityProcessRunning = fakeUnityProcessLookup(t, projectRoot, true, nil)
	deps.canAnswerDialogs = false

	run := runStatusForTest(context.Background(), unreachableConnection(projectRoot), nil, deps)

	report := assertStatusReport(t, run, "BlockedByDialog")
	assertStatusNextActions(t, report, statusTestUnanswerableDialogNextActions)
}

// Verifies a dialog without readable buttons, such as a modal Editor window, is not offered to
// uloop press-dialog-button even where uloop can press buttons.
func TestRunStatusCommandAsksTheUserForADialogWithoutButtons(t *testing.T) {
	projectRoot := writeFakeUnityProject(t)
	server := startFakeUnityResultServer(t, projectRoot, editorStatusBridgeCommandName, editorStatusResultJSON(t, map[string]any{
		"OpenDialogs": editorDialogsJSON(statusDialog{Title: "Probe modal", Buttons: []string{}}),
	}))
	deps := withDialogInspector(statusTestDeps(t), &fakeDialogInspector{})

	run := runStatusForTest(context.Background(), server.connection, nil, deps)

	report := assertStatusReport(t, run, "BlockedByDialog")
	assertStatusNextActions(t, report, statusTestUnanswerableDialogNextActions)
}

// editorDialogsJSON spells dialogs the way the Editor sends OpenDialogs.
func editorDialogsJSON(dialogs ...statusDialog) []any {
	encoded := make([]any, 0, len(dialogs))
	for _, dialog := range dialogs {
		encoded = append(encoded, map[string]any{"Title": dialog.Title, "Message": dialog.Message, "Buttons": dialog.Buttons})
	}
	return encoded
}

// busyEditorStatusOverrides describes execute-dynamic-code holding the slot, which uloop cancel
// can stop, so a missing cancel action shows the dialog took over.
func busyEditorStatusOverrides(stallSeconds float64, openDialogs []any) map[string]any {
	return map[string]any{
		"IsBusy":                         true,
		"RunningToolName":                "execute-dynamic-code",
		"RunningToolElapsedSeconds":      8,
		"RunningToolPhase":               "Executing",
		"SecondsSinceLastMainThreadTick": stallSeconds,
		"OpenDialogs":                    openDialogs,
	}
}

func assertBusyBlockedByDialogReport(t *testing.T, run statusRun, wantDialogs []statusDialog) {
	t.Helper()
	report := assertStatusReport(t, run, "BlockedByDialog")
	assertStatusFields(t, report,
		append(append(append([]string{}, statusTestEditorStateFields...), statusTestRunningToolFields...), "Dialogs")...)
	assertStatusValue(t, report, "RunningToolName", "execute-dynamic-code")
	assertStatusNextActions(t, report, statusTestBlockedByDialogNextActions)
	assertStatusDialogs(t, run, wantDialogs)
}

// assertStatusNextActions compares the whole list; assertStatusValue compares with != and slices
// are not comparable.
func assertStatusNextActions(t *testing.T, report map[string]any, want []any) {
	t.Helper()
	if !reflect.DeepEqual(report["NextActions"], want) {
		t.Fatalf("NextActions = %#v, want %#v", report["NextActions"], want)
	}
}

func assertStatusDialogs(t *testing.T, run statusRun, want []statusDialog) {
	t.Helper()
	var printed struct {
		Dialogs []statusDialog `json:"Dialogs"`
	}
	if err := json.Unmarshal([]byte(run.stdout), &printed); err != nil {
		t.Fatalf("stdout is not one JSON object: %v\n%s", err, run.stdout)
	}
	if !reflect.DeepEqual(printed.Dialogs, want) {
		t.Fatalf("Dialogs = %#v, want %#v", printed.Dialogs, want)
	}
}

// assertDialogCheckError checks the reason and that the hint about the failed look comes last.
func assertDialogCheckError(t *testing.T, report map[string]any, want string) {
	t.Helper()
	assertStatusValue(t, report, "DialogCheckError", want)
	nextActions, _ := report["NextActions"].([]any)
	if len(nextActions) == 0 || nextActions[len(nextActions)-1] != statusTestDialogCheckNextAction {
		t.Fatalf("NextActions must end with the hint about the failed look: %#v", report["NextActions"])
	}
}
