package projectrunner

import (
	"context"
	"time"

	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

type compileWaitDeps struct {
	queryCompileStatus     func(context.Context, unityipc.Connection, string) (compileStatusResponse, error)
	attachProbeTimeout     time.Duration
	attachProbeInterval    time.Duration
	attachWaitPollInterval time.Duration
}

func defaultCompileWaitDeps() compileWaitDeps {
	return compileWaitDeps{
		queryCompileStatus:     queryCompileStatusFromUnity,
		attachProbeTimeout:     compileAttachProbeTimeout,
		attachProbeInterval:    compileAttachProbeInterval,
		attachWaitPollInterval: compileWaitPollInterval,
	}
}
