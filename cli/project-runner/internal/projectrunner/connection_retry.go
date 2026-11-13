package projectrunner

import (
	"context"
	"errors"
	"fmt"
	"strings"
	"time"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
	"github.com/hatayama/unity-cli-loop/common/vibelog"

	"github.com/hatayama/unity-cli-loop/common/unityipc"
	"github.com/hatayama/unity-cli-loop/common/unityprocess"
)

const (
	focusRestoreTimeout                 = 2 * time.Second
	serverConnectionRetryDefaultTimeout = 10 * time.Second
	serverConnectionRetryDefaultPoll    = 1 * time.Second
)

type connectionRetryDeps struct {
	findRunningUnityProcess func(context.Context, string) (*unityprocess.UnityProcess, error)
	focusUnityProcess       func(context.Context, int) (unityprocess.RestoreFocusFunc, error)
	retryTimeout            time.Duration
	retryPoll               time.Duration
	// returnBusyWithoutRetry hands the first busy answer back instead of resending every
	// retryPoll. Hot reload sets it because it waits for the busy slot to free on the Editor
	// status: resending here would only delay that wait.
	returnBusyWithoutRetry bool
	// dialogWatch looks for the dialog that holds Unity's main thread when the response waits on a
	// long stall, and when the send ends in main_thread_blocked or after the five-minute stall.
	dialogWatch dialogWatchDeps
}

func defaultConnectionRetryDeps() connectionRetryDeps {
	return connectionRetryDeps{
		findRunningUnityProcess: unityProcessLookup,
		focusUnityProcess:       unityprocess.FocusUnityProcessWithRestore,
		retryTimeout:            serverConnectionRetryDefaultTimeout,
		retryPoll:               serverConnectionRetryDefaultPoll,
		dialogWatch:             defaultDialogWatchDeps(),
	}
}

type connectionRetryFocusReason string

const (
	focusReasonUndispatchedConnectionFailure connectionRetryFocusReason = "undispatched_connection_failure"
	focusReasonPreAcceptTimeout              connectionRetryFocusReason = "pre_accept_timeout"
	focusReasonMainThreadStall               connectionRetryFocusReason = "main_thread_stall"
	focusReasonHeartbeatSilenceTimeout       connectionRetryFocusReason = "heartbeat_silence_timeout"
	focusReasonFinalResponseTimeout          connectionRetryFocusReason = "final_response_timeout"
	focusReasonCompileStartPending           connectionRetryFocusReason = "compile_start_pending"
)

// Why: domain reload on large projects can keep the IPC endpoint down well past the
// base retry window, and an undispatched dial failure is always safe to retry. While
// a running Unity process is confirmed, the dial-retry window stretches to
// base * factor (60s by default) so an external reload does not fail commands
// spuriously. Busy responses stay bounded by the base window: busy proves the server
// is alive and surfacing it quickly lets the caller decide.
const serverConnectionRetryUnityAliveFactor = 6

func unityAliveRetryWindow(deps connectionRetryDeps) time.Duration {
	return deps.retryTimeout * serverConnectionRetryUnityAliveFactor
}

type connectionRetryFocusController struct {
	connection   unityipc.Connection
	method       string
	deps         connectionRetryDeps
	attempted    bool
	restoreFocus unityprocess.RestoreFocusFunc
	// Captured when the focus succeeds so restore-outcome logs can be joined to the
	// attempt logs through the same correlation ID instead of standing alone.
	focusCorrelationID string
	focusedPid         int
	focusReason        connectionRetryFocusReason
	// focusSucceeded is kept apart from restoreFocus because a focus can succeed without a
	// restorer when the previously frontmost process could not be read.
	focusSucceeded bool
}

func newConnectionRetryFocusController(connection unityipc.Connection, method string, deps connectionRetryDeps) *connectionRetryFocusController {
	return &connectionRetryFocusController{
		connection: connection,
		method:     method,
		deps:       deps,
	}
}

