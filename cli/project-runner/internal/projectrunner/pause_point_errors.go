package projectrunner

import (
	"fmt"
	"strconv"
	"strings"
	"time"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"

	"github.com/hatayama/unity-cli-loop/common/clicore"
)

func pausePointWaitError(
	projectRoot string,
	options waitForPausePointOptions,
	response pausePointStatusResponse,
	state pausePointWaitState,
	hasNewHitBaseline bool,
	markerClearedByThisCommand bool,
	triggerResult *pausePointTriggerResult,
) clierrors.CLIError {
	response = normalizePausePointStatusResponse(response)

	waitErr := clierrors.CLIError{}
	switch state {
	case pausePointWaitStateNotEnabled:
		waitErr = pausePointStateError(
			clierrors.ErrorCodePausePointNotEnabled,
			"Pause point is not enabled.",
			projectRoot,
			options,
			response,
			false)
	case pausePointWaitStateExpired:
		waitErr = pausePointStateError(
			clierrors.ErrorCodePausePointExpired,
			"Pause point expired before it was hit.",
			projectRoot,
			options,
			response,
			true)
		hint := pausePointExpiredHint(response, triggerResult)
		if hint != "" {
			waitErr.Details["Hint"] = hint
		}
		applyPausePointExpiredResolvedFieldDetails(waitErr.Details, response)
		if note := pausePointExpiredResolvedFieldsNote(response); note != "" {
			waitErr.Message = waitErr.Message + " " + note
		}
	case pausePointWaitStateTriggerFailed:
		waitErr = pausePointStateError(
			clierrors.ErrorCodePausePointTriggerFailed,
			"The --trigger command was rejected before it ran (argument parsing or an unknown command "+
				"name), so the wait was abandoned instead of waiting out the remaining timeout. This "+
				"command did not clear the marker: see Details.TriggerResult for the rejection and "+
				"Details.RemainingMilliseconds for how long the marker stays armed. A zero "+
				"RemainingMilliseconds with an empty Details.Status means the final status re-read "+
				"failed — run pause-point-status to confirm the marker.",
			projectRoot,
			options,
			response,
			// Retrying the identical command reproduces the same rejection: the trigger value has to
			// change first. Reporting a permanent failure as retryable is what made the original
			// incident waste a full timeout window on it.
			false)
		waitErr.NextActions = pausePointTriggerFailedNextActions(options.id)
	case pausePointWaitStateCleared:
		waitErr = pausePointStateError(
			clierrors.ErrorCodePausePointCleared,
			"Pause point was cleared before it was hit.",
			projectRoot,
			options,
			response,
			true)
	default:
		waitErr = pausePointStateError(
			clierrors.ErrorCodePausePointWaitTimeout,
			fmt.Sprintf("Pause point was not hit within %ds.", options.timeoutSeconds),
			projectRoot,
			options,
			response,
			true)
		hint := pausePointTimeoutHint(response, hasNewHitBaseline, markerClearedByThisCommand, triggerResult)
		if hint != "" {
			waitErr.Details["Hint"] = hint
		}
		if markerClearedByThisCommand {
			waitErr.Details["MarkerClearedByThisCommand"] = true
		}
	}
	if pausePointTriggerFailed(triggerResult) {
		waitErr.Details["TriggerFailed"] = true
	}
	return waitErr
}

// pausePointTriggerFailedNextActions replaces the generic enable/id-mismatch guidance, which does
// not apply here: the marker was confirmed armed and only the --trigger value is wrong.
//
// Why re-running the same command comes first: this response answers the command the caller just
// ran, so "fix the --trigger value in that command and run it again" asks them to change one value
// they already typed, with no argument they have to guess. Re-enabling is also the cleaner reset —
// it starts a fresh marker entry (HitCount and IsHit back to zero, the --timeout-seconds countdown
// restarted) while re-patching an id that is already patched is a no-op.
//
// Why the await form carries the real id: it is the one recovery command this function can spell
// out completely, and naming a command without its arguments is exactly the failure this guidance
// exists to prevent.
func pausePointTriggerFailedNextActions(id string) []string {
	return []string{
		"Fix the --trigger value in the command you just ran and run that command again. Re-running " +
			"`enable-pause-point --await` is safe and is the cleanest reset: it restarts the marker's " +
			"HitCount and --timeout-seconds countdown, and re-patching an already patched id is a no-op.",
		fmt.Sprintf(
			"The marker is still armed, so you can also wait on it directly: "+
				"uloop await-pause-point --id %q --trigger \"<corrected trigger command>\"", id),
		"For an INVALID_ARGUMENT rejection, check the rejected value against the triggered command's own --help; for UNKNOWN_COMMAND, the first token must be a uloop subcommand name written without the leading 'uloop'.",
	}
}

