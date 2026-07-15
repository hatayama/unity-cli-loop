package unityipc

import (
	"context"
	"net"
)

type (
	endpointDialer    func(context.Context, Endpoint) (net.Conn, error)
	endpointValidator func(Endpoint) error
)

// dialEndpoint is the only client transport boundary. Why: project resolution is also
// used by commands that never connect, so security validation belongs immediately before dial.
func dialEndpoint(ctx context.Context, endpoint Endpoint) (net.Conn, error) {
	return dialEndpointWithValidatorAndDialer(ctx, endpoint, validateEndpointSecurity, platformDialEndpoint)
}

func dialEndpointWithValidatorAndDialer(ctx context.Context, endpoint Endpoint, validator endpointValidator, dialer endpointDialer) (net.Conn, error) {
	if err := validator(endpoint); err != nil {
		return nil, err
	}
	return dialer(ctx, endpoint)
}
