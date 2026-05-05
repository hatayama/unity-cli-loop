package dispatcher

import (
	"encoding/json"
	"io"
	"os"
	"path/filepath"
	"strings"

	dispatchercontract "github.com/hatayama/unity-cli-loop/Packages/src/Cli/Dispatcher"
)

type nativeCommandEntry struct {
	name        string
	description string
}

type cachedTools struct {
	Tools []cachedTool `json:"tools"`
}

type cachedTool struct {
	Name            string                `json:"name"`
	Description     string                `json:"description"`
	InputSchema     cachedToolInputSchema `json:"inputSchema"`
	ParameterSchema cachedToolInputSchema `json:"parameterSchema"`
}

type cachedToolInputSchema struct {
	Properties map[string]cachedToolProperty `json:"properties"`
}

type cachedToolProperty struct {
	Type         string `json:"type"`
	Default      any    `json:"default"`
	DefaultValue any    `json:"DefaultValue"`
}

var nativeCommands = []nativeCommandEntry{
	{name: "launch", description: "Open this Unity project with the matching Editor version"},
	{name: "list", description: "Show Unity tools currently exposed by the Editor"},
	{name: "sync", description: "Refresh .uloop/tools.json from the running Editor"},
	{name: "focus-window", description: "Bring the Unity Editor window to the foreground"},
	{name: "fix", description: "Remove stale uloop lock files after an interrupted run"},
	{name: "skills", description: "List, install, or uninstall agent skills"},
	{name: "completion", description: "Print or install shell completion"},
	{name: "update", description: "Update the global uloop launcher binary"},
}

func printHelp(stdout io.Writer) {
	printMainHelp(stdout, cachedTools{}, false)
}

func printHelpForResolvedProject(stdout io.Writer, startPath string, explicitProjectPath string) {
	projectRoot, err := resolveProjectRoot(startPath, explicitProjectPath, []string{"help"})
	if err != nil {
		printHelp(stdout)
		return
	}

	cache, ok := loadCachedTools(projectRoot)
	printMainHelp(stdout, cache, ok)
}

func printMainHelp(stdout io.Writer, cache cachedTools, hasProjectToolCache bool) {
	writeFormat(stdout, "uloop %s\n\n", dispatchercontract.Current.DispatcherVersion)
	writeLine(stdout, "Usage:")
	writeLine(stdout, "  uloop <command> [options]")
	writeLine(stdout, "")
	writeLine(stdout, "Global dispatcher. Finds the Unity project, then dispatches to the project-local uloop-core binary.")
	writeLine(stdout, "")
	writeLine(stdout, "Native commands:")
	for _, entry := range nativeCommands {
		writeFormat(stdout, "  %-14s %s\n", entry.name, entry.description)
	}
	writeLine(stdout, "")
	writeLine(stdout, "Global options:")
	writeLine(stdout, "  --project-path <path>   Run against a Unity project outside the current directory")
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

func printUnityToolCommandHelp(stdout io.Writer, cache cachedTools, hasProjectToolCache bool) {
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

func loadCachedTools(projectRoot string) (cachedTools, bool) {
	content, err := os.ReadFile(filepath.Join(projectRoot, ".uloop", "tools.json"))
	if err != nil {
		return cachedTools{}, false
	}

	var cache cachedTools
	if json.Unmarshal(content, &cache) != nil {
		return cachedTools{}, false
	}
	return filterInternalSkillTools(projectRoot, cache), true
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
