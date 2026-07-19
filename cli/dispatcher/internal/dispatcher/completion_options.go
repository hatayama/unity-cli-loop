package dispatcher

import (
	"github.com/hatayama/unity-cli-loop/common/clicore"
	"github.com/hatayama/unity-cli-loop/common/tooldocs"
)

var nativeCommandOptions = map[string][]string{
	clicore.CompletionCommand: {installCompletionFlag, shellFlag},
	clicore.LaunchCommandName: {
		"--" + tooldocs.ProjectPathFlagName,
		"--delete-recovery",
		"--editor-version",
		"--max-depth",
		"--platform",
		"--quit",
		"--restart",
	},
	clicore.InstallCommandName: {"--" + installDirFlagName},
	clicore.UpdateCommandName:  {"--" + updateToVersionFlagName},
	clicore.PausePointAwaitCommandName: {
		"--" + clicore.PausePointIDFlagName,
		"--" + clicore.PausePointTimeoutFlagName,
		"--" + clicore.PausePointLogsMaxCountFlagName,
		"--" + clicore.PausePointCapturedVariablesFlagName,
		"--" + tooldocs.ProjectPathFlagName,
	},
	clicore.PausePointStatusUserCommandName: {
		"--" + clicore.PausePointIDFlagName,
		"--" + clicore.PausePointCapturedVariablesFlagName,
		"--" + tooldocs.ProjectPathFlagName,
	},
}
