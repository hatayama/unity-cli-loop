package projectrunner

import (
	"context"
	"testing"
)

const earlyDialogProbeTestProcessID = 4242

// waitingCommandEditorStatusOverrides describes get-logs holding the slot while it waits for the
// main thread, the state a sent request is in when a dialog opened just after it was sent.
func waitingCommandEditorStatusOverrides(stallSeconds float64, phase string) map[string]any {
	return map[string]any{
		"IsBusy":                         true,
		"RunningToolName":                "get-logs",
		"RunningToolElapsedSeconds":      2,
		"RunningToolPhase":               phase,
		"SecondsSinceLastMainThreadTick": stallSeconds,
		"ProcessId":                      earlyDialogProbeTestProcessID,
	}
}

// probeWhileUnansweredForTest asks a fake Editor that gives one answer, the way the early dialog
// report asks while its request is unanswered.
func probeWhileUnansweredForTest(
	t *testing.T,
	overrides map[string]any,
	inspector *fakeDialogInspector,
) (statusReport, fakeUnityServer) {
	t.Helper()
	projectRoot := writeFakeUnityProject(t)
	server := startFakeEditorStatusSequenceServer(t, projectRoot,
		editorStatusRPCResponse(editorStatusResultJSON(t, overrides)))
	report, err := probeEditorStatusWhileUnansweredWithDeps(
		context.Background(),
		server.connection,
		withDialogInspector(preSendStatusDeps(statusTestDeps(t)), inspector))
	if err != nil {
		t.Fatalf("probe: %v", err)
	}
	return report, server
}

func assertProbeState(t *testing.T, report statusReport, want string, wantDialogs int) {
	t.Helper()
	if report.State != want || len(report.Dialogs) != wantDialogs {
		t.Fatalf("state = %q with %d dialogs, want %q with %d", report.State, len(report.Dialogs), want, wantDialogs)
	}
}

// Verifies a dialog the Editor reports itself stays BlockedByDialog without a look at Unity's windows.
func TestProbeWhileUnansweredKeepsTheEditorsOwnDialogReport(t *testing.T) {
	overrides := waitingCommandEditorStatusOverrides(3.5, "WaitingForMainThread")
	overrides["OpenDialogs"] = editorDialogsJSON(statusTestSaveSceneDialog)
	inspector := &fakeDialogInspector{dialogs: []statusDialog{statusTestSaveSceneDialog}}

	report, server := probeWhileUnansweredForTest(t, overrides, inspector)

	assertProbeState(t, report, statusStateBlockedByDialog, 1)
	assertStatusRequestCount(t, server, 1)
	assertDialogInspections(t, inspector, 0)
}

// Verifies a command waiting for a main thread still for under five seconds gets one look at Unity's
// windows, with the Editor's process ID: a dialog found there is BlockedByDialog, and none keeps Busy.
func TestProbeWhileUnansweredLooksForADialogBehindAWaitingCommand(t *testing.T) {
	cases := map[string]struct {
		dialogs     []statusDialog
		wantState   string
		wantDialogs int
	}{
		"dialog open": {dialogs: []statusDialog{statusTestSaveSceneDialog}, wantState: statusStateBlockedByDialog, wantDialogs: 1},
		"no dialog":   {dialogs: []statusDialog{}, wantState: statusStateBusy, wantDialogs: 0},
	}
	for name, tc := range cases {
		t.Run(name, func(t *testing.T) {
			inspector := &fakeDialogInspector{dialogs: tc.dialogs}

			report, server := probeWhileUnansweredForTest(t,
				waitingCommandEditorStatusOverrides(3.5, "WaitingForMainThread"), inspector)

			assertProbeState(t, report, tc.wantState, tc.wantDialogs)
			assertStatusRequestCount(t, server, 1)
			assertDialogInspections(t, inspector, 1)
			if inspector.processIDs[0] != earlyDialogProbeTestProcessID {
				t.Fatalf("look process ID = %d, want %d", inspector.processIDs[0], earlyDialogProbeTestProcessID)
			}
		})
	}
}

// Verifies a Busy Editor is not looked at while its main thread ran within the last second, or while
// the running command is in its own code rather than waiting for the main thread.
func TestProbeWhileUnansweredDoesNotLookWithoutAWaitOrAStall(t *testing.T) {
	cases := map[string]map[string]any{
		"main thread just ran":      waitingCommandEditorStatusOverrides(0.5, "WaitingForMainThread"),
		"command runs its own code": waitingCommandEditorStatusOverrides(3.5, "Executing"),
	}
	for name, overrides := range cases {
		t.Run(name, func(t *testing.T) {
			inspector := &fakeDialogInspector{dialogs: []statusDialog{statusTestSaveSceneDialog}}

			report, server := probeWhileUnansweredForTest(t, overrides, inspector)

			assertProbeState(t, report, statusStateBusy, 0)
			assertStatusRequestCount(t, server, 1)
			assertDialogInspections(t, inspector, 0)
		})
	}
}

// Verifies a stall of five seconds or more, which uloop status already looks behind, gets exactly one
// look at Unity's windows in one probe.
func TestProbeWhileUnansweredLooksOnlyOnceAfterALongStall(t *testing.T) {
	inspector := &fakeDialogInspector{dialogs: []statusDialog{statusTestSaveSceneDialog}}

	report, server := probeWhileUnansweredForTest(t,
		waitingCommandEditorStatusOverrides(6.0, "WaitingForMainThread"), inspector)

	assertProbeState(t, report, statusStateBlockedByDialog, 1)
	assertStatusRequestCount(t, server, 1)
	assertDialogInspections(t, inspector, 1)
}
