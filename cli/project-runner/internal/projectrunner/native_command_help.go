package projectrunner

import (
	"io"
	"sort"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	"github.com/hatayama/unity-cli-loop/common/tooldocs"
)

// runnerNativeCommandOptions lists the flags accepted by each runner-owned
// native command. It lives with the runner binary (rather than the
// dispatcher) so that adding a flag to a runner-owned command never requires
// a dispatcher code change or release.
var runnerNativeCommandOptions = map[string][]string{
	clicore.PausePointAwaitCommandName: {
		"--" + clicore.PausePointIDFlagName,
		"--" + clicore.PausePointTimeoutFlagName,
		"--" + clicore.PausePointLogsMaxCountFlagName,
		"--" + clicore.PausePointCapturedVariablesFlagName,
	},
	clicore.PausePointStatusUserCommandName: {
		"--" + clicore.PausePointIDFlagName,
		"--" + clicore.PausePointCapturedVariablesFlagName,
	},
}

// tryPrintNativeCommandHelp prints command-specific help for a runner-owned
// native command and reports whether it did. The dispatcher forwards `--help`
// for these commands to the pinned runner instead of keeping a hardcoded
// options table, so this output must stay in sync with the runner's actual
// flags by construction.
func tryPrintNativeCommandHelp(command string, stdout io.Writer) bool {
	if !clicore.IsRunnerOwnedCommandName(command) {
		return false
	}
	printNativeCommandHelp(command, stdout)
	return true
}

func printNativeCommandHelp(command string, stdout io.Writer) {
	entry, _ := clicore.NativeCommand(command)
	options := sortedNativeCommandOptions(runnerNativeCommandOptions[command])

	clicore.WriteLine(stdout, "Usage:")
	if len(options) > 0 {
		clicore.WriteFormat(stdout, "  uloop %s [options]\n", command)
		clicore.WriteLine(stdout, "")
		clicore.WriteLine(stdout, entry.Description)
		clicore.WriteLine(stdout, "")
		clicore.WriteLine(stdout, "Options:")
		for _, option := range options {
			clicore.WriteFormat(stdout, "  %s\n", option)
		}
	} else {
		clicore.WriteFormat(stdout, "  uloop %s\n", command)
		clicore.WriteLine(stdout, "")
		clicore.WriteLine(stdout, entry.Description)
	}

	clicore.WriteLine(stdout, "")
	clicore.WriteLine(stdout, "Global options:")
	clicore.WriteFormat(stdout, "  --%s <path>   Run against a Unity project outside the current directory\n", tooldocs.ProjectPathFlagName)
}

func sortedNativeCommandOptions(options []string) []string {
	result := append([]string{}, options...)
	sort.Strings(result)
	return result
}
