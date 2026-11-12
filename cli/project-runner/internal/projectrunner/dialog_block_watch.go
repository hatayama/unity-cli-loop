package projectrunner

import (
	"context"
	"time"

	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

const (
	// dialogWatchStallThresholdSeconds matches editorStatusMainThreadStallThresholdSeconds: uloop
	// status calls a main thread blocked from 5 seconds, so a wait looks for a dialog from then on.
	dialogWatchStallThresholdSeconds = 5
	// dialogWatchInterval keeps the look at Unity's windows, which starts osascript on macOS, to one
	// per 10 seconds however often the wait polls.
	dialogWatchInterval = 10 * time.Second
	// pausePointDialogWatchInterval is the longer interval of the waits that run while a pause point
	// may hold the main thread on purpose, where a look finds no dialog most of the time.
	pausePointDialogWatchInterval = 30 * time.Second
)

// dialogWatchDeps is how a wait reads the Editor for a dialog. A nil probe turns the watch off, so
// only the default dependencies of each wait look; tests that build their own leave it nil.
type dialogWatchDeps struct {
	// probeEditorStatus reads the Editor the way uloop status does: the Editor's own dialog report
	// on Unity 6000.7 and later, otherwise a look at Unity's windows when the main thread stalled.
	probeEditorStatus func(context.Context, unityipc.Connection) (statusReport, error)
	// canPressDialogs is whether uloop press-dialog-button works on this operating system.
	canPressDialogs bool
	now             func() time.Time
}

func defaultDialogWatchDeps() dialogWatchDeps {
	return dialogWatchDeps{
		probeEditorStatus: probeEditorStatusForDialogs,
		canPressDialogs:   defaultDialogInspector().CanPress(),
		now:               time.Now,
	}
}

// dialogBlockWatch answers, during one wait, whether a modal dialog is holding Unity's main thread.
// It looks only once the main thread has stalled for its threshold, and at most once per interval.
type dialogBlockWatch struct {
	connection            unityipc.Connection
	deps                  dialogWatchDeps
	interval              time.Duration
	stallThresholdSeconds float64
	probed                bool
	lastProbeAt           time.Time
	unanswered            bool
	unansweredSince       time.Time
}

func newDialogBlockWatch(
	connection unityipc.Connection,
	deps dialogWatchDeps,
	interval time.Duration,
	stallThresholdSeconds float64,
) *dialogBlockWatch {
	return &dialogBlockWatch{
		connection:            connection,
		deps:                  deps,
		interval:              interval,
		stallThresholdSeconds: stallThresholdSeconds,
	}
}

// observeStall returns the open dialogs when the main thread has stalled for stallSeconds and a
// look, allowed by the interval, finds one; nil otherwise. A failed look counts as no dialog.
func (watch *dialogBlockWatch) observeStall(ctx context.Context, stallSeconds float64) []statusDialog {
	if watch.deps.probeEditorStatus == nil || stallSeconds < watch.stallThresholdSeconds {
		return nil
	}
	now := watch.deps.now()
	if watch.probed && now.Sub(watch.lastProbeAt) < watch.interval {
		return nil
	}
	watch.probed = true
	watch.lastProbeAt = now
	return findBlockingDialogs(ctx, watch.connection, watch.deps)
}

// observePoll is observeStall for a wait that polls Unity without learning the stall: a poll that
// goes unanswered is what a blocked main thread looks like, so the time since polls stopped being
// answered stands in for the stall.
func (watch *dialogBlockWatch) observePoll(ctx context.Context, answered bool) []statusDialog {
	if watch.deps.probeEditorStatus == nil {
		return nil
	}
	now := watch.deps.now()
	if answered {
		watch.unanswered = false
		return nil
	}
	if !watch.unanswered {
		watch.unanswered = true
		watch.unansweredSince = now
	}
	return watch.observeStall(ctx, now.Sub(watch.unansweredSince).Seconds())
}

// observeEditorStatus is observeStall for a wait that already reads the Editor status: dialogs the
// Editor reported end the wait at once, and the reported stall decides whether to look further.
func (watch *dialogBlockWatch) observeEditorStatus(
	ctx context.Context,
	status editorStatusResponse,
	answered bool,
) []statusDialog {
	if !answered {
		return watch.observePoll(ctx, false)
	}
	watch.unanswered = false
	if len(status.OpenDialogs) > 0 {
		return status.OpenDialogs
	}
	return watch.observeStall(ctx, status.SecondsSinceLastMainThreadTick)
}
