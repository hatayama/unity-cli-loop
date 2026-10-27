package dispatcher

import (
	"bytes"
	"context"
	"errors"
	"testing"
	"time"

	"github.com/hatayama/unity-cli-loop/common/clicore"
)

// unityStopRecorder records what stopUnityProcess asked of its dependencies.
type unityStopRecorder struct {
	quitRequests int
	killedPids   []int
	exitWaits    []time.Duration
}

// unityStopTestDeps returns launch dependencies whose quit request answers quitTaken and whose exit waits
// return the given results in order, recording every call in the returned recorder.
func unityStopTestDeps(quitTaken bool, exitWaitResults ...error) (launchDeps, *unityStopRecorder) {
	recorder := &unityStopRecorder{}
	deps := defaultLaunchDeps()
	deps.requestUnityQuit = func(context.Context, string) bool {
		recorder.quitRequests++
		return quitTaken
	}
	deps.killUnityProcess = func(pid int) error {
		recorder.killedPids = append(recorder.killedPids, pid)
		return nil
	}
	deps.waitForUnityProcessExit = func(_ context.Context, _ string, _ int, _ time.Duration, timeout time.Duration) error {
		index := len(recorder.exitWaits)
		recorder.exitWaits = append(recorder.exitWaits, timeout)
		if index < len(exitWaitResults) {
			return exitWaitResults[index]
		}
		return nil
	}
	return deps, recorder
}

// Verifies an Editor that takes the quit request and exits is never killed, so it keeps the window
// layout it saved on the way out.
func TestStopUnityProcessLetsAnEditorThatQuitsExitOnItsOwn(t *testing.T) {
	deps, recorder := unityStopTestDeps(true)

	method, err := stopUnityProcess(context.Background(), "/projects/sample", 222, false, deps)
	if err != nil {
		t.Fatalf("stopUnityProcess() error = %v", err)
	}
	if method != launchStopMethodQuit {
		t.Fatalf("method = %q, want %q", method, launchStopMethodQuit)
	}
	if len(recorder.killedPids) != 0 {
		t.Fatalf("killed %v, want no kill for an Editor that quit", recorder.killedPids)
	}
	if len(recorder.exitWaits) != 1 || recorder.exitWaits[0] != launchQuitExitTimeout {
		t.Fatalf("exit waits = %v, want one wait of %s", recorder.exitWaits, launchQuitExitTimeout)
	}
}

// Verifies an Editor that took the quit request but is still running when the wait ends is force-killed,
// and that the kill is waited out before launch goes on.
func TestStopUnityProcessKillsAnEditorThatDoesNotExitAfterTakingTheQuitRequest(t *testing.T) {
	deps, recorder := unityStopTestDeps(true, launchProcessExitTimeoutError{pid: 222, timeout: launchQuitExitTimeout}, nil)

	method, err := stopUnityProcess(context.Background(), "/projects/sample", 222, false, deps)
	if err != nil {
		t.Fatalf("stopUnityProcess() error = %v", err)
	}
	if method != launchStopMethodForceKill {
		t.Fatalf("method = %q, want %q", method, launchStopMethodForceKill)
	}
	if len(recorder.killedPids) != 1 || recorder.killedPids[0] != 222 {
		t.Fatalf("killed %v, want pid 222 killed once", recorder.killedPids)
	}
	if len(recorder.exitWaits) != 2 || recorder.exitWaits[1] != launchProcessExitTimeout {
		t.Fatalf("exit waits = %v, want the quit wait and then the kill wait of %s", recorder.exitWaits, launchProcessExitTimeout)
	}
}

// Verifies an Editor that cannot take the quit request, such as a frozen one or one whose package predates
// the request, is force-killed at once instead of being waited on.
func TestStopUnityProcessKillsAnEditorThatDoesNotTakeTheQuitRequest(t *testing.T) {
	deps, recorder := unityStopTestDeps(false)

	method, err := stopUnityProcess(context.Background(), "/projects/sample", 222, false, deps)
	if err != nil {
		t.Fatalf("stopUnityProcess() error = %v", err)
	}
	if method != launchStopMethodForceKill {
		t.Fatalf("method = %q, want %q", method, launchStopMethodForceKill)
	}
	if recorder.quitRequests != 1 {
		t.Fatalf("quit requests = %d, want 1", recorder.quitRequests)
	}
	if len(recorder.killedPids) != 1 {
		t.Fatalf("killed %v, want one kill", recorder.killedPids)
	}
	if len(recorder.exitWaits) != 1 || recorder.exitWaits[0] != launchProcessExitTimeout {
		t.Fatalf("exit waits = %v, want only the kill wait of %s", recorder.exitWaits, launchProcessExitTimeout)
	}
}

// Verifies a V2 project's Editor is force-killed without a quit request, because its server predates the
// request and asking would only delay the kill.
func TestStopUnityProcessKillsAV2EditorWithoutAskingItToQuit(t *testing.T) {
	deps, recorder := unityStopTestDeps(true)

	method, err := stopUnityProcess(context.Background(), "/projects/sample", 222, true, deps)
	if err != nil {
		t.Fatalf("stopUnityProcess() error = %v", err)
	}
	if method != launchStopMethodForceKill {
		t.Fatalf("method = %q, want %q", method, launchStopMethodForceKill)
	}
	if recorder.quitRequests != 0 {
		t.Fatalf("quit requests = %d, want none for a V2 project", recorder.quitRequests)
	}
	if len(recorder.killedPids) != 1 {
		t.Fatalf("killed %v, want one kill", recorder.killedPids)
	}
}

