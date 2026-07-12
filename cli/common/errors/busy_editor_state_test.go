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
