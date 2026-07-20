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
		hint := pausePointTimeoutHint(response)
		if hint != "" {
			timeoutError.Details["Hint"] = hint
		}
		return timeoutError
	}
}

const (
	pausePointHintPlayModeNotRunning  = "PlayMode is not running. Start PlayMode (or trigger the marker code path in Edit Mode), then wait again."
	pausePointHintEditorAlreadyPaused = "Unity is already paused, so gameplay cannot reach the marker. Resume PlayMode before waiting again."
)

// pausePointTimeoutHint maps the final probed status to a deterministic diagnosis,
// because timeouts are where agents struggle to tell a missed code path from Editor state.
func pausePointTimeoutHint(response pausePointStatusResponse) string {
	if !response.EditorState.IsPlaying {
		return pausePointHintPlayModeNotRunning
	}
	if response.EditorState.IsPaused {
		return pausePointHintEditorAlreadyPaused
	}
	if response.HitCount == 0 && response.Status == pausePointStatusEnabled {
		return "Marker was enabled but never hit. Confirm the id matches UloopPausePoint.Pause(\"<id>\") and that the code path was executed. In fast-progressing games the state may have already moved past the marker (for example back to Ready or GameOver), so re-trigger the code path and wait again. " +
			"If the marker targets a Unity message method such as OnCollisionEnter2D/OnTriggerEnter2D, check whether `enable-pause-point`'s response carried a Warning about cached message dispatch: Unity can resolve a GameObject's message dispatch before the marker patch is installed, so a GameObject that already existed at enable time may never reach the marker even though the method body runs. Recreating the GameObject after enabling, or embedding UloopPausePoint.Pause(\"id\") directly in the method body, avoids this. " +
			"If the target line is inside a very small method, Mono's JIT may have inlined it into callers and the pause point never fires; move the pause point into the calling method."
	}
	return ""
}

// pausePointExpiredHint mirrors the timeout diagnosis for expired markers, because a marker
// whose enable window ends before the wait deadline surfaces as PAUSE_POINT_EXPIRED instead
// of a timeout and would otherwise carry no hint at all.
func pausePointExpiredHint(response pausePointStatusResponse) string {
	if !response.EditorState.IsPlaying {
		return pausePointHintPlayModeNotRunning
	}
	if response.EditorState.IsPaused {
		return pausePointHintEditorAlreadyPaused
	}
	if response.HitCount == 0 {
		return "Marker expired before it was hit: the enable-pause-point --timeout-seconds window (measured from enable, not from this wait) ran out. Re-enable the marker with a longer --timeout-seconds and trigger the code path again."
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