const (
	pausePointHintPlayModeNotRunning  = "PlayMode is not running. Start PlayMode (or trigger the marker code path in Edit Mode), then wait again."
	pausePointHintEditorAlreadyPaused = "Unity is already paused, so gameplay cannot reach the marker. Resume PlayMode before waiting again."

	// Diagnoses a completed --trigger that Unity rejected (Success:false) or whose dispatch
	// failed (Error). Why before the IsPaused / auto-cleared / non-firing branches: those assume
	// the trigger fired and the marker still missed, which is the opposite of this case and is
	// what sent agents into execute-dynamic-code loops on a paused-PlayMode input rejection.
	pausePointHintTriggerRejected = "The trigger command ran but was rejected. Read Details.TriggerResult (Response.Message, or Error when the command failed to dispatch) for the reason (for example, input commands are rejected while PlayMode is paused by an earlier pause-point hit). Resume PlayMode with 'clear-pause-point --all' (which releases a pause owned by any marker) or 'control-play-mode --action Play', then re-enable the marker and retry."

	// Returned when await timed out while waiting for a new hit on an already-hit continuous/trace
	// marker. Why not reuse pausePointHintEditorAlreadyPaused: that hint diagnoses a marker that
	// never fired, whereas here the marker already hit and the wait needs Play resumed so a later
	// sequence can occur.
	pausePointHintAlreadyHitWaitingForNew = "The marker had already hit and Unity may still be paused by that hit; pass --resume-play or resume Play Mode so a new hit can occur."

	pausePointHintTimeoutAutoCleared = "This command disarmed the marker on timeout; re-enable the pause point (enable-pause-point) before waiting again. "

	// Explains how to read resolved-line Details on Expired when HitCount is still 0.
	// Why not mention ResolvedLineText: C# omits empty text, so Details may carry only ResolvedLine.
	// Why not emit this when HitCount > 0: a trace/continuous hit can land just before expiry and
	// the next poll then reports Expired; claiming the line never ran would contradict HitCount.
	pausePointExpiredResolvedFieldsGuidance = "The marker stayed armed at the resolved line shown in Details; that line was never executed within the window."

	// Shared by both pausePointTimeoutHint and pausePointExpiredHint: reasons a wait saw no hit —
	// a physics/message callback missing a pre-existing GameObject, a pre-bound delegate
	// bypassing the patch, control flow exiting on an earlier branch, or --line resolving
	// against a compiled map that no longer matches the editor after a hot reload. Kept as a
	// single constant so the two hints stay in sync instead of drifting copies of the same
	// diagnosis.
	pausePointNonFiringPatternsHint = "If the target line never hit despite the trigger firing, check the non-firing patterns: " +
		"(1) the method is a physics/message callback or is called from one on a GameObject that existed before enable — recreate the GameObject or embed UloopPausePoint.Pause; " +
		"(2) the method was already bound into a delegate/event before enable — the pre-bound invocation path bypasses the patch; " +
		"(3) the method ran but exited on an earlier branch (for example a guard rejected the action because game state had already moved on) — arm a second marker on the early-return line to see which path ran. " +
		"(4) the file has active hot-reload patches and the marker resolved against the last compiled source, so the armed line may sit in a different method than the editor shows — check ResolvedMethod, or run 'uloop compile' and re-enable." +
		" For patterns (1) and (2), hot-reloading a temporary log line into the method (`uloop hot-reload`) and re-triggering gives a one-way check: the log appearing proves the body ran even though the marker missed. The log staying absent proves nothing — the same cached dispatch can bypass the hot-reload patch too." +
		" Note: arming that temporary hot reload itself creates the pattern (4) condition for any later --line in the same file."

	// pausePointHintSuppressedByHotReload short-circuits every other timeout diagnosis:
	// a suppressed marker cannot fire no matter what the caller does in PlayMode.
	pausePointHintSuppressedByHotReload = "The marker's method is hot-reload patched and the marker could not be re-targeted onto the patched body. Revert the patch with 'uloop hot-reload --revert-all' or run 'uloop compile', then re-enable the marker."
)

// pausePointSuppressedByHotReloadHint returns Unity's reason alone when present: both
// retarget/restore reason strings already include recovery steps, and concatenating the
// fixed "currently patched / revert-all" fallback after a restore-failure reason contradicts
// itself (the patch was already reverted).
func pausePointSuppressedByHotReloadHint(response pausePointStatusResponse) string {
	if response.SuppressedByHotReloadReason != "" {
		return response.SuppressedByHotReloadReason
	}
	return pausePointHintSuppressedByHotReload
}

