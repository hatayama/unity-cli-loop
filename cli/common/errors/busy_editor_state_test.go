package clierrors

import "testing"

// Verifies compiling busy payloads surface script-compile guidance in NextActions.
func TestUnityServerBusyNextActions_WhenCompiling_IncludesCompileGuidance(t *testing.T) {
	data := map[string]any{
		"isCompiling": true,
	}

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
	data := map[string]any{
		"runningToolName": "compile",
	}

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
	data := map[string]any{
		"runningToolName": "unity-compile",
		"isCompiling":     true,
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
	data := map[string]any{
		"secondsSinceLastMainThreadTick": 12.0,
	}

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
	data := map[string]any{
		"isCompiling":                    true,
		"isUpdating":                     false,
		"secondsSinceLastMainThreadTick": 1.5,
	}

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
