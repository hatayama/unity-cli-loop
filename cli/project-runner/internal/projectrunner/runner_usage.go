package projectrunner

import (
	"io"

	"github.com/hatayama/unity-cli-loop/common/clicore"
)

// tryHandleRunnerInfoRequest answers the project runner's own identity
// requests. Version output must stay here because the dispatcher forwards
// project-scoped version queries to the pinned runner, while help output is
// kept minimal: the full help UX is owned by the global uloop launcher.
func tryHandleRunnerInfoRequest(args []string, stdout io.Writer) (bool, int) {
	if len(args) == 0 || clicore.IsHelpRequest(args) {
		printRunnerUsage(stdout)
		return true, 0
	}
	if clicore.IsVersionJSONRequest(args) {
		clicore.WriteVersionJSON(stdout)
		return true, 0
	}
	if clicore.IsVersionRequest(args) {
		clicore.WriteLine(stdout, clicore.Version())
		return true, 0
	}
	return false, 0
}

// printRunnerUsage keeps direct runner help minimal so the help UX lives in
// exactly one binary: interactive use always goes through the global launcher.
func printRunnerUsage(stdout io.Writer) {
	clicore.WriteLine(stdout, "Usage:")
	clicore.WriteLine(stdout, "  uloop-project-runner <command> [options]")
	clicore.WriteLine(stdout, "")
	clicore.WriteLine(stdout, "This binary executes Unity project commands forwarded by the global `uloop` launcher.")
	clicore.WriteLine(stdout, "Run `uloop --help` for the full command list and command help.")
}

func dispatcherOwnedCommandError(command string) clicore.CLIError {
	return clicore.CLIError{
		ErrorCode:   clicore.ErrorCodeInvalidArgument,
		Phase:       clicore.ErrorPhaseArgumentParsing,
		Message:     "The `" + command + "` command is handled by the global uloop launcher, not by the project runner binary.",
		Retryable:   false,
		SafeToRetry: false,
		Command:     command,
		NextActions: []string{"Run `uloop " + command + "` so the global uloop launcher handles it."},
	}
}
