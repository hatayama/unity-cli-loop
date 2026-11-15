package projectrunner

import (
	"bytes"
	"encoding/json"
	"fmt"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
)

const (
	pausePointTriggerNotRunStoppedFormat = "uloop %s stopped waiting; its --trigger command was not run, and the marker " +
		"stays armed."
	pausePointTriggerNotRunClosedNextActionFormat = "After the dialog is closed, run uloop await-pause-point --id %s " +
		"again with the same --trigger."
	// pausePointTriggerNotRunResumedNextAction follows the closed step only when --resume-play resumed
	// PlayMode, which keeps playing once the dialog closes.
	pausePointTriggerNotRunResumedNextAction = "--resume-play resumed PlayMode, and it keeps playing once the dialog " +
		"is closed; to wait from a paused PlayMode again, run uloop control-play-mode --action Pause first, and pass " +
		"--resume-play along with the same --trigger."
	pausePointTriggerResultDetailKey = "TriggerResult"
	// pausePointTriggerMainThreadBlockedErrorCode is the ErrorCode of Unity's main_thread_blocked answer
	// when the trigger's send found no dialog to name. Why a local copy of the wire value: exporting
	// the one in the shared errors module would make it a shared release input for this alone.
	pausePointTriggerMainThreadBlockedErrorCode = "MAIN_THREAD_BLOCKED"
	pausePointResumePlayResultDetailKey         = "ResumePlayResult"
)

const (
	pausePointTriggerNotSentStoppedFormat   = "uloop %s stopped waiting; its --trigger command was not sent."
	pausePointResumeNotSentStoppedFormat    = "uloop %s stopped waiting; --resume-play did not resume PlayMode."
	pausePointNotSentClosedNextActionFormat = "After the dialog is closed, run uloop await-pause-point --id %s " +
		"again with the same %s."
)

// pausePointTriggerNotRunBehindDialog returns the dialogs to report when a --trigger command was kept
// from running by a dialog, and ok false for every other result. It accepts the trigger's
// BLOCKED_BY_DIALOG envelope with Details.CommandNotRun, which names the dialogs itself, and, when
// foundDialogs is not empty, Unity's bare main_thread_blocked answer: the trigger's send reports that
// when its own look found no dialog, for example because the look failed, while the wait's own watch
// had found foundDialogs. A dialog error of a trigger that had already started is not accepted.
func pausePointTriggerNotRunBehindDialog(result *pausePointTriggerResult, foundDialogs []statusDialog) ([]statusDialog, bool) {
	envelope, ok := decodePausePointTriggerErrorEnvelope(result)
	if !ok {
		return nil, false
	}
	if envelope.ErrorCode == pausePointTriggerMainThreadBlockedErrorCode && len(foundDialogs) > 0 {
		return foundDialogs, true
	}
	if envelope.ErrorCode != clierrors.ErrorCodeBlockedByDialog || !envelope.Details.CommandNotRun ||
		len(envelope.Details.Dialogs) == 0 {
		return nil, false
	}
	return envelope.Details.Dialogs, true
}

// pausePointTriggerNotRunOnStalledMainThread reports whether Unity answered the trigger with
// main_thread_blocked: its main thread did not run the trigger within Unity's wait, so the trigger
// performed nothing and waiting longer cannot bring it.
func pausePointTriggerNotRunOnStalledMainThread(result *pausePointTriggerResult) bool {
	envelope, ok := decodePausePointTriggerErrorEnvelope(result)
	return ok && envelope.ErrorCode == pausePointTriggerMainThreadBlockedErrorCode
}

// pausePointTriggerErrorEnvelope is the part of a trigger's stderr error envelope the wait reads.
type pausePointTriggerErrorEnvelope struct {
	ErrorCode string `json:"ErrorCode"`
	Details   struct {
		CommandNotRun bool           `json:"CommandNotRun"`
		Dialogs       []statusDialog `json:"Dialogs"`
	} `json:"Details"`
}

func decodePausePointTriggerErrorEnvelope(result *pausePointTriggerResult) (pausePointTriggerErrorEnvelope, bool) {
	if result == nil {
		return pausePointTriggerErrorEnvelope{}, false
	}
	trimmed := bytes.TrimSpace([]byte(result.Error))
	if len(trimmed) == 0 {
		return pausePointTriggerErrorEnvelope{}, false
	}
	envelope := struct {
		Error pausePointTriggerErrorEnvelope `json:"Error"`
	}{}
	if err := json.Unmarshal(trimmed, &envelope); err != nil {
		return pausePointTriggerErrorEnvelope{}, false
	}
	return envelope.Error, true
}

