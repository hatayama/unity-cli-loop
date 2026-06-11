package cli

func compileWaitTimeoutError(projectRoot string) cliError {
	return cliError{
		ErrorCode:   errorCodeCompileWaitTimeout,
		Phase:       errorPhaseCompileWaiting,
		Message:     "Compile status wait timed out after 180000ms. This does not mean the Unity Editor is frozen; the compile may simply still be running.",
		Retryable:   true,
		SafeToRetry: true,
		ProjectRoot: projectRoot,
		Command:     compileCommandName,
		// Agents have terminated whole sessions after misreading this timeout as a
		// frozen Editor, so the guidance must walk them through a responsiveness check.
		NextActions: []string{
			"Run a light command such as `uloop get-logs --max-count 1` to check whether Unity is responsive before treating this as a freeze.",
			"If Unity responds, retry `uloop compile`; the previous compile likely finished in the meantime.",
			"Only if Unity does not respond to any command, restart it with `uloop launch -r`.",
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
