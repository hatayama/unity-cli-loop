//go:build windows

package unityipc

import (
	"context"
	"net"

	"github.com/Microsoft/go-winio"
)

func dialEndpoint(ctx context.Context, endpoint Endpoint) (net.Conn, error) {
	return winio.DialPipeContext(ctx, endpoint.Address)
}
