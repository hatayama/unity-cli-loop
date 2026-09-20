package clierrors

import (
	"encoding/json"
	"testing"
)

// Verifies compiling busy payloads surface script-compile guidance in NextActions.
func TestUnityServerBusyNextActions_WhenCompiling_IncludesCompileGuidance(t *testing.T) {
	isCompiling := true
	data := serverBusyErrorData{IsCompiling: &isCompiling}

	actions := unityServerBusyNextActions(data)
	if len(actions) < 3 {
		t.Fatalf("expected compile-specific guidance, got %#v", actions)
	}
	if actions[0] != "Unity is compiling scripts; wait for compilation to finish before retrying." {
		t.Fatalf("first action mismatch: %#v", actions)
	}
}

// Verifies a busy compile tool adds reattach guidance that only applies after the
// caller's own COMPILE_WAIT_TIMEOUT (not for unrelated clients or unity-compile).
func TestUnityServerBusyNextActions_WhenRunningCompileTool_IncludesReattachGuidance(t *testing.T) {
	data := serverBusyErrorData{RunningToolName: "compile"}

	actions := unityServerBusyNextActions(data)
	expected := "A compile can take several minutes on large projects. Wait for it to finish, then retry. If your own `uloop compile` previously failed with COMPILE_WAIT_TIMEOUT, re-running `uloop compile` reattaches to that compile instead of starting a new one."
	found := false
	for _, action := range actions {
		if action == expected {
			found = true
			break
		}
	}
	if !found {
		t.Fatalf("compile reattach guidance missing: %#v", actions)
	}
}

// Verifies editor-state unity-compile busy does not promise uloop compile reattach.
func TestUnityServerBusyNextActions_WhenRunningUnityCompile_OmitsReattachGuidance(t *testing.T) {
	isCompiling := true
	data := serverBusyErrorData{
		RunningToolName: "unity-compile",
		IsCompiling:     &isCompiling,
	}

	actions := unityServerBusyNextActions(data)
	for _, action := range actions {
		if action == "A compile can take several minutes on large projects. Wait for it to finish, then retry. If your own `uloop compile` previously failed with COMPILE_WAIT_TIMEOUT, re-running `uloop compile` reattaches to that compile instead of starting a new one." {
			t.Fatalf("unity-compile busy must not promise reattach: %#v", actions)
		}
	}
}

// Verifies stalled main-thread ticks add a lightweight responsiveness check action.
func TestUnityServerBusyNextActions_WhenMainThreadStalled_IncludesResponsivenessCheck(t *testing.T) {
	stallSeconds := 12.0
	data := serverBusyErrorData{SecondsSinceLastMainThreadTick: &stallSeconds}

	actions := unityServerBusyNextActions(data)
	found := false
	for _, action := range actions {
		if action == "Run a light command such as `uloop get-logs --max-count 1` to check whether Unity is still responsive before treating this as a freeze." {
			found = true
			break
		}
	}
	if !found {
		t.Fatalf("responsiveness action missing: %#v", actions)
	}
}

// Verifies editor activity summaries copy only populated busy-state fields.
func TestUnityServerBusyEditorActivitySummary_CopiesKnownFields(t *testing.T) {
	data := decodeServerBusyErrorData(json.RawMessage(
		`{"isCompiling":true,"isUpdating":false,"secondsSinceLastMainThreadTick":1.5}`))

	summary := unityServerBusyEditorActivitySummary(data)
	if summary["isCompiling"] != true {
		t.Fatalf("isCompiling mismatch: %#v", summary)
	}
	if _, ok := summary["isUpdating"]; ok {
		t.Fatalf("false bool fields should be omitted: %#v", summary)
	}
	if summary["secondsSinceLastMainThreadTick"] != 1.5 {
		t.Fatalf("stall seconds mismatch: %#v", summary)
	}
}

