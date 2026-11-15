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

// pausePointTriggerNotRunConfirmWait is how long a wait that reported a dialog to Unity waits for its
// trigger's answer. Why 6 seconds: Unity checks reported requests within a second, and the trigger's
// send then reads the Editor again after a second and looks at Unity's windows for about a second on
// macOS before it reports; 6 is twice those 3 seconds.
var pausePointTriggerNotRunConfirmWait = 6 * time.Second

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
	// Why look before the arm query: an expiry extension Unity did not answer usually means a dialog
	// holds the main thread, and the arm query would only wait out its own timeout behind it.
	if options.expiryExtensionUnanswered {
		if dialogs := findBlockingDialogs(ctx, connection, options.dialogWatch); dialogs != nil {
			return pausePointWaitBehindDialogAtStart(options, dialogs)
		}
	}
	sideEffects := startPausePointWaitSideEffects(ctx, connection, options)

	if sideEffects.immediateHit != nil {
		// Returned without polling at all: the arm confirmation is the hit this wait reports, and a
		// re-query could observe the marker cleared or expired in the meantime (or fail transiently
		// and poll on to a timeout), turning "the recorded hit, as-is" into some other outcome.
		return *sideEffects.immediateHit, pausePointWaitStateHit, sideEffects.skippedTriggerResult,
			sideEffects.resumeResult, sideEffects.hasBaseline, nil
	}

	resumeResult := sideEffects.resumeResult
	response, state, polledTriggerResult, hasBaseline, err := waitForPausePointStatus(ctx, connection, options, sideEffects)

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
	lastResponse  pausePointStatusResponse
	lastErr       error
	triggerResult *pausePointTriggerResult
	// skippedTriggerResult is the reason the wait did not send its --trigger, nil when it sent it.
	skippedTriggerResult *pausePointTriggerResult
	markerNotArmed       bool
	hasResponse          bool
	baselineSequence     int
	hasBaseline          bool
	baselineDecided      bool
	exitWatch            *clicore.UnityExitWatch
	dialogWatch          *dialogBlockWatch
}

func waitForPausePointStatus(
	ctx context.Context,
	connection unityipc.Connection,
	options waitForPausePointOptions,
	sideEffects pausePointWaitSideEffects,
) (pausePointStatusResponse, pausePointWaitState, *pausePointTriggerResult, bool, error) {
	waitContext, cancel := context.WithTimeout(ctx, options.timeout)
	defer cancel()

	state := pausePointWaitLoopState{
		lastResponse:         pausePointStatusResponse{Id: options.id},
		skippedTriggerResult: sideEffects.skippedTriggerResult,
		markerNotArmed:       sideEffects.markerNotArmed,
		baselineSequence:     sideEffects.baselineSequence,
		hasBaseline:          sideEffects.hasBaseline,
		baselineDecided:      sideEffects.baselineDecided,
		exitWatch:            newUnityExitWatch(connection),
		// Why the longer interval: a pause point holds the main thread on purpose, so most looks
		// while this wait's polls go unanswered find no dialog.
		dialogWatch: newDialogBlockWatch(connection, options.dialogWatch, pausePointDialogWatchInterval, dialogWatchStallThresholdSeconds),
	}
	resumeResult := sideEffects.resumeResult
	triggerDone := sideEffects.triggerHandle.doneChannel()
	// Why look before the first poll: an arm query Unity did not answer usually means a dialog holds
	// the main thread, and the watch would otherwise find it only after its stall threshold. Why not
	// after an unanswered expiry extension: the wait already looked then, and a look takes about 1s.
	if sideEffects.armQueryUnanswered && !options.expiryExtensionUnanswered {
		if dialogs := state.dialogWatch.observeUnansweredProbe(waitContext); dialogs != nil {
			response, waitState, waitErr := finishPausePointWaitBehindDialog(
				ctx, waitContext, connection, options, &state, triggerDone, dialogs, resumeResult)
			return response, waitState, state.triggerResult, state.hasBaseline, waitErr
		}
	}
	ticker := time.NewTicker(pausePointStatusPoll)
	defer ticker.Stop()
	for {
		response, err := queryPausePointStatus(waitContext, connection, options.id)
		waitState, done, pollErr := applyPausePointWaitPoll(waitContext, &state, response, err, options.markerJustEnabled)
		if done {
			return state.lastResponse, waitState, state.triggerResult, state.hasBaseline, pollErr
		}
		if dialogs := state.dialogWatch.observePoll(waitContext, err == nil); dialogs != nil {
			response, waitState, waitErr := finishPausePointWaitBehindDialog(
				ctx, waitContext, connection, options, &state, triggerDone, dialogs, resumeResult)
			return response, waitState, state.triggerResult, state.hasBaseline, waitErr
		}

		select {
		case <-waitContext.Done():
			return finishPausePointWaitOnTimeout(ctx, connection, options, &state)
		case result := <-triggerDone:
			// Nil the channel so this case can never fire twice: the handle's channel holds a single
			// buffered value, and the caller reuses the result received here instead of joining.
			state.triggerResult = result
			triggerDone = nil
			if response, waitState, done, err := settlePausePointWaitOnTriggerResult(
				ctx, connection, options, &state, nil, resumeResult); done {
				return response, waitState, state.triggerResult, state.hasBaseline, err
			}
		case <-ticker.C:
		}
	}
}