// pausePointTriggerWaitsForMainThread reports whether the report a dialog look returned shows the
// --trigger command holding Unity's command slot while it still waits for the main thread. Why the name
// match: another client's command, or the compile run-tests starts first, can hold the slot instead.
func pausePointTriggerWaitsForMainThread(report statusReport, triggerCommand string) bool {
	return triggerCommand != "" &&
		report.RunningToolName == triggerCommand &&
		report.RunningToolPhase == waitingForMainThreadPhase
}

// pausePointTriggerNotRunBlockedByDialog is the error of a pause-point wait whose --trigger command a
// dialog kept from running. The marker stays armed and the trigger never ran, so waiting again with the
// same --trigger is safe. triggerResult and resumeResult may be nil and are then left out of Details.
func pausePointTriggerNotRunBlockedByDialog(
	options waitForPausePointOptions,
	dialogs []statusDialog,
	triggerResult *pausePointTriggerResult,
	resumeResult *pausePointResumePlayResult,
) blockedByDialogError {
	details := map[string]any{
		pausePointDialogIDDetailKey: options.id,
		commandNotRunDetailKey:      true,
	}
	if triggerResult != nil {
		details[pausePointTriggerResultDetailKey] = triggerResult
	}
	var afterClosed []string
	if resumeResult != nil {
		details[pausePointResumePlayResultDetailKey] = resumeResult
		// Why no re-pause, as a rejected trigger gets: the dialog holds the main thread, so a Pause
		// request would only wait behind it. The agent pauses after closing the dialog instead.
		if resumeResult.Resumed {
			afterClosed = []string{pausePointTriggerNotRunResumedNextAction}
		}
	}
	return blockedByDialogError{
		command:                options.commandName(),
		dialogs:                dialogs,
		canPress:               options.dialogWatch.canPressDialogs,
		stopped:                fmt.Sprintf(pausePointTriggerNotRunStoppedFormat, options.commandName()),
		closedNextAction:       fmt.Sprintf(pausePointTriggerNotRunClosedNextActionFormat, shellSafePausePointID(options.id)),
		afterClosedNextActions: afterClosed,
		phase:                  clierrors.ErrorPhaseResponseWaiting,
		safeToRetry:            true,
		details:                details,
	}
}

// pausePointWaitUnsentBehindDialog is the error of a wait that a dialog ended before it sent its
// --trigger or resumed PlayMode, and ok false when it did either, or used neither. Nothing reached the
// game then, so waiting again with the same arguments is safe. Why it does not say the marker stays
// armed: the trigger was also skipped when Unity answered that the marker is not armed.
func pausePointWaitUnsentBehindDialog(
	options waitForPausePointOptions,
	dialogs []statusDialog,
	skippedTriggerResult *pausePointTriggerResult,
	resumeResult *pausePointResumePlayResult,
) (blockedByDialogError, bool) {
	details := map[string]any{pausePointDialogIDDetailKey: options.id}
	if resumeResult != nil {
		details[pausePointResumePlayResultDetailKey] = resumeResult
	}
	var stoppedFormat, sameArguments string
	switch {
	case skippedTriggerResult != nil:
		details[commandNotRunDetailKey] = true
		details[pausePointTriggerResultDetailKey] = skippedTriggerResult
		stoppedFormat, sameArguments = pausePointTriggerNotSentStoppedFormat, "--trigger"
		if options.resumePlay {
			sameArguments = "--trigger and --resume-play"
		}
	case options.triggerCommand == "" && resumeResult != nil && !resumeResult.Resumed:
		stoppedFormat, sameArguments = pausePointResumeNotSentStoppedFormat, "--resume-play"
	default:
		return blockedByDialogError{}, false
	}
	return blockedByDialogError{
		command:          options.commandName(),
		dialogs:          dialogs,
		canPress:         options.dialogWatch.canPressDialogs,
		stopped:          fmt.Sprintf(stoppedFormat, options.commandName()),
		closedNextAction: fmt.Sprintf(pausePointNotSentClosedNextActionFormat, shellSafePausePointID(options.id), sameArguments),
		phase:            clierrors.ErrorPhaseResponseWaiting,
		safeToRetry:      true,
		details:          details,
	}, true
}
