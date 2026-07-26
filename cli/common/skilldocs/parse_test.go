package skilldocs

import (
	"strings"
	"testing"
)

const singleToolSkill = `---
name: uloop-simulate-keyboard
toolName: simulate-keyboard
description: "Simulate keyboard input in PlayMode."
---

# Task

## Actions

| Action | Behavior | Use Case |
|--------|----------|----------|
| ` + "`Press`" + ` | KeyDown then KeyUp | One-shot tap |

## Tool Reference

### Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| ` + "`--action`" + ` | enum | ` + "`Press`" + ` | Press \| KeyDown \| KeyUp |
| ` + "`--duration`" + ` | number | ` + "`0`" + ` | Hold duration in seconds. |
| ` + "`--ignored`" + ` | string | - |  |

## Notes

Prose after the table.
`

const multiToolSkill = `---
name: uloop-pause-point
description: "Pauses Unity playback at any source file:line."
---

# uloop await-pause-point

## Parameters

CLI-only flags are described in the sections above.

### enable-pause-point

Enable a pause point so Unity pauses when that code path is reached

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| ` + "`--id`" + ` | string | - | Named pause point id |
| ` + "`--max-history`" + ` | integer | ` + "`20`" + ` | Maximum number of captured hit frames to retain (1-100) |

### clear-watch

Clear one or all registered C# watch expressions

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| ` + "`--all`" + ` | flag | - | Clear every registered watch expression |

## Capture Modes and History

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| ` + "`--not-a-parameter`" + ` | string | - | This table is outside the Parameters section |
`

// Verifies a one-tool skill yields its frontmatter description plus the rows of its parameter table,
// and that tables with other headers are not read as parameter documentation.
func TestParseSkillReadsASingleToolSkill(t *testing.T) {
	docs := ParseSkill(singleToolSkill)

	if len(docs) != 1 {
		t.Fatalf("expected exactly one documented tool, got %v", docs)
	}
	toolDocs, ok := docs["simulate-keyboard"]
	if !ok {
		t.Fatalf("simulate-keyboard is missing: %v", docs)
	}
	if toolDocs.ToolDescription != "Simulate keyboard input in PlayMode." {
		t.Errorf("tool description not taken from frontmatter: %q", toolDocs.ToolDescription)
	}
	if got := toolDocs.ParamDescriptions["duration"]; got != "Hold duration in seconds." {
		t.Errorf("--duration description: %q", got)
	}
	if _, ok := toolDocs.ParamDescriptions["Press"]; ok {
		t.Error("the Actions table must not be read as parameter documentation")
	}
	if _, ok := toolDocs.ParamDescriptions["ignored"]; ok {
		t.Error("an empty description cell must not register a parameter")
	}
}

// Verifies an escaped pipe inside a description survives as a literal pipe instead of truncating the
// cell and shifting every later column.
func TestParseSkillUnescapesPipesInDescriptions(t *testing.T) {
	docs := ParseSkill(singleToolSkill)

	if got := docs["simulate-keyboard"].ParamDescriptions["action"]; got != "Press | KeyDown | KeyUp" {
		t.Errorf("escaped pipes were not restored: %q", got)
	}
}

// Verifies a skill covering several commands documents each one from its own subsection, and that a
// parameter table outside the Parameters section is ignored.
func TestParseSkillReadsAMultiToolSkill(t *testing.T) {
	docs := ParseSkill(multiToolSkill)

	if len(docs) != 2 {
		t.Fatalf("expected the two documented commands, got %v", docs)
	}
	enable, ok := docs["enable-pause-point"]
	if !ok {
		t.Fatalf("enable-pause-point is missing: %v", docs)
	}
	if enable.ToolDescription != "Enable a pause point so Unity pauses when that code path is reached" {
		t.Errorf("tool description not taken from the line under the heading: %q", enable.ToolDescription)
	}
	if got := enable.ParamDescriptions["max-history"]; got != "Maximum number of captured hit frames to retain (1-100)" {
		t.Errorf("--max-history description: %q", got)
	}
	if got := docs["clear-watch"].ParamDescriptions["all"]; got != "Clear every registered watch expression" {
		t.Errorf("--all description: %q", got)
	}
	if _, ok := docs["pause-point"]; ok {
		t.Error("a multi-tool skill must not register its own skill name as a tool")
	}
}

// Verifies a CRLF checkout parses identically to an LF one, since Windows checkouts rewrite line
// endings and help text must not depend on which platform read the file.
func TestParseSkillToleratesCRLFLineEndings(t *testing.T) {
	crlf := strings.ReplaceAll(singleToolSkill, "\n", "\r\n")

	if got := ParseSkill(crlf); got["simulate-keyboard"].ParamDescriptions["duration"] != "Hold duration in seconds." {
		t.Errorf("CRLF content parsed differently: %v", got)
	}
	crlfMultiTool := strings.ReplaceAll(multiToolSkill, "\n", "\r\n")
	if got := ParseSkill(crlfMultiTool); len(got) != 2 {
		t.Errorf("CRLF multi-tool content parsed differently: %v", got)
	}
}

// Verifies a leading byte order mark does not hide the frontmatter, which would otherwise drop the
// tool name and silently document nothing.
func TestParseSkillToleratesAByteOrderMark(t *testing.T) {
	docs := ParseSkill(byteOrderMark + singleToolSkill)

	if _, ok := docs["simulate-keyboard"]; !ok {
		t.Errorf("a BOM must not hide the frontmatter: %v", docs)
	}
}

// Verifies a skill with no parameter table still reports its tool description, which is the only
// documentation a tool with no parameters has.
func TestParseSkillKeepsTheDescriptionOfATablelessSkill(t *testing.T) {
	docs := ParseSkill(`---
name: uloop-focus-window
description: "Bring the Unity Editor window to front."
---

# uloop focus-window
`)

	toolDocs, ok := docs["focus-window"]
	if !ok {
		t.Fatalf("focus-window is missing: %v", docs)
	}
	if toolDocs.ToolDescription != "Bring the Unity Editor window to front." {
		t.Errorf("tool description: %q", toolDocs.ToolDescription)
	}
	if len(toolDocs.ParamDescriptions) != 0 {
		t.Errorf("a skill with no table documents no parameters: %v", toolDocs.ParamDescriptions)
	}
}

// Verifies a skill whose frontmatter names no tool documents nothing rather than guessing a name.
func TestParseSkillIgnoresASkillWithNoToolName(t *testing.T) {
	docs := ParseSkill(`---
name: some-unrelated-skill
description: "Not a uloop tool skill."
---

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| ` + "`--flag`" + ` | flag | - | Should not be registered |
`)

	if len(docs) != 0 {
		t.Errorf("expected no documented tool, got %v", docs)
	}
}
