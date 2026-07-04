package tools

import (
	"embed"
	"encoding/json"
	"os"
	"path/filepath"
)

//go:embed default-tools.json
var embeddedTools embed.FS

const (
	CacheDirectoryName = ".uloop"
	CacheFileName      = "tools.json"
	defaultToolsFile   = "default-tools.json"
)

func Load(projectRoot string, internalToolNames map[string]bool) (ToolCatalog, error) {
	if cache, ok := LoadProjectCache(projectRoot, internalToolNames); ok {
		return cache, nil
	}

	content, err := embeddedTools.ReadFile(defaultToolsFile)
	if err != nil {
		return ToolCatalog{}, err
	}

	var cache ToolCatalog
	if err := json.Unmarshal(content, &cache); err != nil {
		return ToolCatalog{}, err
	}
	return FilterInternalTools(cache, internalToolNames), nil
}

func LoadProjectCache(projectRoot string, internalToolNames map[string]bool) (ToolCatalog, bool) {
	cachePath := filepath.Join(projectRoot, CacheDirectoryName, CacheFileName)
	content, err := os.ReadFile(cachePath)
	if err != nil {
		return ToolCatalog{}, false
	}

	var cache ToolCatalog
	if json.Unmarshal(content, &cache) != nil {
		return ToolCatalog{}, false
	}
	return FilterInternalTools(cache, internalToolNames), true
}

func LoadDefault() ToolCatalog {
	content, err := embeddedTools.ReadFile(defaultToolsFile)
	if err != nil {
		return ToolCatalog{}
	}

	var cache ToolCatalog
	if json.Unmarshal(content, &cache) != nil {
		return ToolCatalog{}
	}
	return cache
}

func Find(cache ToolCatalog, name string) (ToolDefinition, bool) {
	for _, tool := range cache.Tools {
		if tool.Name == name {
			return tool, true
		}
	}
	return ToolDefinition{}, false
}

func FindForCommand(
	projectRoot string,
	command string,
	internalToolNames map[string]bool,
	preferEmbedded bool,
) (ToolDefinition, ToolCatalog, bool, error) {
	if preferEmbedded {
		cache := LoadDefault()
		tool, ok := Find(cache, command)
		return tool, cache, ok, nil
	}

	cache, err := Load(projectRoot, internalToolNames)
	if err != nil {
		return ToolDefinition{}, ToolCatalog{}, false, err
	}

	tool, ok := Find(cache, command)
	return tool, cache, ok, nil
}

func FilterInternalTools(cache ToolCatalog, internalToolNames map[string]bool) ToolCatalog {
	if len(internalToolNames) == 0 {
		return cache
	}

	filteredTools := make([]ToolDefinition, 0, len(cache.Tools))
	for _, tool := range cache.Tools {
		if internalToolNames[tool.Name] {
			continue
		}
		filteredTools = append(filteredTools, tool)
	}
	cache.Tools = filteredTools
	return cache
}
