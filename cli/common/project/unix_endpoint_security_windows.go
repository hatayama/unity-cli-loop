//go:build windows

package project

import "github.com/hatayama/unity-cli-loop/common/unityipc"

type platformEndpointDirectoryValidator struct{}

func (platformEndpointDirectoryValidator) Validate(endpoint unityipc.Endpoint) error {
	return nil
}
