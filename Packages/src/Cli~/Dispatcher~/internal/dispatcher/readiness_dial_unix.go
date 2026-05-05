//go:build !windows

package dispatcher

import (
	"context"
	"net"

	"github.com/hatayama/unity-cli-loop/Packages/src/Cli/Shared/domain"
)

func dialLaunchReadyEndpoint(ctx context.Context, endpoint domain.Endpoint) (net.Conn, error) {
	dialer := net.Dialer{}
	return dialer.DialContext(ctx, endpoint.Network, endpoint.Address)
}
