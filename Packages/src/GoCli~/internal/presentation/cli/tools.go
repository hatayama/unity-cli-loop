package cli

import (
	"embed"
	"encoding/json"
	"os"
	"path/filepath"
	"strconv"
	"strings"

	"github.com/hatayama/unity-cli-loop/Packages/src/GoCli/internal/domain"
)

//go:embed default-tools.json
var embeddedTools embed.FS

const (
	version             = "3.0.0-beta.0"
	cacheDirectoryName  = ".uloop"
	cacheFileName       = "tools.json"
	defaultToolsFile    = "default-tools.json"
	projectPathFlagName = "project-path"
)

type (
	toolsCache     = domain.ToolCatalog
	toolDefinition = domain.ToolDefinition
	inputSchema    = domain.ToolInputSchema
	toolProperty   = domain.ToolProperty
)

func loadTools(projectRoot string) (toolsCache, error) {
	cachePath := filepath.Join(projectRoot, cacheDirectoryName, cacheFileName)
	if content, err := os.ReadFile(cachePath); err == nil {
		var cache toolsCache
		if json.Unmarshal(content, &cache) == nil {
			return filterInternalSkillTools(projectRoot, cache), nil
		}
	}

	content, err := embeddedTools.ReadFile(defaultToolsFile)
	if err != nil {
		return toolsCache{}, err
	}

	var cache toolsCache
	if err := json.Unmarshal(content, &cache); err != nil {
		return toolsCache{}, err
	}
	return filterInternalSkillTools(projectRoot, cache), nil
}

func loadCachedTools(projectRoot string) (toolsCache, bool) {
	cachePath := filepath.Join(projectRoot, cacheDirectoryName, cacheFileName)
	content, err := os.ReadFile(cachePath)
	if err != nil {
		return toolsCache{}, false
	}

	var cache toolsCache
	if json.Unmarshal(content, &cache) != nil {
		return toolsCache{}, false
	}
	return filterInternalSkillTools(projectRoot, cache), true
}

func loadDefaultTools() toolsCache {
	content, err := embeddedTools.ReadFile(defaultToolsFile)
	if err != nil {
		return toolsCache{}
	}

	var cache toolsCache
	if json.Unmarshal(content, &cache) != nil {
		return toolsCache{}
	}
	return cache
}

func findTool(cache toolsCache, name string) (toolDefinition, bool) {
	for _, tool := range cache.Tools {
		if tool.Name == name {
			return tool, true
		}
	}
	return toolDefinition{}, false
}

func filterInternalSkillTools(projectRoot string, cache toolsCache) toolsCache {
	internalToolNames := collectInternalSkillToolNames(projectRoot)
	if len(internalToolNames) == 0 {
		return cache
	}

	filteredTools := make([]toolDefinition, 0, len(cache.Tools))
	for _, tool := range cache.Tools {
		if internalToolNames[tool.Name] {
			continue
		}
		filteredTools = append(filteredTools, tool)
	}
	cache.Tools = filteredTools
	return cache
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
				nextActions: []string{"Run `uloop completion --list-options " + tool.Name + "` to inspect supported options."},
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
		if !strings.HasPrefix(arg, "--"+projectPathFlagName) {
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
		if optionNameForProperty(propertyName, property) == kebabName {
			return propertyName, property, isNegatedBooleanProperty(property), true
		}
	}
	return "", toolProperty{}, false, false
}

func convertValue(value string, property toolProperty, option string) (any, error) {
	switch strings.ToLower(property.Type) {
	case "boolean":
		switch strings.ToLower(value) {
		case "true":
			return true, nil
		case "false":
			return false, nil
		default:
			return nil, invalidValueArgumentError(option, value, "boolean")
		}
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
	case "object":
		var parsed map[string]any
		if err := json.Unmarshal([]byte(value), &parsed); err != nil {
			return nil, invalidValueArgumentError(option, value, "object")
		}
		return parsed, nil
	default:
		return value, nil
	}
}

func optionNameForProperty(propertyName string, property toolProperty) string {
	kebabName := pascalToKebab(propertyName)
	if isNegatedBooleanProperty(property) {
		return "no-" + kebabName
	}
	return kebabName
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
