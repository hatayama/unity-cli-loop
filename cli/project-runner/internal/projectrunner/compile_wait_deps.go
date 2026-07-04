package projectrunner

import (
	"context"

	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

type compileWaitDeps struct {
	queryCompileStatus func(context.Context, unityipc.Connection, string) (compileStatusResponse, error)
}

func defaultCompileWaitDeps() compileWaitDeps {
	return compileWaitDeps{
		queryCompileStatus: queryCompileStatusFromUnity,
	}
}
