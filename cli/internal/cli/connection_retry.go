package cli

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
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

type unityServerNotRespondingError struct {
	projectRoot string
	endpoint    string
	cause       error
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
	retryContext, cancel := context.WithTimeout(ctx, serverConnectionRetryTimeout)
	defer cancel()

	var lastOutcome unityipc.UnitySendOutcome
	var lastErr error
	var restoreFocus restoreFocusFunc
	defer func() {
		if restoreFocus == nil {
			return
		}
		restoreContext, cancel := context.WithTimeout(context.Background(), focusRestoreTimeout)
		defer cancel()
		_ = restoreFocus(restoreContext)
	}()

	focusAttempted := false
	for {
		client := unityipc.NewClient(connection, version)
		if responseTimeout > 0 {
			client = client.WithResponseTimeout(responseTimeout)
		}
		outcome, err := client.SendWithProgressOutcomeAcceptContext(ctx, retryContext, method, params, progress)
		if isUnityServerBusyRPCError(err) {
			// Busy means the request was never executed, so a bounded retry is safe and
			// usually absorbs back-to-back tool calls without bothering the caller.
			lastOutcome = outcome
			lastErr = err
			select {
			case <-retryContext.Done():
				if ctx.Err() != nil {
					return lastOutcome, ctx.Err()
				}
				return lastOutcome, lastErr
			case <-time.After(serverConnectionRetryPoll):
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
		if !focusAttempted {
			correlationID := newCliVibeCorrelationID()
			logConnectionRetryFocusAttempt(connection, method, runningProcess.pid, err, correlationID)
			restorer, focusErr := focusUnityProcessForConnectionRetry(retryContext, runningProcess.pid)
			if focusErr == nil {
				restoreFocus = restorer
				logConnectionRetryFocusSuccess(connection, method, runningProcess.pid, err, correlationID)
			} else {
				logConnectionRetryFocusFailure(connection, method, runningProcess.pid, err, focusErr, correlationID)
			}
			focusAttempted = true
		}

		lastOutcome = outcome
		lastErr = err
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
		case <-time.After(serverConnectionRetryPoll):
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
	retryCause error,
	correlationID string,
) {
	_ = writeCliVibeLog(connection.ProjectRoot, cliVibeLogEntry{
		Level:     "INFO",
		Operation: "cli_connection_retry_focus_attempt",
		Message:   "Attempting to focus Unity before retrying an undispatched request.",
		Context: map[string]any{
			"command":  method,
			"pid":      pid,
			"endpoint": connection.Endpoint.Address,
			"cause":    errorMessage(retryCause),
		},
		CorrelationID: correlationID,
	})
}

func logConnectionRetryFocusSuccess(
	connection unityipc.Connection,
	method string,
	pid int,
	retryCause error,
	correlationID string,
) {
	_ = writeCliVibeLog(connection.ProjectRoot, cliVibeLogEntry{
		Level:     "INFO",
		Operation: "cli_connection_retry_focus_success",
		Message:   "Focused Unity before retrying an undispatched request.",
		Context: map[string]any{
			"command":  method,
			"pid":      pid,
			"endpoint": connection.Endpoint.Address,
			"cause":    errorMessage(retryCause),
		},
		CorrelationID: correlationID,
	})
}

func logConnectionRetryFocusFailure(
	connection unityipc.Connection,
	method string,
	pid int,
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
