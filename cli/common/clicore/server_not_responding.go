package clicore

import "github.com/hatayama/unity-cli-loop/common/project"

func resolveProjectEndpointAddress(projectRoot string) string {
	connection, err := project.ResolveConnection(projectRoot, projectRoot)
	if err != nil {
		return ""
	}
	return connection.Endpoint.Address
}
