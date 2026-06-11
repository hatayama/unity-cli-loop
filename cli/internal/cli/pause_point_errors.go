package cli

import (
	"fmt"
	"time"
)

func pausePointWaitError(
	projectRoot string,
	options waitForPausePointOptions,
	response pausePointStatusResponse,
	state pausePointWaitState,
) cliError {
	switch state {
	case pausePointWaitStateNotEnabled:
		return pausePointStateError(
			errorCodePausePointNotEnabled,
			"Pause point is not enabled.",
			projectRoot,
			options,
			response,
			false)
	case pausePointWaitStateExpired:
		expiredError := pausePointStateError(
			errorCodePausePointExpired,
			"Pause point expired before it was hit.",
			projectRoot,
			options,
			response,
			true)
		hint := pausePointExpiredHint(response)
		if hint != "" {
			expiredError.Details["hint"] = hint
		}
		return expiredError
	case pausePointWaitStateCleared:
		return pausePointStateError(
			errorCodePausePointCleared,
			"Pause point was cleared before it was hit.",
			projectRoot,
			options,
			response,
			true)
	default:
		timeoutError := pausePointStateError(
			errorCodePausePointWaitTimeout,
			fmt.Sprintf("Pause point was not hit within %ds.", options.timeoutSeconds),
			projectRoot,
			options,
			response,
			true)
		hint := pausePointTimeoutHint(response)
		if hint != "" {
			timeoutError.Details["hint"] = hint
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
	if !response.IsPlaying {
		return pausePointHintPlayModeNotRunning
	}
	if response.IsPaused {
		return pausePointHintEditorAlreadyPaused
	}
	if response.HitCount == 0 && response.Status == pausePointStatusEnabled {
		return "Marker was enabled but never hit. Confirm the id matches UloopPausePoint.Pause(\"<id>\") and that the code path was executed. In fast-progressing games the state may have already moved past the marker (for example back to Ready or GameOver), so re-trigger the code path and wait again."
	}
	return ""
}

// pausePointExpiredHint mirrors the timeout diagnosis for expired markers, because a marker
// whose enable window ends before the wait deadline surfaces as PAUSE_POINT_EXPIRED instead
// of a timeout and would otherwise carry no hint at all.
func pausePointExpiredHint(response pausePointStatusResponse) string {
	if !response.IsPlaying {
		return pausePointHintPlayModeNotRunning
	}
	if response.IsPaused {
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
) cliError {
	return cliError{
		ErrorCode:   errorCode,
		Phase:       errorPhaseResponseWaiting,
		Message:     message,
		Retryable:   retryable,
		SafeToRetry: retryable,
		ProjectRoot: projectRoot,
		Command:     pausePointWaitCommandName,
		NextActions: []string{
			"Run `uloop enable-pause-point --id <marker-id>` before waiting.",
			"Confirm the code path calls `UloopPausePoint.Pause(\"<marker-id>\")` with the same id.",
			"Check `details.status`, `details.isPlaying`, `details.isPaused`, `details.elapsedSinceEnabledMilliseconds`, and `details.remainingMilliseconds` to distinguish a missed code path from an already-paused Editor.",
			"If the marker is inside a custom asmdef, add a reference to `UnityCLILoop.PausePoints.Runtime`.",
		},
		Details: map[string]any{
			"id":                              options.id,
			"status":                          response.Status,
			"hitCount":                        response.HitCount,
			"timeoutSeconds":                  options.timeoutSeconds,
			"elapsedSinceEnabledMilliseconds": response.ElapsedSinceEnabledMilliseconds,
			"isPlaying":                       response.IsPlaying,
			"isPaused":                        response.IsPaused,
			"remainingMilliseconds":           pausePointRemainingMilliseconds(options, response),
			"markerMessage":                   response.Message,
		},
	}
}

func pausePointRemainingMilliseconds(options waitForPausePointOptions, response pausePointStatusResponse) int64 {
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
