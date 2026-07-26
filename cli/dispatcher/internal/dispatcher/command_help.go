package dispatcher

import (
	"io"
	"sort"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
	"github.com/hatayama/unity-cli-loop/common/tooldocs"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	"github.com/hatayama/unity-cli-loop/common/project"
)

func tryHandleCommandHelp(command string, startPath string, projectPath string, stdout io.Writer, stderr io.Writer) (bool, int) {
	// Runner-owned commands are intentionally excluded here: their --help must
	// go through the normal dispatch path to the pinned runner (see
	// shouldRunInDispatcherProcess), which answers with its own up-to-date
	// flag list instead of a dispatcher-side table that can drift.
	if clicore.IsDispatcherOwnedCommandName(command) {
		printNativeSingleCommandHelp(command, stdout)
		return true, 0
	}

	connection, err := project.ResolveConnection(startPath, projectPath)
	if err != nil {
		if projectPath == "" {
			if tool, ok := clicore.FindDefaultTool(command); ok {
				printToolHelp(tool, stdout)
				return true, 0
			}
		}
		clierrors.WriteClassifiedError(stderr, err, clierrors.ErrorContext{Command: command})
		return true, 1
	}
	tool, cache, ok, err := clicore.FindToolForCommand(connection.ProjectRoot, command)
	if err != nil {
		clierrors.WriteClassifiedError(stderr, err, clierrors.ErrorContext{ProjectRoot: connection.ProjectRoot, Command: command})
		return true, 1
	}
	if !ok {
		clierrors.WriteErrorEnvelope(stderr, clicore.UnknownCommandError(command, cache, clierrors.ErrorContext{
			ProjectRoot: connection.ProjectRoot,
			Command:     command,
		}))
		return true, 1
	}

	printToolHelp(tool, stdout)
	return true, 0
}

func printNativeSingleCommandHelp(command string, stdout io.Writer) {
	clicore.WriteLine(stdout, "Usage:")
	clicore.WriteFormat(stdout, "  uloop %s", command)
	if options, ok := nativeCommandOptions[command]; ok && len(options) > 0 {
		clicore.WriteLine(stdout, " [options]")
		clicore.WriteLine(stdout, "")
		if description, ok := nativeCommandDescription(command); ok {
			clicore.WriteLine(stdout, description)
			clicore.WriteLine(stdout, "")
		}
		clicore.WriteLine(stdout, "Options:")
		for _, option := range sortedStrings(options) {
			clicore.WriteFormat(stdout, "  %s\n", option)
		}
		if nativeCommandUsesProject(command) {
			clicore.WriteLine(stdout, "")
			printGlobalOptionsHelp(stdout)
		}
		printSkillGuidanceHelp(command, stdout)
		return
	}

	clicore.WriteLine(stdout, "")
	if description, ok := nativeCommandDescription(command); ok {
		clicore.WriteLine(stdout, "")
		clicore.WriteLine(stdout, description)
	}
	if nativeCommandUsesProject(command) {
		clicore.WriteLine(stdout, "")
		printGlobalOptionsHelp(stdout)
	}
	printSkillGuidanceHelp(command, stdout)
}

func printToolHelp(tool clicore.ToolDefinition, stdout io.Writer) {
	clicore.WriteLine(stdout, "Usage:")
	clicore.WriteFormat(stdout, "  uloop %s", tool.Name)
	if len(tooldocs.VisibleOptionHelpEntriesForTool(tool)) > 0 {
		clicore.WriteLine(stdout, " [options]")
	} else {
		clicore.WriteLine(stdout, "")
	}

	if description := tooldocs.FirstHelpLine(tool.Description); description != "" {
		clicore.WriteLine(stdout, "")
		clicore.WriteLine(stdout, description)
	}

	entries := tooldocs.VisibleOptionHelpEntriesForTool(tool)
	if len(entries) > 0 {
		clicore.WriteLine(stdout, "")
		clicore.WriteLine(stdout, "Options:")
		for _, entry := range entries {
			// Wide enough for the longest usage string (--captured-variable-names <value>), so no
			// single row pushes its description out of the column.
			clicore.WriteFormat(stdout, "  %-34s %s\n", entry.Usage, entry.Description)
		}
	}

	clicore.WriteLine(stdout, "")
	printGlobalOptionsHelp(stdout)
	printSkillGuidanceHelp(tool.Name, stdout)
}

// printSkillGuidanceHelp closes a command's help with the instruction to load its skill. Nothing is
// printed for a command with no skill (custom commands), so the output never names a skill that
// cannot be loaded.
func printSkillGuidanceHelp(command string, stdout io.Writer) {
	guidance, ok := tooldocs.SkillGuidanceLine(command)
	if !ok {
		return
	}
	clicore.WriteLine(stdout, "")
	clicore.WriteLine(stdout, guidance)
}

func nativeCommandDescription(command string) (string, bool) {
	entry, ok := clicore.NativeCommand(command)
	if !ok {
		return "", false
	}
	return entry.Description, true
}

func nativeCommandUsesProject(command string) bool {
	switch command {
	case clicore.LaunchCommandName, clicore.SkillsCommandName:
		return true
	default:
		return false
	}
}

func sortedStrings(values []string) []string {
	result := append([]string{}, values...)
	sort.Strings(result)
	return result
}
