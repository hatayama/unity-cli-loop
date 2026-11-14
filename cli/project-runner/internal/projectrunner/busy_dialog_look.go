package projectrunner

import (
	"context"

	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

// busyDialogLook looks once per send for a dialog behind a busy answer whose slot holder waits for a
// main thread that has been still for a second. It only looks: it never reports the dialog to Unity,
// so the holder, which may be a compile uloop said keeps running, is not failed.
// Why look at all: this send was never run, so a dialog that holds the slot is what the caller must
// answer, while the busy error would tell it to wait or restart Unity.
type busyDialogLook struct {
	looked bool
}

// observe returns the dialogs found behind err, or nil when err is not such a busy answer, a look
// already ran for this send, probe is nil, or the look finds none.
// Why only a still holder waiting for the main thread: busy answers between back-to-back commands are
// ordinary, and a look starts osascript on macOS.
func (look *busyDialogLook) observe(
	ctx context.Context,
	connection unityipc.Connection,
	err error,
	probe func(context.Context, unityipc.Connection) (statusReport, error),
) []statusDialog {
	if look.looked || probe == nil {
		return nil
	}
	holder, ok := decodeServerBusyHolder(err)
	if !ok || holder.RunningToolPhase != waitingForMainThreadPhase ||
		holder.SecondsSinceLastMainThreadTick == nil ||
		*holder.SecondsSinceLastMainThreadTick < editorStatusEarlyStallSeconds {
		return nil
	}
	look.looked = true
	return findDialog(ctx, connection, probe)
}
