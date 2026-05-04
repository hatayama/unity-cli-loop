package application

import (
	"context"

	"github.com/hatayama/unity-cli-loop/Packages/src/GoCli/internal/domain"
	"github.com/hatayama/unity-cli-loop/Packages/src/GoCli/internal/ports"
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
