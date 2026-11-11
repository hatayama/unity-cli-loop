package projectrunner

import (
	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
)

const (
	hotReloadStoppedWaitingSentence = " uloop hot-reload stopped waiting, and nothing was applied."
	hotReloadDialogClosedNextAction = "After the dialog is closed, run uloop hot-reload again."
)

// hotReloadBlockedByDialogError ends a hot-reload wait for the Editor because the Editor reported
// a modal dialog: the dialog holds the main thread until someone answers it, and the agent that
// runs hot-reload cannot send another command to answer it while hot-reload keeps waiting.
type hotReloadBlockedByDialogError struct {
	// dialogs is what the Editor reported, frontmost first; never empty.
	dialogs []statusDialog
	// canPress is whether uloop press-dialog-button works on this operating system.
	canPress bool
}

func (err hotReloadBlockedByDialogError) Error() string {
	return blockedByDialogMessage(err.dialogs) + hotReloadStoppedWaitingSentence
}

// ToCLIError reports the dialogs the way uloop status does, with the last step pointing back at
// hot-reload. Safe to retry: both waits stop before any apply runs.
func (err hotReloadBlockedByDialogError) ToCLIError(context clierrors.ErrorContext) clierrors.CLIError {
	return clierrors.CLIError{
		ErrorCode:   clierrors.ErrorCodeBlockedByDialog,
		Phase:       clierrors.ErrorPhaseExecution,
		Message:     err.Error(),
		Retryable:   true,
		SafeToRetry: true,
		ProjectRoot: context.ProjectRoot,
		Command:     context.Command,
		NextActions: dialogAnswerNextActions(err.dialogs, err.canPress, hotReloadDialogClosedNextAction),
		Details:     map[string]any{"Dialogs": err.dialogs},
	}
}

// hotReloadDialogBlock returns the error that ends a wait when the Editor answered the status
// request and reported an open modal dialog, and nil otherwise. Only the dialogs the Editor itself
// reports count: an Editor older than Unity 6000.7 reports none, and the wait goes on as before.
func hotReloadDialogBlock(status editorStatusResponse, answered bool, canPress bool) error {
	if !answered || len(status.OpenDialogs) == 0 {
		return nil
	}
	return hotReloadBlockedByDialogError{dialogs: status.OpenDialogs, canPress: canPress}
}