// pausePointTimeoutHint maps the final probed status to a deterministic diagnosis,
// because timeouts are where agents struggle to tell a missed code path from Editor state.
func pausePointTimeoutHint(
	response pausePointStatusResponse,
	hasNewHitBaseline bool,
	markerClearedByThisCommand bool,
	triggerResult *pausePointTriggerResult,
) string {
	if response.SuppressedByHotReload {
		return pausePointSuppressedByHotReloadHint(response)
	}
	if pausePointTriggerFailed(triggerResult) {
		return pausePointHintTriggerRejected
	}
	if hasNewHitBaseline {
		return pausePointHintAlreadyHitWaitingForNew
	}
	if !response.EditorState.IsPlaying {
		return pausePointHintPlayModeNotRunning
	}
	if response.EditorState.IsPaused {
		return pausePointHintEditorAlreadyPaused
	}
	if markerClearedByThisCommand {
		return pausePointHintTimeoutAutoCleared + pausePointNonFiringPatternsHint
	}
	if response.HitCount == 0 && response.Status == pausePointStatusEnabled {
		return "Marker was enabled but never hit. Confirm the id matches UloopPausePoint.Pause(\"<id>\") and that the code path was executed. In fast-progressing games the state may have already moved past the marker (for example back to Ready or GameOver), so re-trigger the code path and wait again. " +
			"If the marker targets a Unity message method such as OnCollisionEnter2D/OnTriggerEnter2D, check whether `enable-pause-point`'s response carried a Warning about cached message dispatch: Unity can resolve a GameObject's message dispatch before the marker patch is installed, so a GameObject that already existed at enable time may never reach the marker even though the method body runs. Recreating the GameObject after enabling, or embedding UloopPausePoint.Pause(\"id\") directly in the method body, avoids this. " +
			"If the target line is inside a very small method, Mono's JIT may have inlined it into callers and the pause point never fires; move the pause point into the calling method. " +
			"If PlayMode kept progressing on its own while you were arranging state (timers, gravity, spawners), the scenario may have already been consumed before this marker could fire; next time, run `control-play-mode --action Pause` before setup and resume with `control-play-mode --action Play` only after `enable-pause-point` succeeds. " +
			pausePointNonFiringPatternsHint
	}
	return ""
}

// pausePointExpiredHint mirrors the timeout diagnosis for expired markers, because a marker
// whose enable window ends before the wait deadline surfaces as PAUSE_POINT_EXPIRED instead
// of a timeout and would otherwise carry no hint at all.
func pausePointExpiredHint(response pausePointStatusResponse, triggerResult *pausePointTriggerResult) string {
	if response.SuppressedByHotReload {
		return pausePointSuppressedByHotReloadHint(response)
	}
	if pausePointTriggerFailed(triggerResult) {
		return pausePointHintTriggerRejected
	}
	if !response.EditorState.IsPlaying {
		return pausePointHintPlayModeNotRunning
	}
	if response.EditorState.IsPaused {
		return pausePointHintEditorAlreadyPaused
	}
	if response.HitCount == 0 {
		return "Marker expired before it was hit: the enable-pause-point --timeout-seconds window (measured from enable, not from this wait) ran out. Re-enable the marker with a longer --timeout-seconds and trigger the code path again. " +
			pausePointNonFiringPatternsHint
	}
	return ""
}

func pausePointStateError(
	errorCode string,
	message string,
	projectRoot string,
	options waitForPausePointOptions,
	response pausePointStatusResponse,
	retryable bool,
) clierrors.CLIError {
	return clierrors.CLIError{
		ErrorCode:   errorCode,
		Phase:       clierrors.ErrorPhaseResponseWaiting,
		Message:     message,
		Retryable:   retryable,
		SafeToRetry: retryable,
		ProjectRoot: projectRoot,
		Command:     clicore.PausePointAwaitCommandName,
		NextActions: pausePointStateNextActions(options.id, response),
		Details:     pausePointStateErrorDetails(options, response),
	}
}

func pausePointStateNextActions(id string, response pausePointStatusResponse) []string {
	rearmAction := "Run `uloop enable-pause-point --id <marker-id>` before waiting."
	confirmAction := "Confirm the code path calls `UloopPausePoint.Pause(\"<marker-id>\")` with the same id."
	if path, line, ok := parsePausePointFileLineID(id); ok {
		rearmAction = fmt.Sprintf(
			"Re-arm it with uloop enable-pause-point --file %q --line %d before waiting.", path, line)
		confirmAction = fmt.Sprintf(
			"Confirm the code path executes line %d of %s while the marker is armed.", line, path)
	}
	nextActions := []string{
		rearmAction,
		confirmAction,
		"Check `Details.Status`, `Details.EditorState`, `Details.ElapsedSinceEnabledMilliseconds`, and `Details.RemainingMilliseconds` to distinguish a missed code path from an already-paused Editor.",
		"If the marker is inside a custom asmdef, add a reference to `UnityCLILoop.PausePoints.Runtime`.",
	}
	if response.RecommendedNextAction == "" {
		return nextActions
	}
	return append([]string{response.RecommendedNextAction}, nextActions...)
}

