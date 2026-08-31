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

	cache, err := decodeEmbeddedCatalog()
	if err != nil {
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
	// Unity's generator produces placeholder descriptions, so a synced cache alone would strip every
	// option's help text; the embedded catalog fills those gaps in.
	return ApplyEmbeddedDescriptionFallback(FilterInternalTools(cache, internalToolNames)), true
}

func LoadDefault() ToolCatalog {
	cache, err := decodeEmbeddedCatalog()
	if err != nil {
		return ToolCatalog{}
	}
	return cache
}

// decodeEmbeddedCatalog reads the catalog compiled into this binary. Its description text is
// generated from the package's skill parameter tables, so every property it carries is marked as
// skill-sourced and renders verbatim.
func decodeEmbeddedCatalog() (ToolCatalog, error) {
	content, err := embeddedTools.ReadFile(defaultToolsFile)
	if err != nil {
		return ToolCatalog{}, err
	}

	cache := ToolCatalog{}
	if err := json.Unmarshal(content, &cache); err != nil {
		return ToolCatalog{}, err
	}
	for _, tool := range cache.Tools {
		schema := tool.EffectiveInputSchema()
		for propertyName, property := range schema.Properties {
			property.SkillSourcedDescription = true
			schema.Properties[propertyName] = property
		}
	}
	return cache, nil
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
