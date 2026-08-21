package projectrunner

import (
	"context"
	"errors"
	"time"
)

var errCompileStartStallNoActivity = errors.New("compile status has not shown activity")

func compileActivityHasStarted(status compileStatusResponse) bool {
	return status.IsCompiling || status.IsUpdating || status.IsDomainReloadInProgress || status.HasResult
}

func noteCompileActivityStarted(status compileStatusResponse, err error, activityObserved bool) bool {
	if err != nil || activityObserved {
		return activityObserved
	}
	return compileActivityHasStarted(status)
}

func maybeAttemptCompileStartStallFocus(
	ctx context.Context,
	startedAt time.Time,
	activityObserved bool,
	lastErr error,
	controller *connectionRetryFocusController,
	deps compileWaitDeps,
) {
	// Why: domain-reload silence after compile has started is normal. Focusing then
	// would steal window order from the user for a healthy wait.
	if activityObserved {
		return
	}
	if time.Since(startedAt) < compileStartStallFocusThresholdFor(deps) {
		return
	}
	cause := lastErr
	if cause == nil {
		cause = errCompileStartStallNoActivity
	}
	controller.tryFocus(ctx, focusReasonCompileStartStall, cause)
}
