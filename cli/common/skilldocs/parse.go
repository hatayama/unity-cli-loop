package skilldocs

import (
	"strings"

	"github.com/hatayama/unity-cli-loop/common/skillscan"
)

const (
	parametersSectionHeading = "## Parameters"
	skillNamePrefix          = "uloop-"

	sectionHeadingPrefix    = "## "
	subsectionHeadingPrefix = "### "
	byteOrderMark           = "\uFEFF"
)

// standardParameterTableCells is the header every parameter table in this repository uses. Tables
// with any other header (action matrices, comparison tables) are prose and must not be read as
// parameter documentation.
var standardParameterTableCells = []string{"Parameter", "Type", "Default", "Description"}

// ParseSkill reads one SKILL.md body. Two layouts exist and both are supported:
//
//	(i)  one skill per tool - frontmatter toolName/description plus a single parameter table
//	     anywhere in the file (first-party tool skills put it under "### Parameters").
//	(ii) one skill covering several commands (pause-point) - a "## Parameters" section whose
//	     "### <tool-name>" subsections each carry a description line and their own table.
//
// A file that documents no parameters still yields its tool description, which is why the result is
// keyed by tool name rather than returned only when a table was found.
func ParseSkill(content string) map[string]ToolDocs {
	lines := normalizedLines(content)
	sectionLines, ok := parametersSectionLines(lines)
	if ok && hasSubsectionHeading(sectionLines) {
		return parseMultiToolSkill(sectionLines)
	}
	return parseSingleToolSkill(lines)
}

// normalizedLines makes the parser indifferent to how the checkout wrote the file. A Windows
// checkout produces CRLF and some editors prepend a BOM; neither may change what help prints.
func normalizedLines(content string) []string {
	content = strings.TrimPrefix(content, byteOrderMark)
	content = strings.ReplaceAll(content, "\r\n", "\n")
	content = strings.ReplaceAll(content, "\r", "\n")
	return strings.Split(content, "\n")
}

// parseSingleToolSkill reads the frontmatter from the same normalized lines the table is read from, so
// line endings and a BOM are handled in exactly one place rather than once per consumer.
func parseSingleToolSkill(lines []string) map[string]ToolDocs {
	frontmatter := skillscan.ParseSkillFrontmatter(strings.Join(lines, "\n"))
	toolName := singleSkillToolName(frontmatter)
	if toolName == "" {
		return map[string]ToolDocs{}
	}

	docs := ToolDocs{
		ToolDescription:   frontmatter["description"],
		ParamDescriptions: map[string]string{},
	}
	if headerIndex, ok := findParameterTableHeader(lines, 0); ok {
		docs.ParamDescriptions = parseParameterTable(lines, headerIndex)
	}
	return map[string]ToolDocs{toolName: docs}
}

// singleSkillToolName resolves the tool a one-tool skill documents. toolName is authoritative when
// present; otherwise the skill name carries it, since every skill in this package is named
// "uloop-<tool-name>" (focus-window's skill declares no toolName).
func singleSkillToolName(frontmatter map[string]string) string {
	if toolName := frontmatter["toolName"]; toolName != "" {
		return toolName
	}
	name := frontmatter["name"]
	if !strings.HasPrefix(name, skillNamePrefix) {
		return ""
	}
	return strings.TrimPrefix(name, skillNamePrefix)
}

func parseMultiToolSkill(sectionLines []string) map[string]ToolDocs {
	result := map[string]ToolDocs{}
	for index := 0; index < len(sectionLines); index++ {
		line := strings.TrimSpace(sectionLines[index])
		if !strings.HasPrefix(line, subsectionHeadingPrefix) {
			continue
		}

		toolName := strings.TrimSpace(strings.TrimPrefix(line, subsectionHeadingPrefix))
		if toolName == "" {
			continue
		}
		blockLines := subsectionLines(sectionLines, index)
		docs := ToolDocs{
			ToolDescription:   firstProseLine(blockLines),
			ParamDescriptions: map[string]string{},
		}
		if headerIndex, ok := findParameterTableHeader(blockLines, 0); ok {
			docs.ParamDescriptions = parseParameterTable(blockLines, headerIndex)
		}
		result[toolName] = docs
	}
	return result
}

// parametersSectionLines returns the body of the "## Parameters" section, which is where a
// multi-tool skill keeps its per-command subsections.
func parametersSectionLines(lines []string) ([]string, bool) {
	for index, line := range lines {
		if strings.TrimSpace(line) != parametersSectionHeading {
			continue
		}
		for end := index + 1; end < len(lines); end++ {
			if strings.HasPrefix(strings.TrimSpace(lines[end]), sectionHeadingPrefix) {
				return lines[index+1 : end], true
			}
		}
		return lines[index+1:], true
	}
	return nil, false
}

func hasSubsectionHeading(lines []string) bool {
	for _, line := range lines {
		if strings.HasPrefix(strings.TrimSpace(line), subsectionHeadingPrefix) {
			return true
		}
	}
	return false
}

