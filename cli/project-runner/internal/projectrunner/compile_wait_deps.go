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
