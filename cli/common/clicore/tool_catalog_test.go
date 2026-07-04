package clicore

import (
	"path/filepath"
	"slices"
	"strings"
	"testing"

	"github.com/hatayama/unity-cli-loop/common/clitest"
	"github.com/hatayama/unity-cli-loop/common/skillscan"
)

// Tests that run-tests-specific help text does not leak into unrelated tool schemas.
func TestVisibleOptionHelpEntriesKeepsGenericSaveBeforeRunHelpForOtherTools(t *testing.T) {
	tool := ToolDefinition{
		Name: "sample-tool",
		InputSchema: InputSchema{
			Properties: map[string]ToolProperty{
				"SaveBeforeRun": {Type: "boolean", Default: true},
			},
		},
	}

	entries := VisibleOptionHelpEntriesForTool(tool)

	if len(entries) != 1 {
		t.Fatalf("entry count mismatch: %#v", entries)
	}
	if entries[0].Name != "--no-save-before-run" {
		t.Fatalf("option name mismatch: %#v", entries[0].Name)
	}
	if entries[0].Description != "Disable save before run; default: enabled" {
		t.Fatalf("description mismatch: %#v", entries[0].Description)
	}
}

// Tests that execute-dynamic-code lists CLI-side file input without exposing internal flags.
func TestVisibleOptionNamesIncludesExecuteDynamicCodeCodeFile(t *testing.T) {
	tool, ok := FindTool(LoadDefaultTools(), ExecuteDynamicCodeCommandName)
	if !ok {
		t.Fatal("execute-dynamic-code was not found in default tools")
	}

	options := VisibleOptionNamesForTool(tool)

	if !slices.Contains(options, "--code") {
		t.Fatalf("execute-dynamic-code code option was not listed: %#v", options)
	}
	if !slices.Contains(options, DynamicCodeFileOptionName) {
		t.Fatalf("execute-dynamic-code code-file option was not listed: %#v", options)
	}
	if slices.Contains(options, "--compile-only") {
		t.Fatalf("execute-dynamic-code internal compile-only option should stay hidden: %#v", options)
	}
}

// Tests that execute-dynamic-code help describes CLI-side file input.
func TestVisibleOptionHelpEntriesIncludesExecuteDynamicCodeCodeFile(t *testing.T) {
	tool, ok := FindTool(LoadDefaultTools(), ExecuteDynamicCodeCommandName)
	if !ok {
		t.Fatal("execute-dynamic-code was not found in default tools")
	}

	entries := VisibleOptionHelpEntriesForTool(tool)

	found := false
	for _, entry := range entries {
		if entry.Name != DynamicCodeFileOptionName {
			continue
		}
		found = true
		if entry.Usage != DynamicCodeFileOptionUsage {
			t.Fatalf("code-file usage mismatch: %#v", entry)
		}
		if !strings.Contains(entry.Description, "Read C# code from a file") {
			t.Fatalf("code-file description mismatch: %#v", entry)
		}
	}
	if !found {
		t.Fatalf("execute-dynamic-code code-file help was not listed: %#v", entries)
	}
}

// Tests that cached tool loading hides tools whose source skills are internal.
func TestLoadToolsFiltersInternalSkillToolsFromCache(t *testing.T) {
	projectRoot := t.TempDir()
	writeTestSkill(t, projectRoot, "Assets/Editor/InternalTool/Skill", `---
name: uloop-internal-tool
internal: true
---

# internal
`)
	writeToolCache(t, projectRoot, `{
  "version": "test",
  "tools": [
    {
      "name": "internal-tool",
      "description": "internal",
      "inputSchema": {"type": "object", "properties": {}}
    },
    {
      "name": "public-tool",
      "description": "public",
      "inputSchema": {"type": "object", "properties": {}}
    }
  ]
}`)

	cache, err := LoadTools(projectRoot)
	if err != nil {
		t.Fatalf("loadTools failed: %v", err)
	}

	if _, ok := FindTool(cache, "internal-tool"); ok {
		t.Fatalf("internal tool was not filtered: %#v", cache.Tools)
	}
	if _, ok := FindTool(cache, "public-tool"); !ok {
		t.Fatalf("public tool was filtered: %#v", cache.Tools)
	}
}

