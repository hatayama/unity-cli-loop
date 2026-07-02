package cli

import (
	"io"
	"os"
	"strings"

	"github.com/hatayama/unity-cli-loop/cli/internal/project"
)

const (
	nativeCLIDescription            = "Native CLI. Runs uloop commands and dispatches live Unity tool commands."
	maxCommandListDescriptionLength = 96
)

func printHelp(stdout io.Writer) {
	printMainHelp(
		stdout,
		version,
		nativeCLIDescription,
		toolsCache{},
		false)
}

func printHelpForResolvedProject(stdout io.Writer, explicitProjectPath string) {
	startPath, err := os.Getwd()
	if err != nil {
		printHelp(stdout)
		return
	}

	connection, err := project.ResolveConnection(startPath, explicitProjectPath)
	if err != nil {
		printHelp(stdout)
		return
	}

	cache, ok := loadProjectToolCache(connection.ProjectRoot)
	printMainHelp(stdout, version, nativeCLIDescription, cache, ok)
}

func printLauncherHelp(stdout io.Writer) {
	printMainHelp(
		stdout,
		dispatcherVersion,
		"Dispatcher launcher. Finds the Unity project, then dispatches live Unity tool commands.",
		toolsCache{},
		false)
}

func printMainHelp(stdout io.Writer, displayVersion string, description string, cache toolsCache, hasProjectToolCache bool) {
	writeFormat(stdout, "uloop %s\n\n", displayVersion)
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
	writeLine(stdout, "  uloop <command> --help                      Show help for native and Unity tool commands")
	writeLine(stdout, "  uloop completion --help                     Show shell completion setup and helpers")
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
		writeFormat(stdout, "  %-22s %s\n", tool.Name, commandListDescription(tool.Description))
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

func commandListDescription(description string) string {
	line := firstHelpLine(description)
	for index, value := range line {
		if value == '.' || value == '!' || value == '?' {
			return strings.TrimSpace(line[:index+len(string(value))])
		}
	}

	runes := []rune(line)
	if len(runes) <= maxCommandListDescriptionLength {
		return line
	}
	return strings.TrimSpace(string(runes[:maxCommandListDescriptionLength-3])) + "..."
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
