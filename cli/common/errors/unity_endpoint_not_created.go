package clierrors

import "fmt"

// UnityEndpointNotCreatedError reports that Unity has not created its private IPC directory yet.
type UnityEndpointNotCreatedError struct {
	EndpointDirectory string
}

func (err UnityEndpointNotCreatedError) Error() string {
	return fmt.Sprintf("Unity IPC endpoint directory has not been created: %s", err.EndpointDirectory)
}

func (err UnityEndpointNotCreatedError) ToCLIError(context ErrorContext) CLIError {
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