// parsePausePointFileLineID reports a file:line marker id that ends in `.cs:<digits>`.
// Why this suffix only: code-marker ids can contain colons, so a generic last-colon split
// would rewrite Pause("scene:jump") guidance into a --file/--line command that cannot arm it.
func parsePausePointFileLineID(id string) (string, int, bool) {
	suffixIndex := strings.LastIndex(id, ".cs:")
	if suffixIndex < 0 {
		return "", 0, false
	}
	path := id[:suffixIndex+len(".cs")]
	lineText := id[suffixIndex+len(".cs:"):]
	if lineText == "" {
		return "", 0, false
	}
	for _, r := range lineText {
		if r < '0' || r > '9' {
			return "", 0, false
		}
	}
	line, err := strconv.Atoi(lineText)
	if err != nil {
		return "", 0, false
	}
	// Why reject 0: C# only builds file:line ids from Line > 0, so `--line 0` is always
	// rejected by enable-pause-point. Digits-only parsing makes this a zero check.
	if line <= 0 {
		return "", 0, false
	}
	return path, line, true
}

func pausePointStateErrorDetails(
	options waitForPausePointOptions,
	response pausePointStatusResponse,
) map[string]any {
	details := map[string]any{
		"Id":                              options.id,
		"Status":                          response.Status,
		"Expired":                         response.Expired,
		"HitCount":                        response.HitCount,
		"TimeoutSeconds":                  pausePointMarkerTimeoutSeconds(options, response),
		"EnabledAtUtc":                    response.EnabledAtUtc,
		"ElapsedSinceEnabledMilliseconds": response.ElapsedSinceEnabledMilliseconds,
		"Generation":                      response.Generation,
		"EditorState":                     response.EditorState,
		"RemainingMilliseconds":           pausePointRemainingMilliseconds(options, response),
		"MarkerMessage":                   response.Message,
		"RecommendedNextAction":           response.RecommendedNextAction,
		"SuppressedByHotReload":           response.SuppressedByHotReload,
	}
	if response.ClearedReason != "" {
		details["ClearedReason"] = response.ClearedReason
	}
	if response.StatusBeforeClear != "" {
		details["StatusBeforeClear"] = response.StatusBeforeClear
	}
	if response.LateHitDiscardedAfterClear {
		details["LateHitDiscardedAfterClear"] = true
	}
	return details
}

func pausePointMarkerTimeoutSeconds(options waitForPausePointOptions, response pausePointStatusResponse) int {
	if response.TimeoutSeconds > 0 {
		return response.TimeoutSeconds
	}
	return options.timeoutSeconds
}

func pausePointRemainingMilliseconds(options waitForPausePointOptions, response pausePointStatusResponse) int64 {
	if response.RemainingMilliseconds > 0 {
		return response.RemainingMilliseconds
	}
	if response.Expired || response.Status == pausePointStatusExpired || response.Status == pausePointStatusHit ||
		response.Status == pausePointStatusCleared {
		return 0
	}

	timeoutSeconds := response.TimeoutSeconds
	if timeoutSeconds <= 0 {
		return 0
	}

	totalMilliseconds := int64(timeoutSeconds) * int64(time.Second/time.Millisecond)
	remainingMilliseconds := totalMilliseconds - response.ElapsedSinceEnabledMilliseconds
	if remainingMilliseconds <= 0 {
		return 0
	}
	return remainingMilliseconds
}

func pausePointExpiredResolvedFieldsNote(response pausePointStatusResponse) string {
	if response.HitCount != 0 || response.ResolvedLine == 0 {
		return ""
	}
	return pausePointExpiredResolvedFieldsGuidance
}

func applyPausePointExpiredResolvedFieldDetails(details map[string]any, response pausePointStatusResponse) {
	if response.ResolvedLine != 0 {
		details["ResolvedLine"] = response.ResolvedLine
	}
	if response.ResolvedLineText != "" {
		details["ResolvedLineText"] = response.ResolvedLineText
	}
	if response.ResolvedMethod != "" {
		details["ResolvedMethod"] = response.ResolvedMethod
	}
	if response.SnapshotTiming != "" {
		details["SnapshotTiming"] = response.SnapshotTiming
	}
}
