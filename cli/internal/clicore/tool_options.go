package clicore

import (
	"sort"
	"strings"
)

const (
	ProjectPathFlagName                    = "project-path"
	ReloadExternalSceneChangesPropertyName = "ReloadExternalSceneChanges"
)

func FindProperty(tool ToolDefinition, kebabName string) (string, ToolProperty, bool, bool) {
	schema := tool.EffectiveInputSchema()
	for propertyName, property := range schema.Properties {
		if OptionNameForProperty(tool.Name, propertyName, property) == kebabName {
			return propertyName, property, IsNegatedBooleanProperty(property), true
		}
	}
	return "", ToolProperty{}, false, false
}

func OptionNameForProperty(toolName string, propertyName string, property ToolProperty) string {
	kebabName := pascalToKebab(propertyName)
	if IsNegatedBooleanProperty(property) {
		if isRunTestsSaveBeforeRunOption(toolName, propertyName, property) {
			return "fail-on-unsaved-changes"
		}
		if isCompileReloadExternalSceneChangesOption(toolName, propertyName, property) {
			return "stop-on-external-scene-changes"
		}
		return "no-" + kebabName
	}
	return kebabName
}

func isRunTestsSaveBeforeRunOption(toolName string, propertyName string, property ToolProperty) bool {
	return toolName == RunTestsCommandName &&
		propertyName == "SaveBeforeRun" &&
		IsNegatedBooleanProperty(property)
}

func isCompileReloadExternalSceneChangesOption(toolName string, propertyName string, property ToolProperty) bool {
	return toolName == CompileCommandName &&
		propertyName == ReloadExternalSceneChangesPropertyName &&
		IsNegatedBooleanProperty(property)
}

func VisibleOptionNamesForTool(tool ToolDefinition) []string {
	schema := tool.EffectiveInputSchema()
	options := make([]string, 0, len(schema.Properties))
	for propertyName, property := range schema.Properties {
		if property.Hidden {
			continue
		}
		options = append(options, "--"+OptionNameForProperty(tool.Name, propertyName, property))
	}
	options = appendDynamicCodeFileOptionName(tool, options)
	sort.Strings(options)
	return options
}

func IsBooleanProperty(property ToolProperty) bool {
	return strings.EqualFold(property.Type, "boolean")
}

func IsNegatedBooleanProperty(property ToolProperty) bool {
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
