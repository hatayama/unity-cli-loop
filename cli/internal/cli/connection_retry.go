package cli

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"strings"
	"time"

	"github.com/hatayama/unity-cli-loop/cli/internal/project"
	"github.com/hatayama/unity-cli-loop/cli/internal/unityipc"
)

var (
	findRunningUnityProcessForConnectionRetry = findRunningUnityProcess
	focusUnityProcessForConnectionRetry       = focusUnityProcessWithRestore
	serverConnectionRetryTimeout              = 10 * time.Second
	serverConnectionRetryPoll                 = 1 * time.Second
)

const focusRestoreTimeout = 2 * time.Second

type connectionRetryFocusReason string

const (
	focusReasonUndispatchedConnectionFailure connectionRetryFocusReason = "undispatched_connection_failure"
	focusReasonPreAcceptTimeout              connectionRetryFocusReason = "pre_accept_timeout"
	focusReasonMainThreadStall               connectionRetryFocusReason = "main_thread_stall"
	focusReasonHeartbeatSilenceTimeout       connectionRetryFocusReason = "heartbeat_silence_timeout"
	focusReasonFinalResponseTimeout          connectionRetryFocusReason = "final_response_timeout"
)

// Why: domain reload on large projects can keep the IPC endpoint down well past the
// base retry window, and an undispatched dial failure is always safe to retry. While
// a running Unity process is confirmed, the dial-retry window stretches to
// base * factor (60s by default) so an external reload does not fail commands
// spuriously. Busy responses stay bounded by the base window: busy proves the server
// is alive and surfacing it quickly lets the caller decide.
const serverConnectionRetryUnityAliveFactor = 6

func unityAliveRetryWindow() time.Duration {
	return serverConnectionRetryTimeout * serverConnectionRetryUnityAliveFactor
}

type unityServerNotRespondingError struct {
	projectRoot string
	endpoint    string
	cause       error
}

type connectionRetryFocusController struct {
	connection   unityipc.Connection
	method       string
	attempted    bool
	restoreFocus restoreFocusFunc
}

func (err unityServerNotRespondingError) Error() string {
	if err.cause != nil {
		return fmt.Sprintf("Unity is running but the Unity CLI Loop server is not responding: %s", err.cause)
	}
	return "Unity is running but the Unity CLI Loop server is not responding"
}

func (err unityServerNotRespondingError) Unwrap() error {
	return err.cause
}

func (err unityServerNotRespondingError) causeText() string {
	if err.cause == nil {
		return ""
	}
	return err.cause.Error()
}

func newConnectionRetryFocusController(connection unityipc.Connection, method string) *connectionRetryFocusController {
	return &connectionRetryFocusController{
		connection: connection,
		method:     method,
	}
}

func (controller *connectionRetryFocusController) restore(ctx context.Context) {
	if controller.restoreFocus == nil {
		return
	}
	_ = controller.restoreFocus(ctx)
}

func (controller *connectionRetryFocusController) keepUnityFocusedAfterReturn() {
	// Why: terminal timeout recovery has no in-flight request left, so immediate
	// restore hides the auto-front signal that tells the user Unity needs attention.
	controller.restoreFocus = nil
}

