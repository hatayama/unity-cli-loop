package dispatcher

import (
	"context"
	"io"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"

	"github.com/hatayama/unity-cli-loop/common/clicore"
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
		printDispatcherHelp(stdout)
		return true, 0
	}
	if clicore.IsVersionJSONRequest(args) {
		writeDispatcherVersionOutput(stdout, true)
		return true, 0
	}
	if clicore.IsVersionRequest(args) {
		writeDispatcherVersionOutput(stdout, false)
		return true, 0
	}
	return false, 0
}

func tryHandlePreConnectionRequestWithDeps(
	ctx context.Context,
	remainingArgs []string,
	command string,
	commandArgs []string,
	startPath string,
	projectPath string,
	stdout io.Writer,
	stderr io.Writer,
	deps dispatcherRunDeps,
) (bool, int) {
	if handled, code := tryHandleCompletionRequest(remainingArgs); handled {
		return true, code
	}
	if clicore.IsUnknownLeadingOption(command) {
		clierrors.WriteClassifiedError(stderr, &clierrors.ArgumentError{
			Message:     "Unknown global option: " + command,
			Option:      command,
			NextActions: []string{"Run `uloop --help` to inspect supported global options."},
		}, clierrors.ErrorContext{})
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
	if handled, code := tryHandleLaunchRequestWithDeps(ctx, remainingArgs, startPath, projectPath, stdout, stderr, deps.launch); handled {
		return true, code
	}
	if handled, code := tryHandleSkillsRequest(remainingArgs, startPath, projectPath, stdout, stderr); handled {
		return true, code
	}
	if handled, code := tryHandleVersionRequest(remainingArgs, stdout, stderr); handled {
		return true, code
	}
	if clicore.ContainsHelpRequest(commandArgs) {
		if handled, code := tryHandleCommandHelp(command, startPath, projectPath, stdout, stderr); handled {
			return true, code
		}
	}
	return false, 0
}
