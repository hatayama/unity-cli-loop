package cli

import (
	"github.com/hatayama/unity-cli-loop/cli/internal/tools"
)

const (
	cacheDirectoryName = tools.CacheDirectoryName
	cacheFileName      = tools.CacheFileName
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
