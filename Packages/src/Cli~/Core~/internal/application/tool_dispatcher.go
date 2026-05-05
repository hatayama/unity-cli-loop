package application

import (
	"context"

	"github.com/hatayama/unity-cli-loop/Packages/src/Cli/Core/internal/ports"
	"github.com/hatayama/unity-cli-loop/Packages/src/Cli/Shared/domain"
)

type ToolDispatchRequest struct {
	Command  string
	Params   map[string]any
	Progress func(string)
}

type ToolDispatcher struct {
	Bridge ports.UnityBridge
}

func (dispatcher ToolDispatcher) Dispatch(ctx context.Context, request ToolDispatchRequest) (domain.UnitySendOutcome, error) {
	return dispatcher.Bridge.SendWithProgressOutcome(ctx, request.Command, request.Params, request.Progress)
}
