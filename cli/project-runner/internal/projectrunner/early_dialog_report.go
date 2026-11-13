package projectrunner

import (
	"context"
	"time"

	"github.com/hatayama/unity-cli-loop/common/clicontract"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

const (
	reportBlockingDialogBridgeCommandName = "report-blocking-dialog"

	// earlyDialogLookDelay is how long a sent request may stay unanswered before uloop looks for a
	// dialog. Why 2 seconds: most commands have answered by then, so they never pay for the look,
	// and it is past the one-second stall Unity asks for before it fails a request for a reported dialog.
	earlyDialogLookDelay = 2 * time.Second

	// earlyDialogLookWindow pairs with MainThreadEntryWatchdog.BlockedStallSeconds in the Unity
	// package: from 30 seconds on Unity fails a request that has not started by itself, and the
	// heartbeat watch covers one that has.
	earlyDialogLookWindow = 30 * time.Second

	// Long enough for an Editor that answers off its main thread, which report-blocking-dialog
	// always does.
	reportBlockingDialogTimeout = 5 * time.Second
)

// earlyDialogReportDeps is how the send looks for a dialog that opened after the check before
// sending. A nil probe or report turns it off, so only the default dependencies look.
type earlyDialogReportDeps struct {
	// probeEditorStatus reads the Editor the way the check before sending does.
	probeEditorStatus func(context.Context, unityipc.Connection) (statusReport, error)
	// reportBlockingDialog tells Unity that a dialog holds its main thread.
	reportBlockingDialog func(context.Context, unityipc.Connection) error
	waitFor              func(time.Duration) <-chan time.Time
}

func defaultEarlyDialogReportDeps() earlyDialogReportDeps {
	return earlyDialogReportDeps{
		probeEditorStatus:    probeEditorStatusBeforeSend,
		reportBlockingDialog: sendReportBlockingDialog,
		waitFor:              time.After,
	}
}

// startEarlyDialogReport watches one sent request for a dialog that opened after the check before
// sending, and returns the function that ends the watch and waits for it to stop.
// It only tells Unity about the dialog. Unity then answers the request with main_thread_blocked if
// its tool code has not started, and leaves it alone if it has.
// Why uloop never ends the wait itself here: hanging up on a request Unity has not started does not
// stop it, because compile and some run-tests requests still run after the client is gone.
func startEarlyDialogReport(
	ctx context.Context,
	connection unityipc.Connection,
	method string,
	deps earlyDialogReportDeps,
) func() {
	if deps.probeEditorStatus == nil || deps.reportBlockingDialog == nil || internalBridgeMethods[method] {
		return func() {}
	}
	watchCtx, cancel := context.WithCancel(ctx)
	stopped := make(chan struct{})
	go func() {
		defer close(stopped)
		runEarlyDialogReport(watchCtx, connection, deps, heartbeatDialogInterval(ctx))
	}()
	return func() {
		cancel()
		<-stopped
	}
}

// runEarlyDialogReport looks once after earlyDialogLookDelay and then every interval, until the
// window ends, ctx is done, or Unity does not take the report.
func runEarlyDialogReport(
	ctx context.Context,
	connection unityipc.Connection,
	deps earlyDialogReportDeps,
	interval time.Duration,
) {
	wait := earlyDialogLookDelay
	for elapsed := earlyDialogLookDelay; elapsed < earlyDialogLookWindow; elapsed += interval {
		select {
		case <-ctx.Done():
			return
		case <-deps.waitFor(wait):
		}
		wait = interval
		if !isBlockedByDialog(ctx, connection, deps) {
			continue
		}
		// Why stop on a failed report: a Unity package older than this command answers it with an
		// error every time, and that package keeps its 30-second answer.
		if err := deps.reportBlockingDialog(ctx, connection); err != nil {
			return
		}
	}
}

func isBlockedByDialog(ctx context.Context, connection unityipc.Connection, deps earlyDialogReportDeps) bool {
	report, err := deps.probeEditorStatus(ctx, connection)
	return err == nil && report.State == statusStateBlockedByDialog && len(report.Dialogs) > 0
}

func sendReportBlockingDialog(ctx context.Context, connection unityipc.Connection) error {
	sendCtx, cancel := context.WithTimeout(ctx, reportBlockingDialogTimeout)
	defer cancel()
	_, err := unityipc.NewClient(connection, clicontract.ProjectRunnerVersion()).Send(
		sendCtx,
		reportBlockingDialogBridgeCommandName,
		map[string]any{})
	return err
}
