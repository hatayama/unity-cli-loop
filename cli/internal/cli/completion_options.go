package cli

var nativeCommandOptions = map[string][]string{
	completionCommand: {installCompletionFlag, shellFlag},
	launchCommandName: {
		"--" + projectPathFlagName,
		"--delete-recovery",
		"--editor-version",
		"--ignore-compiler-errors",
		"--max-depth",
		"--platform",
		"--quit",
		"--restart",
		"-i",
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
