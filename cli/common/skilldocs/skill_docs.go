// Package skilldocs reads the parameter tables out of the package's own SKILL.md files and uses
// them as the source of truth for help text. Descriptions used to live in three hand-maintained
// places (the skill prose, Unity's schema attributes, and the catalog embedded in this binary), so
// editing one left the others stale. Reading the skill at render time removes that drift for good:
// the file an agent reads and the help an agent runs cannot disagree.
//
// This package intentionally depends only on tools, tooldocs, skillscan and vibelog. clicore
// already imports tools and tooldocs, so importing it here would create an import cycle.
package skilldocs

// ToolDocs is what one skill file says about one tool.
type ToolDocs struct {
	// ToolDescription is the tool's own summary line (frontmatter description, or the line under
	// the tool's heading in a multi-tool skill). Empty when the skill states none.
	ToolDescription string
	// ParamDescriptions is keyed by CLI option name without the leading "--", which is what
	// tooldocs.OptionNameForProperty produces for a schema property.
	ParamDescriptions map[string]string
}
