package cli

import (
	"context"
	"errors"
	"fmt"
	"time"

	"github.com/hatayama/unity-cli-loop/Packages/src/Cli/internal/project"
	"github.com/hatayama/unity-cli-loop/Packages/src/Cli/internal/unityipc"
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
		outcome, err := unityipc.NewClient(connection, version).
			SendWithProgressOutcomeAcceptContext(ctx, retryContext, method, params, progress)
		if !shouldRetryUndispatchedConnection(err, outcome) {
			return outcome, err
		}

		runningProcess, processErr := findRunningUnityProcessForConnectionRetry(retryContext, connection.ProjectRoot)
		if processErr != nil {
			if retryContext.Err() != nil {
				if ctx.Err() != nil {
					return outcome, ctx.Err()
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
			return outcome, err
		}
		if !focusAttempted {
			restorer, focusErr := focusUnityProcessForConnectionRetry(retryContext, runningProcess.pid)
			if focusErr == nil {
				restoreFocus = restorer
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

func shouldRetryUndispatchedConnection(err error, outcome unityipc.UnitySendOutcome) bool {
	if err == nil || outcome.RequestDispatched {
		return false
	}

	var connectionErr *unityipc.ConnectionAttemptError
	return errors.As(err, &connectionErr)
}

func resolveProjectEndpointAddress(projectRoot string) string {
	connection, err := project.ResolveConnection(projectRoot, projectRoot)
	if err != nil {
		return ""
	}
	return connection.Endpoint.Address
}