func TestLoadToolsFiltersSingleQuotedInternalSkillToolsFromCache(t *testing.T) {
	// Verifies YAML single quotes do not expose internal skill tools from cache-backed help.
	projectRoot := t.TempDir()
	writeTestSkill(t, projectRoot, "Assets/Editor/InternalTool/Skill", `---
name: uloop-internal-tool
internal: 'true'
---

# internal
`)
	writeToolCache(t, projectRoot, `{
  "version": "test",
  "tools": [
    {
      "name": "internal-tool",
      "description": "internal",
      "inputSchema": {"type": "object", "properties": {}}
    },
    {
      "name": "public-tool",
      "description": "public",
      "inputSchema": {"type": "object", "properties": {}}
    }
  ]
}`)

	cache, err := LoadTools(projectRoot)
	if err != nil {
		t.Fatalf("loadTools failed: %v", err)
	}

	if _, ok := FindTool(cache, "internal-tool"); ok {
		t.Fatalf("single-quoted internal tool was not filtered: %#v", cache.Tools)
	}
	if _, ok := FindTool(cache, "public-tool"); !ok {
		t.Fatalf("public tool was filtered: %#v", cache.Tools)
	}
}

func TestFindToolForCommandUsesEmbeddedExecuteDynamicCodeDefinition(t *testing.T) {
	// Verifies that execute-dynamic-code avoids project tool-cache loading on the hot path.
	projectRoot := t.TempDir()
	writeToolCache(t, projectRoot, `{
  "version": "test",
  "tools": []
}`)

	tool, _, ok, err := FindToolForCommand(projectRoot, ExecuteDynamicCodeCommandName)
	if err != nil {
		t.Fatalf("findToolForCommand failed: %v", err)
	}

	if !ok {
		t.Fatal("execute-dynamic-code was not loaded from embedded definitions")
	}
	if tool.Name != ExecuteDynamicCodeCommandName {
		t.Fatalf("tool name mismatch: %s", tool.Name)
	}
}

func TestFindToolForCommandSkipsInternalSkillScanForExecuteDynamicCode(t *testing.T) {
	// Verifies that execute-dynamic-code does not scan project skills before using the embedded hot-path definition.
	collectorCalled := false

	tool, _, ok, err := findToolForCommandWithInternalToolNames(
		t.TempDir(),
		ExecuteDynamicCodeCommandName,
		shouldPreferEmbeddedToolDefinition(ExecuteDynamicCodeCommandName),
		func(string) map[string]bool {
			collectorCalled = true
			return map[string]bool{}
		})
	if err != nil {
		t.Fatalf("findToolForCommandWithInternalToolNames failed: %v", err)
	}

	if collectorCalled {
		t.Fatal("execute-dynamic-code should not collect internal skill names")
	}
	if !ok {
		t.Fatal("execute-dynamic-code was not loaded from embedded definitions")
	}
	if tool.Name != ExecuteDynamicCodeCommandName {
		t.Fatalf("tool name mismatch: %s", tool.Name)
	}
}

func TestFindToolForCommandUsesProjectCacheForRegularTools(t *testing.T) {
	// Verifies that non-hot-path tools still come from the project tool cache.
	projectRoot := t.TempDir()
	writeToolCache(t, projectRoot, `{
  "version": "test",
  "tools": [
    {
      "name": "cached-tool",
      "description": "cached",
      "inputSchema": {"type": "object", "properties": {}}
    }
  ]
}`)

	tool, _, ok, err := FindToolForCommand(projectRoot, "cached-tool")
	if err != nil {
		t.Fatalf("findToolForCommand failed: %v", err)
	}

	if !ok {
		t.Fatal("cached tool was not loaded")
	}
	if tool.Name != "cached-tool" {
		t.Fatalf("tool name mismatch: %s", tool.Name)
	}
}

