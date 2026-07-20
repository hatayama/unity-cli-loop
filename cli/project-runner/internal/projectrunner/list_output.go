package projectrunner

import (
	"encoding/json"
	"sort"

	"github.com/hatayama/unity-cli-loop/common/tooldocs"

	"github.com/hatayama/unity-cli-loop/common/clicontract"
	"github.com/hatayama/unity-cli-loop/common/clicore"
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
	var cache clicore.ToolsCache
	if err := json.Unmarshal(result, &cache); err != nil {
		return result
	}

	content, err := json.Marshal(newListCatalog(cache))
	if err != nil {
		panic(err)
	}
	return content
}

func newListCatalog(cache clicore.ToolsCache) listCatalog {
	tools := make([]listTool, 0, len(cache.Tools))
	for _, tool := range cache.Tools {
		tools = append(tools, newListTool(tool))
	}

	// Sourced from the embedded CLI contract because the tool catalog no longer
	// carries a release-please-stamped version field of its own.
	return listCatalog{
		Version:       clicontract.ProjectRunnerVersion(),
		ServerVersion: cache.ServerVersion,
		UpdatedAt:     cache.UpdatedAt,
		Tools:         tools,
	}
}

func newListTool(tool clicore.ToolDefinition) listTool {
	return listTool{
		Name:        tool.Name,
		Description: tool.Description,
		Options:     listOptionsForTool(tool),
	}
}

func listOptionsForTool(tool clicore.ToolDefinition) []listOption {
	schema := tool.EffectiveInputSchema()
	options := make([]listOption, 0, len(schema.Properties))
	for propertyName, property := range schema.Properties {
		if property.Hidden {
			continue
		}
		options = append(options, newListOption(tool, propertyName, property))
	}

	options = appendDynamicCodeFileListOption(tool, options)
	options = appendPausePointEnableAwaitListOptions(tool, options)
	sort.Slice(options, func(i int, j int) bool {
		return options[i].Name < options[j].Name
	})
	return options
}

func newListOption(tool clicore.ToolDefinition, propertyName string, property clicore.ToolProperty) listOption {
	return listOption{
		Name:        "--" + tooldocs.OptionNameForProperty(tool.Name, propertyName, property),
		Type:        property.Type,
		Description: tooldocs.OptionSummary(tool.Name, propertyName, property),
		Default:     listOptionDefault(property),
		Values:      property.Enum,
	}
}

func listOptionDefault(property clicore.ToolProperty) any {
	if tooldocs.IsNegatedBooleanProperty(property) {
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

func appendDynamicCodeFileListOption(tool clicore.ToolDefinition, options []listOption) []listOption {
	if tool.Name != clicore.ExecuteDynamicCodeCommandName {
		return options
	}
	for _, option := range options {
		if option.Name == tooldocs.DynamicCodeFileOptionName {
			return options
		}
	}
	return append(options, listOption{
		Name:        tooldocs.DynamicCodeFileOptionName,
		Type:        "string",
		Description: tooldocs.DynamicCodeFileOptionDescription,
	})
}

// appendPausePointEnableAwaitListOptions documents --await/--captured-variables/
// --captured-variable-names on enable-pause-point's catalog entry, mirroring
// appendDynamicCodeFileListOption: these are CLI-only orchestration flags (pause_point_enable.go)
// that are not part of the Unity-side EnablePausePointSchema, so they never appear in
// listOptionsForTool's schema-driven loop above.
func appendPausePointEnableAwaitListOptions(tool clicore.ToolDefinition, options []listOption) []listOption {
	if tool.Name != pausePointEnableCommandName {
		return options
	}
	if hasListOption(options, "--"+pausePointEnableAwaitFlagName) {
		return options
	}
	return append(options,
		listOption{
			Name:        "--" + pausePointEnableAwaitFlagName,
			Type:        "boolean",
			Description: "Wait for the marker to be hit (or time out) after enabling, in a single call, instead of a separate await-pause-point call",
		},
		listOption{
			Name:        "--" + PausePointCapturedVariablesFlagName,
			Type:        "string",
			Description: "Requires --await. Same as await-pause-point's --captured-variables",
			Values:      []string{string(pausePointCapturedVariablesModeFull), string(pausePointCapturedVariablesModeNames)},
		},
		listOption{
			Name:        "--" + PausePointCapturedVariableNamesFlagName,
			Type:        "string",
			Description: "Requires --await. Same as await-pause-point's --captured-variable-names",
		},
	)
}

func hasListOption(options []listOption, name string) bool {
	for _, option := range options {
		if option.Name == name {
			return true
		}
	}
	return false
}
