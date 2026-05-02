package cli

import (
	"embed"
	"encoding/json"
	"fmt"
	"os"
	"path/filepath"
	"strconv"
	"strings"
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

type toolsCache struct {
	Version       string           `json:"version"`
	ServerVersion string           `json:"serverVersion,omitempty"`
	UpdatedAt     string           `json:"updatedAt,omitempty"`
	Tools         []toolDefinition `json:"tools"`
}

type toolDefinition struct {
	Name        string      `json:"name"`
	Description string      `json:"description"`
	InputSchema inputSchema `json:"inputSchema"`
}

type inputSchema struct {
	Type       string                  `json:"type"`
	Properties map[string]toolProperty `json:"properties"`
	Required   []string                `json:"required,omitempty"`
}

type toolProperty struct {
	Type        string   `json:"type"`
	Description string   `json:"description,omitempty"`
	Default     any      `json:"default,omitempty"`
	Enum        []string `json:"enum,omitempty"`
	Items       *struct {
		Type string `json:"type"`
	} `json:"items,omitempty"`
}

func loadTools(projectRoot string) (toolsCache, error) {
	cachePath := filepath.Join(projectRoot, cacheDirectoryName, cacheFileName)
	if content, err := os.ReadFile(cachePath); err == nil {
		var cache toolsCache
		if json.Unmarshal(content, &cache) == nil {
			return cache, nil
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
	return cache, nil
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

func buildToolParams(args []string, tool toolDefinition) (map[string]any, string, error) {
	params := map[string]any{}
	projectPath := ""

	for index := 0; index < len(args); index++ {
		arg := args[index]
		if !strings.HasPrefix(arg, "--") {
			return nil, "", fmt.Errorf("unexpected argument: %s", arg)
		}

		name, value, consumedNext, err := parseFlagValue(arg, args, index)
		if err != nil {
			return nil, "", err
		}
		if consumedNext {
			index++
		}

		if name == projectPathFlagName {
			projectPath = value
			continue
		}

		propertyName, property, ok := findProperty(tool, name)
		if !ok {
			return nil, "", fmt.Errorf("unknown option for %s: --%s", tool.Name, name)
		}

		converted, err := convertValue(value, property)
		if err != nil {
			return nil, "", err
		}
		params[propertyName] = converted
	}

	return params, projectPath, nil
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
		return "", "", false, fmt.Errorf("invalid option: %s", arg)
	}

	if strings.Contains(trimmed, "=") {
		parts := strings.SplitN(trimmed, "=", 2)
		if parts[1] == "" {
			return "", "", false, fmt.Errorf("--%s requires a value", parts[0])
		}
		return parts[0], parts[1], false, nil
	}

	if index+1 >= len(args) || isNextOptionToken(args[index+1]) {
		return "", "", false, fmt.Errorf("--%s requires a value", trimmed)
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

func findProperty(tool toolDefinition, kebabName string) (string, toolProperty, bool) {
	for propertyName, property := range tool.InputSchema.Properties {
		if pascalToKebab(propertyName) == kebabName {
			return propertyName, property, true
		}
	}
	return "", toolProperty{}, false
}

func convertValue(value string, property toolProperty) (any, error) {
	switch strings.ToLower(property.Type) {
	case "boolean":
		switch strings.ToLower(value) {
		case "true":
			return true, nil
		case "false":
			return false, nil
		default:
			return nil, fmt.Errorf("invalid boolean value: %s", value)
		}
	case "integer":
		parsed, err := strconv.Atoi(value)
		if err != nil {
			return nil, fmt.Errorf("invalid integer value: %s", value)
		}
		return parsed, nil
	case "number":
		parsed, err := strconv.ParseFloat(value, 64)
		if err != nil {
			return nil, fmt.Errorf("invalid number value: %s", value)
		}
		return parsed, nil
	case "array":
		if strings.HasPrefix(value, "[") {
			var parsed []any
			if err := json.Unmarshal([]byte(value), &parsed); err != nil {
				return nil, fmt.Errorf("invalid array value: %s", value)
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
			return nil, fmt.Errorf("invalid object value: %s", value)
		}
		return parsed, nil
	default:
		return value, nil
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
