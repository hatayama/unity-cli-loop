package projectrunner

import (
	"context"

	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

// hotReloadDialogWatch ends hot-reload's waits for the Editor when a modal dialog holds it. Why stop:
// the agent that runs hot-reload cannot send another command to answer the dialog while hot-reload
// keeps waiting.
type hotReloadDialogWatch struct {
	watch    *dialogBlockWatch
	canPress bool
}

func newHotReloadDialogWatch(connection unityipc.Connection, options hotReloadEditorReadyWaitOptions) hotReloadDialogWatch {
	return hotReloadDialogWatch{
		watch:    newDialogBlockWatch(connection, options.dialogWatch, dialogWatchInterval, dialogWatchStallThresholdSeconds),
		canPress: options.dialogWatch.canPressDialogs,
	}
}

// hotReloadBlock returns the error that ends the wait when the status shows a dialog, and nil
// otherwise. The dialogs the Editor reports count at once; on an Editor older than Unity 6000.7,
// which reports none, Unity's windows are looked at once the reported main-thread stall is long.
func (watch hotReloadDialogWatch) hotReloadBlock(ctx context.Context, status editorStatusResponse, answered bool) error {
	dialogs := watch.watch.observeEditorStatus(ctx, status, answered)
	if dialogs == nil {
		return nil
	}
	return hotReloadWaitBlockedByDialog(dialogs, watch.canPress)
}
