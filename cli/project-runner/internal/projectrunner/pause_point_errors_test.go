package projectrunner

import (
	"reflect"
	"testing"
)

// Verifies that a suppressed marker short-circuits timeout diagnosis even when the
// response would otherwise look like an already-hit continuous wait or an enabled-but-never-hit marker.
func TestPausePointTimeoutHint_SuppressedByHotReload_WinsOverOtherBranches(t *testing.T) {
	response := pausePointStatusResponse{
		Status:                pausePointStatusEnabled,
		HitCount:              0,
		SuppressedByHotReload: true,
		EditorState: pausePointEditorState{
			IsPlaying: true,
			IsPaused:  false,
		},
	}

	hint := pausePointTimeoutHint(response, true, false)
	if hint != pausePointHintSuppressedByHotReload {
		t.Fatalf("expected suppressed hint, got %q", hint)
	}
}

// Verifies a non-empty Unity reason is returned as-is (no fixed-hint concatenation).
func TestPausePointTimeoutHint_SuppressedByHotReload_ReturnsReasonOnly(t *testing.T) {
	const reason = "Line no longer resolves inside the patched body."
	response := pausePointStatusResponse{
		Status:                      pausePointStatusEnabled,
		HitCount:                    0,
		SuppressedByHotReload:       true,
		SuppressedByHotReloadReason: reason,
		EditorState: pausePointEditorState{
			IsPlaying: true,
			IsPaused:  false,
		},
	}

	hint := pausePointTimeoutHint(response, false, false)
	if hint != reason {
		t.Fatalf("expected reason-only hint %q, got %q", reason, hint)
	}
}

// Verifies the fixed suppressed hint is used only when Unity omitted a reason.
func TestPausePointTimeoutHint_SuppressedByHotReload_EmptyReasonUsesFallback(t *testing.T) {
	response := pausePointStatusResponse{
		Status:                      pausePointStatusEnabled,
		HitCount:                    0,
		SuppressedByHotReload:       true,
		SuppressedByHotReloadReason: "",
		EditorState: pausePointEditorState{
			IsPlaying: true,
			IsPaused:  false,
		},
	}

	hint := pausePointTimeoutHint(response, false, false)
	if hint != pausePointHintSuppressedByHotReload {
		t.Fatalf("expected fallback suppressed hint, got %q", hint)
	}
}

// Verifies the enabled-but-never-hit timeout hint still wins when suppression is false,
// so inserting the suppressed short-circuit does not reorder the remaining branches.
func TestPausePointTimeoutHint_NotSuppressed_ReturnsEnabledNeverHitHint(t *testing.T) {
	response := pausePointStatusResponse{
		Status:                pausePointStatusEnabled,
		HitCount:              0,
		SuppressedByHotReload: false,
		EditorState: pausePointEditorState{
			IsPlaying: true,
			IsPaused:  false,
		},
	}

	hint := pausePointTimeoutHint(response, false, false)
	if hint == pausePointHintSuppressedByHotReload {
		t.Fatalf("did not expect suppressed hint when SuppressedByHotReload is false")
	}
	if hint == "" {
		t.Fatalf("expected the enabled-but-never-hit hint, got empty")
	}
	if hint == pausePointHintPlayModeNotRunning || hint == pausePointHintEditorAlreadyPaused {
		t.Fatalf("expected the enabled-but-never-hit hint, got %q", hint)
	}
}

// Verifies that a suppressed marker short-circuits expired-marker diagnosis as well.
func TestPausePointExpiredHint_SuppressedByHotReload_ReturnsSuppressedHint(t *testing.T) {
	response := pausePointStatusResponse{
		HitCount:              0,
		SuppressedByHotReload: true,
		EditorState: pausePointEditorState{
			IsPlaying: true,
			IsPaused:  false,
		},
	}

	hint := pausePointExpiredHint(response)
	if hint != pausePointHintSuppressedByHotReload {
		t.Fatalf("expected suppressed hint, got %q", hint)
	}
}

// Verifies pausePointStateError exposes SuppressedByHotReload in Details for await failures.
func TestPausePointStateError_DetailsIncludeSuppressedByHotReload(t *testing.T) {
	response := pausePointStatusResponse{
		Status:                pausePointStatusEnabled,
		SuppressedByHotReload: true,
	}
	options := waitForPausePointOptions{id: "marker", timeoutSeconds: 30}

	err := pausePointStateError(
		"PAUSE_POINT_WAIT_TIMEOUT",
		"Pause point was not hit within 30s.",
		"/tmp/project",
		options,
		response,
		true)

	value, ok := err.Details["SuppressedByHotReload"]
	if !ok {
		t.Fatalf("Details missing SuppressedByHotReload")
	}
	suppressed, ok := value.(bool)
	if !ok {
		t.Fatalf("SuppressedByHotReload Details value has type %T, want bool", value)
	}
	if !suppressed {
		t.Fatalf("expected SuppressedByHotReload Details to be true")
	}
}

