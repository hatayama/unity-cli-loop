//go:build !windows

package unityipc

import (
	"context"
	"net"
)

func platformDialEndpoint(ctx context.Context, endpoint Endpoint) (net.Conn, error) {
	dialer := net.Dialer{}
	return dialer.DialContext(ctx, endpoint.Network, endpoint.Address)
}
