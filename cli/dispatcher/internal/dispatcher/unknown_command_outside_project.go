package dispatcher

import (
	"errors"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
)

// projectResolutionError returns the error to report when the project for command could not be
// resolved. When no Unity project was found and command is not a built-in command, it reports an
// unknown command instead of the missing project.
// Why: without a project only the built-in commands are known, and "project not found" sends
// someone who mistyped `uloop update` looking for a project the command never needed. The
// project guidance stays in NextActions for a name that is a tool of a project not found here.
func projectResolutionError(err error, command string, context clierrors.ErrorContext) clierrors.CLIError {
	if !errors.As(err, new(clierrors.ProjectNotFoundError)) || command == "" || isBuiltInCommandName(command) {
		return clierrors.ClassifyError(err, context)
	}
	unknownCommand := clicore.UnknownCommandError(command, clicore.LoadDefaultTools(), context)
	// Why replace the runner's next actions: `uloop list` and `uloop sync` read a project, so
	// outside one they would only fail again.
	unknownCommand.NextActions = []string{
		"Run `uloop --help` to see the built-in commands.",
		"If `" + command + "` is a tool of your Unity project, run the command from inside a Unity project.",
		"Pass `--project-path <path>` when targeting another Unity project.",
	}
	return unknownCommand
}

// isBuiltInCommandName reports whether command names a native command, hidden ones included, or a
// tool in the catalog embedded in this binary.
func isBuiltInCommandName(command string) bool {
	if _, ok := clicore.NativeCommand(command); ok {
		return true
	}
	_, ok := clicore.FindDefaultTool(command)
	return ok
}
