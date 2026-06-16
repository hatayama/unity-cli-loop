package cli

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

func unityEditorUnresponsiveError(err *unityipc.EditorUnresponsiveError, context errorContext) cliError {
	return cliError{
		ErrorCode:   errorCodeUnityEditorUnresponsive,
		Phase:       errorPhaseResponseWaiting,
		Message:     err.Error(),
		Retryable:   true,
		SafeToRetry: isSafeRetryCommand(context.command),
		ProjectRoot: context.projectRoot,
		Command:     context.command,
		NextActions: []string{
			"Check Unity for a modal dialog or long editor operation that is blocking the Editor main thread.",
			"Run `uloop focus-window` if Unity is hidden behind another window.",
			"Close the modal dialog or wait for the Editor operation to finish, then retry the command.",
		},
		Details: map[string]any{
			"stallSeconds": err.StallSeconds,
			"cause":        err.Error(),
		},
	}
}
