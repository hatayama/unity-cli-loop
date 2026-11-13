package projectrunner

import (
	"context"
	"errors"
	"testing"
)

// windowsStatusTestDeps is statusTestDeps as status runs on Windows, where it looks at Unity's
// windows on every answer that would let the reader send a command.
func windowsStatusTestDeps(t *testing.T, inspector *fakeDialogInspector) statusCommandDeps {
	t.Helper()
	deps := withDialogInspector(statusTestDeps(t), inspector)
	deps.goos = "windows"
	return deps
}

// startSingleEditorStatusServer answers exactly one get-editor-status request, so a recheck would
// run into the probe timeout and fail the request count.
func startSingleEditorStatusServer(t *testing.T, projectRoot string, overrides map[string]any) fakeUnityServer {
	t.Helper()
	return startFakeEditorStatusSequenceServer(t, projectRoot, editorStatusRPCResponse(editorStatusResultJSON(t, overrides)))
}

// Verifies that on Windows an answer that would let the reader send a command becomes
// BlockedByDialog when Unity's windows show a dialog, whatever the stall seconds, without asking
// the Editor again, and that the look is given the process ID the Editor reported.
func TestRunStatusCommandOnWindowsLooksAtWindowsRightAfterTheFirstAnswer(t *testing.T) {
	cases := map[string]map[string]any{
		"Ready":                   {"SecondsSinceLastMainThreadTick": 0.1},
		"Ready after short stall": {"SecondsSinceLastMainThreadTick": 2.0},
		"Compiling":               {"SecondsSinceLastMainThreadTick": 0.1, "IsCompiling": true},
		"ImportingAssets":         {"SecondsSinceLastMainThreadTick": 0.1, "IsUpdating": true},
	}
	for name, overrides := range cases {
		t.Run(name, func(t *testing.T) {
			projectRoot := writeFakeUnityProject(t)
			overrides["ProcessId"] = 4242
			server := startSingleEditorStatusServer(t, projectRoot, overrides)
			inspector := &fakeDialogInspector{dialogs: []statusDialog{statusTestSaveSceneDialog}}

			run := runStatusForTest(context.Background(), server.connection, nil, windowsStatusTestDeps(t, inspector))

			assertStatusReport(t, run, "BlockedByDialog")
			assertStatusRequestCount(t, server, 1)
			assertDialogInspections(t, inspector, 1)
			if len(inspector.processIDs) != 1 || inspector.processIDs[0] != 4242 {
				t.Fatalf("looked with process IDs %v, want [4242]", inspector.processIDs)
			}
		})
	}
}

// Verifies that on Windows a look that finds no dialog keeps the first answer's Ready, with one
// look and no second question to the Editor.
func TestRunStatusCommandOnWindowsStaysReadyWhenTheLookFindsNoDialog(t *testing.T) {
	projectRoot := writeFakeUnityProject(t)
	server := startSingleEditorStatusServer(t, projectRoot, map[string]any{"SecondsSinceLastMainThreadTick": 0.1})
	inspector := &fakeDialogInspector{dialogs: []statusDialog{}}

	run := runStatusForTest(context.Background(), server.connection, nil, windowsStatusTestDeps(t, inspector))

	assertStatusReport(t, run, "Ready")
	assertReadyNextActionsAreEmpty(t, run)
	assertStatusRequestCount(t, server, 1)
	assertDialogInspections(t, inspector, 1)
}

// Verifies that on Windows a failed look prints exactly what status printed before it looked on
// every answer: Ready, with no failure hint or error.
func TestRunStatusCommandOnWindowsPrintsTheUsualReadyWhenTheLookFails(t *testing.T) {
	macProjectRoot := writeFakeUnityProject(t)
	macServer := startSingleEditorStatusServer(t, macProjectRoot, map[string]any{"SecondsSinceLastMainThreadTick": 0.1})
	macDeps := statusTestDeps(t)
	macDeps.goos = "darwin"
	want := runStatusForTest(context.Background(), macServer.connection, nil, macDeps)

	projectRoot := writeFakeUnityProject(t)
	server := startSingleEditorStatusServer(t, projectRoot, map[string]any{"SecondsSinceLastMainThreadTick": 0.1})
	inspector := &fakeDialogInspector{err: errors.New("window listing refused")}

	run := runStatusForTest(context.Background(), server.connection, nil, windowsStatusTestDeps(t, inspector))

	assertStatusReport(t, run, "Ready")
	assertDialogInspections(t, inspector, 1)
	if run.stdout != want.stdout {
		t.Fatalf("stdout after a failed look =\n%s\nwant the usual Ready =\n%s", run.stdout, want.stdout)
	}
}

// Verifies that on Windows a Busy answer, which never tells the reader to send a command, gets no
// extra look at Unity's windows.
func TestRunStatusCommandOnWindowsDoesNotLookForABusyEditor(t *testing.T) {
	projectRoot := writeFakeUnityProject(t)
	server := startSingleEditorStatusServer(t, projectRoot, busyEditorStatusOverrides(0.1, []any{}))
	inspector := &fakeDialogInspector{dialogs: []statusDialog{statusTestSaveSceneDialog}}

	run := runStatusForTest(context.Background(), server.connection, nil, windowsStatusTestDeps(t, inspector))

	assertStatusReport(t, run, "Busy")
	assertDialogInspections(t, inspector, 0)
}

// Verifies that on macOS a Ready answer with a running main thread still gets no look at Unity's
// windows.
func TestRunStatusCommandOnMacOSDoesNotLookForAReadyEditor(t *testing.T) {
	projectRoot := writeFakeUnityProject(t)
	server := startSingleEditorStatusServer(t, projectRoot, map[string]any{"SecondsSinceLastMainThreadTick": 0.1})
	inspector := &fakeDialogInspector{dialogs: []statusDialog{statusTestSaveSceneDialog}}
	deps := withDialogInspector(statusTestDeps(t), inspector)
	deps.goos = "darwin"

	run := runStatusForTest(context.Background(), server.connection, nil, deps)

	assertStatusReport(t, run, "Ready")
	assertDialogInspections(t, inspector, 0)
}

// Verifies that on Windows a dialog the Editor reports itself is BlockedByDialog without a look at
// Unity's windows.
func TestRunStatusCommandOnWindowsDoesNotLookWhenTheEditorReportsADialog(t *testing.T) {
	projectRoot := writeFakeUnityProject(t)
	server := startSingleEditorStatusServer(t, projectRoot, map[string]any{
		"OpenDialogs": editorDialogsJSON(statusTestSaveSceneDialog),
	})
	inspector := &fakeDialogInspector{}

	run := runStatusForTest(context.Background(), server.connection, nil, windowsStatusTestDeps(t, inspector))

	assertStatusReport(t, run, "BlockedByDialog")
	assertDialogInspections(t, inspector, 0)
}
