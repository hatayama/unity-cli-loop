package cli

import (
	"sort"
	"strings"
)

const (
	projectPathFlagName                    = "project-path"
	reloadExternalSceneChangesPropertyName = "ReloadExternalSceneChanges"
)

func findProperty(tool toolDefinition, kebabName string) (string, toolProperty, bool, bool) {
	schema := tool.EffectiveInputSchema()
	for propertyName, property := range schema.Properties {
		if optionNameForProperty(tool.Name, propertyName, property) == kebabName {
			return propertyName, property, isNegatedBooleanProperty(property), true
		}
	}
	return "", toolProperty{}, false, false
}

func optionNameForProperty(toolName string, propertyName string, property toolProperty) string {
	kebabName := pascalToKebab(propertyName)
	if isNegatedBooleanProperty(property) {
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

func isRunTestsSaveBeforeRunOption(toolName string, propertyName string, property toolProperty) bool {
	return toolName == runTestsCommandName &&
		propertyName == "SaveBeforeRun" &&
		isNegatedBooleanProperty(property)
}

func isCompileReloadExternalSceneChangesOption(toolName string, propertyName string, property toolProperty) bool {
	return toolName == compileCommandName &&
		propertyName == reloadExternalSceneChangesPropertyName &&
		isNegatedBooleanProperty(property)
}

func visibleOptionNamesForTool(tool toolDefinition) []string {
	schema := tool.EffectiveInputSchema()
	options := make([]string, 0, len(schema.Properties))
	for propertyName, property := range schema.Properties {
		if property.Hidden {
			continue
		}
		options = append(options, "--"+optionNameForProperty(tool.Name, propertyName, property))
	}
	options = appendDynamicCodeFileOptionName(tool, options)
	sort.Strings(options)
	return options
}

func isBooleanProperty(property toolProperty) bool {
	return strings.EqualFold(property.Type, "boolean")
}

func isNegatedBooleanProperty(property toolProperty) bool {
	defaultValue, ok := property.EffectiveDefault().(bool)
	return isBooleanProperty(property) && ok && defaultValue
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
