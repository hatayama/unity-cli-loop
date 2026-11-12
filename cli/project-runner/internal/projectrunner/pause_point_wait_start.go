package projectrunner

import (
	"context"
	"fmt"

	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

// This file holds the wait-start stage of pause-point --await: confirming the marker is armed,
// running --resume-play / --trigger, and deciding the new-hit baseline, before the poll loop starts.

// pausePointWaitSideEffects is what the pre-wait --resume-play / --trigger stage produced. Why a
// type: the stage has seven outputs and one of them (immediateHit) changes what the caller does next,
// which a positional tuple made impossible to read at the call site.
type pausePointWaitSideEffects struct {
	triggerHandle        *pausePointTriggerHandle
	skippedTriggerResult *pausePointTriggerResult
	resumeResult         *pausePointResumePlayResult
	baselineSequence     int
	hasBaseline          bool
	baselineDecided      bool

	// immediateHit is the arm-confirmation response when it already settles the wait: an
	// already-hit marker that awaits no later hit. It is the wait's answer, so the poll loop must
	// not run at all — see waitForPausePoint.
	immediateHit *pausePointStatusResponse
}

// startPausePointWaitSideEffects performs the pre-wait --resume-play / --trigger work, returning
// either a live trigger handle or the fixed TriggerResult explaining why the trigger was skipped.
// When it queries status to confirm arming, that response is the wait-start snapshot for the
// new-hit baseline (decided before --resume-play), so a post-resume hit advances LastHitSequence
// past the baseline. baselineDecided is false only when no arming query ran (plain await).
func startPausePointWaitSideEffects(
	ctx context.Context,
	connection unityipc.Connection,
	options waitForPausePointOptions,
) pausePointWaitSideEffects {
	if options.triggerCommand == "" && !options.resumePlay {
		return pausePointWaitSideEffects{baselineSequence: -1}
	}

	armResponse, armed, armQueryErr := queryPausePointArmStatus(ctx, connection, options.id)
	if !armed {
		unarmedSuffix := pausePointUnarmedAtWaitStartSuffix(armResponse, armQueryErr)
		var resumeResult *pausePointResumePlayResult
		var skippedTriggerResult *pausePointTriggerResult
		if options.resumePlay {
			// --resume-play always yields a ResumePlayResult when given, even when skipped: a
			// silently omitted result would hide "arm was never confirmed" behind a plain
			// timeout/not-enabled error with no clue why Play was never resumed.
			resumeResult = &pausePointResumePlayResult{
				Error: pausePointResumeNotArmedAtWaitStartError + unarmedSuffix,
			}
		}
		if options.triggerCommand != "" {
			// --trigger always yields a TriggerResult when given, even when skipped: a silently
			// omitted TriggerResult would be indistinguishable from "the trigger ran but this CLI
			// exited before it reported anything," hiding the real reason (marker not confirmed
			// armed) behind a plain timeout/not-enabled error with no clue why the trigger never fired.
			skippedTriggerResult = &pausePointTriggerResult{
				Command: pausePointTriggerCommandString(options.triggerCommand, options.triggerArgs),
				Error:   pausePointTriggerNotArmedAtWaitStartError + unarmedSuffix,
			}
		}
		// Why baselineDecided=false: a transient arm-query failure is also reported as not armed.
		// Leaving baseline undecided lets the first successful poll establish it, so a stale
		// continuous Hit cannot slip through as an immediate wait success.
		return pausePointWaitSideEffects{
			skippedTriggerResult: skippedTriggerResult,
			resumeResult:         resumeResult,
			baselineSequence:     -1,
		}
	}

	baselineSequence, hasBaseline, baselineDecided := decidePausePointNewHitBaseline(armResponse, options.markerJustEnabled)
	sideEffects := pausePointWaitSideEffects{
		baselineSequence: baselineSequence,
		hasBaseline:      hasBaseline,
		baselineDecided:  baselineDecided,
	}

	if pausePointWaitSucceedsOnExistingHit(armResponse, hasBaseline) {
		// This response already settles the wait, so resuming Play (or firing the trigger into the
		// running game) would restart the game and then report the old hit as if the wait had just
		// observed it — the response would claim a pause the Editor is no longer holding.
		sideEffects.resumeResult, sideEffects.skippedTriggerResult = pausePointSideEffectsSkippedForExistingHit(options)
		sideEffects.immediateHit = &armResponse
		return sideEffects
	}

	if options.resumePlay {
		result := resumePlayModeForPausePoint(ctx, connection)
		sideEffects.resumeResult = &result
		if result.Error != "" {
			if options.triggerCommand != "" {
				sideEffects.skippedTriggerResult = &pausePointTriggerResult{
					Command: pausePointTriggerCommandString(options.triggerCommand, options.triggerArgs),
					Error:   "trigger was not dispatched: --resume-play failed to resume play mode",
				}
			}
			return sideEffects
		}
	}

	if options.triggerCommand == "" {
		return sideEffects
	}

	sideEffects.triggerHandle = startPausePointTrigger(
		ctx, connection, options.startPath, options.triggerCommand, options.triggerArgs)
	return sideEffects
}

// pausePointWaitSucceedsOnExistingHit reports that the arm-confirmation response is already the
// wait's answer, so no pre-wait side effect may run: the marker is hit and no new-hit baseline makes
// the wait look past that hit. hasBaseline is the value decidePausePointNewHitBaseline just
// returned, which covers enable --await too — a marker enabled by this very command gets no
// baseline, so a hit its arm query already observes is likewise returned without polling.
func pausePointWaitSucceedsOnExistingHit(response pausePointStatusResponse, hasBaseline bool) bool {
	return response.Status == pausePointStatusHit && !hasBaseline
}

// pausePointSideEffectsSkippedForExistingHit builds the reported outcomes for the flags that were
// passed but deliberately not performed. Both are reported rather than omitted for the same reason as
// on the not-armed path: a silently missing result is indistinguishable from one that never reported.
func pausePointSideEffectsSkippedForExistingHit(
	options waitForPausePointOptions,
) (*pausePointResumePlayResult, *pausePointTriggerResult) {
	var resumeResult *pausePointResumePlayResult
	if options.resumePlay {
		resumeResult = &pausePointResumePlayResult{Skipped: pausePointResumeSkippedForExistingHitMessage}
	}
	var triggerResult *pausePointTriggerResult
	if options.triggerCommand != "" {
		triggerResult = &pausePointTriggerResult{
			Command: pausePointTriggerCommandString(options.triggerCommand, options.triggerArgs),
			Error:   pausePointTriggerSkippedForExistingHitError,
		}
	}
	return resumeResult, triggerResult
}

// queryPausePointArmStatus reports whether the marker is enabled or already hit. A query failure is
// treated as not armed: dispatching a --trigger command against a marker this CLI cannot even
// confirm exists would inject the trigger's action into the game with no corresponding wait.
func queryPausePointArmStatus(
	ctx context.Context,
	connection unityipc.Connection,
	id string,
) (pausePointStatusResponse, bool, error) {
	response, err := queryPausePointStatus(ctx, connection, id)
	if err != nil {
		return pausePointStatusResponse{}, false, err
	}
	state := pausePointWaitStateForStatus(response.Status)
	return response, state == "" || state == pausePointWaitStateHit, nil
}

const (
	pausePointResumeNotArmedAtWaitStartError  = "resume was not dispatched: the marker could not be confirmed armed at wait start"
	pausePointTriggerNotArmedAtWaitStartError = "trigger was not dispatched: the marker could not be confirmed armed at wait start"

	pausePointResumeSkippedForExistingHitMessage = "resume was skipped: the marker had already hit " +
		"before this wait started, so the recorded hit is returned as-is and Play Mode is left as it is; " +
		"clear or re-enable the marker and wait again to capture a new hit"
	pausePointTriggerSkippedForExistingHitError = "trigger was not dispatched: the marker had already " +
		"hit before this wait started, so the recorded hit is returned as-is; clear or re-enable the " +
		"marker and wait again to capture a new hit"
)

func pausePointUnarmedAtWaitStartSuffix(response pausePointStatusResponse, queryErr error) string {
	if queryErr != nil {
		return ""
	}
	return fmt.Sprintf(
		" marker status was '%s' (ClearedReason: %s)",
		response.Status,
		response.ClearedReason,
	)
}

// decidePausePointNewHitBaseline returns (sequence, hasBaseline, decided) for the wait-start
// snapshot. markerJustEnabled forces no baseline: any Hit during enable --await is the success
// itself, including a race where the first status query already observes sequence 1.
func decidePausePointNewHitBaseline(
	response pausePointStatusResponse,
	markerJustEnabled bool,
) (int, bool, bool) {
	if markerJustEnabled {
		return -1, false, true
	}
	sequence, hasBaseline := pausePointNewHitBaseline(response)
	return sequence, hasBaseline, true
}

// pausePointNewHitBaseline records LastHitSequence when await starts against an already-hit
// continuous/trace marker. Mode is allowlisted (never `!= "single-shot"`): an empty Mode is the
// old-package skew case and must keep the historical immediate-Hit success path.
func pausePointNewHitBaseline(response pausePointStatusResponse) (int, bool) {
	if response.Status != pausePointStatusHit {
		return -1, false
	}
	if response.Mode != pausePointModeContinuous && response.Mode != pausePointModeTrace {
		return -1, false
	}
	return response.LastHitSequence, true
}
