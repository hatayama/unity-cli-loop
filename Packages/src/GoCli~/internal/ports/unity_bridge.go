package ports

import (
	"context"

	"github.com/hatayama/unity-cli-loop/Packages/src/GoCli/internal/domain"
)

type UnityBridge interface {
	SendWithProgressOutcome(
		ctx context.Context,
		method string,
		params map[string]any,
		progress func(string),
	) (domain.UnitySendOutcome, error)
}
