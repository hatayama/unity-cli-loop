package projectrunner

import (
	"context"
	"errors"
	"fmt"
	"strings"
	"testing"
)

// startEarlyStallStatusServer answers a first probe whose main thread has not run for
// firstTick seconds and then the given second answer.
func startEarlyStallStatusServer(t *testing.T, projectRoot string, firstTick float64, secondResult string) fakeUnityServer {
	t.Helper()
	return startFakeEditorStatusSequenceServer(t, projectRoot,
		editorStatusRPCResponse(editorStatusResultJSON(t, map[string]any{"SecondsSinceLastMainThreadTick": firstTick})),
		secondResult,
	)
}

func assertReadyNextActionsAreEmpty(t *testing.T, run statusRun) {
	t.Helper()
	if !strings.Contains(run.stdout, `"NextActions": []`) {
		t.Fatalf("Ready must print NextActions as an empty array:\n%s", run.stdout)
	}
}

// Verifies an Editor whose main thread ran within the last second is Ready from one probe, with
// no second probe and no look at Unity's windows.
func TestRunStatusCommandDoesNotLookEarlyWhenTheMainThreadJustRan(t *testing.T) {
	projectRoot := writeFakeUnityProject(t)
	server := startFakeUnityResultServer(t, projectRoot, editorStatusBridgeCommandName, editorStatusResultJSON(t, map[string]any{
		"SecondsSinceLastMainThreadTick": 0.2,
	}))
	inspector := &fakeDialogInspector{dialogs: []statusDialog{statusTestSaveSceneDialog}}

	run := runStatusForTest(context.Background(), server.connection, nil, withDialogInspector(statusTestDeps(t), inspector))

	assertStatusReport(t, run, "Ready")
	assertStatusRequestCount(t, server, 1)
	assertDialogInspections(t, inspector, 0)
}

// Verifies a dialog found in Unity's windows after the main thread stayed still for over a second
// across two probes is BlockedByDialog, reported as a five-second stall would be.
func TestRunStatusCommandReportsADialogFoundAfterAShortStall(t *testing.T) {
	projectRoot := writeFakeUnityProject(t)
	server := startEarlyStallStatusServer(t, projectRoot, 2.0, editorStatusRPCResponse(editorStatusResultJSON(t, map[string]any{
		"SecondsSinceLastMainThreadTick": 2.6,
	})))
	inspector := &fakeDialogInspector{dialogs: []statusDialog{statusTestSaveSceneDialog}}

	run := runStatusForTest(context.Background(), server.connection, nil, withDialogInspector(statusTestDeps(t), inspector))

	report := assertStatusReport(t, run, "BlockedByDialog")
	assertStatusFields(t, report, append(append([]string{}, statusTestEditorStateFields...), "Dialogs")...)
	assertStatusMessage(t, report, statusTestSaveSceneMessage)
	assertStatusValue(t, report, "SecondsSinceLastMainThreadTick", 2.6)
	assertStatusNextActions(t, report, statusTestBlockedByDialogNextActions)
	assertStatusDialogs(t, run, []statusDialog{statusTestSaveSceneDialog})
	assertStatusRequestCount(t, server, 2)
	assertDialogInspections(t, inspector, 1)
}

// Verifies a main thread that ran between the two probes was only sleeping: the second answer
// is reported as Ready without a look at Unity's windows.
func TestRunStatusCommandDoesNotLookWhenTheMainThreadRanBetweenProbes(t *testing.T) {
	projectRoot := writeFakeUnityProject(t)
	server := startEarlyStallStatusServer(t, projectRoot, 2.0, editorStatusRPCResponse(editorStatusResultJSON(t, map[string]any{
		"SecondsSinceLastMainThreadTick": 0.1,
	})))
	inspector := &fakeDialogInspector{dialogs: []statusDialog{statusTestSaveSceneDialog}}

	run := runStatusForTest(context.Background(), server.connection, nil, withDialogInspector(statusTestDeps(t), inspector))

	report := assertStatusReport(t, run, "Ready")
	assertStatusValue(t, report, "SecondsSinceLastMainThreadTick", 0.1)
	assertStatusRequestCount(t, server, 2)
	assertDialogInspections(t, inspector, 0)
}

// Verifies a main thread that ran between the probes and then stalled again for over a second is
// still taken as running: only the drop in stall seconds shows it ran, so Unity's windows are not
// searched.
func TestRunStatusCommandDoesNotLookWhenTheStallRestartedBetweenProbes(t *testing.T) {
	projectRoot := writeFakeUnityProject(t)
	server := startEarlyStallStatusServer(t, projectRoot, 3.0, editorStatusRPCResponse(editorStatusResultJSON(t, map[string]any{
		"SecondsSinceLastMainThreadTick": 1.5,
	})))
	inspector := &fakeDialogInspector{dialogs: []statusDialog{statusTestSaveSceneDialog}}

	run := runStatusForTest(context.Background(), server.connection, nil, withDialogInspector(statusTestDeps(t), inspector))

	report := assertStatusReport(t, run, "Ready")
	assertStatusValue(t, report, "SecondsSinceLastMainThreadTick", 1.5)
	assertStatusRequestCount(t, server, 2)
	assertDialogInspections(t, inspector, 0)
}

