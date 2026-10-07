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
	// Zero keeps compileWaitPollInterval for a fresh compile's status wait. Tests shorten it so
	// they do not wait 1s between status queries.
	freshWaitPollInterval time.Duration
}

func freshWaitPollIntervalFor(deps compileWaitDeps) time.Duration {
	if deps.freshWaitPollInterval > 0 {
		return deps.freshWaitPollInterval
	}
	return compileWaitPollInterval
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
