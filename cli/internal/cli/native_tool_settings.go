package cli

func isSettingsManagedNativeToolCommand(command string) bool {
	switch command {
	case pausePointWaitCommandName, pausePointStatusUserCommandName:
		return true
	default:
		return false
	}
}

func nativeToolDisabledError(projectRoot string, command string) cliError {
	return cliError{
		ErrorCode:   errorCodeToolDisabled,
		Phase:       errorPhaseDispatch,
		Message:     "Tool is disabled in Unity CLI Loop settings: " + command,
		Retryable:   false,
		SafeToRetry: false,
		ProjectRoot: projectRoot,
		Command:     command,
		NextActions: []string{
			"Enable the tool in Unity CLI Loop settings before running this command.",
		},
	}
}
