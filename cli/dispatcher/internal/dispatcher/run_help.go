package dispatcher

import (
	"io"
	"os"
	"strings"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	"github.com/hatayama/unity-cli-loop/common/project"
)

const (
	nativeCLIDescription            = "Native CLI. Runs uloop commands and dispatches live Unity tool commands."
	maxCommandListDescriptionLength = 96
)

func printHelp(stdout io.Writer) {
	printMainHelp(
		stdout,
		clicore.Version(),
		nativeCLIDescription,
		clicore.ToolsCache{},
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

	cache, ok := clicore.LoadProjectToolCache(connection.ProjectRoot)
	printMainHelp(stdout, clicore.Version(), nativeCLIDescription, cache, ok)
}

func printLauncherHelp(stdout io.Writer) {
	printMainHelp(
		stdout,
		dispatcherVersion,
		"Dispatcher launcher. Finds the Unity project, then dispatches live Unity tool commands.",
		clicore.ToolsCache{},
		false)
}

func printMainHelp(stdout io.Writer, displayVersion string, description string, cache clicore.ToolsCache, hasProjectToolCache bool) {
	clicore.WriteFormat(stdout, "uloop %s\n\n", displayVersion)
	clicore.WriteLine(stdout, "Usage:")
	clicore.WriteLine(stdout, "  uloop <command> [options]")
	clicore.WriteLine(stdout, "")
	clicore.WriteLine(stdout, description)
	clicore.WriteLine(stdout, "")
	printNativeCommandHelp(stdout)
	clicore.WriteLine(stdout, "")
	printGlobalOptionsHelp(stdout)
	clicore.WriteLine(stdout, "")
	printUnityToolCommandHelp(stdout, cache, hasProjectToolCache)
	clicore.WriteLine(stdout, "")
	clicore.WriteLine(stdout, "More:")
	clicore.WriteLine(stdout, "  uloop list                                  Show the live Unity tool list")
	clicore.WriteLine(stdout, "  uloop --project-path /path/to/project list  Show tools for another Unity project")
	clicore.WriteLine(stdout, "  uloop <command> --help                      Show help for native and Unity tool commands")
	clicore.WriteLine(stdout, "  uloop completion --help                     Show shell completion setup and helpers")
}

func printNativeCommandHelp(stdout io.Writer) {
	clicore.WriteLine(stdout, "Native commands:")
	for _, entry := range clicore.NativeCommands {
		clicore.WriteFormat(stdout, "  %-14s %s\n", entry.Name, entry.Description)
	}
}

func printGlobalOptionsHelp(stdout io.Writer) {
	clicore.WriteLine(stdout, "Global options:")
	clicore.WriteLine(stdout, "  --project-path <path>   Run against a Unity project outside the current directory")
}

func printUnityToolCommandHelp(stdout io.Writer, cache clicore.ToolsCache, hasProjectToolCache bool) {
	if !hasProjectToolCache {
		clicore.WriteLine(stdout, "Unity tool commands are project-specific.")
		clicore.WriteLine(stdout, "  Run `uloop list` inside a Unity project to show the live tool list.")
		clicore.WriteLine(stdout, "  Run `uloop sync` after the Editor tool set changes to refresh cached commands.")
		return
	}

	clicore.WriteLine(stdout, "Unity tool commands from this project's cache:")
	if len(cache.Tools) == 0 {
		clicore.WriteLine(stdout, "  No cached Unity tools found. Run `uloop sync` while Unity is running.")
		return
	}

	for _, tool := range cache.Tools {
		if isNativeCommandName(tool.Name) {
			continue
		}
		clicore.WriteFormat(stdout, "  %-22s %s\n", tool.Name, commandListDescription(tool.Description))
	}
	clicore.WriteLine(stdout, "  Run `uloop sync` after the Editor tool set changes to refresh this list.")
}

func isNativeCommandName(name string) bool {
	for _, entry := range clicore.NativeCommands {
		if entry.Name == name {
			return true
		}
	}
	return false
}

func commandListDescription(description string) string {
	line := clicore.FirstHelpLine(description)
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

func loadCompletionTools(startPath string, projectPath string) clicore.ToolsCache {
	connection, err := project.ResolveConnection(startPath, projectPath)
	if err != nil {
		return clicore.LoadDefaultTools()
	}
	cache, err := clicore.LoadTools(connection.ProjectRoot)
	if err != nil {
		return clicore.LoadDefaultTools()
	}
	return cache
}
