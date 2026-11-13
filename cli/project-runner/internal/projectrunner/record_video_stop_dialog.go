package projectrunner

import (
	"errors"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
	"github.com/hatayama/unity-cli-loop/common/tooldocs"
)

const (
	recordVideoStopBlockedSentence = "uloop record-video could not stop the recording it started; the recording goes on " +
		"in Unity."
	recordVideoStopDialogClosedNextAction = "After the dialog is closed, run uloop record-video --action stop to stop " +
		"the recording and get its files."
)

// recordingStopBlockedByDialog rewords a dialog that kept the one-shot's stop from running. Why: the
// stop's own error says record-video was not run and is safe to retry, but the start earlier in the
// same command did run, so the recording goes on in Unity and running the one-shot again would start
// another. Any other error is returned unchanged.
func recordingStopBlockedByDialog(err error) error {
	var blocked blockedByDialogError
	if !errors.As(err, &blocked) || (!blocked.notSent && blocked.notRun == nil) {
		return err
	}
	return blockedByDialogError{
		command:          tooldocs.RecordVideoCommandName,
		dialogs:          blocked.dialogs,
		canPress:         blocked.canPress,
		stopped:          recordVideoStopBlockedSentence,
		closedNextAction: recordVideoStopDialogClosedNextAction,
		phase:            clierrors.ErrorPhaseExecution,
		safeToRetry:      false,
		details:          blockedStopDetails(blocked),
	}
}

// blockedStopDetails keeps what the stop's error reported beside Dialogs, such as the stall Unity
// saw before it answered main_thread_blocked.
func blockedStopDetails(blocked blockedByDialogError) map[string]any {
	details := map[string]any{}
	for key, value := range blocked.details {
		details[key] = value
	}
	if blocked.notRun != nil {
		notRun := clierrors.ClassifyError(blocked.notRun, clierrors.ErrorContext{Command: tooldocs.RecordVideoCommandName})
		for key, value := range notRun.Details {
			details[key] = value
		}
	}
	return details
}
