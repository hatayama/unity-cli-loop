package clicore

import (
	"fmt"

	"github.com/hatayama/unity-cli-loop/common/project"
)

// UnityServerNotRespondingError reports that a running Unity Editor accepted a
// connection but its Unity CLI Loop server did not respond in time. Both the
// dispatcher's launch-readiness wait and the runner's connection retry loop
// construct this error, and the shared CORE classifiers turn it into a CLIError.
type UnityServerNotRespondingError struct {
	ProjectRoot string
	Endpoint    string
	Cause       error
}

func (err UnityServerNotRespondingError) Error() string {
	if err.Cause != nil {
		return fmt.Sprintf("Unity is running but the Unity CLI Loop server is not responding: %s", err.Cause)
	}
	return "Unity is running but the Unity CLI Loop server is not responding"
}

func (err UnityServerNotRespondingError) Unwrap() error {
	return err.Cause
}

func (err UnityServerNotRespondingError) causeText() string {
	if err.Cause == nil {
		return ""
	}
	return err.Cause.Error()
}

func resolveProjectEndpointAddress(projectRoot string) string {
	connection, err := project.ResolveConnection(projectRoot, projectRoot)
	if err != nil {
		return ""
	}
	return connection.Endpoint.Address
}
