package clicore

import (
	"fmt"
	"sort"
	"strings"
)

// --code-file is CLI-side sugar for execute-dynamic-code: it loads a C# source file into
// the Code parameter instead of requiring the value inline, so option-name and
// option-help listings need to know about it even though Unity's tool schema does not.
const (
	DynamicCodeFileFlagName    = "code-file"
	DynamicCodeFileOptionName  = "--" + DynamicCodeFileFlagName
	DynamicCodeFileOptionUsage = DynamicCodeFileOptionName + " <path>"
)

const DynamicCodeFileOptionDescription = "Read C# code from a file instead of --code when shell quoting would alter multiline code"

// OptionHelpEntry is one row of a tool's --help option listing.
type OptionHelpEntry struct {
	Name        string
	Usage       string
	Description string
}

func VisibleOptionHelpEntriesForTool(tool ToolDefinition) []OptionHelpEntry {
	schema := tool.EffectiveInputSchema()
	entries := make([]OptionHelpEntry, 0, len(schema.Properties))
	for propertyName, property := range schema.Properties {
		if property.Hidden {
			continue
		}

		optionName := "--" + OptionNameForProperty(tool.Name, propertyName, property)
		entries = append(entries, OptionHelpEntry{
			Name:        optionName,
			Usage:       optionUsage(optionName, property),
			Description: optionDescription(tool.Name, propertyName, property),
		})
	}

	entries = appendDynamicCodeFileOptionHelpEntry(tool, entries)
	sort.Slice(entries, func(i int, j int) bool {
		return entries[i].Name < entries[j].Name
	})
	return entries
}

func optionUsage(optionName string, property ToolProperty) string {
	if IsBooleanProperty(property) {
		return optionName
	}
	return optionName + " <" + optionValueName(property) + ">"
}

func optionValueName(property ToolProperty) string {
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

func optionDescription(toolName string, propertyName string, property ToolProperty) string {
	if isRunTestsSaveBeforeRunOption(toolName, propertyName, property) {
		return OptionSummary(toolName, propertyName, property) + "; default: auto-save enabled"
	}
	if isCompileReloadExternalSceneChangesOption(toolName, propertyName, property) {
		return OptionSummary(toolName, propertyName, property) + "; default: auto-reload enabled"
	}

	parts := []string{}
	if description := OptionSummary(toolName, propertyName, property); description != "" {
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

func defaultValueText(value any) string {
	if boolValue, ok := value.(bool); ok {
		if boolValue {
			return "enabled"
		}
		return "disabled"
	}
	return fmt.Sprint(value)
}

func OptionSummary(toolName string, propertyName string, property ToolProperty) string {
	if IsNegatedBooleanProperty(property) {
		if isRunTestsSaveBeforeRunOption(toolName, propertyName, property) {
			return "Fail before execution if unsaved editor changes remain instead of auto-saving them"
		}
		if isCompileReloadExternalSceneChangesOption(toolName, propertyName, property) {
			return "Stop before execution if open Scene files changed externally instead of auto-reloading them"
		}
		summary := FirstHelpLine(property.Description)
		normalizedSummary := strings.ToLower(summary)
		if strings.HasPrefix(normalizedSummary, "disable ") || strings.HasPrefix(normalizedSummary, "do not ") {
			return summary
		}
		return "Disable " + pascalToWords(propertyName)
	}
	return FirstHelpLine(property.Description)
}

func appendDynamicCodeFileOptionName(tool ToolDefinition, options []string) []string {
	if tool.Name != ExecuteDynamicCodeCommandName {
		return options
	}
	for _, option := range options {
		if option == DynamicCodeFileOptionName {
			return options
		}
	}
	return append(options, DynamicCodeFileOptionName)
}

func appendDynamicCodeFileOptionHelpEntry(tool ToolDefinition, entries []OptionHelpEntry) []OptionHelpEntry {
	if tool.Name != ExecuteDynamicCodeCommandName {
		return entries
	}
	for _, entry := range entries {
		if entry.Name == DynamicCodeFileOptionName {
			return entries
		}
	}
	return append(entries, OptionHelpEntry{
		Name:        DynamicCodeFileOptionName,
		Usage:       DynamicCodeFileOptionUsage,
		Description: DynamicCodeFileOptionDescription,
	})
}
