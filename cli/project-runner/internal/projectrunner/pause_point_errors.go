package projectrunner

import (
	"fmt"
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
) clierrors.CLIError {
	response = normalizePausePointStatusResponse(response)

	switch state {
	case pausePointWaitStateNotEnabled:
		return pausePointStateError(
			clierrors.ErrorCodePausePointNotEnabled,
			"Pause point is not enabled.",
			projectRoot,
			options,
			response,
			false)
	case pausePointWaitStateExpired:
		expiredError := pausePointStateError(
			clierrors.ErrorCodePausePointExpired,
			"Pause point expired before it was hit.",
			projectRoot,
			options,
			response,
			true)
		hint := pausePointExpiredHint(response)
		if hint != "" {
			expiredError.Details["Hint"] = hint
		}
		return expiredError
	case pausePointWaitStateTriggerFailed:
		triggerFailedError := pausePointStateError(
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
		triggerFailedError.NextActions = pausePointTriggerFailedNextActions(options.id)
		return triggerFailedError
	case pausePointWaitStateCleared:
		return pausePointStateError(
			clierrors.ErrorCodePausePointCleared,
			"Pause point was cleared before it was hit.",
			projectRoot,
			options,
			response,
			true)
	default:
		timeoutError := pausePointStateError(
			clierrors.ErrorCodePausePointWaitTimeout,
			fmt.Sprintf("Pause point was not hit within %ds.", options.timeoutSeconds),
			projectRoot,
			options,
			response,
			true)
		hint := pausePointTimeoutHint(response, hasNewHitBaseline)
		if hint != "" {
			timeoutError.Details["Hint"] = hint
		}
		return timeoutError
	}
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
		"Check the rejected value against the triggered command's own `--help` before retrying, so the " +
			"same value is not retried twice.",
	}
}

const (
	pausePointHintPlayModeNotRunning  = "PlayMode is not running. Start PlayMode (or trigger the marker code path in Edit Mode), then wait again."
	pausePointHintEditorAlreadyPaused = "Unity is already paused, so gameplay cannot reach the marker. Resume PlayMode before waiting again."

	// Returned when await timed out while waiting for a new hit on an already-hit continuous/trace
	// marker. Why not reuse pausePointHintEditorAlreadyPaused: that hint diagnoses a marker that
	// never fired, whereas here the marker already hit and the wait needs Play resumed so a later
	// sequence can occur.
	pausePointHintAlreadyHitWaitingForNew = "The marker had already hit and Unity may still be paused by that hit; pass --resume-play or resume Play Mode so a new hit can occur."

	// Shared by both pausePointTimeoutHint and pausePointExpiredHint: patterns where the method
	// body genuinely ran (or was invoked) yet the marker never fired — a physics/message callback
	// missing a pre-existing GameObject, a pre-bound delegate bypassing the patch, or control flow
	// exiting on an earlier branch before the target line. Kept as a single constant so the two
	// hints stay in sync instead of drifting copies of the same diagnosis.
	pausePointNonFiringPatternsHint = "If the target line never hit despite the trigger firing, check the non-firing patterns: " +
		"(1) the method is a physics/message callback or is called from one on a GameObject that existed before enable — recreate the GameObject or embed UloopPausePoint.Pause; " +
		"(2) the method was already bound into a delegate/event before enable — the pre-bound invocation path bypasses the patch; " +
		"(3) the method ran but exited on an earlier branch (for example a guard rejected the action because game state had already moved on) — arm a second marker on the early-return line to see which path ran."

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
func pausePointTimeoutHint(response pausePointStatusResponse, hasNewHitBaseline bool) string {
	if response.SuppressedByHotReload {
		return pausePointSuppressedByHotReloadHint(response)
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
func pausePointExpiredHint(response pausePointStatusResponse) string {
	if response.SuppressedByHotReload {
		return pausePointSuppressedByHotReloadHint(response)
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
		NextActions: []string{
			"Run `uloop enable-pause-point --id <marker-id>` before waiting.",
			"Confirm the code path calls `UloopPausePoint.Pause(\"<marker-id>\")` with the same id.",
			"Check `Details.Status`, `Details.EditorState`, `Details.ElapsedSinceEnabledMilliseconds`, and `Details.RemainingMilliseconds` to distinguish a missed code path from an already-paused Editor.",
			"If the marker is inside a custom asmdef, add a reference to `UnityCLILoop.PausePoints.Runtime`.",
		},
		Details: map[string]any{
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
		},
	}
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