func (controller *connectionRetryFocusController) restore(ctx context.Context) {
	// Silent by design when no restorer is stored: focus never happened (the common
	// per-command case), the restore was intentionally skipped, or the missing
	// restorer was already logged at focus time.
	if controller.restoreFocus == nil {
		return
	}
	restoreErr := controller.restoreFocus(ctx)
	if restoreErr != nil {
		logConnectionRetryFocusRestoreFailure(
			controller.connection,
			controller.method,
			controller.focusedPid,
			controller.focusReason,
			restoreErr,
			controller.focusCorrelationID,
		)
		return
	}
	logConnectionRetryFocusRestoreSuccess(
		controller.connection,
		controller.method,
		controller.focusedPid,
		controller.focusReason,
		controller.focusCorrelationID,
	)
}

func (controller *connectionRetryFocusController) keepUnityFocusedAfterReturn() {
	// Why: terminal timeout recovery has no in-flight request left, so immediate
	// restore hides the auto-front signal that tells the user Unity needs attention.
	if controller.restoreFocus == nil {
		return
	}
	controller.restoreFocus = nil
	logConnectionRetryFocusRestoreSkipped(
		controller.connection,
		controller.method,
		controller.focusedPid,
		controller.focusReason,
		controller.focusCorrelationID,
	)
}

func (controller *connectionRetryFocusController) tryFocus(
	ctx context.Context,
	reason connectionRetryFocusReason,
	cause error,
) {
	if controller.attempted {
		return
	}

	runningProcess, err := controller.deps.findRunningUnityProcess(ctx, controller.connection.ProjectRoot)
	if err != nil || runningProcess == nil {
		return
	}
	controller.tryFocusProcess(ctx, runningProcess.Pid, reason, cause)
}

func (controller *connectionRetryFocusController) tryFocusProcess(
	ctx context.Context,
	pid int,
	reason connectionRetryFocusReason,
	cause error,
) {
	if controller.attempted {
		return
	}
	controller.attempted = true

	correlationID := vibelog.NewCLIVibeCorrelationID()
	logConnectionRetryFocusAttempt(controller.connection, controller.method, pid, reason, cause, correlationID)
	focusContext, cancel := context.WithTimeout(ctx, unityprocess.FocusCommandTimeout)
	defer cancel()
	restorer, focusErr := controller.deps.focusUnityProcess(focusContext, pid)
	if focusErr == nil {
		controller.focusSucceeded = true
		controller.restoreFocus = restorer
		controller.focusCorrelationID = correlationID
		controller.focusedPid = pid
		controller.focusReason = reason
		logConnectionRetryFocusSuccess(controller.connection, controller.method, pid, reason, cause, correlationID)
		if restorer == nil {
			// A nil restorer with a nil error means the previous frontmost PID could not
			// be read, so no restore can ever run for this focus. Logged once here
			// because restore() stays silent without a restorer.
			logConnectionRetryFocusRestoreUnavailable(controller.connection, controller.method, pid, reason, correlationID)
		}
		return
	}
	logConnectionRetryFocusFailure(controller.connection, controller.method, pid, reason, cause, focusErr, correlationID)
}

func (controller *connectionRetryFocusController) handleMainThreadStall(ctx context.Context, stallSeconds float64) {
	controller.tryFocus(
		ctx,
		focusReasonMainThreadStall,
		fmt.Errorf("unity editor main thread busy for %.0fs", stallSeconds),
	)
}

func sendWithTransientConnectionRetry(
	ctx context.Context,
	connection unityipc.Connection,
	method string,
	params map[string]any,
	progress unityipc.ProgressFunc,
) (unityipc.UnitySendOutcome, error) {
	return sendWithTransientConnectionRetryWithDeps(
		ctx,
		connection,
		method,
		params,
		progress,
		0,
		defaultConnectionRetryDeps(),
	)
}

func sendWithTransientConnectionRetryAndResponseTimeout(
	ctx context.Context,
	connection unityipc.Connection,
	method string,
	params map[string]any,
	progress unityipc.ProgressFunc,
	responseTimeout time.Duration,
) (unityipc.UnitySendOutcome, error) {
	return sendWithTransientConnectionRetryWithDeps(
		ctx,
		connection,
		method,
		params,
		progress,
		responseTimeout,
		defaultConnectionRetryDeps(),
	)
}

