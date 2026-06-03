package cli

var nativeCommandOptions = map[string][]string{
	completionCommand: {installCompletionFlag, shellFlag},
	launchCommandName: {
		"--" + projectPathFlagName,
		"--delete-recovery",
		"--max-depth",
		"--platform",
		"--quit",
		"--restart",
	},
	installCommandName: {"--" + installDirFlagName},
	updateCommandName:  {"--" + updateToVersionFlagName},
	debugBreakWaitCommandName: {
		"--" + debugBreakIDFlagName,
		"--" + debugBreakTimeoutFlagName,
		"--" + projectPathFlagName,
	},
}
