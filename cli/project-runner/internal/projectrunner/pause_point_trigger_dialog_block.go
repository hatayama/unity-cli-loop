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
	pausePointTriggerResultDetailKey    = "TriggerResult"
	pausePointResumePlayResultDetailKey = "ResumePlayResult"
)

// pausePointTriggerNotRunDialogs returns the dialogs a --trigger command reported when it stopped on a
// dialog before it ran: its stderr envelope is BLOCKED_BY_DIALOG with Details.CommandNotRun. ok is
// false for every other result, including a dialog error of a trigger that had already started.
func pausePointTriggerNotRunDialogs(result *pausePointTriggerResult) ([]statusDialog, bool) {
	if result == nil {
		return nil, false
	}
	trimmed := bytes.TrimSpace([]byte(result.Error))
	if len(trimmed) == 0 {
		return nil, false
	}
	envelope := struct {
		Error struct {
			ErrorCode string `json:"ErrorCode"`
			Details   struct {
				CommandNotRun bool           `json:"CommandNotRun"`
				Dialogs       []statusDialog `json:"Dialogs"`
			} `json:"Details"`
		} `json:"Error"`
	}{}
	if err := json.Unmarshal(trimmed, &envelope); err != nil {
		return nil, false
	}
	if envelope.Error.ErrorCode != clierrors.ErrorCodeBlockedByDialog || !envelope.Error.Details.CommandNotRun ||
		len(envelope.Error.Details.Dialogs) == 0 {
		return nil, false
	}
	return envelope.Error.Details.Dialogs, true
}

// pausePointTriggerWaitsToRun reports whether the report a dialog look returned shows the --trigger
// command holding Unity's command slot while it still waits for the main thread, and whether Unity
// drops such a request when this uloop process exits. Both together mean the trigger will not run.
// Why the name match: another client's command, or the compile run-tests starts first, can hold the
// slot instead. Why ClientDisconnectCancelsRunningTool: compile and run-tests can outlive the
// connection, and the report carries only the name, not the options that decide it.
func pausePointTriggerWaitsToRun(report statusReport, triggerCommand string) bool {
	return triggerCommand != "" &&
		report.RunningToolName == triggerCommand &&
		report.RunningToolPhase == waitingForMainThreadPhase &&
		clierrors.ClientDisconnectCancelsRunningTool(report.RunningToolName)
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
