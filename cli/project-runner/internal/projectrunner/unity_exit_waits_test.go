package projectrunner

import (
	"bytes"
	"context"
	"testing"
	"time"

	"github.com/hatayama/unity-cli-loop/common/clicore"
)

// exitedEditorWatch finds no Editor process and moves its clock a second on every reading, so a wait
// against a server nobody listens on confirms the exit without sleeping.
func exitedEditorWatch(projectRoot string) *clicore.UnityExitWatch {
	clock := time.Unix(1_700_000_000, 0)
	return clicore.NewUnityExitWatch(projectRoot, "", noUnityProcessLookup, func() time.Time {
		clock = clock.Add(time.Second)
		return clock
	})
}

// Verifies the play mode state wait ends with UnityNotRunningError once the Editor is confirmed gone,
// instead of polling until its timeout.
func TestWaitForControlPlayModeStateStopsOnceUnityHasExited(t *testing.T) {
	originalPoll := controlPlayModeStatePoll
	controlPlayModeStatePoll = time.Millisecond
	t.Cleanup(func() { controlPlayModeStatePoll = originalPoll })
	connection := unreachableConnection(t.TempDir())

	_, completed, err := waitForControlPlayModeState(
		context.Background(),
		connection,
		"Play",
		// Long enough that only the exit watch, never the timeout, can end the wait first.
		5*time.Second,
		exitedEditorWatch(connection.ProjectRoot),
	)

	if completed {
		t.Fatal("a play mode wait whose Editor exited cannot complete")
	}
	assertUnityExitedDuringCommand(t, err)
}

// Verifies hot reload's wait for the Editor to settle ends with UnityNotRunningError once the Editor is
// confirmed gone.
func TestWaitForEditorReadyStopsOnceUnityHasExited(t *testing.T) {
	connection := unreachableConnection(t.TempDir())
	options := fastEditorReadyWaitOptions()
	options.budget = 5 * time.Second

	_, ready, err := waitForEditorReady(context.Background(), connection, options, exitedEditorWatch(connection.ProjectRoot))

	if ready {
		t.Fatal("an Editor that exited cannot become ready")
	}
	assertUnityExitedDuringCommand(t, err)
}

// Verifies hot reload's wait for a busy Editor ends with UnityNotRunningError once the Editor is
// confirmed gone.
func TestWaitForBusyEditorStopsOnceUnityHasExited(t *testing.T) {
	connection := unreachableConnection(t.TempDir())
	options := fastEditorReadyWaitOptions()
	options.budget = 5 * time.Second
	var stderr bytes.Buffer

	wait, err := waitForBusyEditor(
		context.Background(),
		connection,
		map[string]any{},
		&stderr,
		hotReloadBusyWaitSendDeps(),
		options,
		exitedEditorWatch(connection.ProjectRoot),
	)

	if wait.ready || wait.answered {
		t.Fatalf("an Editor that exited cannot become ready or answer, got %#v", wait)
	}
	assertUnityExitedDuringCommand(t, err)
}

// Verifies a pause point wait ends with UnityNotRunningError once polls against a gone server confirm
// that the Editor exited, instead of polling until --timeout.
func TestApplyPausePointWaitPollStopsOnceUnityHasExited(t *testing.T) {
	connection := unreachableConnection(t.TempDir())
	state := pausePointWaitLoopState{exitWatch: exitedEditorWatch(connection.ProjectRoot)}
	_, pollErr := queryPausePointStatusFromUnity(context.Background(), connection, "marker")
	if pollErr == nil {
		t.Fatal("a status query against an endpoint nobody listens on must fail")
	}

	if _, done, err := applyPausePointWaitPoll(context.Background(), &state, pausePointStatusResponse{}, pollErr, false); done || err != nil {
		t.Fatalf("one lookup must not end the wait, got done=%v err=%v", done, err)
	}
	// The watch's clock moves a second per poll, so a few more polls reach the confirming lookup.
	for poll := 0; poll < 5; poll++ {
		_, done, err := applyPausePointWaitPoll(context.Background(), &state, pausePointStatusResponse{}, pollErr, false)
		if done {
			assertUnityExitedDuringCommand(t, err)
			return
		}
	}
	t.Fatal("the confirmed exit must end the wait")
}
