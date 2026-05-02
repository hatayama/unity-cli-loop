//go:build windows

package unity

import (
	"context"
	"net"

	"github.com/Microsoft/go-winio"
	"github.com/hatayama/unity-cli-loop/Packages/src/GoCli/internal/project"
)

func dialEndpoint(ctx context.Context, endpoint project.Endpoint) (net.Conn, error) {
	return winio.DialPipeContext(ctx, endpoint.Address)
}
