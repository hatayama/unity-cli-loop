package clicore

import "github.com/hatayama/unity-cli-loop/common/tooldocs"

const (
	DynamicCodeFileFlagName          = tooldocs.DynamicCodeFileFlagName
	DynamicCodeFileOptionName        = tooldocs.DynamicCodeFileOptionName
	DynamicCodeFileOptionUsage       = tooldocs.DynamicCodeFileOptionUsage
	DynamicCodeFileOptionDescription = tooldocs.DynamicCodeFileOptionDescription

	ProjectPathFlagName                    = tooldocs.ProjectPathFlagName
	ReloadExternalSceneChangesPropertyName = tooldocs.ReloadExternalSceneChangesPropertyName
)

type OptionHelpEntry = tooldocs.OptionHelpEntry

func VisibleOptionHelpEntriesForTool(tool ToolDefinition) []OptionHelpEntry {
	return tooldocs.VisibleOptionHelpEntriesForTool(tool)
}

func FindProperty(tool ToolDefinition, kebabName string) (string, ToolProperty, bool, bool) {
	return tooldocs.FindProperty(tool, kebabName)
}

func OptionNameForProperty(toolName string, propertyName string, property ToolProperty) string {
	return tooldocs.OptionNameForProperty(toolName, propertyName, property)
}

func VisibleOptionNamesForTool(tool ToolDefinition) []string {
	return tooldocs.VisibleOptionNamesForTool(tool)
}

func IsBooleanProperty(property ToolProperty) bool {
	return tooldocs.IsBooleanProperty(property)
}

func IsNegatedBooleanProperty(property ToolProperty) bool {
	return tooldocs.IsNegatedBooleanProperty(property)
}

func OptionSummary(toolName string, propertyName string, property ToolProperty) string {
	return tooldocs.OptionSummary(toolName, propertyName, property)
}