func (controller *connectionRetryFocusController) tryFocus(
	ctx context.Context,
	reason connectionRetryFocusReason,
	cause error,
) {
	if controller.attempted {
		return
	}

	runningProcess, err := findRunningUnityProcessForConnectionRetry(ctx, controller.connection.ProjectRoot)
	if err != nil || runningProcess == nil {
		controller.attempted = true
		return
	}
	controller.tryFocusProcess(ctx, runningProcess.pid, reason, cause)
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

	correlationID := newCliVibeCorrelationID()
	logConnectionRetryFocusAttempt(controller.connection, controller.method, pid, reason, cause, correlationID)
	restorer, focusErr := focusUnityProcessForConnectionRetry(ctx, pid)
	if focusErr == nil {
		controller.restoreFocus = restorer
		logConnectionRetryFocusSuccess(controller.connection, controller.method, pid, reason, cause, correlationID)
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
	return sendWithTransientConnectionRetryAndResponseTimeout(
		ctx,
		connection,
		method,
		params,
		progress,
		0,
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
	startedAt := time.Now()
	retryContext, cancel := context.WithTimeout(ctx, unityAliveRetryWindow())
	defer cancel()

	var lastOutcome unityipc.UnitySendOutcome
	var lastErr error
	focusController := newConnectionRetryFocusController(connection, method)
	defer func() {
		restoreContext, cancel := context.WithTimeout(context.Background(), focusRestoreTimeout)
		defer cancel()
		focusController.restore(restoreContext)
	}()

	retryTicker := time.NewTicker(serverConnectionRetryPoll)
	defer retryTicker.Stop()
	for {
		client := unityipc.NewClient(connection, version)
		if responseTimeout > 0 {
			client = client.WithResponseTimeout(responseTimeout)
		}
		client = client.WithMainThreadStallHandler(func(stallSeconds float64) {
			focusController.handleMainThreadStall(ctx, stallSeconds)
		})
		// Each attempt keeps the base accept bound: a dispatched request that never gets
		// acked must still fail at the base window. Only the dial-retry loop as a whole
		// uses the extended unity-alive window.
		attemptContext, cancelAttempt := context.WithTimeout(retryContext, serverConnectionRetryTimeout)
		outcome, err := client.SendWithProgressOutcomeAcceptContext(ctx, attemptContext, method, params, progress)
		cancelAttempt()
		if isUnityServerBusyRPCError(err) {
			// Busy means the request was never executed, so a bounded retry is safe and
			// usually absorbs back-to-back tool calls without bothering the caller.
			lastOutcome = outcome
			lastErr = err
			if time.Since(startedAt) >= serverConnectionRetryTimeout {
				if ctx.Err() != nil {
					return lastOutcome, ctx.Err()
				}
				return lastOutcome, lastErr
			}
			select {
			case <-retryContext.Done():
				if ctx.Err() != nil {
					return lastOutcome, ctx.Err()
				}
				return lastOutcome, lastErr
			case <-retryTicker.C:
			}
			continue
		}
		if !shouldRetryUndispatchedConnection(err, outcome) {
			// A transport error after a busy response in this window must not mask the
			// busy; the server answered moments ago, so busy is the truer diagnosis.
			// An RPC error is a real Unity answer, not a transport artifact, and must
			// surface as-is. The transport error is not compared against the window
			// deadline because the connection deadline can fire microseconds before
			// the context reports expiry.
			if err != nil && !isRPCError(err) && isUnityServerBusyRPCError(lastErr) {
				if ctx.Err() != nil {
					return outcome, ctx.Err()
				}
				return lastOutcome, lastErr
			}
			if reason, ok := connectionRetryFocusReasonForError(err, outcome, responseTimeout); ok {
				focusController.tryFocus(ctx, reason, err)
				focusController.keepUnityFocusedAfterReturn()
			}
			return outcome, err
		}

		runningProcess, processErr := findRunningUnityProcessForConnectionRetry(retryContext, connection.ProjectRoot)
		if processErr != nil {
			if retryContext.Err() != nil {
				if ctx.Err() != nil {
					return outcome, ctx.Err()
				}
				// A busy response seen during the window is the truer diagnosis than a
				// final dial cut short by the expiring retry context.
				if isUnityServerBusyRPCError(lastErr) {
					return lastOutcome, lastErr
				}
				return outcome, unityServerNotRespondingError{
					projectRoot: connection.ProjectRoot,
					endpoint:    connection.Endpoint.Address,
					cause:       err,
				}
			}
			return outcome, processErr
		}
		if runningProcess == nil {
			// Same masking as the probe-error path: a busy response seen during the
			// window proves a server answered moments ago, so it is a truer diagnosis
			// than a final dial cut short by the expiring retry context.
			if retryContext.Err() != nil && isUnityServerBusyRPCError(lastErr) {
				return lastOutcome, lastErr
			}
			return outcome, err
		}
		focusController.tryFocusProcess(
			retryContext,
			runningProcess.pid,
			focusReasonUndispatchedConnectionFailure,
			err,
		)

		lastOutcome = outcome
		lastErr = err
		if time.Since(startedAt) >= unityAliveRetryWindow() {
			if ctx.Err() != nil {
				return lastOutcome, ctx.Err()
			}
			return lastOutcome, unityServerNotRespondingError{
				projectRoot: connection.ProjectRoot,
				endpoint:    connection.Endpoint.Address,
				cause:       lastErr,
			}
		}
		select {
		case <-retryContext.Done():
			if ctx.Err() != nil {
				return lastOutcome, ctx.Err()
			}
			return lastOutcome, unityServerNotRespondingError{
				projectRoot: connection.ProjectRoot,
				endpoint:    connection.Endpoint.Address,
				cause:       lastErr,
			}
		case <-retryTicker.C:
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
	decodedData := map[string]any{}
	if len(rpcErr.Data) > 0 {
		_ = json.Unmarshal(rpcErr.Data, &decodedData)
	}
	return rpcDataType(decodedData) == "server_busy"
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

	if outcome.RequestDispatched && !outcome.RequestAccepted && isFinalResponseTimeoutError(err) {
		return focusReasonPreAcceptTimeout, true
	}

	if !outcome.RequestAccepted {
		return "", false
	}
	if responseTimeout > 0 {
		return "", false
	}
	if !isFinalResponseTimeoutError(err) {
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
	return errors.As(err, &connectionErr)
}

func logConnectionRetryFocusAttempt(
	connection unityipc.Connection,
	method string,
	pid int,
	reason connectionRetryFocusReason,
	retryCause error,
	correlationID string,
) {
	_ = writeCliVibeLog(connection.ProjectRoot, cliVibeLogEntry{
		Level:     "INFO",
		Operation: "cli_connection_retry_focus_attempt",
		Message:   "Attempting to focus Unity while recovering a slow or unreachable request.",
		Context: map[string]any{
			"command":  method,
			"pid":      pid,
			"endpoint": connection.Endpoint.Address,
			"reason":   string(reason),
			"cause":    errorMessage(retryCause),
		},
		CorrelationID: correlationID,
	})
}

func logConnectionRetryFocusSuccess(
	connection unityipc.Connection,
	method string,
	pid int,
	reason connectionRetryFocusReason,
	retryCause error,
	correlationID string,
) {
	_ = writeCliVibeLog(connection.ProjectRoot, cliVibeLogEntry{
		Level:     "INFO",
		Operation: "cli_connection_retry_focus_success",
		Message:   "Focused Unity while recovering a slow or unreachable request.",
		Context: map[string]any{
			"command":  method,
			"pid":      pid,
			"endpoint": connection.Endpoint.Address,
			"reason":   string(reason),
			"cause":    errorMessage(retryCause),
		},
		CorrelationID: correlationID,
	})
}

func logConnectionRetryFocusFailure(
	connection unityipc.Connection,
	method string,
	pid int,
	reason connectionRetryFocusReason,
	retryCause error,
	focusErr error,
	correlationID string,
) {
	_ = writeCliVibeLog(connection.ProjectRoot, cliVibeLogEntry{
		Level:     "WARNING",
		Operation: "cli_connection_retry_focus_failed",
		Message:   "Failed to focus Unity before retrying an undispatched request.",
		Context: map[string]any{
			"command":    method,
			"pid":        pid,
			"endpoint":   connection.Endpoint.Address,
			"reason":     string(reason),
			"cause":      errorMessage(retryCause),
			"focusError": errorMessage(focusErr),
		},
		CorrelationID: correlationID,
	})
}

func errorMessage(err error) string {
	if err == nil {
		return ""
	}
	return err.Error()
}

func resolveProjectEndpointAddress(projectRoot string) string {
	connection, err := project.ResolveConnection(projectRoot, projectRoot)
	if err != nil {
		return ""
	}
	return connection.Endpoint.Address
}
