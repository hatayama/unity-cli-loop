package projectrunner

import (
	"io"
	"sort"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	"github.com/hatayama/unity-cli-loop/common/tooldocs"
)

// Runner-specific pause-point flags: --id and --timeout-seconds mirror the Unity-side schema, and
// --matching-logs-max-count only exists on the wait commands. The CLI-only flags shared with
// enable-pause-point's --help listing live in tooldocs instead (pause_point_cli_options.go).
// The string values are the tooldocs constants so parsers and option tables cannot drift.
const (
	PausePointIDFlagName           = tooldocs.PausePointIDFlagName
	PausePointFileFlagName         = tooldocs.PausePointFileFlagName
	PausePointLineFlagName         = tooldocs.PausePointLineFlagName
	PausePointTimeoutFlagName      = tooldocs.PausePointTimeoutSecondsFlagName
	PausePointLogsMaxCountFlagName = tooldocs.PausePointMatchingLogsMaxCountFlagName
)

// runnerNativeCLIOnlyOptions returns the tooldocs table for a runner-owned native command.
// Help rendering and unknown-option owner lookup both read this, so a flag added to a table
// is advertised and recognized without a second handwritten name list.
func runnerNativeCLIOnlyOptions(command string) []tooldocs.PausePointCLIOnlyOption {
	switch command {
	case "list":
		return tooldocs.ListCLIOnlyOptions()
	case clicore.PausePointAwaitCommandName:
		return tooldocs.PausePointAwaitCLIOnlyOptions()
	case clicore.PausePointStatusUserCommandName:
		return tooldocs.PausePointStatusCLIOnlyOptions()
	case clicore.SetCodeOptimizationCommandName:
		return []tooldocs.PausePointCLIOnlyOption{
			{
				FlagName:    "startup",
				Type:        "boolean",
				Description: "Also set the machine-wide startup preference to Debug",
			},
		}
	default:
		return nil
	}
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
	helpEntries := sortedNativeCommandHelpEntries(
		tooldocs.PausePointCLIOnlyHelpEntries(runnerNativeCLIOnlyOptions(command)))

	clicore.WriteLine(stdout, "Usage:")
	if len(helpEntries) > 0 {
		clicore.WriteFormat(stdout, "  %s\n", nativeCommandUsage(command, true))
		clicore.WriteLine(stdout, "")
		clicore.WriteLine(stdout, entry.Description)
		clicore.WriteLine(stdout, "")
		clicore.WriteLine(stdout, "Options:")
		for _, helpEntry := range helpEntries {
			// Wide enough for the longest usage string (--captured-variable-names <value>), so no
			// single row pushes its description out of the column. Matches dispatcher tool help.
			clicore.WriteFormat(stdout, "  %-34s %s\n", helpEntry.Usage, helpEntry.Description)
		}
	} else {
		clicore.WriteFormat(stdout, "  %s\n", nativeCommandUsage(command, false))
		clicore.WriteLine(stdout, "")
		clicore.WriteLine(stdout, entry.Description)
	}

	clicore.WriteLine(stdout, "")
	clicore.WriteLine(stdout, "Global options:")
	clicore.WriteFormat(stdout, "  --%s <path>   Run against a Unity project outside the current directory\n", tooldocs.ProjectPathFlagName)

	// Runner-owned commands print their own help, so the dispatcher's closing skill line never
	// reaches them: await-pause-point and pause-point-status need it added here.
	if guidance, ok := tooldocs.SkillGuidanceLine(command); ok {
		clicore.WriteLine(stdout, "")
		clicore.WriteLine(stdout, guidance)
	}
}

func nativeCommandUsage(command string, hasOptions bool) string {
	if command == clicore.SetCodeOptimizationCommandName {
		return "uloop set-code-optimization debug [options]"
	}
	if hasOptions {
		return "uloop " + command + " [options]"
	}
	return "uloop " + command
}

func sortedNativeCommandHelpEntries(entries []tooldocs.OptionHelpEntry) []tooldocs.OptionHelpEntry {
	result := append([]tooldocs.OptionHelpEntry{}, entries...)
	sort.Slice(result, func(left int, right int) bool {
		return result[left].Name < result[right].Name
	})
	return result
}