// Verifies timeout auto-clear diagnosis is the re-enable hint, not the old "wait again" path.
func TestPausePointTimeoutHint_AutoCleared_ReturnsReEnableHint(t *testing.T) {
	response := pausePointStatusResponse{
		Status:        pausePointStatusCleared,
		ClearedReason: pausePointAwaitTimeoutAutoClearReason,
		EditorState: pausePointEditorState{
			IsPlaying: true,
			IsPaused:  false,
		},
	}

	hint := pausePointTimeoutHint(response, false, true)
	wantHint := pausePointHintTimeoutAutoCleared + pausePointNonFiringPatternsHint
	if hint != wantHint {
		t.Fatalf("hint mismatch:\n got: %q\nwant: %q", hint, wantHint)
	}
}

// Verifies a new-hit-baseline timeout keeps the already-hit hint even when Status is still Enabled.
func TestPausePointTimeoutHint_NewHitBaseline_KeepsAlreadyHitHint(t *testing.T) {
	response := pausePointStatusResponse{
		Status:   pausePointStatusEnabled,
		HitCount: 1,
		EditorState: pausePointEditorState{
			IsPlaying: true,
			IsPaused:  true,
		},
	}

	hint := pausePointTimeoutHint(response, true, false)
	if hint != pausePointHintAlreadyHitWaitingForNew {
		t.Fatalf("hint mismatch: got %q, want %q", hint, pausePointHintAlreadyHitWaitingForNew)
	}
}

// Verifies pausePointStateError exposes ClearedReason and StatusBeforeClear when Unity populated them.
func TestPausePointStateError_DetailsIncludeClearedReasonAndStatusBeforeClear(t *testing.T) {
	response := pausePointStatusResponse{
		Status:            pausePointStatusCleared,
		ClearedReason:     pausePointAwaitTimeoutAutoClearReason,
		StatusBeforeClear: pausePointStatusEnabled,
	}
	options := waitForPausePointOptions{id: "marker", timeoutSeconds: 30}

	err := pausePointStateError(
		"PAUSE_POINT_WAIT_TIMEOUT",
		"Pause point was not hit within 30s.",
		"/tmp/project",
		options,
		response,
		true)

	if err.Details["ClearedReason"] != pausePointAwaitTimeoutAutoClearReason {
		t.Fatalf("ClearedReason mismatch: %#v", err.Details)
	}
	if err.Details["StatusBeforeClear"] != pausePointStatusEnabled {
		t.Fatalf("StatusBeforeClear mismatch: %#v", err.Details)
	}
}

// Verifies file:line ids replace re-arm and confirm NextActions with --file/--line wording.
func TestPausePointStateNextActionsReplacesFileLineGuidance(t *testing.T) {
	got := pausePointStateNextActions("Assets/Scripts/Foo.cs:42", pausePointStatusResponse{})
	want := []string{
		"Re-arm it with uloop enable-pause-point --file \"Assets/Scripts/Foo.cs\" --line 42 before waiting.",
		"Confirm the code path executes line 42 of Assets/Scripts/Foo.cs while the marker is armed.",
		"Check `Details.Status`, `Details.EditorState`, `Details.ElapsedSinceEnabledMilliseconds`, and `Details.RemainingMilliseconds` to distinguish a missed code path from an already-paused Editor.",
		"If the marker is inside a custom asmdef, add a reference to `UnityCLILoop.PausePoints.Runtime`.",
	}
	if !reflect.DeepEqual(got, want) {
		t.Fatalf("NextActions mismatch:\n got: %#v\nwant: %#v", got, want)
	}
}

// Verifies a code-marker id keeps the Pause(...) NextActions wording.
func TestPausePointStateNextActionsKeepsCodeMarkerGuidance(t *testing.T) {
	got := pausePointStateNextActions("jump", pausePointStatusResponse{})
	want := []string{
		"Run `uloop enable-pause-point --id <marker-id>` before waiting.",
		"Confirm the code path calls `UloopPausePoint.Pause(\"<marker-id>\")` with the same id.",
		"Check `Details.Status`, `Details.EditorState`, `Details.ElapsedSinceEnabledMilliseconds`, and `Details.RemainingMilliseconds` to distinguish a missed code path from an already-paused Editor.",
		"If the marker is inside a custom asmdef, add a reference to `UnityCLILoop.PausePoints.Runtime`.",
	}
	if !reflect.DeepEqual(got, want) {
		t.Fatalf("NextActions mismatch:\n got: %#v\nwant: %#v", got, want)
	}
}

// Verifies a code-marker id that contains a colon is not treated as file:line.
func TestPausePointStateNextActionsKeepsCodeMarkerGuidanceWhenIdContainsColon(t *testing.T) {
	got := pausePointStateNextActions("scene:jump", pausePointStatusResponse{})
	if got[0] != "Run `uloop enable-pause-point --id <marker-id>` before waiting." {
		t.Fatalf("re-arm NextAction mismatch: %#v", got[0])
	}
	if got[1] != "Confirm the code path calls `UloopPausePoint.Pause(\"<marker-id>\")` with the same id." {
		t.Fatalf("confirm NextAction mismatch: %#v", got[1])
	}
}
