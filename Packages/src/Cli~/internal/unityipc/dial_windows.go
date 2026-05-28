//go:build windows

package unityipc

import (
	"context"
	"net"

	"github.com/Microsoft/go-winio"
)

func dialEndpoint(ctx context.Context, endpoint Endpoint) (net.Conn, error) {
	if endpoint.Network != "" && endpoint.Network != "pipe" && endpoint.Network != "npipe" {
		dialer := net.Dialer{}
		return dialer.DialContext(ctx, endpoint.Network, endpoint.Address)
	}

	return winio.DialPipeContext(ctx, endpoint.Address)
}
