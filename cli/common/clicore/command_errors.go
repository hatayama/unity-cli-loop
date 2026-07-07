package clicore

import clierrors "github.com/hatayama/unity-cli-loop/common/errors"

func UnknownCommandError(command string, cache ToolsCache, context clierrors.ErrorContext) clierrors.CLIError {
	return clierrors.UnknownCommandError(command, availableCommandNames(cache), context)
}

func availableCommandNames(cache ToolsCache) []string {
	seen := map[string]bool{}
	names := []string{}
	for _, name := range NativeCommandNamesForCompletion() {
		seen[name] = true
		names = append(names, name)
	}
	for _, tool := range cache.Tools {
		if seen[tool.Name] {
			continue
		}
		seen[tool.Name] = true
		names = append(names, tool.Name)
	}
	return names
}