// Verifies a short stall with no dialog in Unity's windows stays Ready from the second answer,
// with the same fields and NextActions as any Ready report.
func TestRunStatusCommandStaysReadyWhenAShortStallShowsNoDialog(t *testing.T) {
	projectRoot := writeFakeUnityProject(t)
	server := startEarlyStallStatusServer(t, projectRoot, 2.0, editorStatusRPCResponse(editorStatusResultJSON(t, map[string]any{
		"SecondsSinceLastMainThreadTick": 2.6,
	})))
	inspector := &fakeDialogInspector{dialogs: []statusDialog{}}

	run := runStatusForTest(context.Background(), server.connection, nil, withDialogInspector(statusTestDeps(t), inspector))

	report := assertStatusReport(t, run, "Ready")
	assertStatusFields(t, report, statusTestEditorStateFields...)
	assertStatusMessage(t, report, "Unity is idle and can take a command now.")
	assertStatusValue(t, report, "SecondsSinceLastMainThreadTick", 2.6)
	assertReadyNextActionsAreEmpty(t, run)
	assertDialogInspections(t, inspector, 1)
}

// Verifies a failed look after a short stall stays Ready without DialogCheckError or the hint,
// so an Editor that hesitates for a second does not print a permission hint on every status.
func TestRunStatusCommandStaysQuietWhenTheEarlyLookFails(t *testing.T) {
	projectRoot := writeFakeUnityProject(t)
	server := startEarlyStallStatusServer(t, projectRoot, 2.0, editorStatusRPCResponse(editorStatusResultJSON(t, map[string]any{
		"SecondsSinceLastMainThreadTick": 2.6,
	})))
	inspector := &fakeDialogInspector{err: errors.New("osascript is not allowed assistive access")}

	run := runStatusForTest(context.Background(), server.connection, nil, withDialogInspector(statusTestDeps(t), inspector))

	report := assertStatusReport(t, run, "Ready")
	assertStatusFields(t, report, statusTestEditorStateFields...)
	assertReadyNextActionsAreEmpty(t, run)
	assertDialogInspections(t, inspector, 1)
}

// Verifies a second probe that gets no answer after a short stall reports that failure, as the
// five-second recheck does, instead of the first answer's Ready.
func TestRunStatusCommandReportsTheSecondProbeFailureAfterAShortStall(t *testing.T) {
	projectRoot := writeFakeUnityProject(t)
	server := startEarlyStallStatusServer(t, projectRoot, 2.0, "")
	inspector := &fakeDialogInspector{dialogs: []statusDialog{statusTestSaveSceneDialog}}
	deps := withDialogInspector(statusTestDeps(t), inspector)
	deps.isUnityProcessRunning = fakeUnityProcessLookup(t, projectRoot, false, nil)

	run := runStatusForTest(context.Background(), server.connection, nil, deps)

	assertStatusReport(t, run, "NotRunning")
	assertStatusRequestCount(t, server, 1)
	assertDialogInspections(t, inspector, 0)
}

// Verifies a dialog the Editor reports after a short stall is used as is: one probe, and no look
// at Unity's windows.
func TestRunStatusCommandUsesTheEditorDialogAfterAShortStall(t *testing.T) {
	projectRoot := writeFakeUnityProject(t)
	server := startFakeUnityResultServer(t, projectRoot, editorStatusBridgeCommandName, editorStatusResultJSON(t, map[string]any{
		"SecondsSinceLastMainThreadTick": 2.0,
		"OpenDialogs":                    editorDialogsJSON(statusTestSaveSceneDialog),
	}))
	inspector := &fakeDialogInspector{}

	run := runStatusForTest(context.Background(), server.connection, nil, withDialogInspector(statusTestDeps(t), inspector))

	assertStatusReport(t, run, "BlockedByDialog")
	assertStatusDialogs(t, run, []statusDialog{statusTestSaveSceneDialog})
	assertStatusRequestCount(t, server, 1)
	assertDialogInspections(t, inspector, 0)
}

// Verifies a compiling Editor that stalls briefly gets the early look and stays Compiling when
// Unity's windows show no dialog.
func TestRunStatusCommandKeepsCompilingWhenAShortStallShowsNoDialog(t *testing.T) {
	projectRoot := writeFakeUnityProject(t)
	server := startFakeEditorStatusSequenceServer(t, projectRoot,
		editorStatusRPCResponse(editorStatusResultJSON(t, map[string]any{"SecondsSinceLastMainThreadTick": 2.0, "IsCompiling": true})),
		editorStatusRPCResponse(editorStatusResultJSON(t, map[string]any{"SecondsSinceLastMainThreadTick": 2.6, "IsCompiling": true})),
	)
	inspector := &fakeDialogInspector{dialogs: []statusDialog{}}

	run := runStatusForTest(context.Background(), server.connection, nil, withDialogInspector(statusTestDeps(t), inspector))

	assertStatusReport(t, run, "Compiling")
	assertStatusRequestCount(t, server, 2)
	assertDialogInspections(t, inspector, 1)
}

