package cli

import "io"

// tryHandleRunnerInfoRequest answers the project runner's own identity
// requests. Version output must stay here because the dispatcher forwards
// project-scoped version queries to the pinned runner, while help output is
// kept minimal: the full help UX is owned by the global uloop launcher.
func tryHandleRunnerInfoRequest(args []string, stdout io.Writer) (bool, int) {
	if len(args) == 0 || isHelpRequest(args) {
		printRunnerUsage(stdout)
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

// printRunnerUsage keeps direct runner help minimal so the help UX lives in
// exactly one binary: interactive use always goes through the global launcher.
func printRunnerUsage(stdout io.Writer) {
	writeLine(stdout, "Usage:")
	writeLine(stdout, "  uloop-project-runner <command> [options]")
	writeLine(stdout, "")
	writeLine(stdout, "This binary executes Unity project commands forwarded by the global `uloop` launcher.")
	writeLine(stdout, "Run `uloop --help` for the full command list and command help.")
}

// isDispatcherOwnedCommand reports whether a command belongs to the global
// launcher's bootstrap surface. The project runner must reject these instead
// of executing them so the two binaries keep disjoint responsibilities.
func isDispatcherOwnedCommand(command string) bool {
	switch command {
	case launchCommandName, installCommandName, updateCommandName, uninstallCommandName, skillsCommandName:
		return true
	default:
		return false
	}
}

func dispatcherOwnedCommandError(command string) cliError {
	return cliError{
		ErrorCode:   errorCodeInvalidArgument,
		Phase:       errorPhaseArgumentParsing,
		Message:     "The `" + command + "` command is handled by the global uloop launcher, not by the project runner binary.",
		Retryable:   false,
		SafeToRetry: false,
		Command:     command,
		NextActions: []string{"Run `uloop " + command + "` so the global uloop launcher handles it."},
	}
}
