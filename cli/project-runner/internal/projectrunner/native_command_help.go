package projectrunner

import (
	"fmt"
	"io"
	"sort"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	"github.com/hatayama/unity-cli-loop/common/tooldocs"
)

const (
	PausePointIDFlagName                    = "id"
	PausePointTimeoutFlagName               = "timeout-seconds"
	PausePointLogsMaxCountFlagName          = "matching-logs-max-count"
	PausePointCapturedVariablesFlagName     = "captured-variables"
	PausePointCapturedVariableNamesFlagName = "captured-variable-names"
	PausePointExpectFlagName                = "expect"
	PausePointTriggerFlagName               = "trigger"
)

// runnerNativeCommandOptions lists the flags accepted by each runner-owned
// native command. It lives with the runner binary (rather than the
// dispatcher) so that adding a flag to a runner-owned command never requires
// a dispatcher code change or release.
var runnerNativeCommandOptions = map[string][]string{
	clicore.PausePointAwaitCommandName: {
		"--" + PausePointIDFlagName,
		"--" + PausePointTimeoutFlagName,
		"--" + PausePointLogsMaxCountFlagName,
		"--" + PausePointCapturedVariablesFlagName,
		"--" + PausePointCapturedVariableNamesFlagName,
		"--" + PausePointExpectFlagName,
		"--" + PausePointTriggerFlagName,
	},
	clicore.PausePointStatusUserCommandName: {
		"--" + PausePointIDFlagName,
		"--" + PausePointCapturedVariablesFlagName,
		"--" + PausePointCapturedVariableNamesFlagName,
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

// pausePointUnknownOptionError reports an unrecognized flag for a runner-owned native
// command. The hint calls out an outdated installed project runner as the likely cause when
// the flag is documented in the skill but this runner build predates it, rather than leaving
// the caller to guess between a typo and a stale binary.
func pausePointUnknownOptionError(command string, name string) *clierrors.ArgumentError {
	return &clierrors.ArgumentError{
		Message: fmt.Sprintf(
			"Unknown option %q for %s. If the skill documentation mentions this option, the installed "+
				"project runner may be older than the docs — check 'uloop --version' and update the CLI.",
			"--"+name, command),
		Option:      "--" + name,
		Command:     command,
		NextActions: []string{fmt.Sprintf("Run `uloop %s --help` to inspect supported options.", command)},
	}
}

func sortedNativeCommandOptions(options []string) []string {
	result := append([]string{}, options...)
	sort.Strings(result)
	return result
}
