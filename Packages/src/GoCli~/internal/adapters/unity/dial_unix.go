//go:build !windows

package unity

import (
	"context"
	"net"

	"github.com/hatayama/unity-cli-loop/Packages/src/GoCli/internal/domain"
)

func dialEndpoint(ctx context.Context, endpoint domain.Endpoint) (net.Conn, error) {
	dialer := net.Dialer{}
	return dialer.DialContext(ctx, endpoint.Network, endpoint.Address)
}
