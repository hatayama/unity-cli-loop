package projectrunner

import (
	"context"
	"fmt"
	"time"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
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
//
// hasNewHitBaseline is true when the wait started against an already-hit continuous/trace marker
// and must observe a later LastHitSequence before succeeding. Callers pass it into timeout error
// construction so the hint can explain why a still-Hit marker did not count as a wait success.
func waitForPausePoint(
	ctx context.Context,
	connection unityipc.Connection,
	options waitForPausePointOptions,
) (pausePointStatusResponse, pausePointWaitState, *pausePointTriggerResult, *pausePointResumePlayResult, bool, error) {
	sideEffects := startPausePointWaitSideEffects(ctx, connection, options)

	if sideEffects.immediateHit != nil {
		// Returned without polling at all: the arm confirmation is the hit this wait reports, and a
		// re-query could observe the marker cleared or expired in the meantime (or fail transiently
		// and poll on to a timeout), turning "the recorded hit, as-is" into some other outcome.
		return *sideEffects.immediateHit, pausePointWaitStateHit, sideEffects.skippedTriggerResult,
			sideEffects.resumeResult, sideEffects.hasBaseline, nil
	}

	resumeResult := sideEffects.resumeResult
	response, state, polledTriggerResult, hasBaseline, err := waitForPausePointStatus(
		ctx, connection, options, sideEffects.triggerHandle,
		sideEffects.baselineSequence, sideEffects.hasBaseline, sideEffects.baselineDecided)

	if state == pausePointWaitStateTriggerFailed && resumeResult != nil && resumeResult.Resumed {
		repaused := repausePlayModeAfterAbandonedWait(ctx, connection, *resumeResult)
		resumeResult = &repaused
	}

	if sideEffects.skippedTriggerResult != nil {
		return response, state, sideEffects.skippedTriggerResult, resumeResult, hasBaseline, err
	}
	// The trigger goroutine's buffered channel yields exactly one value, so a result already
	// received by the poll loop must be reused here instead of joining again — a second receive
	// would block for the whole grace window and then report Completed:false over a real result.
	if polledTriggerResult != nil {
		return response, state, polledTriggerResult, resumeResult, hasBaseline, err
	}
	if sideEffects.triggerHandle != nil {
		return response, state, sideEffects.triggerHandle.join(), resumeResult, hasBaseline, err
	}
	return response, state, nil, resumeResult, hasBaseline, err
}

// pausePointWaitLoopState is the mutable snapshot the await poll loop updates
// across ticks. Why a type: the timeout and poll-success paths both rewrite
// the same fields, and passing them as a bag of locals kept waitForPausePointStatus
// above the cyclop limit.
type pausePointWaitLoopState struct {
	lastResponse     pausePointStatusResponse
	lastErr          error
	triggerResult    *pausePointTriggerResult
	hasResponse      bool
	baselineSequence int
	hasBaseline      bool
	baselineDecided  bool
	exitWatch        *clicore.UnityExitWatch
	dialogWatch      *dialogBlockWatch
}

func waitForPausePointStatus(
	ctx context.Context,
	connection unityipc.Connection,
	options waitForPausePointOptions,
	triggerHandle *pausePointTriggerHandle,
	baselineSequence int,
	hasBaseline bool,
	baselineDecided bool,
) (pausePointStatusResponse, pausePointWaitState, *pausePointTriggerResult, bool, error) {
	waitContext, cancel := context.WithTimeout(ctx, options.timeout)
	defer cancel()

	state := pausePointWaitLoopState{
		lastResponse:     pausePointStatusResponse{Id: options.id},
		baselineSequence: baselineSequence,
		hasBaseline:      hasBaseline,
		baselineDecided:  baselineDecided,
		exitWatch:        newUnityExitWatch(connection),
		// Why the longer interval: a pause point holds the main thread on purpose, so most looks
		// while this wait's polls go unanswered find no dialog.
		dialogWatch: newDialogBlockWatch(connection, options.dialogWatch, pausePointDialogWatchInterval, dialogWatchStallThresholdSeconds),
	}
	triggerDone := triggerHandle.doneChannel()
	ticker := time.NewTicker(pausePointStatusPoll)
	defer ticker.Stop()
	for {
		response, err := queryPausePointStatus(waitContext, connection, options.id)
		waitState, done, pollErr := applyPausePointWaitPoll(waitContext, &state, response, err, options.markerJustEnabled)
		if done {
			return state.lastResponse, waitState, state.triggerResult, state.hasBaseline, pollErr
		}
		if dialogs := state.dialogWatch.observePoll(waitContext, err == nil); dialogs != nil {
			// Not safe to retry: the marker and any --trigger stay in effect in Unity.
			blocked := startedCommandBlockedByDialog(options.commandName(), dialogs, options.dialogWatch.canPressDialogs, false)
			blocked.closedNextAction = pausePointDialogClosedNextAction(options.id)
			blocked.details = map[string]any{pausePointDialogIDDetailKey: options.id}
			return state.lastResponse, "", state.triggerResult, state.hasBaseline, blocked
		}

		select {
		case <-waitContext.Done():
			return finishPausePointWaitOnTimeout(ctx, connection, options, &state)
		case result := <-triggerDone:
			// Nil the channel so this case can never fire twice: the handle's channel holds a single
			// buffered value, and the caller reuses the result received here instead of joining.
			state.triggerResult = result
			triggerDone = nil
			if pausePointTriggerRejectedBeforeExecution(result) ||
				pausePointTriggerRejectedByUnityBeforeExecution(result) {
				abortResponse, abortState := abortPausePointWaitAfterTriggerRejection(
					ctx, connection, options.id, state.lastResponse, state.baselineSequence, state.hasBaseline)
				return abortResponse, abortState, state.triggerResult, state.hasBaseline, nil
			}
		case <-ticker.C:
		}
	}
}

// applyPausePointWaitPoll records one status query. Why abort on a permanent
// connect error or an exited Editor: every poll dials again, so either would
// otherwise burn the whole --timeout before it is reported.
func applyPausePointWaitPoll(
	ctx context.Context,
	state *pausePointWaitLoopState,
	response pausePointStatusResponse,
	err error,
	markerJustEnabled bool,
) (pausePointWaitState, bool, error) {
	if exitErr := state.exitWatch.Observe(ctx, err); exitErr != nil {
		return "", true, exitErr
	}
	if err != nil {
		if clierrors.IsPermanentConnectError(err) {
			return "", true, err
		}
		state.lastErr = err
		return "", false, nil
	}

	state.lastResponse = response
	state.hasResponse = true
	// Why only once: a later Enabled→Hit transition is the await success itself
	// (enable --await). Re-baselining on that first mid-wait Hit would demand a second
	// sequence bump and never return.
	if !state.baselineDecided {
		state.baselineSequence, state.hasBaseline, state.baselineDecided = decidePausePointNewHitBaseline(
			response, markerJustEnabled)
	}
	waitState := pausePointWaitStateForPolledStatus(response, state.baselineSequence, state.hasBaseline)
	if waitState != "" {
		return waitState, true, nil
	}
	return "", false, nil
}

// finishPausePointWaitOnTimeout takes one last status probe after the wait
// deadline. Why a helper: the deadline path has its own baseline and error
// fallbacks, and leaving them inline kept waitForPausePointStatus over the
// cyclop limit.
func finishPausePointWaitOnTimeout(
	ctx context.Context,
	connection unityipc.Connection,
	options waitForPausePointOptions,
	state *pausePointWaitLoopState,
) (pausePointStatusResponse, pausePointWaitState, *pausePointTriggerResult, bool, error) {
	if ctx.Err() != nil {
		return state.lastResponse, "", state.triggerResult, state.hasBaseline, ctx.Err()
	}
	finalResponse, finalState, hasFinalResponse, finalErr := queryPausePointStatusAtTimeout(
		ctx, connection, options.id, state.baselineSequence, state.hasBaseline)
	if hasFinalResponse {
		state.lastResponse = finalResponse
		state.hasResponse = true
		if !state.baselineDecided {
			state.baselineSequence, state.hasBaseline, _ = decidePausePointNewHitBaseline(
				finalResponse, options.markerJustEnabled)
			finalState = pausePointWaitStateForPolledStatus(finalResponse, state.baselineSequence, state.hasBaseline)
		}
		if finalState != "" {
			return finalResponse, finalState, state.triggerResult, state.hasBaseline, nil
		}
	} else if state.lastErr == nil {
		state.lastErr = finalErr
	}
	if state.hasResponse {
		return state.lastResponse, pausePointWaitStateTimeout, state.triggerResult, state.hasBaseline, nil
	}
	if state.lastErr != nil {
		return state.lastResponse, "", state.triggerResult, state.hasBaseline, fmt.Errorf("timed out waiting for pause point status: %w", state.lastErr)
	}
	return state.lastResponse, pausePointWaitStateTimeout, state.triggerResult, state.hasBaseline, nil
}

// abortPausePointWaitAfterTriggerRejection takes one last status reading before abandoning the
// wait, because a trigger rejection can race a genuine hit produced by the game itself: without
// this the hit would be discarded and reported as a trigger failure.
func abortPausePointWaitAfterTriggerRejection(
	ctx context.Context,
	connection unityipc.Connection,
	id string,
	lastResponse pausePointStatusResponse,
	baselineSequence int,
	hasBaseline bool,
) (pausePointStatusResponse, pausePointWaitState) {
	response, state, hasResponse, _ := queryPausePointStatusAtTimeout(
		ctx, connection, id, baselineSequence, hasBaseline)
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
	baselineSequence int,
	hasBaseline bool,
) (pausePointStatusResponse, pausePointWaitState, bool, error) {
	finalContext, cancel := context.WithTimeout(ctx, pausePointFinalStatusProbeTimeout)
	defer cancel()

	response, err := queryPausePointStatus(finalContext, connection, id)
	if err != nil {
		return pausePointStatusResponse{}, "", false, err
	}

	return response, pausePointWaitStateForPolledStatus(response, baselineSequence, hasBaseline), true, nil
}

// pausePointWaitStateForPolledStatus maps a polled status to a terminal wait state, treating an
// already-hit continuous/trace marker as non-terminal until LastHitSequence advances past baseline.
func pausePointWaitStateForPolledStatus(
	response pausePointStatusResponse,
	baselineSequence int,
	hasBaseline bool,
) pausePointWaitState {
	if response.Status == pausePointStatusHit {
		if !hasBaseline || response.LastHitSequence > baselineSequence {
			return pausePointWaitStateHit
		}
		return ""
	}
	return pausePointWaitStateForStatus(response.Status)
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
