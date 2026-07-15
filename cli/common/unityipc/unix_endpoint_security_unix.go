//go:build !windows

package unityipc

import (
	"github.com/hatayama/unity-cli-loop/common/ipcendpoint"
)

func validateEndpointSecurity(endpoint Endpoint) error {
	return ipcendpoint.Validate(endpoint.Network, endpoint.Address)
}
