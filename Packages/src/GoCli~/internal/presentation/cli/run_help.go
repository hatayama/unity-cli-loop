package cli

import (
	"io"
	"strings"

	"github.com/hatayama/unity-cli-loop/Packages/src/GoCli/internal/adapters/project"
)

func isVersionRequest(args []string) bool {
	return len(args) == 1 && (args[0] == "--version" || args[0] == "-v")
}

func isHelpRequest(args []string) bool {
	return len(args) == 1 && (args[0] == "--help" || args[0] == "-h")
}

func printHelp(stdout io.Writer) {
	printMainHelp(
		stdout,
		"Project-local CLI. Runs native uloop commands and dispatches live Unity tool commands.",
		toolsCache{},
		false)
}

func printLauncherHelp(stdout io.Writer) {
	printMainHelp(
		stdout,
		"Global dispatcher. Finds the Unity project, then dispatches to the project-local uloop-core binary.",
		toolsCache{},
		false)
}

func printLauncherHelpForResolvedProject(stdout io.Writer, startPath string, explicitProjectPath string) {
	projectRoot, err := resolveLauncherProjectRoot(startPath, explicitProjectPath)
	if err != nil {
		printLauncherHelp(stdout)
		return
	}

	cache, ok := loadCachedTools(projectRoot)
	printMainHelp(
		stdout,
		"Global dispatcher. Finds the Unity project, then dispatches to the project-local uloop-core binary.",
		cache,
		ok)
}

func printMainHelp(stdout io.Writer, description string, cache toolsCache, hasProjectToolCache bool) {
	writeFormat(stdout, "uloop %s\n\n", version)
	writeLine(stdout, "Usage:")
	writeLine(stdout, "  uloop <command> [options]")
	writeLine(stdout, "")
	writeLine(stdout, description)
	writeLine(stdout, "")
	printNativeCommandHelp(stdout)
	writeLine(stdout, "")
	printGlobalOptionsHelp(stdout)
	writeLine(stdout, "")
	printUnityToolCommandHelp(stdout, cache, hasProjectToolCache)
	writeLine(stdout, "")
	writeLine(stdout, "More:")
	writeLine(stdout, "  uloop list                                  Show the live Unity tool list")
	writeLine(stdout, "  uloop --project-path /path/to/project list  Show tools for another Unity project")
	writeLine(stdout, "  uloop <command> --help                      Show help for native commands that support it")
	writeLine(stdout, "  uloop completion --list-commands            Print command names for completion")
	writeLine(stdout, "  uloop completion --list-options <command>   Print options for a Unity tool command")
}

func printNativeCommandHelp(stdout io.Writer) {
	writeLine(stdout, "Native commands:")
	for _, entry := range nativeCommands {
		writeFormat(stdout, "  %-14s %s\n", entry.name, entry.description)
	}
}

func printGlobalOptionsHelp(stdout io.Writer) {
	writeLine(stdout, "Global options:")
	writeLine(stdout, "  --project-path <path>   Run against a Unity project outside the current directory")
}

func printUnityToolCommandHelp(stdout io.Writer, cache toolsCache, hasProjectToolCache bool) {
	if !hasProjectToolCache {
		writeLine(stdout, "Unity tool commands are project-specific.")
		writeLine(stdout, "  Run `uloop list` inside a Unity project to show the live tool list.")
		writeLine(stdout, "  Run `uloop sync` after the Editor tool set changes to refresh cached commands.")
		return
	}

	writeLine(stdout, "Unity tool commands from this project's cache:")
	if len(cache.Tools) == 0 {
		writeLine(stdout, "  No cached Unity tools found. Run `uloop sync` while Unity is running.")
		return
	}

	for _, tool := range cache.Tools {
		if isNativeCommandName(tool.Name) {
			continue
		}
		writeFormat(stdout, "  %-22s %s\n", tool.Name, firstHelpLine(tool.Description))
	}
	writeLine(stdout, "  Run `uloop sync` after the Editor tool set changes to refresh this list.")
}

func isNativeCommandName(name string) bool {
	for _, entry := range nativeCommands {
		if entry.name == name {
			return true
		}
	}
	return false
}

func firstHelpLine(description string) string {
	for _, line := range strings.Split(description, "\n") {
		trimmed := strings.TrimSpace(line)
		if trimmed != "" {
			return trimmed
		}
	}
	return ""
}

func loadCompletionTools(startPath string, projectPath string) toolsCache {
	connection, err := project.ResolveConnection(startPath, projectPath)
	if err != nil {
		return loadDefaultTools()
	}
	cache, err := loadTools(connection.ProjectRoot)
	if err != nil {
		return loadDefaultTools()
	}
	return cache
}
