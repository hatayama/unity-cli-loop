package projectrunner

import (
	"context"
	"errors"
	"time"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

// heartbeatDialogStallThresholdSeconds is how long the main thread must have stalled before the
// heartbeat wait looks for a dialog. Why above 30: Unity answers a request that has not started with
// main_thread_blocked once the stall reaches 30 seconds (MainThreadEntryWatchdog.BlockedStallSeconds
// in the Unity package, which this value pairs with), so a request still waiting past that has
// started, and stopping it then cannot be mistaken for one that never ran.
const heartbeatDialogStallThresholdSeconds = 35

// internalBridgeMethods are the requests Unity runs without a command slot, so its 30-second answer
// never comes for them and a long stall does not mean they started. Pairs with
// InternalBridgeCommandRouter.IsInternalCommand in the Unity package.
var internalBridgeMethods = map[string]bool{
	"get-version":                              true,
	compileStatusCommandName:                   true,
	runTestsStatusCommandName:                  true,
	pausePointStatusCommandName:                true,
	pausePointClearStatusCommandName:           true,
	pausePointExtendStatusCommandName:          true,
	setCodeOptimizationDebugCommandName:        true,
	setCodeOptimizationDebugStartupCommandName: true,
	"get-tool-details":                         true,
	"quit-editor":                              true,
}

type heartbeatDialogIntervalKey struct{}

// withHeartbeatDialogInterval sets how often the heartbeat wait of the commands sent with ctx may
// look for a dialog. Why a context value: the pause-point trigger sends an ordinary command through
// the whole command dispatch, and only its look must slow to the pause-point interval, because a
// pause point holds the main thread on purpose and most looks find nothing.
func withHeartbeatDialogInterval(ctx context.Context, interval time.Duration) context.Context {
	return context.WithValue(ctx, heartbeatDialogIntervalKey{}, interval)
}

func heartbeatDialogInterval(ctx context.Context) time.Duration {
	if interval, ok := ctx.Value(heartbeatDialogIntervalKey{}).(time.Duration); ok {
		return interval
	}
	return dialogWatchInterval
}

// heartbeatDialogWatch ends a sent request's wait for its response when the heartbeats show a long
// stall and a modal dialog is found: it cancels the send and reports the dialog instead.
type heartbeatDialogWatch struct {
	ctx        context.Context
	sendCtx    context.Context
	cancelSend context.CancelFunc
	watch      *dialogBlockWatch
	method     string
	deps       dialogWatchDeps
	dialogs    []statusDialog
}

// newHeartbeatDialogWatch returns nil for a method the watch does not cover, and every method on
// the nil watch then does nothing.
func newHeartbeatDialogWatch(
	ctx context.Context,
	connection unityipc.Connection,
	method string,
	deps dialogWatchDeps,
) *heartbeatDialogWatch {
	if deps.probeEditorStatus == nil || internalBridgeMethods[method] {
		return nil
	}
	sendCtx, cancelSend := context.WithCancel(ctx)
	return &heartbeatDialogWatch{
		ctx:        ctx,
		sendCtx:    sendCtx,
		cancelSend: cancelSend,
		watch:      newDialogBlockWatch(connection, deps, heartbeatDialogInterval(ctx), heartbeatDialogStallThresholdSeconds),
		method:     method,
		deps:       deps,
	}
}

// sendContext is the context to send with: cancelling it is how the watch ends the wait.
func (watch *heartbeatDialogWatch) sendContext(ctx context.Context) context.Context {
	if watch == nil {
		return ctx
	}
	return watch.sendCtx
}

// observeStall takes each stall the heartbeats report. Why the look runs here, inside the client's
// read loop: the client sees the cancelled send context when it reads the next heartbeat, so the wait
// ends within one heartbeat of the look instead of at the five-minute stall.
func (watch *heartbeatDialogWatch) observeStall(stallSeconds float64) {
	if watch == nil || watch.dialogs != nil {
		return
	}
	dialogs := watch.watch.observeStall(watch.ctx, stallSeconds)
	if dialogs == nil {
		return
	}
	watch.dialogs = dialogs
	watch.cancelSend()
}

// blockedError returns the dialog error when the watch ended the wait, and nil otherwise. Why the
// watch's own record and not the send's error: the cancelled send reports a context or connection
// error that must not reach the callers, which would read it as a lost connection.
func (watch *heartbeatDialogWatch) blockedError() error {
	if watch == nil || watch.dialogs == nil {
		return nil
	}
	safeToRetry := clierrors.ClassifyError(&unityipc.EditorUnresponsiveError{}, clierrors.ErrorContext{Command: watch.method}).SafeToRetry
	err := startedCommandBlockedByDialog(watch.method, watch.dialogs, watch.deps.canPressDialogs, safeToRetry)
	err.stopped = heartbeatStoppedSentence(watch.method)
	return err
}

// isUnityAnswerError reports whether err is Unity's own answer to the request rather than the send
// giving up on it.
func isUnityAnswerError(err error) bool {
	var rpcErr *unityipc.RPCError
	return errors.As(err, &rpcErr)
}

func (watch *heartbeatDialogWatch) release() {
	if watch != nil {
		watch.cancelSend()
	}
}
