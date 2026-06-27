package cli

import (
	"encoding/json"
	"sort"
	"strconv"
	"strings"

	clicontract "github.com/hatayama/unity-cli-loop/cli"
	"github.com/hatayama/unity-cli-loop/cli/internal/tools"
)

var (
	version                   = clicontract.Current.CliVersion
	protocolVersion           = clicontract.Current.ProtocolVersion
	dispatcherVersion         = clicontract.DispatcherCurrent.DispatcherVersion
	dispatcherContractVersion = clicontract.DispatcherCurrent.DispatcherContractVersion
)

const (
	cacheDirectoryName                     = tools.CacheDirectoryName
	cacheFileName                          = tools.CacheFileName
	projectPathFlagName                    = "project-path"
	runTestsCommandName                    = "run-tests"
	reloadExternalSceneChangesPropertyName = "ReloadExternalSceneChanges"
)

type (
	toolsCache     = tools.ToolCatalog
	toolDefinition = tools.ToolDefinition
	inputSchema    = tools.ToolInputSchema
	toolProperty   = tools.ToolProperty
)

func loadTools(projectRoot string) (toolsCache, error) {
	return tools.Load(projectRoot, collectInternalSkillToolNames(projectRoot))
}

func loadProjectToolCache(projectRoot string) (toolsCache, bool) {
	return tools.LoadProjectCache(projectRoot, collectInternalSkillToolNames(projectRoot))
}

func loadDefaultTools() toolsCache {
	return tools.LoadDefault()
}

func findTool(cache toolsCache, name string) (toolDefinition, bool) {
	return tools.Find(cache, name)
}

func findDefaultTool(name string) (toolDefinition, bool) {
	return findTool(loadDefaultTools(), name)
}

func findToolForCommand(projectRoot string, command string) (toolDefinition, toolsCache, bool, error) {
	return findToolForCommandWithInternalToolNames(projectRoot, command, collectInternalSkillToolNames)
}

func findToolForCommandWithInternalToolNames(
	projectRoot string,
	command string,
	collectInternalToolNames func(string) map[string]bool,
) (toolDefinition, toolsCache, bool, error) {
	defaultCache := loadDefaultTools()
	if tool, ok := findTool(defaultCache, command); ok {
		return tool, defaultCache, true, nil
	}

	return tools.FindForCommand(projectRoot, command, collectInternalToolNames(projectRoot))
}

func buildToolParams(args []string, tool toolDefinition) (map[string]any, string, error) {
	params := map[string]any{}
	projectPath := ""

	for index := 0; index < len(args); index++ {
		arg := args[index]
		if !strings.HasPrefix(arg, "--") {
			return nil, "", &argumentError{
				message:     "Unexpected argument: " + arg,
				received:    arg,
				command:     tool.Name,
				nextActions: []string{"Pass tool inputs as `--option value` pairs."},
			}
		}

		flag, err := parseToolFlag(arg)
		if err != nil {
			return nil, "", err
		}

		if flag.name == projectPathFlagName {
			value, consumedNext, err := flagValue(flag, args, index)
			if err != nil {
				return nil, "", err
			}
			projectPath = value
			if consumedNext {
				index++
			}
			continue
		}

		propertyName, property, negated, ok := findProperty(tool, flag.name)
		if !ok {
			return nil, "", &argumentError{
				message:     "Unknown option for " + tool.Name + ": --" + flag.name,
				option:      "--" + flag.name,
				command:     tool.Name,
				nextActions: []string{"Run `uloop --list-options " + tool.Name + "` to inspect supported options."},
			}
		}

		option := "--" + flag.name
		if isBooleanProperty(property) {
			if flag.hasValue {
				return nil, "", booleanValueArgumentError(option, flag.value)
			}
			if index+1 < len(args) && !isNextOptionToken(args[index+1]) {
				return nil, "", booleanValueArgumentError(option, args[index+1])
			}
			params[propertyName] = !negated
			continue
		}

		value, consumedNext, err := flagValue(flag, args, index)
		if err != nil {
			return nil, "", err
		}
		if consumedNext {
			index++
		}

		converted, err := convertValue(value, property, option)
		if err != nil {
			return nil, "", err
		}
		params[propertyName] = converted
	}

	return params, projectPath, nil
}

type parsedToolFlag struct {
	name     string
	value    string
	hasValue bool
}

func parseToolFlag(arg string) (parsedToolFlag, error) {
	trimmed := strings.TrimPrefix(arg, "--")
	if trimmed == "" {
		return parsedToolFlag{}, &argumentError{
			message:     "Invalid option: " + arg,
			option:      arg,
			nextActions: []string{"Use `--option` for boolean flags or `--option value` for valued options."},
		}
	}

	if strings.Contains(trimmed, "=") {
		parts := strings.SplitN(trimmed, "=", 2)
		if parts[1] == "" {
			return parsedToolFlag{}, missingValueArgumentError("--" + parts[0])
		}
		return parsedToolFlag{name: parts[0], value: parts[1], hasValue: true}, nil
	}

	return parsedToolFlag{name: trimmed}, nil
}

