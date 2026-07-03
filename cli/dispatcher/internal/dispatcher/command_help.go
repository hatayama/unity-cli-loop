package dispatcher

import (
	"io"
	"sort"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	"github.com/hatayama/unity-cli-loop/common/project"
)

func tryHandleCommandHelp(command string, startPath string, projectPath string, stdout io.Writer, stderr io.Writer) (bool, int) {
	if isNativeCommandName(command) {
		printNativeSingleCommandHelp(command, stdout)
		return true, 0
	}
	if tool, ok := clicore.FindDefaultTool(command); ok {
		printToolHelp(tool, stdout)
		return true, 0
	}

	connection, err := project.ResolveConnection(startPath, projectPath)
	if err != nil {
		clicore.WriteClassifiedError(stderr, err, clicore.ErrorContext{Command: command})
		return true, 1
	}
	tool, cache, ok, err := clicore.FindToolForCommand(connection.ProjectRoot, command)
	if err != nil {
		clicore.WriteClassifiedError(stderr, err, clicore.ErrorContext{ProjectRoot: connection.ProjectRoot, Command: command})
		return true, 1
	}
	if !ok {
		clicore.WriteErrorEnvelope(stderr, clicore.UnknownCommandError(command, cache, clicore.ErrorContext{
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
}

func printToolHelp(tool clicore.ToolDefinition, stdout io.Writer) {
	clicore.WriteLine(stdout, "Usage:")
	clicore.WriteFormat(stdout, "  uloop %s", tool.Name)
	if len(clicore.VisibleOptionHelpEntriesForTool(tool)) > 0 {
		clicore.WriteLine(stdout, " [options]")
	} else {
		clicore.WriteLine(stdout, "")
	}

	if description := clicore.FirstHelpLine(tool.Description); description != "" {
		clicore.WriteLine(stdout, "")
		clicore.WriteLine(stdout, description)
	}

	entries := clicore.VisibleOptionHelpEntriesForTool(tool)
	if len(entries) > 0 {
		clicore.WriteLine(stdout, "")
		clicore.WriteLine(stdout, "Options:")
		for _, entry := range entries {
			clicore.WriteFormat(stdout, "  %-32s %s\n", entry.Usage, entry.Description)
		}
	}

	clicore.WriteLine(stdout, "")
	printGlobalOptionsHelp(stdout)
}

func nativeCommandDescription(command string) (string, bool) {
	for _, entry := range clicore.NativeCommands {
		if entry.Name == command {
			return entry.Description, true
		}
	}
	return "", false
}

func nativeCommandUsesProject(command string) bool {
	switch command {
	case clicore.LaunchCommandName, "list", "sync", "focus-window", clicore.SkillsCommandName, clicore.PausePointWaitCommandName, clicore.PausePointStatusUserCommandName:
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
