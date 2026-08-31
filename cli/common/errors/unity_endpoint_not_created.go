package clierrors

import "github.com/hatayama/unity-cli-loop/common/ipcendpoint"

type UnityEndpointNotCreatedError = ipcendpoint.UnityEndpointNotCreatedError

func unityEndpointNotCreatedCLIError(err UnityEndpointNotCreatedError, context ErrorContext) CLIError {
	return CLIError{
		ErrorCode:   ErrorCodeUnityNotReachable,
		Phase:       ErrorPhaseConnection,
		Message:     "The Unity CLI Loop server is not reachable for this project.",
		Retryable:   true,
		SafeToRetry: true,
		ProjectRoot: context.ProjectRoot,
		Command:     context.Command,
		NextActions: []string{
			"If Unity is closed, run `uloop launch`.",
			"If Unity is starting, compiling, or reloading scripts, wait and retry.",
			"Confirm that the command targets the intended Unity project.",
		},
		Details: map[string]any{
			"EndpointDirectory": err.EndpointDirectory,
		},
	}
}
