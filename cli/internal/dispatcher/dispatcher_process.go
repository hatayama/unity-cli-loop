package dispatcher

import (
	"context"
	"io"

	"github.com/hatayama/unity-cli-loop/cli/internal/clicore"
)

// tryHandleProjectScopeHelpRequest serves project-scoped help locally; help
// never needs the pinned runner, while project-scoped version requests are
// forwarded so the runner that actually executes commands reports its version.
func tryHandleProjectScopeHelpRequest(args []string, projectPath string, stdout io.Writer) (bool, int) {
	if len(args) == 0 || clicore.IsHelpRequest(args) {
		printHelpForResolvedProject(stdout, projectPath)
		return true, 0
	}
	return false, 0
}

func tryHandleDispatcherInfoRequest(args []string, stdout io.Writer) (bool, int) {
	if len(args) == 0 || clicore.IsHelpRequest(args) {
		printLauncherHelp(stdout)
		return true, 0
	}
	if clicore.IsVersionJSONRequest(args) {
		writeDispatcherVersionJSON(stdout)
		return true, 0
	}
	if clicore.IsVersionRequest(args) {
		clicore.WriteLine(stdout, dispatcherVersion)
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
	if clicore.ShouldHandleCompletionRequest(remainingArgs) {
		completionTools := loadCompletionTools(startPath, projectPath)
		if handled, code := tryHandleCompletionRequest(remainingArgs, completionTools, stdout, stderr); handled {
			return true, code
		}
	}
	if clicore.IsUnknownLeadingOption(command) {
		clicore.WriteClassifiedError(stderr, &clicore.ArgumentError{
			Message:     "Unknown global option: " + command,
			Option:      command,
			NextActions: []string{"Run `uloop --help` to inspect supported global options."},
		}, clicore.ErrorContext{})
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
	if clicore.ContainsHelpRequest(commandArgs) {
		if handled, code := tryHandleCommandHelp(command, startPath, projectPath, stdout, stderr); handled {
			return true, code
		}
	}
	return false, 0
}
