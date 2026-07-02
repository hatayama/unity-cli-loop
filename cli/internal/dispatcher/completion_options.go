package dispatcher

import "github.com/hatayama/unity-cli-loop/common/clicore"

var nativeCommandOptions = map[string][]string{
	clicore.CompletionCommand: {installCompletionFlag, shellFlag},
	clicore.LaunchCommandName: {
		"--" + clicore.ProjectPathFlagName,
		"--delete-recovery",
		"--editor-version",
		"--max-depth",
		"--platform",
		"--quit",
		"--restart",
	},
	clicore.InstallCommandName: {"--" + installDirFlagName},
	clicore.UpdateCommandName:  {"--" + updateToVersionFlagName},
	clicore.PausePointWaitCommandName: {
		"--" + clicore.PausePointIDFlagName,
		"--" + clicore.PausePointTimeoutFlagName,
		"--" + clicore.PausePointLogsMaxCountFlagName,
		"--" + clicore.ProjectPathFlagName,
	},
	clicore.PausePointStatusUserCommandName: {
		"--" + clicore.PausePointIDFlagName,
		"--" + clicore.ProjectPathFlagName,
	},
}
