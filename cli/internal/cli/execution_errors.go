package cli

func compileWaitTimeoutError(projectRoot string) cliError {
	return cliError{
		ErrorCode:   errorCodeCompileWaitTimeout,
		Phase:       errorPhaseCompileWaiting,
		Message:     "Compile status wait timed out after 180000ms.",
		Retryable:   true,
		SafeToRetry: true,
		ProjectRoot: projectRoot,
		Command:     compileCommandName,
		NextActions: []string{
			"Retry `uloop compile` after Unity becomes responsive.",
		},
	}
}

func internalCLIError(message string, context errorContext) cliError {
	return cliError{
		ErrorCode:   errorCodeInternalError,
		Phase:       errorPhaseExecution,
		Message:     message,
		Retryable:   false,
		SafeToRetry: false,
		ProjectRoot: context.projectRoot,
		Command:     context.command,
		NextActions: []string{
			"Read the message and fix the local environment or command input before retrying.",
		},
	}
}
