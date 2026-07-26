package skilldocs

import (
	"github.com/hatayama/unity-cli-loop/common/tooldocs"
	"github.com/hatayama/unity-cli-loop/common/tools"
)

// ApplyToCatalog overlays the installed package's skill documentation onto a tool catalog. The skill
// wins over every other source: it is the text a human reviewed and an agent reads, while the schema
// carries generated placeholders and the embedded catalog is a snapshot of an older generation.
// Tools with no skill (project-local custom commands) pass through untouched.
func ApplyToCatalog(catalog tools.ToolCatalog, projectRoot string) tools.ToolCatalog {
	docs := Load(projectRoot)
	if len(docs) == 0 {
		return catalog
	}

	for index, tool := range catalog.Tools {
		catalog.Tools[index] = applyToolDocs(tool, docs)
	}
	return catalog
}

// ApplyToTool is ApplyToCatalog for the single-command help path.
func ApplyToTool(tool tools.ToolDefinition, projectRoot string) tools.ToolDefinition {
	docs := Load(projectRoot)
	if len(docs) == 0 {
		return tool
	}
	return applyToolDocs(tool, docs)
}

func applyToolDocs(tool tools.ToolDefinition, docs map[string]ToolDocs) tools.ToolDefinition {
	toolDocs, ok := docs[tool.Name]
	if !ok {
		return tool
	}

	if toolDocs.ToolDescription != "" {
		tool.Description = toolDocs.ToolDescription
	}

	// Properties are matched by their CLI option name, produced by the one kebab-conversion in the
	// codebase. Writing the inverse conversion here would be a second rule to keep in step.
	schema := tool.EffectiveInputSchema()
	for propertyName, property := range schema.Properties {
		optionName := tooldocs.OptionNameForProperty(tool.Name, propertyName, property)
		description, ok := toolDocs.ParamDescriptions[optionName]
		if !ok || description == "" {
			continue
		}
		property.Description = description
		property.SkillSourcedDescription = true
		schema.Properties[propertyName] = property
	}
	return tool
}
