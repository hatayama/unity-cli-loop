//go:build !windows

package unity

import (
	"context"
	"net"

	"github.com/hatayama/unity-cli-loop/Packages/src/GoCli/internal/project"
)

func dialEndpoint(ctx context.Context, endpoint project.Endpoint) (net.Conn, error) {
	dialer := net.Dialer{}
	return dialer.DialContext(ctx, endpoint.Network, endpoint.Address)
}