// sendWithTransientConnectionRetryWithDeps sends one tool request, retrying the failures that mean
// it never ran, and names the dialog when one kept it from running.
// Why the dialog is looked up here: every tool path (plain tools, compile, execute-dynamic-code,
// control-play-mode, run-tests, hot-reload) sends through this function but writes its own failure,
// so this is the one place that covers them all.
func sendWithTransientConnectionRetryWithDeps(
	ctx context.Context,
	connection unityipc.Connection,
	method string,
	params map[string]any,
	progress unityipc.ProgressFunc,
	responseTimeout time.Duration,
	deps connectionRetryDeps,
) (unityipc.UnitySendOutcome, error) {
	outcome, err := sendWithTransientConnectionRetryAttempts(ctx, connection, method, params, progress, responseTimeout, deps)
	return outcome, explainBlockedByDialog(ctx, connection, method, err, deps.dialogWatch)
}

func sendWithTransientConnectionRetryAttempts(
	ctx context.Context,
	connection unityipc.Connection,
	method string,
	params map[string]any,
	progress unityipc.ProgressFunc,
	responseTimeout time.Duration,
	deps connectionRetryDeps,
) (unityipc.UnitySendOutcome, error) {
	startedAt := time.Now()
	retryContext, cancel := context.WithTimeout(ctx, unityAliveRetryWindow(deps))
	defer cancel()

	var lastOutcome unityipc.UnitySendOutcome
	var lastErr error
	focusController := newConnectionRetryFocusController(connection, method, deps)
	defer func() {
		restoreContext, cancel := context.WithTimeout(context.Background(), focusRestoreTimeout)
		defer cancel()
		focusController.restore(restoreContext)
	}()

	dialogWatch := newHeartbeatDialogWatch(ctx, connection, method, deps.dialogWatch)
	defer dialogWatch.release()
	// serverWaitDialogWatch looks for a dialog while the server stays unreachable with Unity running.
	// Why: a dialog Unity shows while it starts keeps its server from starting, so the wait below
	// would last its whole window and end in a "wait and retry" error that never names the dialog.
	serverWaitDialogWatch := newDialogBlockWatch(connection, deps.dialogWatch, dialogWatchInterval, dialogWatchStallThresholdSeconds)

	retryTicker := time.NewTicker(deps.retryPoll)
	defer retryTicker.Stop()
	for {
		client := newConnectionRetryClient(connection, method, responseTimeout, func(stallSeconds float64) {
			focusController.handleMainThreadStall(ctx, stallSeconds)
			dialogWatch.observeStall(stallSeconds)
		})
		// Each attempt keeps the base accept bound: a dispatched request that never gets
		// acked must still fail at the base window. Only the dial-retry loop as a whole
		// uses the extended unity-alive window.
		attemptContext, cancelAttempt := context.WithTimeout(retryContext, deps.retryTimeout)
		outcome, err := client.SendWithProgressOutcomeAcceptContext(dialogWatch.sendContext(ctx), attemptContext, method, params, progress)
		cancelAttempt()
		// Why Unity's answer first, a result or an error: it can arrive while the watch looks, and a
		// command that finished must report how it ended rather than a dialog that no longer stops it.
		if blockedErr := dialogWatch.blockedError(); blockedErr != nil && err != nil && !isUnityAnswerError(err) {
			return outcome, blockedErr
		}
		if isUnityServerBusyRPCError(err) {
			if deps.returnBusyWithoutRetry {
				return outcome, err
			}
			// Busy means the request was never executed, so a bounded retry is safe and
			// usually absorbs back-to-back tool calls without bothering the caller.
			lastOutcome = outcome
			lastErr = err
			if finished, finalOutcome, finalErr := finishBusyRetry(
				ctx,
				retryContext,
				startedAt,
				retryTicker,
				lastOutcome,
				lastErr,
				deps,
			); finished {
				return finalOutcome, finalErr
			}
			continue
		}
		if !shouldRetryUndispatchedConnection(err, outcome) {
			finalOutcome, finalErr := finishNonRetryableConnectionAttempt(
				ctx,
				connection,
				sendAttempt{
					outcome: outcome,
					err:     err,
				},
				sendAttempt{
					outcome: lastOutcome,
					err:     lastErr,
				},
				responseTimeout,
				focusController,
			)
			return finalOutcome, reportUnityExitDuringRequest(ctx, connection, finalOutcome, finalErr, deps)
		}

		// A caller that cancelled the command gets that cancellation back, as everywhere else that
		// waits on Unity. Why here: the probe below inherits the cancellation and fails, and its
		// failure would otherwise be reported as an unreachable Unity — telling the user to launch
		// an editor they never asked about, and recording a probe warning for their own Ctrl-C.
		if ctx.Err() != nil {
			return outcome, ctx.Err()
		}
		runningProcess, processErr := deps.findRunningUnityProcess(retryContext, connection.ProjectRoot)
		if finished, finalOutcome, finalErr := finishUndispatchedRetryProbe(
			retryContext,
			connection,
			sendAttempt{
				outcome: outcome,
				err:     err,
			},
			processErr,
			runningProcess,
			sendAttempt{
				outcome: lastOutcome,
				err:     lastErr,
			},
		); finished {
			return finalOutcome, finalErr
		}
		focusController.tryFocusProcess(
			retryContext,
			runningProcess.Pid,
			focusReasonUndispatchedConnectionFailure,
			err,
		)
		// Safe to report as not run: no attempt reached Unity. Why in step with the dial loop: the
		// look takes about a second once per interval, and a server that came up meanwhile gets the
		// command on the next attempt.
		if dialogs := serverWaitDialogWatch.observePoll(retryContext, false); dialogs != nil {
			return outcome, notSentBlockedByDialog(method, dialogs, deps.dialogWatch.canPressDialogs)
		}

		lastOutcome = outcome
		lastErr = err
		if finished, finalOutcome, finalErr := finishUnityAliveRetryWait(
			ctx,
			retryContext,
			startedAt,
			retryTicker,
			connection,
			lastOutcome,
			lastErr,
			deps,
		); finished {
			return finalOutcome, finalErr
		}
	}
}

