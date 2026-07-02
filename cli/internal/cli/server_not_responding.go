package cli

import (
	"fmt"

	"github.com/hatayama/unity-cli-loop/cli/internal/project"
)

// unityServerNotRespondingError reports that a running Unity Editor accepted a
// connection but its Unity CLI Loop server did not respond in time. Both the
// dispatcher's launch-readiness wait and the runner's connection retry loop
// construct this error, and the shared CORE classifiers turn it into a cliError.
type unityServerNotRespondingError struct {
	projectRoot string
	endpoint    string
	cause       error
}

func (err unityServerNotRespondingError) Error() string {
	if err.cause != nil {
		return fmt.Sprintf("Unity is running but the Unity CLI Loop server is not responding: %s", err.cause)
	}
	return "Unity is running but the Unity CLI Loop server is not responding"
}

func (err unityServerNotRespondingError) Unwrap() error {
	return err.cause
}

func (err unityServerNotRespondingError) causeText() string {
	if err.cause == nil {
		return ""
	}
	return err.cause.Error()
}

func resolveProjectEndpointAddress(projectRoot string) string {
	connection, err := project.ResolveConnection(projectRoot, projectRoot)
	if err != nil {
		return ""
	}
	return connection.Endpoint.Address
}
