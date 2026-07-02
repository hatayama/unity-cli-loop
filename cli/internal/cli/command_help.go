package cli

import (
	"io"
	"sort"

	"github.com/hatayama/unity-cli-loop/cli/internal/project"
)

func tryHandleCommandHelp(command string, startPath string, projectPath string, stdout io.Writer, stderr io.Writer) (bool, int) {
	if isNativeCommandName(command) {
		printNativeSingleCommandHelp(command, stdout)
		return true, 0
	}
	if tool, ok := findDefaultTool(command); ok {
		printToolHelp(tool, stdout)
		return true, 0
	}

	connection, err := project.ResolveConnection(startPath, projectPath)
	if err != nil {
		writeClassifiedError(stderr, err, errorContext{command: command})
		return true, 1
	}
	tool, cache, ok, err := findToolForCommand(connection.ProjectRoot, command)
	if err != nil {
		writeClassifiedError(stderr, err, errorContext{projectRoot: connection.ProjectRoot, command: command})
		return true, 1
	}
	if !ok {
		writeErrorEnvelope(stderr, unknownCommandError(command, cache, errorContext{
			projectRoot: connection.ProjectRoot,
			command:     command,
		}))
		return true, 1
	}

	printToolHelp(tool, stdout)
	return true, 0
}

func printNativeSingleCommandHelp(command string, stdout io.Writer) {
	writeLine(stdout, "Usage:")
	writeFormat(stdout, "  uloop %s", command)
	if options, ok := nativeCommandOptions[command]; ok && len(options) > 0 {
		writeLine(stdout, " [options]")
		writeLine(stdout, "")
		writeLine(stdout, "Options:")
		for _, option := range sortedStrings(options) {
			writeFormat(stdout, "  %s\n", option)
		}
		if nativeCommandUsesProject(command) {
			writeLine(stdout, "")
			printGlobalOptionsHelp(stdout)
		}
		return
	}

	writeLine(stdout, "")
	if description, ok := nativeCommandDescription(command); ok {
		writeLine(stdout, "")
		writeLine(stdout, description)
	}
	if nativeCommandUsesProject(command) {
		writeLine(stdout, "")
		printGlobalOptionsHelp(stdout)
	}
}

func printToolHelp(tool toolDefinition, stdout io.Writer) {
	writeLine(stdout, "Usage:")
	writeFormat(stdout, "  uloop %s", tool.Name)
	if len(visibleOptionHelpEntriesForTool(tool)) > 0 {
		writeLine(stdout, " [options]")
	} else {
		writeLine(stdout, "")
	}

	if description := firstHelpLine(tool.Description); description != "" {
		writeLine(stdout, "")
		writeLine(stdout, description)
	}

	entries := visibleOptionHelpEntriesForTool(tool)
	if len(entries) > 0 {
		writeLine(stdout, "")
		writeLine(stdout, "Options:")
		for _, entry := range entries {
			writeFormat(stdout, "  %-32s %s\n", entry.usage, entry.description)
		}
	}

	writeLine(stdout, "")
	printGlobalOptionsHelp(stdout)
}

func nativeCommandDescription(command string) (string, bool) {
	for _, entry := range nativeCommands {
		if entry.name == command {
			return entry.description, true
		}
	}
	return "", false
}

func nativeCommandUsesProject(command string) bool {
	switch command {
	case launchCommandName, "list", "sync", "focus-window", skillsCommandName, pausePointWaitCommandName, pausePointStatusUserCommandName:
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
