package projectrunner

import (
	"context"
	"fmt"
	"time"

	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

const pausePointFinalStatusProbeTimeout = 250 * time.Millisecond

var pausePointStatusPoll = 50 * time.Millisecond

type pausePointWaitState string

const (
	pausePointWaitStateHit        pausePointWaitState = "hit"
	pausePointWaitStateTimeout    pausePointWaitState = "timeout"
	pausePointWaitStateNotEnabled pausePointWaitState = "not_enabled"
	pausePointWaitStateExpired    pausePointWaitState = "expired"
	pausePointWaitStateCleared    pausePointWaitState = "cleared"
)

// waitForPausePoint confirms the marker is actually armed with one status query before starting
// --resume-play / --trigger: extendPausePointExpiryBeforeWait (the await-pause-point entry point)
// is best-effort and does not confirm arm, so a typo'd or already-expired --id must not still get
// Play resumed or the trigger dispatched into the running game before the wait immediately fails
// on it. Once confirmed armed (or already hit), optional resume runs synchronously, then the
// trigger races the status poll loop and is joined once the wait itself settles, so a slow trigger
// cannot delay reporting a pause-point hit.
func waitForPausePoint(
	ctx context.Context,
	connection unityipc.Connection,
	options waitForPausePointOptions,
) (pausePointStatusResponse, pausePointWaitState, *pausePointTriggerResult, *pausePointResumePlayResult, error) {
	var triggerHandle *pausePointTriggerHandle
	var skippedTriggerResult *pausePointTriggerResult
	var resumeResult *pausePointResumePlayResult

	if options.triggerCommand != "" || options.resumePlay {
		if pausePointIsArmed(ctx, connection, options.id) {
			if options.resumePlay {
				result := resumePlayModeForPausePoint(ctx, connection)
				resumeResult = &result
				if result.Error != "" {
					if options.triggerCommand != "" {
						skippedTriggerResult = &pausePointTriggerResult{
							Command: pausePointTriggerCommandString(options.triggerCommand, options.triggerArgs),
							Error:   "trigger was not dispatched: --resume-play failed to resume play mode",
						}
					}
				} else if options.triggerCommand != "" {
					triggerHandle = startPausePointTrigger(ctx, connection, options.startPath, options.triggerCommand, options.triggerArgs)
				}
			} else if options.triggerCommand != "" {
				triggerHandle = startPausePointTrigger(ctx, connection, options.startPath, options.triggerCommand, options.triggerArgs)
			}
		} else {
			if options.resumePlay {
				// --resume-play always yields a ResumePlayResult when given, even when skipped: a
				// silently omitted result would hide "arm was never confirmed" behind a plain
				// timeout/not-enabled error with no clue why Play was never resumed.
				resumeResult = &pausePointResumePlayResult{
					Error: "resume was not dispatched: the marker could not be confirmed armed at wait start",
				}
			}
			if options.triggerCommand != "" {
				// --trigger always yields a TriggerResult when given, even when skipped: a silently
				// omitted TriggerResult would be indistinguishable from "the trigger ran but this CLI
				// exited before it reported anything," hiding the real reason (marker not confirmed
				// armed) behind a plain timeout/not-enabled error with no clue why the trigger never fired.
				skippedTriggerResult = &pausePointTriggerResult{
					Command: pausePointTriggerCommandString(options.triggerCommand, options.triggerArgs),
					Error:   "trigger was not dispatched: the marker could not be confirmed armed at wait start",
				}
			}
		}
	}

	response, state, err := waitForPausePointStatus(ctx, connection, options)
	if skippedTriggerResult != nil {
		return response, state, skippedTriggerResult, resumeResult, err
	}
	if triggerHandle != nil {
		return response, state, triggerHandle.join(), resumeResult, err
	}
	return response, state, nil, resumeResult, err
}

// pausePointIsArmed reports whether the marker is enabled or already hit. A query failure is
// treated as not armed: dispatching a --trigger command against a marker this CLI cannot even
// confirm exists would inject the trigger's action into the game with no corresponding wait.
func pausePointIsArmed(ctx context.Context, connection unityipc.Connection, id string) bool {
	response, err := queryPausePointStatus(ctx, connection, id)
	if err != nil {
		return false
	}
	state := pausePointWaitStateForStatus(response.Status)
	return state == "" || state == pausePointWaitStateHit
}

func waitForPausePointStatus(
	ctx context.Context,
	connection unityipc.Connection,
	options waitForPausePointOptions,
) (pausePointStatusResponse, pausePointWaitState, error) {
	waitContext, cancel := context.WithTimeout(ctx, options.timeout)
	defer cancel()

	lastResponse := pausePointStatusResponse{Id: options.id}
	var lastErr error
	hasResponse := false
	ticker := time.NewTicker(pausePointStatusPoll)
	defer ticker.Stop()
	for {
		response, err := queryPausePointStatus(waitContext, connection, options.id)
		if err == nil {
			lastResponse = response
			hasResponse = true
			state := pausePointWaitStateForStatus(response.Status)
			if state != "" {
				return response, state, nil
			}
		} else {
			lastErr = err
		}

		select {
		case <-waitContext.Done():
			if ctx.Err() != nil {
				return lastResponse, "", ctx.Err()
			}
			finalResponse, finalState, hasFinalResponse, finalErr := queryPausePointStatusAtTimeout(ctx, connection, options.id)
			if hasFinalResponse {
				lastResponse = finalResponse
				hasResponse = true
				if finalState != "" {
					return finalResponse, finalState, nil
				}
			} else if lastErr == nil {
				lastErr = finalErr
			}
			if hasResponse {
				return lastResponse, pausePointWaitStateTimeout, nil
			}
			if lastErr != nil {
				return lastResponse, "", fmt.Errorf("timed out waiting for pause point status: %w", lastErr)
			}
			return lastResponse, pausePointWaitStateTimeout, nil
		case <-ticker.C:
		}
	}
}

func queryPausePointStatusAtTimeout(
	ctx context.Context,
	connection unityipc.Connection,
	id string,
) (pausePointStatusResponse, pausePointWaitState, bool, error) {
	finalContext, cancel := context.WithTimeout(ctx, pausePointFinalStatusProbeTimeout)
	defer cancel()

	response, err := queryPausePointStatus(finalContext, connection, id)
	if err != nil {
		return pausePointStatusResponse{}, "", false, err
	}

	return response, pausePointWaitStateForStatus(response.Status), true, nil
}

func pausePointWaitStateForStatus(status string) pausePointWaitState {
	switch status {
	case pausePointStatusHit:
		return pausePointWaitStateHit
	case pausePointStatusNotEnabled:
		return pausePointWaitStateNotEnabled
	case pausePointStatusExpired:
		return pausePointWaitStateExpired
	case pausePointStatusCleared:
		return pausePointWaitStateCleared
	case pausePointStatusEnabled:
		return ""
	default:
		return ""
	}
}
