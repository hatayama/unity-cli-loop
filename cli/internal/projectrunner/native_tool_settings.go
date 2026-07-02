package projectrunner

import "github.com/hatayama/unity-cli-loop/cli/internal/clicore"

func isSettingsManagedNativeToolCommand(command string) bool {
	switch command {
	case clicore.PausePointWaitCommandName, clicore.PausePointStatusUserCommandName:
		return true
	default:
		return false
	}
}

func nativeToolDisabledError(projectRoot string, command string) clicore.CLIError {
	return clicore.CLIError{
		ErrorCode:   clicore.ErrorCodeToolDisabled,
		Phase:       clicore.ErrorPhaseDispatch,
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
