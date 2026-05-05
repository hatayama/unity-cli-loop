//go:build windows

package dispatcher

import (
	"context"
	"net"

	"github.com/Microsoft/go-winio"
	"github.com/hatayama/unity-cli-loop/Packages/src/Cli/Shared/domain"
)

func dialLaunchReadyEndpoint(ctx context.Context, endpoint domain.Endpoint) (net.Conn, error) {
	return winio.DialPipeContext(ctx, endpoint.Address)
}
