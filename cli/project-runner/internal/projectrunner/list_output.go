package projectrunner

import (
	"encoding/json"
	"sort"

	"github.com/hatayama/unity-cli-loop/common/skilldocs"
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

func formatToolListResult(result json.RawMessage, projectRoot string) json.RawMessage {
	var cache clicore.ToolsCache
	if err := json.Unmarshal(result, &cache); err != nil {
		return result
	}

	// list formats get-tool-details' raw response and never goes through the project-cache loader,
	// so the placeholder fallback has to be applied here too. Without it `--help` would show real
	// descriptions while `list` kept reporting "Parameter: <Name>".
	cache = clicore.ApplyEmbeddedDescriptionFallback(cache)

	// The installed package's SKILL.md tables win over both the cache and the embedded catalog, so
	// list reports the same text `uloop <command> --help` does.
	cache = skilldocs.ApplyToCatalog(cache, projectRoot)

	content, err := json.Marshal(newListCatalog(cache))
	if err != nil {
		panic(err)
	}
	return content
}

func newListCatalog(cache clicore.ToolsCache) listCatalog {
	listTools := make([]listTool, 0, len(cache.Tools))
	for _, tool := range cache.Tools {
		listTools = append(listTools, newListTool(tool))
	}

	// Sourced from the embedded CLI contract because the tool catalog no longer
	// carries a release-please-stamped version field of its own.
	return listCatalog{
		Version:       clicontract.ProjectRunnerVersion(),
		ServerVersion: cache.ServerVersion,
		UpdatedAt:     cache.UpdatedAt,
		Tools:         listTools,
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
	options = appendRunTestsSkipCompileListOption(tool, options)
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
	if enumValue, ok := tooldocs.EnumValueForNumericDefault(defaultValue, property.Enum); ok {
		return enumValue
	}
	// Unity reports an empty string as the default of every unset string parameter. Reporting it
	// would claim the option has a default of "", and omitempty cannot drop it on its own because the
	// field is an interface. Nil keeps list symmetric with --help, which omits the same value.
	if defaultValue == "" {
		return nil
	}
	return defaultValue
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

func appendRunTestsSkipCompileListOption(tool clicore.ToolDefinition, options []listOption) []listOption {
	if tool.Name != clicore.RunTestsCommandName || hasListOption(options, tooldocs.RunTestsSkipCompileOptionName) {
		return options
	}
	return append(options, listOption{
		Name:        tooldocs.RunTestsSkipCompileOptionName,
		Type:        "boolean",
		Description: tooldocs.RunTestsSkipCompileOptionDescription,
	})
}

// appendPausePointEnableAwaitListOptions documents enable-pause-point's CLI-only orchestration
// flags on its catalog entry, mirroring appendDynamicCodeFileListOption: they are not part of the
// Unity-side EnablePausePointSchema, so they never appear in listOptionsForTool's schema-driven
// loop above. The flag table itself is shared with the dispatcher's `--help` renderer
// (tooldocs.PausePointEnableCLIOnlyOptions) so the two listings cannot drift apart.
func appendPausePointEnableAwaitListOptions(tool clicore.ToolDefinition, options []listOption) []listOption {
	if tool.Name != pausePointEnableCommandName {
		return options
	}

	for _, option := range tooldocs.PausePointEnableCLIOnlyOptions() {
		optionName := "--" + option.FlagName
		if hasListOption(options, optionName) {
			continue
		}
		options = append(options, listOption{
			Name:        optionName,
			Type:        option.Type,
			Description: option.Description,
			Values:      option.Values,
		})
	}
	return options
}

func hasListOption(options []listOption, name string) bool {
	for _, option := range options {
		if option.Name == name {
			return true
		}
	}
	return false
}
