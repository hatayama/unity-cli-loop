package projectrunner

import (
	"encoding/json"
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

	hint := pausePointTimeoutHint(response, true, false, nil)
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

	hint := pausePointTimeoutHint(response, false, false, nil)
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

	hint := pausePointTimeoutHint(response, false, false, nil)
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

	hint := pausePointTimeoutHint(response, false, false, nil)
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

	hint := pausePointExpiredHint(response, nil)
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

	hint := pausePointTimeoutHint(response, false, true, nil)
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

	hint := pausePointTimeoutHint(response, true, false, nil)
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

const wantPausePointTriggerFailedUnknownCommandNextAction = "For an INVALID_ARGUMENT rejection, check the rejected value against the triggered command's own --help; for UNKNOWN_COMMAND, the first token must be a uloop subcommand name written without the leading 'uloop'."

// Verifies the trigger-failed third NextAction distinguishes INVALID_ARGUMENT help-checking
// from UNKNOWN_COMMAND's leading-uloop format mistake, so a prefixed value is not sent to
// the triggered command's --help.
func TestPausePointTriggerFailedNextActionsDiagnosesUnknownCommandPrefix(t *testing.T) {
	got := pausePointTriggerFailedNextActions("jump")
	want := []string{
		"Fix the --trigger value in the command you just ran and run that command again. Re-running " +
			"`enable-pause-point --await` is safe and is the cleanest reset: it restarts the marker's " +
			"HitCount and --timeout-seconds countdown, and re-patching an already patched id is a no-op.",
		`The marker is still armed, so you can also wait on it directly: uloop await-pause-point --id "jump" --trigger "<corrected trigger command>"`,
		wantPausePointTriggerFailedUnknownCommandNextAction,
	}
	if !reflect.DeepEqual(got, want) {
		t.Fatalf("NextActions mismatch:\n got: %#v\nwant: %#v", got, want)
	}
}

// Verifies a `.cs:0` suffix is not treated as a file:line id, because enable-pause-point
// rejects --line 0 and C# never emits that id.
func TestPausePointStateNextActionsKeepsCodeMarkerGuidanceForZeroLine(t *testing.T) {
	got := pausePointStateNextActions("scene.cs:0", pausePointStatusResponse{})
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

func failedPausePointTriggerResult() *pausePointTriggerResult {
	return &pausePointTriggerResult{
		Command:   "simulate-mouse-input --action Click --button Right",
		Completed: true,
		Response:  json.RawMessage(pausePointRejectedTriggerResponse("Assets/Scripts/Foo.cs:1")),
	}
}

func dispatchFailedPausePointTriggerResult() *pausePointTriggerResult {
	return &pausePointTriggerResult{
		Command:   "simulate-mouse-input --action Click --button Right",
		Completed: true,
		Error:     `{"Error":{"ErrorCode":"UNITY_NOT_REACHABLE"}}`,
	}
}

func succeededPausePointTriggerResult() *pausePointTriggerResult {
	return &pausePointTriggerResult{
		Command:   "simulate-keyboard --action Press --key Space",
		Completed: true,
		Response:  json.RawMessage(`{"Success":true}`),
	}
}

func playingPausedEditorState() pausePointEditorState {
	return pausePointEditorState{IsPlaying: true, IsPaused: true, CapturedAt: "Current"}
}

// Verifies a rejected trigger on timeout replaces the auto-cleared non-firing hint and sets
// TriggerFailed, including when the Editor is still paused. The want string is a full literal
// so a wording change cannot pass by moving with the constant.
func TestPausePointTimeoutError_TriggerRejected_SetsHintAndTriggerFailed(t *testing.T) {
	response := pausePointStatusResponse{
		Status:      pausePointStatusCleared,
		EditorState: playingPausedEditorState(),
	}
	cliErr := pausePointWaitError("/tmp/MyProject", waitForPausePointOptions{
		id:             "Assets/Scripts/Foo.cs:2",
		timeoutSeconds: 25,
	}, response, pausePointWaitStateTimeout, false, true, failedPausePointTriggerResult())

	const wantHint = "The trigger command ran but was rejected. Read Details.TriggerResult (Response.Message, or Error when the command failed to dispatch) for the reason (for example, input commands are rejected while PlayMode is paused by an earlier pause-point hit). Resume PlayMode with 'clear-pause-point --all' (which releases a pause owned by any marker) or 'control-play-mode --action Play', then re-enable the marker and retry."
	if cliErr.Details["Hint"] != wantHint {
		t.Fatalf("hint mismatch:\n got: %#v\nwant: %q", cliErr.Details["Hint"], wantHint)
	}
	if cliErr.Details["TriggerFailed"] != true {
		t.Fatalf("TriggerFailed mismatch: %#v", cliErr.Details["TriggerFailed"])
	}
}

// Verifies a trigger dispatch failure on timeout uses the same rejected Hint (which names Error
// as well as Response.Message) and still sets TriggerFailed.
func TestPausePointTimeoutError_TriggerDispatchFailed_SetsErrorHintAndTriggerFailed(t *testing.T) {
	response := pausePointStatusResponse{
		Status:      pausePointStatusCleared,
		EditorState: playingPausedEditorState(),
	}
	cliErr := pausePointWaitError("/tmp/MyProject", waitForPausePointOptions{
		id:             "Assets/Scripts/Foo.cs:2",
		timeoutSeconds: 25,
	}, response, pausePointWaitStateTimeout, false, true, dispatchFailedPausePointTriggerResult())

	if cliErr.Details["Hint"] != pausePointHintTriggerRejected {
		t.Fatalf("hint mismatch:\n got: %#v\nwant: %q", cliErr.Details["Hint"], pausePointHintTriggerRejected)
	}
	if cliErr.Details["TriggerFailed"] != true {
		t.Fatalf("TriggerFailed mismatch: %#v", cliErr.Details["TriggerFailed"])
	}
}

// Verifies a successful trigger on timeout keeps the existing auto-cleared diagnosis and omits
// TriggerFailed.
func TestPausePointTimeoutError_TriggerSucceeded_KeepsExistingHint(t *testing.T) {
	response := pausePointStatusResponse{
		Status:      pausePointStatusCleared,
		EditorState: pausePointEditorState{IsPlaying: true, IsPaused: false, CapturedAt: "Current"},
	}
	cliErr := pausePointWaitError("/tmp/MyProject", waitForPausePointOptions{
		id:             "jump",
		timeoutSeconds: 25,
	}, response, pausePointWaitStateTimeout, false, true, succeededPausePointTriggerResult())

	wantHint := pausePointHintTimeoutAutoCleared + pausePointNonFiringPatternsHint
	if cliErr.Details["Hint"] != wantHint {
		t.Fatalf("hint mismatch:\n got: %#v\nwant: %q", cliErr.Details["Hint"], wantHint)
	}
	if _, ok := cliErr.Details["TriggerFailed"]; ok {
		t.Fatalf("TriggerFailed must be omitted on a successful trigger: %#v", cliErr.Details["TriggerFailed"])
	}
}

// Verifies a timeout with no trigger keeps the existing auto-cleared diagnosis.
func TestPausePointTimeoutError_NoTrigger_KeepsExistingHint(t *testing.T) {
	response := pausePointStatusResponse{
		Status:      pausePointStatusCleared,
		EditorState: pausePointEditorState{IsPlaying: true, IsPaused: false, CapturedAt: "Current"},
	}
	cliErr := pausePointWaitError("/tmp/MyProject", waitForPausePointOptions{
		id:             "jump",
		timeoutSeconds: 25,
	}, response, pausePointWaitStateTimeout, false, true, nil)

	wantHint := pausePointHintTimeoutAutoCleared + pausePointNonFiringPatternsHint
	if cliErr.Details["Hint"] != wantHint {
		t.Fatalf("hint mismatch:\n got: %#v\nwant: %q", cliErr.Details["Hint"], wantHint)
	}
	if _, ok := cliErr.Details["TriggerFailed"]; ok {
		t.Fatalf("TriggerFailed must be omitted when no trigger ran: %#v", cliErr.Details["TriggerFailed"])
	}
}

// Verifies a rejected trigger on expiry uses the same trigger-rejected hint and TriggerFailed flag.
func TestPausePointExpiredError_TriggerRejected_SetsHintAndTriggerFailed(t *testing.T) {
	response := pausePointStatusResponse{
		Status:      pausePointStatusExpired,
		Expired:     true,
		EditorState: playingPausedEditorState(),
	}
	cliErr := pausePointWaitError("/tmp/MyProject", waitForPausePointOptions{
		id:             "Assets/Scripts/Foo.cs:2",
		timeoutSeconds: 25,
	}, response, pausePointWaitStateExpired, false, false, failedPausePointTriggerResult())

	if cliErr.Details["Hint"] != pausePointHintTriggerRejected {
		t.Fatalf("hint mismatch:\n got: %#v\nwant: %q", cliErr.Details["Hint"], pausePointHintTriggerRejected)
	}
	if cliErr.Details["TriggerFailed"] != true {
		t.Fatalf("TriggerFailed mismatch: %#v", cliErr.Details["TriggerFailed"])
	}
}

// Verifies a successful trigger on expiry keeps the existing paused hint and omits TriggerFailed.
func TestPausePointExpiredError_TriggerSucceeded_KeepsExistingHint(t *testing.T) {
	response := pausePointStatusResponse{
		Status:      pausePointStatusExpired,
		Expired:     true,
		EditorState: playingPausedEditorState(),
	}
	cliErr := pausePointWaitError("/tmp/MyProject", waitForPausePointOptions{
		id:             "jump",
		timeoutSeconds: 25,
	}, response, pausePointWaitStateExpired, false, false, succeededPausePointTriggerResult())

	if cliErr.Details["Hint"] != pausePointHintEditorAlreadyPaused {
		t.Fatalf("hint mismatch:\n got: %#v\nwant: %q", cliErr.Details["Hint"], pausePointHintEditorAlreadyPaused)
	}
	if _, ok := cliErr.Details["TriggerFailed"]; ok {
		t.Fatalf("TriggerFailed must be omitted on a successful trigger: %#v", cliErr.Details["TriggerFailed"])
	}
}

// Verifies hot-reload suppression still wins the hint, while TriggerFailed stays set so the
// rejection is visible in Details.
func TestPausePointTimeoutHint_SuppressedByHotReload_WinsOverTriggerRejected(t *testing.T) {
	response := pausePointStatusResponse{
		Status:                pausePointStatusEnabled,
		SuppressedByHotReload: true,
		EditorState:           playingPausedEditorState(),
	}
	hint := pausePointTimeoutHint(response, false, false, failedPausePointTriggerResult())
	if hint != pausePointHintSuppressedByHotReload {
		t.Fatalf("expected suppressed hint, got %q", hint)
	}

	cliErr := pausePointWaitError("/tmp/MyProject", waitForPausePointOptions{
		id:             "jump",
		timeoutSeconds: 25,
	}, response, pausePointWaitStateTimeout, false, false, failedPausePointTriggerResult())
	if cliErr.Details["Hint"] != pausePointHintSuppressedByHotReload {
		t.Fatalf("hint mismatch: %#v", cliErr.Details["Hint"])
	}
	if cliErr.Details["TriggerFailed"] != true {
		t.Fatalf("TriggerFailed must still be set when the trigger failed: %#v", cliErr.Details["TriggerFailed"])
	}
}

// Verifies a rejected trigger wins over the already-hit new-hit-baseline diagnosis.
func TestPausePointTimeoutError_TriggerRejected_WinsOverNewHitBaseline(t *testing.T) {
	response := pausePointStatusResponse{
		Status:      pausePointStatusHit,
		HitCount:    1,
		EditorState: playingPausedEditorState(),
	}
	cliErr := pausePointWaitError("/tmp/MyProject", waitForPausePointOptions{
		id:             "jump",
		timeoutSeconds: 25,
	}, response, pausePointWaitStateTimeout, true, false, failedPausePointTriggerResult())

	const wantHint = "The trigger command ran but was rejected. Read Details.TriggerResult (Response.Message, or Error when the command failed to dispatch) for the reason (for example, input commands are rejected while PlayMode is paused by an earlier pause-point hit). Resume PlayMode with 'clear-pause-point --all' (which releases a pause owned by any marker) or 'control-play-mode --action Play', then re-enable the marker and retry."
	if cliErr.Details["Hint"] != wantHint {
		t.Fatalf("hint mismatch:\n got: %#v\nwant: %q", cliErr.Details["Hint"], wantHint)
	}
	if cliErr.Details["TriggerFailed"] != true {
		t.Fatalf("TriggerFailed mismatch: %#v", cliErr.Details["TriggerFailed"])
	}
}
