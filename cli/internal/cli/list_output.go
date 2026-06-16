package cli

import (
	"encoding/json"
	"sort"
)

type listCatalog struct {
	Version       string     `json:"version,omitempty"`
	ServerVersion string     `json:"serverVersion,omitempty"`
	UpdatedAt     string     `json:"updatedAt,omitempty"`
	Tools         []listTool `json:"tools"`
}

type listTool struct {
	Name        string       `json:"name"`
	Description string       `json:"description,omitempty"`
	Options     []listOption `json:"options"`
}

type listOption struct {
	Name        string   `json:"name"`
	Type        string   `json:"type,omitempty"`
	Description string   `json:"description,omitempty"`
	Default     any      `json:"default,omitempty"`
	Values      []string `json:"values,omitempty"`
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
	return property.EffectiveDefault()
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