// Tests that internal skills without frontmatter names are filtered by their directory-derived tool names.
func TestLoadToolsFiltersDerivedInternalSkillToolNameFromCache(t *testing.T) {
	projectRoot := t.TempDir()
	writeTestSkill(t, projectRoot, "Assets/Editor/uloop-derived-internal/Skill", `---
internal: true
---

# internal
`)
	writeToolCache(t, projectRoot, `{
  "version": "test",
  "tools": [
    {
      "name": "derived-internal",
      "description": "internal",
      "inputSchema": {"type": "object", "properties": {}}
    },
    {
      "name": "public-tool",
      "description": "public",
      "inputSchema": {"type": "object", "properties": {}}
    }
  ]
}`)

	cache, err := LoadTools(projectRoot)
	if err != nil {
		t.Fatalf("loadTools failed: %v", err)
	}

	if _, ok := FindTool(cache, "derived-internal"); ok {
		t.Fatalf("derived internal tool was not filtered: %#v", cache.Tools)
	}
	if _, ok := FindTool(cache, "public-tool"); !ok {
		t.Fatalf("public tool was filtered: %#v", cache.Tools)
	}
}

// Tests that embedded fallback tools do not expose internal-only commands.
func TestLoadDefaultToolsDoesNotExposeInternalSkillTools(t *testing.T) {
	cache := LoadDefaultTools()

	for _, toolName := range []string{"get-project-info", "get-version"} {
		if _, ok := FindTool(cache, toolName); ok {
			t.Fatalf("internal tool %s was exposed by default tools", toolName)
		}
	}
}

// Tests that the global --project-path option is removed before command-specific parsing.
func TestParseGlobalProjectPathAcceptsLeadingOption(t *testing.T) {
	remaining, projectPath, err := ParseGlobalProjectPath(
		[]string{
			"--project-path", "/tmp/project",
			"compile",
			"--force-recompile",
		},
	)
	if err != nil {
		t.Fatalf("parseGlobalProjectPath failed: %v", err)
	}

	if projectPath != "/tmp/project" {
		t.Fatalf("project path mismatch: %s", projectPath)
	}
	expected := []string{"compile", "--force-recompile"}
	if len(remaining) != len(expected) {
		t.Fatalf("remaining length mismatch: %#v", remaining)
	}
	for index, value := range expected {
		if remaining[index] != value {
			t.Fatalf("remaining mismatch: %#v", remaining)
		}
	}
}

// Tests that similarly prefixed option names are not consumed as --project-path.
func TestParseGlobalProjectPathRequiresExactFlagName(t *testing.T) {
	remaining, projectPath, err := ParseGlobalProjectPath([]string{"--project-pathology"})
	if err != nil {
		t.Fatalf("parseGlobalProjectPath failed: %v", err)
	}

	if projectPath != "" {
		t.Fatalf("project path should be empty, got %q", projectPath)
	}
	expected := []string{"--project-pathology"}
	if len(remaining) != len(expected) {
		t.Fatalf("remaining length mismatch: %#v", remaining)
	}
	for index, value := range expected {
		if remaining[index] != value {
			t.Fatalf("remaining mismatch: %#v", remaining)
		}
	}
}

// writeToolCache seeds the project tool cache fixture used by several tests in
// this file.
func writeToolCache(t *testing.T, projectRoot string, content string) {
	t.Helper()
	clitest.WriteProjectFile(t, projectRoot, filepath.Join(CacheDirectoryName, CacheFileName), content)
}

// writeTestSkill seeds a SKILL.md fixture at projectRoot/relativeDir via the
// shared clitest.WriteSkillFile helper, which owns the CRLF normalization.
func writeTestSkill(t *testing.T, projectRoot string, relativeDir string, content string) {
	t.Helper()
	clitest.WriteSkillFile(t, projectRoot, relativeDir, skillscan.SkillFileName, content)
}