// Verifies a failure to watch the Editor exit, other than running out of time, is reported instead of
// being taken as an Editor to kill, since it says nothing about whether the Editor is still running.
func TestStopUnityProcessReportsAnExitWaitThatFailsForAnotherReason(t *testing.T) {
	lookupFailure := errors.New("process listing failed")
	deps, recorder := unityStopTestDeps(true, lookupFailure)

	_, err := stopUnityProcess(context.Background(), "/projects/sample", 222, false, deps)

	if !errors.Is(err, lookupFailure) {
		t.Fatalf("err = %v, want the process listing failure", err)
	}
	if len(recorder.killedPids) != 0 {
		t.Fatalf("killed %v, want no kill when the wait failed for another reason", recorder.killedPids)
	}
}

// Verifies a kill that fails is reported, so launch does not start a second Editor beside the first.
func TestStopUnityProcessReportsAFailedKill(t *testing.T) {
	deps, _ := unityStopTestDeps(false)
	killFailure := errors.New("kill denied")
	deps.killUnityProcess = func(int) error { return killFailure }

	_, err := stopUnityProcess(context.Background(), "/projects/sample", 222, false, deps)

	if !errors.Is(err, killFailure) {
		t.Fatalf("err = %v, want the kill failure", err)
	}
}

// Verifies --quit reports that Unity quit on its own, with a message saying the layout was kept and unsaved
// scene changes were dropped.
func TestRunLaunchQuitReportsAnEditorThatQuitOnItsOwn(t *testing.T) {
	deps, recorder := unityStopTestDeps(true)
	deps.findRunningUnityProcess = func(context.Context, string) (*clicore.UnityProcess, error) {
		return &clicore.UnityProcess{Pid: 333}, nil
	}
	projectRoot := createLaunchTestProject(t)
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := runLaunchWithDeps(context.Background(), launchOptions{projectPath: projectRoot, quit: true}, projectRoot, &stdout, &stderr, deps)

	if code != 0 {
		t.Fatalf("exit code mismatch: %d stderr=%s", code, stderr.String())
	}
	if len(recorder.killedPids) != 0 {
		t.Fatalf("killed %v, want no kill", recorder.killedPids)
	}
	response := decodeLaunchResponseFromOutput(t, stdout.String())
	if !response.Quit || response.StopMethod != string(launchStopMethodQuit) {
		t.Fatalf("response = %#v, want Quit with StopMethod %q", response, launchStopMethodQuit)
	}
	if response.Message != launchQuitStoppedMessage {
		t.Fatalf("message = %q, want %q", response.Message, launchQuitStoppedMessage)
	}
}

// Verifies --quit reports a force-kill as one, with a message saying the layout was not saved.
func TestRunLaunchQuitReportsAForceKilledEditor(t *testing.T) {
	deps, _ := unityStopTestDeps(false)
	deps.findRunningUnityProcess = func(context.Context, string) (*clicore.UnityProcess, error) {
		return &clicore.UnityProcess{Pid: 333}, nil
	}
	projectRoot := createLaunchTestProject(t)
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := runLaunchWithDeps(context.Background(), launchOptions{projectPath: projectRoot, quit: true}, projectRoot, &stdout, &stderr, deps)

	if code != 0 {
		t.Fatalf("exit code mismatch: %d stderr=%s", code, stderr.String())
	}
	response := decodeLaunchResponseFromOutput(t, stdout.String())
	if response.StopMethod != string(launchStopMethodForceKill) {
		t.Fatalf("StopMethod = %q, want %q", response.StopMethod, launchStopMethodForceKill)
	}
	if response.Message != launchForceKillStoppedMessage {
		t.Fatalf("message = %q, want %q", response.Message, launchForceKillStoppedMessage)
	}
}

// Verifies --restart reports how the previous Editor stopped in the ready response of the new one.
func TestRunLaunchRestartReportsHowThePreviousEditorStopped(t *testing.T) {
	deps, _ := unityStopTestDeps(true)
	deps.findRunningUnityProcess = func(context.Context, string) (*clicore.UnityProcess, error) {
		return &clicore.UnityProcess{Pid: 222}, nil
	}
	fakeUnityPath := fakeUnityExecutablePath(t)
	deps.resolveUnityExecutablePath = func(string) (string, error) { return fakeUnityPath, nil }
	deps.waitForUnityStartupMarker = func(context.Context, string, time.Duration, time.Duration) error { return nil }
	deps.waitForToolReadiness = func(context.Context, string, time.Duration) error { return nil }
	deps.focusUnityProcess = func(context.Context, int) error { return nil }
	projectRoot := createLaunchTestProject(t)
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := runLaunchWithDeps(
		context.Background(),
		launchOptions{projectPath: projectRoot, restart: true, editorVersion: "6000.0.0f1"},
		projectRoot,
		&stdout,
		&stderr,
		deps,
	)

	if code != 0 {
		t.Fatalf("exit code mismatch: %d stderr=%s", code, stderr.String())
	}
	response := decodeLaunchResponseFromOutput(t, stdout.String())
	if !response.Restarted || response.StopMethod != string(launchStopMethodQuit) {
		t.Fatalf("response = %#v, want Restarted with StopMethod %q", response, launchStopMethodQuit)
	}
}