// Verifies a running command that stalls briefly is not rechecked early: Busy never tells the
// reader to send a command, so it cannot be the wrong Ready this check exists for.
func TestRunStatusCommandDoesNotLookEarlyWhileBusy(t *testing.T) {
	projectRoot := writeFakeUnityProject(t)
	server := startFakeUnityResultServer(t, projectRoot, editorStatusBridgeCommandName, editorStatusResultJSON(t,
		busyEditorStatusOverrides(2.0, []any{})))
	inspector := &fakeDialogInspector{dialogs: []statusDialog{statusTestSaveSceneDialog}}

	run := runStatusForTest(context.Background(), server.connection, nil, withDialogInspector(statusTestDeps(t), inspector))

	assertStatusReport(t, run, "Busy")
	assertStatusRequestCount(t, server, 1)
	assertDialogInspections(t, inspector, 0)
}

// Verifies an Editor that has not recorded its state is not rechecked early, because its stall
// seconds do not describe a running main thread yet.
func TestRunStatusCommandDoesNotLookEarlyWhileStarting(t *testing.T) {
	projectRoot := writeFakeUnityProject(t)
	server := startFakeUnityResultServer(t, projectRoot, editorStatusBridgeCommandName, editorStatusResultJSON(t, map[string]any{
		"HasEditorState":                 false,
		"SecondsSinceLastMainThreadTick": 2.0,
	}))
	inspector := &fakeDialogInspector{dialogs: []statusDialog{statusTestSaveSceneDialog}}

	run := runStatusForTest(context.Background(), server.connection, nil, withDialogInspector(statusTestDeps(t), inspector))

	assertStatusReport(t, run, "Starting")
	assertStatusRequestCount(t, server, 1)
	assertDialogInspections(t, inspector, 0)
}

// Verifies the early look starts at exactly one second: 0.99 seconds is one probe without a look,
// and 1.0 seconds gets the second probe and the look.
func TestRunStatusCommandStartsTheEarlyLookAtOneSecond(t *testing.T) {
	t.Run("0.99 seconds", func(t *testing.T) {
		projectRoot := writeFakeUnityProject(t)
		server := startFakeUnityResultServer(t, projectRoot, editorStatusBridgeCommandName, editorStatusResultJSON(t, map[string]any{
			"SecondsSinceLastMainThreadTick": 0.99,
		}))
		inspector := &fakeDialogInspector{dialogs: []statusDialog{statusTestSaveSceneDialog}}

		run := runStatusForTest(context.Background(), server.connection, nil, withDialogInspector(statusTestDeps(t), inspector))

		assertStatusReport(t, run, "Ready")
		assertStatusRequestCount(t, server, 1)
		assertDialogInspections(t, inspector, 0)
	})
	t.Run("1.0 seconds", func(t *testing.T) {
		projectRoot := writeFakeUnityProject(t)
		server := startEarlyStallStatusServer(t, projectRoot, 1.0, editorStatusRPCResponse(editorStatusResultJSON(t, map[string]any{
			"SecondsSinceLastMainThreadTick": 1.0,
		})))
		inspector := &fakeDialogInspector{dialogs: []statusDialog{statusTestSaveSceneDialog}}

		run := runStatusForTest(context.Background(), server.connection, nil, withDialogInspector(statusTestDeps(t), inspector))

		assertStatusReport(t, run, "BlockedByDialog")
		assertStatusRequestCount(t, server, 2)
		assertDialogInspections(t, inspector, 1)
	})
}

// Verifies a caller that cancels while status looks in Unity's windows after a short stall gets
// the cancellation as an error, not the second answer's Ready with exit code 0.
func TestRunStatusCommandReportsCancellationDuringTheEarlyLook(t *testing.T) {
	projectRoot := writeFakeUnityProject(t)
	server := startEarlyStallStatusServer(t, projectRoot, 2.0, editorStatusRPCResponse(editorStatusResultJSON(t, map[string]any{
		"SecondsSinceLastMainThreadTick": 2.6,
	})))
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	deps := statusTestDeps(t)
	deps.inspectDialogs = func(lookCtx context.Context, _ string, _ int) ([]statusDialog, error) {
		cancel()
		return nil, fmt.Errorf("looking for dialogs was cancelled: %w", lookCtx.Err())
	}

	run := runStatusForTest(ctx, server.connection, nil, deps)

	assertStatusError(t, run, "context canceled")
}
