package cli

import (
	"encoding/json"
	"sort"
)

type listCatalog struct {
	Version       string     `json:"Version,omitempty"`
	ServerVersion string     `json:"ServerVersion,omitempty"`
	UpdatedAt     string     `json:"UpdatedAt,omitempty"`
	Tools         []listTool `json:"Tools"`
}

type listTool struct {
	Name        string       `json:"Name"`
	Description string       `json:"Description,omitempty"`
	Options     []listOption `json:"Options"`
}

type listOption struct {
	Name        string   `json:"Name"`
	Type        string   `json:"Type,omitempty"`
	Description string   `json:"Description,omitempty"`
	Default     any      `json:"Default,omitempty"`
	Values      []string `json:"Values,omitempty"`
}

func formatToolListResult(result json.RawMessage) json.RawMessage {
	var cache toolsCache
	if err := json.Unmarshal(result, &cache); err != nil {
		return result
	}

	content, err := json.Marshal(newListCatalog(cache))
	if err != nil {
		panic(err)
	}
	return content
}

func newListCatalog(cache toolsCache) listCatalog {
	tools := make([]listTool, 0, len(cache.Tools))
	for _, tool := range cache.Tools {
		tools = append(tools, newListTool(tool))
	}

	return listCatalog{
		Version:       cache.Version,
		ServerVersion: cache.ServerVersion,
		UpdatedAt:     cache.UpdatedAt,
		Tools:         tools,
	}
}

func newListTool(tool toolDefinition) listTool {
	return listTool{
		Name:        tool.Name,
		Description: tool.Description,
		Options:     listOptionsForTool(tool),
	}
}

func listOptionsForTool(tool toolDefinition) []listOption {
	schema := tool.EffectiveInputSchema()
	options := make([]listOption, 0, len(schema.Properties))
	for propertyName, property := range schema.Properties {
		if property.Hidden {
			continue
		}
		options = append(options, newListOption(tool, propertyName, property))
	}

	options = appendDynamicCodeFileListOption(tool, options)
	sort.Slice(options, func(i int, j int) bool {
		return options[i].Name < options[j].Name
	})
	return options
}

func newListOption(tool toolDefinition, propertyName string, property toolProperty) listOption {
	return listOption{
		Name:        "--" + optionNameForProperty(tool.Name, propertyName, property),
		Type:        property.Type,
		Description: optionSummary(tool.Name, propertyName, property),
		Default:     listOptionDefault(property),
		Values:      property.Enum,
	}
}

func listOptionDefault(property toolProperty) any {
	if isNegatedBooleanProperty(property) {
		return false
	}
	defaultValue := property.EffectiveDefault()
	if enumValue, ok := enumValueForNumericDefault(defaultValue, property.Enum); ok {
		return enumValue
	}
	return defaultValue
}

func enumValueForNumericDefault(defaultValue any, values []string) (string, bool) {
	if len(values) == 0 || defaultValue == nil {
		return "", false
	}

	switch value := defaultValue.(type) {
	case int:
		return enumValueAtIndex(value, values)
	case float64:
		index := int(value)
		if value != float64(index) {
			return "", false
		}
		return enumValueAtIndex(index, values)
	default:
		return "", false
	}
}

func enumValueAtIndex(index int, values []string) (string, bool) {
	if index < 0 || index >= len(values) {
		return "", false
	}
	return values[index], true
}

func appendDynamicCodeFileListOption(tool toolDefinition, options []listOption) []listOption {
	if tool.Name != executeDynamicCodeCommandName {
		return options
	}
	for _, option := range options {
		if option.Name == dynamicCodeFileOptionName {
			return options
		}
	}
	return append(options, listOption{
		Name:        dynamicCodeFileOptionName,
		Type:        "string",
		Description: dynamicCodeFileOptionDescription,
	})
}