func subsectionLines(sectionLines []string, headingIndex int) []string {
	for end := headingIndex + 1; end < len(sectionLines); end++ {
		if strings.HasPrefix(strings.TrimSpace(sectionLines[end]), subsectionHeadingPrefix) {
			return sectionLines[headingIndex+1 : end]
		}
	}
	return sectionLines[headingIndex+1:]
}

// firstProseLine is the tool description in a multi-tool skill: the first non-empty line under the
// tool's heading that is not part of a table.
func firstProseLine(lines []string) string {
	for _, line := range lines {
		trimmed := strings.TrimSpace(line)
		if trimmed == "" || strings.HasPrefix(trimmed, "|") {
			continue
		}
		return trimmed
	}
	return ""
}

func findParameterTableHeader(lines []string, startIndex int) (int, bool) {
	for index := startIndex; index < len(lines); index++ {
		if isStandardParameterTableHeader(lines[index]) {
			return index, true
		}
	}
	return 0, false
}

func isStandardParameterTableHeader(line string) bool {
	if !strings.HasPrefix(strings.TrimSpace(line), "|") {
		return false
	}
	cells := splitTableRow(line)
	if len(cells) != len(standardParameterTableCells) {
		return false
	}
	for index, expected := range standardParameterTableCells {
		if cells[index] != expected {
			return false
		}
	}
	return true
}

// parseParameterTable reads the rows under a standard header into option name -> description.
func parseParameterTable(lines []string, headerIndex int) map[string]string {
	descriptions := map[string]string{}
	index := headerIndex + 1
	// The separator row (|---|---|) carries no data; a table without one is malformed, and reading
	// it as a row would register a parameter named "---".
	if index < len(lines) && isTableSeparatorRow(lines[index]) {
		index++
	}

	for ; index < len(lines); index++ {
		if !strings.HasPrefix(strings.TrimSpace(lines[index]), "|") {
			break
		}
		cells := splitTableRow(lines[index])
		if len(cells) < len(standardParameterTableCells) {
			continue
		}
		optionName := optionNameFromCell(cells[0])
		description := NormalizeCellText(cells[len(standardParameterTableCells)-1])
		if optionName == "" || description == "" {
			continue
		}
		descriptions[optionName] = description
	}
	return descriptions
}

func isTableSeparatorRow(line string) bool {
	trimmed := strings.TrimSpace(line)
	if !strings.HasPrefix(trimmed, "|") {
		return false
	}
	return strings.Trim(trimmed, "|-: \t") == ""
}

// optionNameFromCell turns a first-column cell such as "`--max-history`" into "max-history", the
// form tooldocs.OptionNameForProperty produces for a schema property.
func optionNameFromCell(cell string) string {
	name := NormalizeCellText(cell)
	if fields := strings.Fields(name); len(fields) > 0 {
		name = fields[0]
	}
	return strings.TrimPrefix(name, "--")
}

// NormalizeCellText turns one raw table cell into the plain text CLI surfaces print. It is the only
// Markdown-to-text step in this package, and every consumer of a skill table - this renderer, the
// catalog generator, and the CI drift check - must run cells through it so all three compare and
// emit exactly the same string.
//
// Two conversions happen, and deliberately no more:
//   - "\|" becomes "|", the escape a table needs for enum alternations such as "Press|KeyDown".
//   - code-span backticks are dropped, because "`Press`, `KeyDown`" reads as noise in terminal help
//     and in list JSON alike.
//
// No other Markdown is interpreted. If bold or a link ever appears in a cell it shows up verbatim in
// help, which is a visible signal to fix the table rather than a reason to grow a Markdown renderer.
func NormalizeCellText(cell string) string {
	text := strings.ReplaceAll(cell, `\|`, "|")
	return strings.TrimSpace(strings.ReplaceAll(text, "`", ""))
}

// splitTableRow splits a Markdown table row on unescaped pipes, leaving each cell's text otherwise
// untouched for NormalizeCellText to convert. Descriptions legitimately contain "|" (enum
// alternations such as "Press|KeyDown"), which the table escapes as "\|"; splitting naively would
// truncate those cells and shift every later column.
func splitTableRow(line string) []string {
	trimmed := strings.TrimSpace(line)
	trimmed = strings.TrimPrefix(trimmed, "|")
	trimmed = strings.TrimSuffix(trimmed, "|")

	cells := []string{}
	current := strings.Builder{}
	escaped := false
	for _, char := range trimmed {
		if escaped {
			// The escape sequence is kept intact; only NormalizeCellText resolves it, so the split
			// stays a purely structural step.
			current.WriteRune('\\')
			current.WriteRune(char)
			escaped = false
			continue
		}
		switch char {
		case '\\':
			escaped = true
		case '|':
			cells = append(cells, strings.TrimSpace(current.String()))
			current.Reset()
		default:
			current.WriteRune(char)
		}
	}
	if escaped {
		current.WriteRune('\\')
	}
	return append(cells, strings.TrimSpace(current.String()))
}
