package ipcendpoint

import "fmt"

// UnityEndpointNotCreatedError reports that Unity has not created its private IPC directory yet.
type UnityEndpointNotCreatedError struct {
	EndpointDirectory string
}

func (err UnityEndpointNotCreatedError) Error() string {
	return fmt.Sprintf("Unity IPC endpoint directory has not been created: %s", err.EndpointDirectory)
}