// Reports whether the error is a real RPC answer from Unity rather than a
// transport-level failure such as a dial or read timeout.
func isRPCError(err error) bool {
	var rpcErr *unityipc.RPCError
	return errors.As(err, &rpcErr)
}

func isUnityServerBusyRPCError(err error) bool {
	var rpcErr *unityipc.RPCError
	if !errors.As(err, &rpcErr) {
		return false
	}
	return clierrors.RPCDataType(rpcErr.Data) == "server_busy"
}

// isMainThreadBlockedRPCError reports whether Unity gave up waiting for its blocked main thread
// before the tool's code ran.
func isMainThreadBlockedRPCError(err error) bool {
	var rpcErr *unityipc.RPCError
	if !errors.As(err, &rpcErr) {
		return false
	}
	return clierrors.RPCDataType(rpcErr.Data) == "main_thread_blocked"
}

func connectionRetryFocusReasonForError(
	err error,
	outcome unityipc.UnitySendOutcome,
	responseTimeout time.Duration,
) (connectionRetryFocusReason, bool) {
	if err == nil || isUnityServerBusyRPCError(err) || isRPCError(err) {
		return "", false
	}

	var unresponsiveErr *unityipc.EditorUnresponsiveError
	if errors.As(err, &unresponsiveErr) {
		return focusReasonMainThreadStall, true
	}

	if outcome.RequestDispatched && !outcome.RequestAccepted && clierrors.IsFinalResponseTimeoutError(err) {
		return focusReasonPreAcceptTimeout, true
	}

	if !outcome.RequestAccepted {
		return "", false
	}
	if responseTimeout > 0 {
		return "", false
	}
	if !clierrors.IsFinalResponseTimeoutError(err) {
		return "", false
	}
	if strings.Contains(err.Error(), "heartbeat") {
		return focusReasonHeartbeatSilenceTimeout, true
	}
	return focusReasonFinalResponseTimeout, true
}

func shouldRetryUndispatchedConnection(err error, outcome unityipc.UnitySendOutcome) bool {
	if err == nil || outcome.RequestDispatched {
		return false
	}

	var connectionErr *unityipc.ConnectionAttemptError
	if !errors.As(err, &connectionErr) {
		return false
	}
	// The retry window exists for a server that is not listening yet. A connect the kernel
	// refused permanently never becomes reachable inside it, and retrying it replaces the
	// syscall error with the window's own deadline expiry.
	return !clierrors.IsPermanentConnectError(connectionErr)
}
