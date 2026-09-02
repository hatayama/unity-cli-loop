package tooldocs

import (
	"sort"
	"strings"

	"github.com/hatayama/unity-cli-loop/common/tools"
)

const (
	ProjectPathFlagName                    = "project-path"
	ReloadExternalSceneChangesPropertyName = "ReloadExternalSceneChanges"
)

func FindProperty(tool tools.ToolDefinition, kebabName string) (string, tools.ToolProperty, bool, bool) {
	schema := tool.EffectiveInputSchema()
	for propertyName, property := range schema.Properties {
		if OptionNameForProperty(tool.Name, propertyName, property) == kebabName {
			return propertyName, property, IsNegatedBooleanProperty(property), true
		}
	}
	return "", tools.ToolProperty{}, false, false
}

func OptionNameForProperty(toolName string, propertyName string, property tools.ToolProperty) string {
	kebabName := pascalToKebab(propertyName)
	if IsNegatedBooleanProperty(property) {
		if isCompileReloadExternalSceneChangesOption(toolName, propertyName, property) {
			return "stop-on-external-scene-changes"
		}
		return "no-" + kebabName
	}
	return kebabName
}

func isCompileReloadExternalSceneChangesOption(toolName string, propertyName string, property tools.ToolProperty) bool {
	return toolName == compileCommandName &&
		propertyName == ReloadExternalSceneChangesPropertyName &&
		IsNegatedBooleanProperty(property)
}

func VisibleOptionNamesForTool(tool tools.ToolDefinition) []string {
	schema := tool.EffectiveInputSchema()
	options := make([]string, 0, len(schema.Properties))
	for propertyName, property := range schema.Properties {
		if property.Hidden {
			continue
		}
		options = append(options, "--"+OptionNameForProperty(tool.Name, propertyName, property))
	}
	options = appendDynamicCodeFileOptionName(tool, options)
	options = appendRunTestsSkipCompileOptionName(tool, options)
	sort.Strings(options)
	return options
}

func appendRunTestsSkipCompileOptionName(tool tools.ToolDefinition, options []string) []string {
	if tool.Name != runTestsCommandName {
		return options
	}
	for _, option := range options {
		if option == RunTestsSkipCompileOptionName {
			return options
		}
	}
	return append(options, RunTestsSkipCompileOptionName)
}

func IsBooleanProperty(property tools.ToolProperty) bool {
	return strings.EqualFold(property.Type, "boolean")
}

func IsNegatedBooleanProperty(property tools.ToolProperty) bool {
	defaultValue, ok := property.EffectiveDefault().(bool)
	return IsBooleanProperty(property) && ok && defaultValue
}

func pascalToKebab(value string) string {
	if value == "" {
		return value
	}

	var builder strings.Builder
	for index, char := range value {
		if index > 0 && char >= 'A' && char <= 'Z' {
			builder.WriteByte('-')
		}
		builder.WriteRune(char)
	}
	return strings.ToLower(builder.String())
}

func pascalToWords(value string) string {
	kebabName := pascalToKebab(value)
	return strings.ReplaceAll(kebabName, "-", " ")
}
