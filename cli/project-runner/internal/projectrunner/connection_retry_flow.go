package projectrunner

import (
	"context"
	"time"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"

	"github.com/hatayama/unity-cli-loop/common/clicontract"
	"github.com/hatayama/unity-cli-loop/common/clicore"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
	"github.com/hatayama/unity-cli-loop/common/unityprocess"
)

func newConnectionRetryClient(
	connection unityipc.Connection,
	method string,
	responseTimeout time.Duration,
	mainThreadStallHandler func(float64),
) *unityipc.Client {
	client := unityipc.NewClient(connection, clicontract.ProjectRunnerVersion())
	if responseTimeout > 0 {
		client = client.WithResponseTimeout(responseTimeout)
	}
	client = client.WithMainThreadStallHandler(mainThreadStallHandler)
	if commandNeedsSelfInducedStallTolerance(method) {
		client = client.WithSelfInducedMainThreadStallTolerance()
	}
	return client
}

// Why only execute-dynamic-code: a long synchronous snippet blocks Unity's main thread
// from pumping update ticks by design, which looks identical to a frozen editor on the
// stall counter alone. Other commands' stalls stay a genuine freeze signal.
func commandNeedsSelfInducedStallTolerance(method string) bool {
	return method == clicore.ExecuteDynamicCodeCommandName
}

func finishBusyRetry(
	ctx context.Context,
	retryContext context.Context,
	startedAt time.Time,
	retryTicker *time.Ticker,
	lastOutcome unityipc.UnitySendOutcome,
	lastErr error,
	deps connectionRetryDeps,
) (bool, unityipc.UnitySendOutcome, error) {
	if time.Since(startedAt) >= deps.retryTimeout {
		if ctx.Err() != nil {
			return true, lastOutcome, ctx.Err()
		}
		return true, lastOutcome, lastErr
	}
	select {
	case <-retryContext.Done():
		if ctx.Err() != nil {
			return true, lastOutcome, ctx.Err()
		}
		return true, lastOutcome, lastErr
	case <-retryTicker.C:
		return false, lastOutcome, nil
	}
}

func finishNonRetryableConnectionAttempt(
	ctx context.Context,
	currentAttempt sendAttempt,
	lastAttempt sendAttempt,
	responseTimeout time.Duration,
	focusController *connectionRetryFocusController,
) (unityipc.UnitySendOutcome, error) {
	// A transport error after a busy response in this window must not mask the
	// busy; the server answered moments ago, so busy is the truer diagnosis.
	// An RPC error is a real Unity answer, not a transport artifact, and must
	// surface as-is. The transport error is not compared against the window
	// deadline because the connection deadline can fire microseconds before
	// the context reports expiry.
	if currentAttempt.err != nil && !isRPCError(currentAttempt.err) && isUnityServerBusyRPCError(lastAttempt.err) {
		if ctx.Err() != nil {
			return currentAttempt.outcome, ctx.Err()
		}
		return lastAttempt.outcome, lastAttempt.err
	}
	if reason, ok := connectionRetryFocusReasonForError(currentAttempt.err, currentAttempt.outcome, responseTimeout); ok {
		focusController.tryFocus(ctx, reason, currentAttempt.err)
		focusController.keepUnityFocusedAfterReturn()
	}
	return currentAttempt.outcome, currentAttempt.err
}

type sendAttempt struct {
	outcome unityipc.UnitySendOutcome
	err     error
}

func finishUndispatchedRetryProbe(
	ctx context.Context,
	retryContext context.Context,
	connection unityipc.Connection,
	currentAttempt sendAttempt,
	processErr error,
	runningProcess *unityprocess.UnityProcess,
	lastAttempt sendAttempt,
) (bool, unityipc.UnitySendOutcome, error) {
	if processErr != nil {
		if retryContext.Err() == nil {
			return true, currentAttempt.outcome, processErr
		}
		if ctx.Err() != nil {
			return true, currentAttempt.outcome, ctx.Err()
		}
		// A busy response seen during the window is the truer diagnosis than a
		// final dial cut short by the expiring retry context.
		if isUnityServerBusyRPCError(lastAttempt.err) {
			return true, lastAttempt.outcome, lastAttempt.err
		}
		return true, currentAttempt.outcome, newUnityServerNotRespondingError(connection, currentAttempt.err)
	}
	if runningProcess != nil {
		return false, currentAttempt.outcome, nil
	}
	// Same masking as the probe-error path: a busy response seen during the
	// window proves a server answered moments ago, so it is a truer diagnosis
	// than a final dial cut short by the expiring retry context.
	if retryContext.Err() != nil && isUnityServerBusyRPCError(lastAttempt.err) {
		return true, lastAttempt.outcome, lastAttempt.err
	}
	return true, currentAttempt.outcome, currentAttempt.err
}

func finishUnityAliveRetryWait(
	ctx context.Context,
	retryContext context.Context,
	startedAt time.Time,
	retryTicker *time.Ticker,
	connection unityipc.Connection,
	lastOutcome unityipc.UnitySendOutcome,
	lastErr error,
	deps connectionRetryDeps,
) (bool, unityipc.UnitySendOutcome, error) {
	if time.Since(startedAt) >= unityAliveRetryWindow(deps) {
		finalOutcome, finalErr := finishUnityAliveRetry(ctx, connection, lastOutcome, lastErr)
		return true, finalOutcome, finalErr
	}
	select {
	case <-retryContext.Done():
		finalOutcome, finalErr := finishUnityAliveRetry(ctx, connection, lastOutcome, lastErr)
		return true, finalOutcome, finalErr
	case <-retryTicker.C:
		return false, lastOutcome, nil
	}
}

func finishUnityAliveRetry(
	ctx context.Context,
	connection unityipc.Connection,
	lastOutcome unityipc.UnitySendOutcome,
	lastErr error,
) (unityipc.UnitySendOutcome, error) {
	if ctx.Err() != nil {
		return lastOutcome, ctx.Err()
	}
	return lastOutcome, newUnityServerNotRespondingError(connection, lastErr)
}

func newUnityServerNotRespondingError(connection unityipc.Connection, cause error) clierrors.UnityServerNotRespondingError {
	return clierrors.UnityServerNotRespondingError{
		ProjectRoot: connection.ProjectRoot,
		Endpoint:    connection.Endpoint.Address,
		Cause:       cause,
	}
}
