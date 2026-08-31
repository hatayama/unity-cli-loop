package projectrunner

import (
	"context"
	"time"

	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

type compileSendFunc func(
	ctx context.Context,
	connection unityipc.Connection,
	method string,
	params map[string]any,
	progress unityipc.ProgressFunc,
	responseTimeout time.Duration,
) (unityipc.UnitySendOutcome, error)

type compileWaitDeps struct {
	queryCompileStatus     func(context.Context, unityipc.Connection, string) (compileStatusResponse, error)
	sendCompile            compileSendFunc
	attachProbeTimeout     time.Duration
	attachProbeInterval    time.Duration
	attachWaitPollInterval time.Duration
	now                    func() time.Time
	interimReportInterval  time.Duration
	reportInterim          compileWaitInterimReporter
	// Zero keeps compileStartStallFocusThreshold. Tests shorten it so they do not wait 10s.
	startStallFocusThreshold time.Duration
	focus                    connectionRetryDeps
}

func compileStartStallFocusThresholdFor(deps compileWaitDeps) time.Duration {
	if deps.startStallFocusThreshold > 0 {
		return deps.startStallFocusThreshold
	}
	return compileStartStallFocusThreshold
}

func compileWaitFocusDeps(deps compileWaitDeps) connectionRetryDeps {
	merged := defaultConnectionRetryDeps()
	if deps.focus.findRunningUnityProcess != nil {
		merged.findRunningUnityProcess = deps.focus.findRunningUnityProcess
	}
	if deps.focus.focusUnityProcess != nil {
		merged.focusUnityProcess = deps.focus.focusUnityProcess
	}
	return merged
}

func compileSendOrDefault(deps compileWaitDeps) compileSendFunc {
	if deps.sendCompile != nil {
		return deps.sendCompile
	}
	return sendWithTransientConnectionRetryAndResponseTimeout
}

func defaultCompileWaitDeps() compileWaitDeps {
	return compileWaitDeps{
		queryCompileStatus:     queryCompileStatusFromUnity,
		attachProbeTimeout:     compileAttachProbeTimeout,
		attachProbeInterval:    compileAttachProbeInterval,
		attachWaitPollInterval: compileWaitPollInterval,
	}
}
