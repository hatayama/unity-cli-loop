package projectrunner

import (
	clierrors "github.com/hatayama/unity-cli-loop/common/errors"

	"github.com/hatayama/unity-cli-loop/common/clicore"
)

const pausePointSettingsToolName = "pause-point"

func settingsToolNameForNativeCommand(command string) (string, bool) {
	switch command {
	case clicore.PausePointAwaitCommandName, clicore.PausePointStatusUserCommandName:
		return pausePointSettingsToolName, true
	default:
		return "", false
	}
}

func nativeToolDisabledError(projectRoot string, command string) clierrors.CLIError {
	return clierrors.CLIError{
		ErrorCode:   clierrors.ErrorCodeToolDisabled,
		Phase:       clierrors.ErrorPhaseDispatch,
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
