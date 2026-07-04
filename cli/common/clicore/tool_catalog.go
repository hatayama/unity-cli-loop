package clicore

import (
	"github.com/hatayama/unity-cli-loop/common/skillscan"
	"github.com/hatayama/unity-cli-loop/common/tools"
)

const (
	CacheDirectoryName = tools.CacheDirectoryName
	CacheFileName      = tools.CacheFileName
)

type (
	ToolsCache     = tools.ToolCatalog
	ToolDefinition = tools.ToolDefinition
	InputSchema    = tools.ToolInputSchema
	ToolProperty   = tools.ToolProperty
)

func LoadTools(projectRoot string) (ToolsCache, error) {
	return tools.Load(projectRoot, skillscan.CollectInternalSkillToolNames(projectRoot))
}

func LoadProjectToolCache(projectRoot string) (ToolsCache, bool) {
	return tools.LoadProjectCache(projectRoot, skillscan.CollectInternalSkillToolNames(projectRoot))
}

func LoadDefaultTools() ToolsCache {
	return tools.LoadDefault()
}

func FindTool(cache ToolsCache, name string) (ToolDefinition, bool) {
	return tools.Find(cache, name)
}

func FindDefaultTool(name string) (ToolDefinition, bool) {
	return FindTool(LoadDefaultTools(), name)
}

func FindToolForCommand(projectRoot string, command string) (ToolDefinition, ToolsCache, bool, error) {
	return findToolForCommandWithInternalToolNames(
		projectRoot,
		command,
		shouldPreferEmbeddedToolDefinition(command),
		skillscan.CollectInternalSkillToolNames)
}

func findToolForCommandWithInternalToolNames(
	projectRoot string,
	command string,
	preferEmbedded bool,
	collectInternalToolNames func(string) map[string]bool,
) (ToolDefinition, ToolsCache, bool, error) {
	defaultCache := LoadDefaultTools()
	if tool, ok := FindTool(defaultCache, command); ok {
		return tool, defaultCache, true, nil
	}

	return tools.FindForCommand(projectRoot, command, collectInternalToolNames(projectRoot), preferEmbedded)
}

func shouldPreferEmbeddedToolDefinition(command string) bool {
	return command == ExecuteDynamicCodeCommandName
}
