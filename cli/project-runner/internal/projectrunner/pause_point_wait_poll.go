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

	// pausePointWaitStateTriggerFailed means the wait was abandoned because the --trigger command was
	// rejected before it executed anything, so it never performed the action the marker was waiting for.
	// Why not reuse the timeout state: the timeout path unconditionally clears the marker, which
	// would force a re-enable just to retry a corrected trigger, and it reports a "not hit within
	// %ds" message with a PlayMode diagnosis that describes neither what happened nor what to fix.
	// Internal to this package — it never appears on the wire.
	pausePointWaitStateTriggerFailed pausePointWaitState = "trigger_failed"
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
	triggerHandle, skippedTriggerResult, resumeResult := startPausePointWaitSideEffects(ctx, connection, options)

	response, state, polledTriggerResult, err := waitForPausePointStatus(ctx, connection, options, triggerHandle)

	if state == pausePointWaitStateTriggerFailed && resumeResult != nil && resumeResult.Resumed {
		repaused := repausePlayModeAfterAbandonedWait(ctx, connection, *resumeResult)
		resumeResult = &repaused
	}

	if skippedTriggerResult != nil {
		return response, state, skippedTriggerResult, resumeResult, err
	}
	// The trigger goroutine's buffered channel yields exactly one value, so a result already
	// received by the poll loop must be reused here instead of joining again — a second receive
	// would block for the whole grace window and then report Completed:false over a real result.
	if polledTriggerResult != nil {
		return response, state, polledTriggerResult, resumeResult, err
	}
	if triggerHandle != nil {
		return response, state, triggerHandle.join(), resumeResult, err
	}
	return response, state, nil, resumeResult, err
}

// startPausePointWaitSideEffects performs the pre-wait --resume-play / --trigger work, returning
// either a live trigger handle or the fixed TriggerResult explaining why the trigger was skipped.
func startPausePointWaitSideEffects(
	ctx context.Context,
	connection unityipc.Connection,
	options waitForPausePointOptions,
) (*pausePointTriggerHandle, *pausePointTriggerResult, *pausePointResumePlayResult) {
	if options.triggerCommand == "" && !options.resumePlay {
		return nil, nil, nil
	}

	if !pausePointIsArmed(ctx, connection, options.id) {
		var resumeResult *pausePointResumePlayResult
		var skippedTriggerResult *pausePointTriggerResult
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
		return nil, skippedTriggerResult, resumeResult
	}

	var resumeResult *pausePointResumePlayResult
	if options.resumePlay {
		result := resumePlayModeForPausePoint(ctx, connection)
		resumeResult = &result
		if result.Error != "" {
			if options.triggerCommand == "" {
				return nil, nil, resumeResult
			}
			return nil, &pausePointTriggerResult{
				Command: pausePointTriggerCommandString(options.triggerCommand, options.triggerArgs),
				Error:   "trigger was not dispatched: --resume-play failed to resume play mode",
			}, resumeResult
		}
	}

	if options.triggerCommand == "" {
		return nil, nil, resumeResult
	}

	handle := startPausePointTrigger(ctx, connection, options.startPath, options.triggerCommand, options.triggerArgs)
	return handle, nil, resumeResult
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
	triggerHandle *pausePointTriggerHandle,
) (pausePointStatusResponse, pausePointWaitState, *pausePointTriggerResult, error) {
	waitContext, cancel := context.WithTimeout(ctx, options.timeout)
	defer cancel()

	lastResponse := pausePointStatusResponse{Id: options.id}
	var lastErr error
	var triggerResult *pausePointTriggerResult
	hasResponse := false
	triggerDone := triggerHandle.doneChannel()
	ticker := time.NewTicker(pausePointStatusPoll)
	defer ticker.Stop()
	for {
		response, err := queryPausePointStatus(waitContext, connection, options.id)
		if err == nil {
			lastResponse = response
			hasResponse = true
			state := pausePointWaitStateForStatus(response.Status)
			if state != "" {
				return response, state, triggerResult, nil
			}
		} else {
			lastErr = err
		}

		select {
		case <-waitContext.Done():
			if ctx.Err() != nil {
				return lastResponse, "", triggerResult, ctx.Err()
			}
			finalResponse, finalState, hasFinalResponse, finalErr := queryPausePointStatusAtTimeout(ctx, connection, options.id)
			if hasFinalResponse {
				lastResponse = finalResponse
				hasResponse = true
				if finalState != "" {
					return finalResponse, finalState, triggerResult, nil
				}
			} else if lastErr == nil {
				lastErr = finalErr
			}
			if hasResponse {
				return lastResponse, pausePointWaitStateTimeout, triggerResult, nil
			}
			if lastErr != nil {
				return lastResponse, "", triggerResult, fmt.Errorf("timed out waiting for pause point status: %w", lastErr)
			}
			return lastResponse, pausePointWaitStateTimeout, triggerResult, nil
		case result := <-triggerDone:
			// Nil the channel so this case can never fire twice: the handle's channel holds a single
			// buffered value, and the caller reuses the result received here instead of joining.
			triggerResult = result
			triggerDone = nil
			if pausePointTriggerRejectedBeforeExecution(result) {
				abortResponse, abortState := abortPausePointWaitAfterTriggerRejection(
					ctx, connection, options.id, lastResponse)
				return abortResponse, abortState, triggerResult, nil
			}
		case <-ticker.C:
		}
	}
}

// abortPausePointWaitAfterTriggerRejection takes one last status reading before abandoning the
// wait, because a trigger rejection can race a genuine hit produced by the game itself: without
// this the hit would be discarded and reported as a trigger failure.
func abortPausePointWaitAfterTriggerRejection(
	ctx context.Context,
	connection unityipc.Connection,
	id string,
	lastResponse pausePointStatusResponse,
) (pausePointStatusResponse, pausePointWaitState) {
	response, state, hasResponse, _ := queryPausePointStatusAtTimeout(ctx, connection, id)
	if !hasResponse {
		return lastResponse, pausePointWaitStateTriggerFailed
	}
	if state != "" {
		return response, state
	}
	return response, pausePointWaitStateTriggerFailed
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
