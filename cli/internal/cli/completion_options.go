package cli

var nativeCommandOptions = map[string][]string{
	completionCommand: {installCompletionFlag, shellFlag},
	launchCommandName: {
		"--" + projectPathFlagName,
		"--delete-recovery",
		"--editor-version",
		"--max-depth",
		"--platform",
		"--quit",
		"--restart",
	},
	installCommandName: {"--" + installDirFlagName},
	updateCommandName:  {"--" + updateToVersionFlagName},
	pausePointWaitCommandName: {
		"--" + pausePointIDFlagName,
		"--" + pausePointTimeoutFlagName,
		"--" + pausePointLogsMaxCountFlagName,
		"--" + projectPathFlagName,
	},
	pausePointStatusUserCommandName: {
		"--" + pausePointIDFlagName,
		"--" + projectPathFlagName,
	},
}
