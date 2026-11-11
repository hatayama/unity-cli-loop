package projectrunner

// hotReloadDialogBlock returns the error that ends a hot-reload wait for the Editor when the Editor
// answered the status request and reported an open modal dialog, and nil otherwise. Only the dialogs
// the Editor itself reports count: an Editor older than Unity 6000.7 reports none, and the wait goes
// on as before. Why stop: the agent that runs hot-reload cannot send another command to answer the
// dialog while hot-reload keeps waiting.
func hotReloadDialogBlock(status editorStatusResponse, answered bool, canPress bool) error {
	if !answered || len(status.OpenDialogs) == 0 {
		return nil
	}
	return blockedByDialogError{command: hotReloadCommandName, dialogs: status.OpenDialogs, canPress: canPress}
}
