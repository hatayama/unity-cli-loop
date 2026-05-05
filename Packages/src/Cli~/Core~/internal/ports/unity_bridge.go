package ports

import (
	"context"

	"github.com/hatayama/unity-cli-loop/Packages/src/Cli/Shared/domain"
)

type UnityBridge interface {
	SendWithProgressOutcome(
		ctx context.Context,
		method string,
		params map[string]any,
		progress func(string),
	) (domain.UnitySendOutcome, error)
}
