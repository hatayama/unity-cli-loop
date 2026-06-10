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
		return pausePointStateError(
			errorCodePausePointExpired,
			"Pause point expired before it was hit.",
			projectRoot,
			options,
			response,
			true)
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

// pausePointTimeoutHint maps the final probed status to a deterministic diagnosis,
// because timeouts are where agents struggle to tell a missed code path from Editor state.
func pausePointTimeoutHint(response pausePointStatusResponse) string {
	if !response.IsPlaying {
		return "PlayMode is not running. Start PlayMode (or trigger the marker code path in Edit Mode), then wait again."
	}
	if response.IsPaused {
		return "Unity is already paused, so gameplay cannot reach the marker. Resume PlayMode before waiting again."
	}
	if response.HitCount == 0 && response.Status == pausePointStatusEnabled {
		return "Marker was enabled but never hit. Confirm the id matches UloopPausePoint.Pause(\"<id>\") and that the code path was executed."
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
