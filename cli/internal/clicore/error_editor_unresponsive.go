package clicore

import "github.com/hatayama/unity-cli-loop/cli/internal/unityipc"

func connectionAttemptCause(err *unityipc.ConnectionAttemptError) string {
	if err == nil {
		return ""
	}
	cause := err.Unwrap()
	if cause == nil {
		return ""
	}
	return cause.Error()
}

func unityEditorUnresponsiveError(err *unityipc.EditorUnresponsiveError, context ErrorContext) CLIError {
	return CLIError{
		ErrorCode:   errorCodeUnityEditorUnresponsive,
		Phase:       ErrorPhaseResponseWaiting,
		Message:     "Unity accepted the request, but the Editor main thread stopped responding.",
		Retryable:   true,
		SafeToRetry: isSafeRetryCommand(context.Command),
		ProjectRoot: context.ProjectRoot,
		Command:     context.Command,
		NextActions: []string{
			"Check Unity for a modal dialog or long editor operation that is blocking the Editor main thread.",
			"Run `uloop focus-window` if Unity is hidden behind another window.",
			"Close the modal dialog or wait for the Editor operation to finish, then retry the command.",
		},
		Details: map[string]any{
			"StallSeconds": err.StallSeconds,
			"Cause":        "Unity Editor main thread did not tick while the IPC heartbeat stayed alive.",
		},
	}
}
