package projectrunner

import (
	"context"
	"time"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

func newConnectionRetryClient(
	connection unityipc.Connection,
	responseTimeout time.Duration,
	mainThreadStallHandler func(float64),
) *unityipc.Client {
	client := unityipc.NewClient(connection, clicore.Version())
	if responseTimeout > 0 {
		client = client.WithResponseTimeout(responseTimeout)
	}
	return client.WithMainThreadStallHandler(mainThreadStallHandler)
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
	outcome unityipc.UnitySendOutcome,
	err error,
	lastOutcome unityipc.UnitySendOutcome,
	lastErr error,
	responseTimeout time.Duration,
	focusController *connectionRetryFocusController,
) (unityipc.UnitySendOutcome, error) {
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

func finishUndispatchedRetryProbe(
	ctx context.Context,
	retryContext context.Context,
	connection unityipc.Connection,
	outcome unityipc.UnitySendOutcome,
	err error,
	processErr error,
	runningProcess *clicore.UnityProcess,
	lastOutcome unityipc.UnitySendOutcome,
	lastErr error,
) (bool, unityipc.UnitySendOutcome, error) {
	if processErr != nil {
		if retryContext.Err() == nil {
			return true, outcome, processErr
		}
		if ctx.Err() != nil {
			return true, outcome, ctx.Err()
		}
		// A busy response seen during the window is the truer diagnosis than a
		// final dial cut short by the expiring retry context.
		if isUnityServerBusyRPCError(lastErr) {
			return true, lastOutcome, lastErr
		}
		return true, outcome, newUnityServerNotRespondingError(connection, err)
	}
	if runningProcess != nil {
		return false, outcome, nil
	}
	// Same masking as the probe-error path: a busy response seen during the
	// window proves a server answered moments ago, so it is a truer diagnosis
	// than a final dial cut short by the expiring retry context.
	if retryContext.Err() != nil && isUnityServerBusyRPCError(lastErr) {
		return true, lastOutcome, lastErr
	}
	return true, outcome, err
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

func newUnityServerNotRespondingError(connection unityipc.Connection, cause error) clicore.UnityServerNotRespondingError {
	return clicore.UnityServerNotRespondingError{
		ProjectRoot: connection.ProjectRoot,
		Endpoint:    connection.Endpoint.Address,
		Cause:       cause,
	}
}