// receivePausePointTriggerResultWithin returns the trigger's result when it arrives in done within
// wait and before ctx ends, and nil otherwise. A nil done returns nil at once.
func receivePausePointTriggerResultWithin(
	ctx context.Context,
	done <-chan *pausePointTriggerResult,
	wait time.Duration,
) *pausePointTriggerResult {
	if done == nil {
		return nil
	}
	timer := time.NewTimer(wait)
	defer timer.Stop()
	select {
	case result := <-done:
		return result
	case <-timer.C:
		return nil
	case <-ctx.Done():
		return nil
	}
}

// finishPausePointWaitBehindDialog ends a wait whose own watch found a dialog. It says the trigger was
// not run only when the trigger itself reports so, the way a trigger result in the poll loop does;
// every other case says the command had started, because the marker and any --trigger stay in effect
// in Unity, and is not safe to retry. waitContext bounds the wait for the trigger's result, so it ends
// with --timeout-seconds or a cancel; ctx is the caller's, for the final marker read.
func finishPausePointWaitBehindDialog(
	ctx context.Context,
	waitContext context.Context,
	connection unityipc.Connection,
	options waitForPausePointOptions,
	state *pausePointWaitLoopState,
	triggerDone <-chan *pausePointTriggerResult,
	dialogs []statusDialog,
	resumeResult *pausePointResumePlayResult,
) (pausePointStatusResponse, pausePointWaitState, error) {
	if blocked, unsent := pausePointWaitUnsentBehindDialog(
		options, dialogs, state.skippedTriggerResult, resumeResult, state.markerNotArmed); unsent {
		return state.lastResponse, "", blocked
	}
	if state.triggerResult == nil {
		state.triggerResult = awaitPausePointTriggerResultBehindDialog(
			waitContext, connection, options, state.dialogWatch.lastFoundReport(), triggerDone)
	}
	if response, waitState, done, err := settlePausePointWaitOnTriggerResult(
		ctx, connection, options, state, dialogs, resumeResult); done {
		return response, waitState, err
	}
	// A trigger result that came in means Unity's main thread ran since the look, so the dialog may be
	// gone and the marker hit; that hit is what the wait reports then. Why not without a result: the
	// dialog still holds the main thread then, so the read would only wait out its timeout.
	if state.triggerResult != nil {
		response, waitState, hasResponse, _ := queryPausePointStatusAtTimeout(
			ctx, connection, options.id, state.baselineSequence, state.hasBaseline)
		if hasResponse && waitState != "" {
			return response, waitState, nil
		}
	}
	return state.lastResponse, "", pausePointWaitStartedBehindDialog(options, dialogs)
}

// awaitPausePointTriggerResultBehindDialog waits briefly for the trigger's result once the wait's own
// watch found a dialog, and returns it, or nil when none came. When the look showed the trigger holding
// Unity's command slot while it waits for the main thread, it first reports the dialog to Unity under
// the trigger's command name: Unity then fails that request with main_thread_blocked if it has not
// started and would be cancelled when uloop disconnects, and the trigger reports that as not run. Why
// ask Unity instead of judging from the look: the look is one moment, and the dialog can close and let
// the trigger run before uloop exits. A request that outlives the disconnect, such as a default
// compile, is not failed, so no answer comes and the wait says the command had started.
// Why a short wait otherwise: a trigger stopped by the same dialog often reports just after the look.
func awaitPausePointTriggerResultBehindDialog(
	ctx context.Context,
	connection unityipc.Connection,
	options waitForPausePointOptions,
	report statusReport,
	triggerDone <-chan *pausePointTriggerResult,
) *pausePointTriggerResult {
	if triggerDone == nil {
		return nil
	}
	wait := pausePointFinalStatusProbeTimeout
	if options.dialogWatch.reportBlockingDialog != nil && pausePointTriggerWaitsForMainThread(report, options.triggerCommand) {
		// A failed report only means no answer comes; the wait then says the command had started.
		_ = options.dialogWatch.reportBlockingDialog(ctx, connection, options.triggerCommand)
		wait = pausePointTriggerNotRunConfirmWait
	}
	return receivePausePointTriggerResultWithin(ctx, triggerDone, wait)
}

// settlePausePointWaitOnTriggerResult ends the wait when the trigger's result shows the trigger
// performed nothing: rejected before it executed, not run by a stalled main thread, or kept from
// running by a dialog; foundDialogs are the dialogs the wait's own watch found, nil when it found
// none. done is false
// when the wait should go on. Either way the wait first reads the marker once more, because the game
// itself can hit it in the meantime, and that hit is what the wait reports then.
func settlePausePointWaitOnTriggerResult(
	ctx context.Context,
	connection unityipc.Connection,
	options waitForPausePointOptions,
	state *pausePointWaitLoopState,
	foundDialogs []statusDialog,
	resumeResult *pausePointResumePlayResult,
) (pausePointStatusResponse, pausePointWaitState, bool, error) {
	result := state.triggerResult
	dialogs, notRunBehindDialog := pausePointTriggerNotRunBehindDialog(result, foundDialogs)
	rejected := pausePointTriggerRejectedBeforeExecution(result) || pausePointTriggerRejectedByUnityBeforeExecution(result) ||
		pausePointTriggerNotRunOnStalledMainThread(result)
	if !rejected && !notRunBehindDialog {
		return pausePointStatusResponse{}, "", false, nil
	}
	response, waitState := abortPausePointWaitAfterTriggerRejection(
		ctx, connection, options.id, state.lastResponse, state.baselineSequence, state.hasBaseline)
	if !notRunBehindDialog || waitState != pausePointWaitStateTriggerFailed {
		return response, waitState, true, nil
	}
	return response, "", true, pausePointTriggerNotRunBlockedByDialog(options, dialogs, result, resumeResult)
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
