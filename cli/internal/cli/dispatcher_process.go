package cli

import (
	"context"
	"io"
)

func tryHandleGlobalInfoRequest(args []string, projectPath string, stdout io.Writer) (bool, int) {
	if len(args) == 0 || isHelpRequest(args) {
		printHelpForResolvedProject(stdout, projectPath)
		return true, 0
	}
	if isVersionJSONRequest(args) {
		writeVersionJSON(stdout)
		return true, 0
	}
	if isVersionRequest(args) {
		writeLine(stdout, version)
		return true, 0
	}
	return false, 0
}

func tryHandleDispatcherInfoRequest(args []string, stdout io.Writer) (bool, int) {
	if len(args) == 0 || isHelpRequest(args) {
		printLauncherHelp(stdout)
		return true, 0
	}
	if isVersionJSONRequest(args) {
		writeDispatcherVersionJSON(stdout)
		return true, 0
	}
	if isVersionRequest(args) {
		writeLine(stdout, dispatcherVersion)
		return true, 0
	}
	return false, 0
}

func tryHandlePreConnectionRequest(
	ctx context.Context,
	remainingArgs []string,
	command string,
	commandArgs []string,
	startPath string,
	projectPath string,
	stdout io.Writer,
	stderr io.Writer,
) (bool, int) {
	if shouldHandleCompletionRequest(remainingArgs) {
		completionTools := loadCompletionTools(startPath, projectPath)
		if handled, code := tryHandleCompletionRequest(remainingArgs, completionTools, stdout, stderr); handled {
			return true, code
		}
	}
	if isUnknownLeadingOption(command) {
		writeClassifiedError(stderr, &argumentError{
			message:     "Unknown global option: " + command,
			option:      command,
			nextActions: []string{"Run `uloop --help` to inspect supported global options."},
		}, errorContext{})
		return true, 1
	}
	if handled, code := tryHandleUpdateRequest(ctx, remainingArgs, stdout, stderr); handled {
		return true, code
	}
	if handled, code := tryHandleInstallRequest(ctx, remainingArgs, stdout, stderr); handled {
		return true, code
	}
	if handled, code := tryHandleUninstallRequest(ctx, remainingArgs, stdout, stderr); handled {
		return true, code
	}
	if handled, code := tryHandleLaunchRequest(ctx, remainingArgs, startPath, projectPath, stdout, stderr); handled {
		return true, code
	}
	if handled, code := tryHandleSkillsRequest(remainingArgs, startPath, projectPath, stdout, stderr); handled {
		return true, code
	}
	if containsHelpRequest(commandArgs) {
		if handled, code := tryHandleCommandHelp(command, startPath, projectPath, stdout, stderr); handled {
			return true, code
		}
	}
	return false, 0
}