func flagValue(flag parsedToolFlag, args []string, index int) (string, bool, error) {
	if flag.hasValue {
		return flag.value, false, nil
	}

	if index+1 >= len(args) || isNextOptionToken(args[index+1]) {
		return "", false, missingValueArgumentError("--" + flag.name)
	}

	return args[index+1], true, nil
}

func parseGlobalProjectPath(args []string) ([]string, string, error) {
	remaining := make([]string, 0, len(args))
	projectPath := ""

	for index := 0; index < len(args); index++ {
		arg := args[index]
		if arg != "--"+projectPathFlagName && !strings.HasPrefix(arg, "--"+projectPathFlagName+"=") {
			remaining = append(remaining, arg)
			continue
		}

		name, value, consumedNext, err := parseFlagValue(arg, args, index)
		if err != nil {
			return nil, "", err
		}
		if name != projectPathFlagName {
			remaining = append(remaining, arg)
			continue
		}
		projectPath = value
		if consumedNext {
			index++
		}
	}

	return remaining, projectPath, nil
}

func parseFlagValue(arg string, args []string, index int) (string, string, bool, error) {
	trimmed := strings.TrimPrefix(arg, "--")
	if trimmed == "" {
		return "", "", false, &argumentError{
			message:     "Invalid option: " + arg,
			option:      arg,
			nextActions: []string{"Use `--option value` or `--option=value`."},
		}
	}

	if strings.Contains(trimmed, "=") {
		parts := strings.SplitN(trimmed, "=", 2)
		if parts[1] == "" {
			return "", "", false, missingValueArgumentError("--" + parts[0])
		}
		return parts[0], parts[1], false, nil
	}

	if index+1 >= len(args) || isNextOptionToken(args[index+1]) {
		return "", "", false, missingValueArgumentError("--" + trimmed)
	}

	return trimmed, args[index+1], true, nil
}

func isNextOptionToken(value string) bool {
	if !strings.HasPrefix(value, "-") {
		return false
	}
	if _, err := strconv.ParseFloat(value, 64); err == nil {
		return false
	}
	return true
}

func findProperty(tool toolDefinition, kebabName string) (string, toolProperty, bool, bool) {
	schema := tool.EffectiveInputSchema()
	for propertyName, property := range schema.Properties {
		if optionNameForProperty(tool.Name, propertyName, property) == kebabName {
			return propertyName, property, isNegatedBooleanProperty(property), true
		}
	}
	return "", toolProperty{}, false, false
}

func convertValue(value string, property toolProperty, option string) (any, error) {
	switch strings.ToLower(property.Type) {
	case "boolean":
		return convertBooleanValue(value, option)
	case "integer":
		parsed, err := strconv.Atoi(value)
		if err != nil {
			return nil, invalidValueArgumentError(option, value, "integer")
		}
		return parsed, nil
	case "number":
		parsed, err := strconv.ParseFloat(value, 64)
		if err != nil {
			return nil, invalidValueArgumentError(option, value, "number")
		}
		return parsed, nil
	case "array":
		return convertArrayValue(value, option)
	case "object":
		return convertObjectValue(value, option)
	default:
		return value, nil
	}
}

func convertBooleanValue(value string, option string) (bool, error) {
	switch strings.ToLower(value) {
	case "true":
		return true, nil
	case "false":
		return false, nil
	default:
		return false, invalidValueArgumentError(option, value, "boolean")
	}
}

func convertArrayValue(value string, option string) (any, error) {
	if strings.HasPrefix(value, "[") {
		var parsed []any
		if err := json.Unmarshal([]byte(value), &parsed); err != nil {
			return nil, invalidValueArgumentError(option, value, "array")
		}
		return parsed, nil
	}

	parts := strings.Split(value, ",")
	result := make([]string, 0, len(parts))
	for _, part := range parts {
		result = append(result, strings.TrimSpace(part))
	}
	return result, nil
}

func convertObjectValue(value string, option string) (map[string]any, error) {
	var parsed map[string]any
	if err := json.Unmarshal([]byte(value), &parsed); err != nil {
		return nil, invalidValueArgumentError(option, value, "object")
	}
	if parsed == nil {
		return nil, invalidValueArgumentError(option, value, "object")
	}
	return parsed, nil
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

func booleanValueArgumentError(option string, received string) *argumentError {
	return &argumentError{
		message:      "Boolean option does not accept a value: " + received,
		option:       option,
		received:     received,
		expectedType: "flag",
		nextActions:  []string{"Use `" + option + "` without a value."},
	}
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
