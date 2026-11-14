package projectrunner

import (
	"context"
	"time"

	"github.com/hatayama/unity-cli-loop/common/clicontract"
	cliprogress "github.com/hatayama/unity-cli-loop/common/progress"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

const (
	reportBlockingDialogBridgeCommandName = "report-blocking-dialog"
	// reportBlockingDialogToolNameParam mirrors ReportBlockingDialogBridgeCommand.ToolNameParam.
	reportBlockingDialogToolNameParam = "ToolName"

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
	// reportBlockingDialog tells Unity that a dialog holds its main thread while a request of the
	// named command waits.
	reportBlockingDialog func(context.Context, unityipc.Connection, string) error
	waitFor              func(time.Duration) <-chan time.Time
}

func defaultEarlyDialogReportDeps() earlyDialogReportDeps {
	return earlyDialogReportDeps{
		probeEditorStatus:    probeEditorStatusWhileUnanswered,
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
// Why a send with a responseTimeout is not watched: it hangs up when the timeout ends and moves on
// to another wait, while its request stays in Unity and runs once the dialog closes. A report then
// would make Unity drop the request as not run while uloop says it keeps running.
func startEarlyDialogReport(
	ctx context.Context,
	connection unityipc.Connection,
	method string,
	responseTimeout time.Duration,
	deps earlyDialogReportDeps,
) func() {
	if deps.probeEditorStatus == nil || deps.reportBlockingDialog == nil || internalBridgeMethods[method] ||
		responseTimeout > 0 {
		return func() {}
	}
	watchCtx, cancel := context.WithCancel(ctx)
	stopped := make(chan struct{})
	go func() {
		defer close(stopped)
		runEarlyDialogReport(watchCtx, connection, method, deps, heartbeatDialogInterval(ctx))
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
	method string,
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
		if !isOwnCommandHeldByDialog(ctx, connection, method, deps.probeEditorStatus) {
			continue
		}
		// Why stop on a failed report: a Unity package older than this command answers it with an
		// error every time, and that package keeps its 30-second answer.
		if err := deps.reportBlockingDialog(ctx, connection, method); err != nil {
			return
		}
	}
}

// isOwnCommandHeldByDialog reports whether the probe finds a dialog holding Unity while a command
// named method holds the command slot.
// Why: Unity acks a request before it takes the slot, so this send's request may still be waiting for
// a busy answer while another command holds the slot. Unity fails early only a request of the
// reported name, so a report sent then would fail nothing; this check saves that report. The safety
// that a report never fails another command's request, such as a compile uloop said keeps running,
// lives in Unity, because the slot can change hands between this look and the report.
func isOwnCommandHeldByDialog(
	ctx context.Context,
	connection unityipc.Connection,
	method string,
	probe func(context.Context, unityipc.Connection) (statusReport, error),
) bool {
	report, err := probe(ctx, connection)
	return err == nil && report.State == statusStateBlockedByDialog && len(report.Dialogs) > 0 &&
		report.RunningToolName == method
}

// findDialog returns the dialogs the probe reports holding Unity, or nil when it reports none or fails.
func findDialog(
	ctx context.Context,
	connection unityipc.Connection,
	probe func(context.Context, unityipc.Connection) (statusReport, error),
) []statusDialog {
	report, err := probe(ctx, connection)
	if err != nil || report.State != statusStateBlockedByDialog {
		return nil
	}
	return report.Dialogs
}

// findDialogWhileUnanswered looks for a dialog the way the early dialog report does.
func findDialogWhileUnanswered(ctx context.Context, connection unityipc.Connection) []statusDialog {
	return findDialog(ctx, connection, probeEditorStatusWhileUnanswered)
}

// sendReportBlockingDialog sends the report with the command name it is for, so Unity fails early
// only a request of that command.
func sendReportBlockingDialog(ctx context.Context, connection unityipc.Connection, toolName string) error {
	sendCtx, cancel := context.WithTimeout(ctx, reportBlockingDialogTimeout)
	defer cancel()
	_, err := unityipc.NewClient(connection, clicontract.ProjectRunnerVersion()).Send(
		sendCtx,
		reportBlockingDialogBridgeCommandName,
		map[string]any{reportBlockingDialogToolNameParam: toolName})
	return err
}

// waitingForMainThreadPhase is the RunningToolPhase of a command that holds the slot but waits for
// the Editor's main thread to start its tool code.
const waitingForMainThreadPhase = "WaitingForMainThread"

func probeEditorStatusWhileUnanswered(ctx context.Context, connection unityipc.Connection) (statusReport, error) {
	return probeEditorStatusWhileUnansweredWithDeps(ctx, connection, preSendStatusDeps(defaultStatusCommandDeps()))
}

// probeEditorStatusWhileUnansweredWithDeps reads the Editor the way the check before sending does,
// and also looks at Unity's windows when a command waits for a main thread that has been still for a
// second. Either way one probe looks at Unity's windows at most once.
// Why: the sent request itself holds the slot, so the Editor answers Busy, and uloop status looks
// behind Busy only from a five-second stall. The early report must find the dialog well before that.
func probeEditorStatusWhileUnansweredWithDeps(
	ctx context.Context,
	connection unityipc.Connection,
	deps statusCommandDeps,
) (statusReport, error) {
	response, report, err := readEditorStatus(ctx, connection, deps)
	if response == nil {
		return report, err
	}
	if classifyEditorState(*response) != statusStateBusy || !isWaitingForStillMainThread(*response) {
		return probeEditorStatusFromResponse(ctx, connection, *response, deps)
	}
	// Why no second question to tell sleeping from blocked: the waiting request keeps waking the
	// Editor loop, and Unity checks again that the main thread has not run since the report.
	return inspectForDialogs(ctx, connection.ProjectRoot, response.ProcessId, reportFromEditorResponse(*response), deps,
		func(dialogs []statusDialog) statusReport {
			return blockedByDialogReport(*response, dialogs)
		}), nil
}

// isWaitingForStillMainThread reports whether a command waits for a main thread that has been still
// for a second.
func isWaitingForStillMainThread(response editorStatusResponse) bool {
	return response.RunningToolPhase == waitingForMainThreadPhase &&
		response.SecondsSinceLastMainThreadTick >= editorStatusEarlyStallSeconds
}

// sendAttemptWithEarlyDialogReport makes one send attempt through send and watches it for a dialog
// only from Unity's accept of that attempt until the attempt returns, waiting for the watch to stop.
// progress may be nil; it still gets every event the attempt reports.
// Why per accepted attempt: Unity acks a request before it takes the command slot, so an attempt it
// then refuses as busy was accepted too. A watch that outlived that attempt would report the dialog
// for another request holding the slot, such as a compile uloop told the user keeps running.
func sendAttemptWithEarlyDialogReport(
	ctx context.Context,
	connection unityipc.Connection,
	method string,
	responseTimeout time.Duration,
	deps earlyDialogReportDeps,
	progress unityipc.ProgressFunc,
	send func(unityipc.ProgressFunc) (unityipc.UnitySendOutcome, error),
) (unityipc.UnitySendOutcome, error) {
	stop := func() {}
	defer func() { stop() }()
	watched := func(event cliprogress.Event) {
		if event.Stage == cliprogress.StageAccepted {
			stop = startEarlyDialogReport(ctx, connection, method, responseTimeout, deps)
		}
		if progress != nil {
			progress(event)
		}
	}
	return send(watched)
}