const (
	expectedPausedBusyStalledAction = "Unity is paused in Play Mode. A running command that waits for a frame or a physics step cannot finish until play resumes, so this busy state does not clear on its own."
	expectedPausedBusyStatusAction  = "Run `uloop pause-point-status` (it answers while Unity is busy) to see whether a pause-point hit is holding the pause."
	expectedPausedBusyStopAction    = "Stop the uloop process that is running the command (Ctrl-C in its terminal, otherwise interrupt or kill that process). Its request is cancelled and returns no result, the Editor pause is released, and the next command can run."
	expectedPausedBusyResumeAction  = "Release the pause in the Editor (Edit > Play Mode > Pause). Frames resume, so the running command finishes and returns its result."
)

// Verifies a paused Play Mode busy payload explains that the running command cannot
// finish on its own and lists the recovery steps before the generic wait/retry pair.
func TestUnityServerBusyNextActions_WhenPausedInPlayMode_LeadsWithPauseRecovery(t *testing.T) {
	isPlaying := true
	isPaused := true
	data := serverBusyErrorData{IsPlaying: &isPlaying, IsPaused: &isPaused}

	actions := unityServerBusyNextActions(data)
	if len(actions) != 6 {
		t.Fatalf("expected four pause actions before the default pair, got %#v", actions)
	}
	if actions[0] != expectedPausedBusyStalledAction {
		t.Fatalf("first action mismatch: %#v", actions)
	}
	if actions[1] != expectedPausedBusyStatusAction {
		t.Fatalf("second action mismatch: %#v", actions)
	}
	if actions[2] != expectedPausedBusyStopAction {
		t.Fatalf("third action mismatch: %#v", actions)
	}
	if actions[3] != expectedPausedBusyResumeAction {
		t.Fatalf("fourth action mismatch: %#v", actions)
	}
	if actions[4] != "Wait for the running Unity command to complete." {
		t.Fatalf("default wait action must stay: %#v", actions)
	}
}

// Verifies a paused flag outside Play Mode adds no pause guidance, because an Editor
// pause only stops frames while Play Mode runs.
func TestUnityServerBusyNextActions_WhenPausedOutsidePlayMode_OmitsPauseRecovery(t *testing.T) {
	isPaused := true
	data := serverBusyErrorData{IsPaused: &isPaused}

	actions := unityServerBusyNextActions(data)
	for _, action := range actions {
		if action == expectedPausedBusyStalledAction {
			t.Fatalf("pause guidance must need Play Mode: %#v", actions)
		}
	}
}

// Verifies a running tool the Editor keeps alive across a client disconnect drops the
// stop-the-process step, because stopping it would not release the Editor pause.
func TestUnityServerBusyNextActions_WhenPausedWhileRunningDisconnectSurvivingTool_OmitsStopStep(t *testing.T) {
	isPlaying := true
	isPaused := true
	for _, runningToolName := range []string{"run-tests", "compile"} {
		data := serverBusyErrorData{IsPlaying: &isPlaying, IsPaused: &isPaused, RunningToolName: runningToolName}

		actions := unityServerBusyNextActions(data)
		for _, action := range actions {
			if action == expectedPausedBusyStopAction {
				t.Fatalf("%s must not promise recovery by stopping the process: %#v", runningToolName, actions)
			}
		}
		if actions[2] != expectedPausedBusyResumeAction {
			t.Fatalf("%s must still offer the Editor resume step: %#v", runningToolName, actions)
		}
	}
}

// Verifies a compile that is also paused keeps the compile line first, because the
// compile finishes on its own and frees the gate whatever the pause does.
func TestUnityServerBusyNextActions_WhenCompilingWhilePaused_KeepsCompileGuidanceFirst(t *testing.T) {
	isCompiling := true
	isPlaying := true
	isPaused := true
	data := serverBusyErrorData{IsCompiling: &isCompiling, IsPlaying: &isPlaying, IsPaused: &isPaused}

	actions := unityServerBusyNextActions(data)
	if actions[0] != "Unity is compiling scripts; wait for compilation to finish before retrying." {
		t.Fatalf("compile guidance must stay first: %#v", actions)
	}
	if actions[1] != expectedPausedBusyStalledAction {
		t.Fatalf("pause guidance must follow the compile line: %#v", actions)
	}
}
