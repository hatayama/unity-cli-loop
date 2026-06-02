package cli

import (
	"fmt"
	"io"
	"sort"
	"strings"

	"github.com/hatayama/unity-cli-loop/cli/internal/project"
)

type optionHelpEntry struct {
	name        string
	usage       string
	description string
}

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

func visibleOptionHelpEntriesForTool(tool toolDefinition) []optionHelpEntry {
	schema := tool.EffectiveInputSchema()
	entries := make([]optionHelpEntry, 0, len(schema.Properties))
	for propertyName, property := range schema.Properties {
		if property.Hidden {
			continue
		}

		optionName := "--" + optionNameForProperty(tool.Name, propertyName, property)
		entries = append(entries, optionHelpEntry{
			name:        optionName,
			usage:       optionUsage(optionName, property),
			description: optionDescription(tool.Name, propertyName, property),
		})
	}

	sort.Slice(entries, func(i int, j int) bool {
		return entries[i].name < entries[j].name
	})
	return entries
}

func optionUsage(optionName string, property toolProperty) string {
	if isBooleanProperty(property) {
		return optionName
	}
	return optionName + " <" + optionValueName(property) + ">"
}

func optionValueName(property toolProperty) string {
	switch strings.ToLower(property.Type) {
	case "integer":
		return "integer"
	case "number":
		return "number"
	case "array":
		return "value[,value]"
	case "object":
		return "json"
	default:
		return "value"
	}
}

func optionDescription(toolName string, propertyName string, property toolProperty) string {
	if isRunTestsSaveBeforeRunOption(toolName, propertyName, property) {
		return optionSummary(toolName, propertyName, property) + "; default: auto-save enabled"
	}
	if isCompileReloadExternalSceneChangesOption(toolName, propertyName, property) {
		return optionSummary(toolName, propertyName, property) + "; default: auto-reload enabled"
	}

	parts := []string{}
	if description := optionSummary(toolName, propertyName, property); description != "" {
		parts = append(parts, description)
	}
	if propertyDefault := property.EffectiveDefault(); propertyDefault != nil {
		parts = append(parts, "default: "+defaultValueText(propertyDefault))
	}
	if len(property.Enum) > 0 {
		parts = append(parts, "values: "+strings.Join(property.Enum, "|"))
	}
	return strings.Join(parts, "; ")
}

func optionSummary(toolName string, propertyName string, property toolProperty) string {
	if isNegatedBooleanProperty(property) {
		if isRunTestsSaveBeforeRunOption(toolName, propertyName, property) {
			return "Fail before execution if unsaved editor changes remain instead of auto-saving them"
		}
		if isCompileReloadExternalSceneChangesOption(toolName, propertyName, property) {
			return "Stop before execution if open Scene files changed externally instead of auto-reloading them"
		}
		summary := firstHelpLine(property.Description)
		normalizedSummary := strings.ToLower(summary)
		if strings.HasPrefix(normalizedSummary, "disable ") || strings.HasPrefix(normalizedSummary, "do not ") {
			return summary
		}
		return "Disable " + pascalToWords(propertyName)
	}
	return firstHelpLine(property.Description)
}

func defaultValueText(value any) string {
	if boolValue, ok := value.(bool); ok {
		if boolValue {
			return "enabled"
		}
		return "disabled"
	}
	return fmt.Sprint(value)
}

func pascalToWords(value string) string {
	kebabName := pascalToKebab(value)
	return strings.ReplaceAll(kebabName, "-", " ")
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
	case launchCommandName, "list", "sync", "focus-window", waitForDebugBreakCommandName, skillsCommandName:
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
