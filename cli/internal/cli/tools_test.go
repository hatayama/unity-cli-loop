package cli

import (
	"os"
	"path/filepath"
	"testing"
)

// Tests that tool arguments are converted according to their schema types.
func TestBuildToolParamsConvertsSchemaTypes(t *testing.T) {
	tool := toolDefinition{
		Name: "sample-tool",
		InputSchema: inputSchema{
			Properties: map[string]toolProperty{
				"Enabled": {Type: "boolean"},
				"Count":   {Type: "integer"},
				"Names":   {Type: "array"},
			},
		},
	}

	params, projectPath, err := buildToolParams(
		[]string{
			"--enabled",
			"--count", "12",
			"--names", "a,b",
			"--project-path", "/tmp/project",
		},
		tool,
	)
	if err != nil {
		t.Fatalf("buildToolParams failed: %v", err)
	}

	if projectPath != "/tmp/project" {
		t.Fatalf("project path mismatch: %s", projectPath)
	}
	if params["Enabled"] != true {
		t.Fatalf("Enabled mismatch: %#v", params["Enabled"])
	}
	if params["Count"] != 12 {
		t.Fatalf("Count mismatch: %#v", params["Count"])
	}
	names, ok := params["Names"].([]string)
	if !ok || len(names) != 2 || names[0] != "a" || names[1] != "b" {
		t.Fatalf("Names mismatch: %#v", params["Names"])
	}
}

// Tests that default-enabled boolean tool arguments are disabled through --no-* flags.
func TestBuildToolParamsConvertsDefaultTrueBooleanToNegatedFlag(t *testing.T) {
	tool := toolDefinition{
		Name: "sample-tool",
		InputSchema: inputSchema{
			Properties: map[string]toolProperty{
				"IncludeComponents": {Type: "boolean", Default: true},
			},
		},
	}

	params, _, err := buildToolParams([]string{"--no-include-components"}, tool)
	if err != nil {
		t.Fatalf("buildToolParams failed: %v", err)
	}

	if params["IncludeComponents"] != false {
		t.Fatalf("IncludeComponents mismatch: %#v", params["IncludeComponents"])
	}
}

// Tests that execute-dynamic-code accepts explicit reload waiting from embedded tools.
func TestBuildToolParamsConvertsExecuteDynamicCodeWaitFlag(t *testing.T) {
	tool, ok := findTool(loadDefaultTools(), executeDynamicCodeCommandName)
	if !ok {
		t.Fatal("execute-dynamic-code was not found in default tools")
	}

	params, _, err := buildToolParams([]string{"--wait-for-domain-reload"}, tool)
	if err != nil {
		t.Fatalf("buildToolParams failed: %v", err)
	}

	if params[compileWaitParam] != true {
		t.Fatalf("WaitForDomainReload mismatch: %#v", params[compileWaitParam])
	}
}

// Tests that run-tests accepts --fail-on-unsaved-changes from embedded tools.
func TestBuildToolParamsConvertsRunTestsFailOnUnsavedChangesFlag(t *testing.T) {
	tool, ok := findTool(loadDefaultTools(), runTestsCommandName)
	if !ok {
		t.Fatal("run-tests was not found in default tools")
	}

	params, _, err := buildToolParams([]string{"--fail-on-unsaved-changes"}, tool)
	if err != nil {
		t.Fatalf("buildToolParams failed: %v", err)
	}

	if params["SaveBeforeRun"] != false {
		t.Fatalf("SaveBeforeRun mismatch: %#v", params["SaveBeforeRun"])
	}
}

// Tests that compile accepts --stop-on-external-scene-changes from embedded tools.
func TestBuildToolParamsConvertsCompileStopOnExternalSceneChangesFlag(t *testing.T) {
	tool, ok := findTool(loadDefaultTools(), compileCommandName)
	if !ok {
		t.Fatal("compile was not found in default tools")
	}

	params, _, err := buildToolParams([]string{"--stop-on-external-scene-changes"}, tool)
	if err != nil {
		t.Fatalf("buildToolParams failed: %v", err)
	}

	if params[reloadExternalSceneChangesPropertyName] != false {
		t.Fatalf("ReloadExternalSceneChanges mismatch: %#v", params[reloadExternalSceneChangesPropertyName])
	}
}

// Tests that run-tests-specific aliases do not leak into unrelated tool schemas.
func TestBuildToolParamsKeepsGenericSaveBeforeRunFlagForOtherTools(t *testing.T) {
	tool := toolDefinition{
		Name: "sample-tool",
		InputSchema: inputSchema{
			Properties: map[string]toolProperty{
				"SaveBeforeRun": {Type: "boolean", Default: true},
			},
		},
	}

	params, _, err := buildToolParams([]string{"--no-save-before-run"}, tool)
	if err != nil {
		t.Fatalf("buildToolParams failed: %v", err)
	}
	if params["SaveBeforeRun"] != false {
		t.Fatalf("SaveBeforeRun mismatch: %#v", params["SaveBeforeRun"])
	}

	_, _, err = buildToolParams([]string{"--fail-on-unsaved-changes"}, tool)
	if err == nil {
		t.Fatal("expected run-tests-specific flag to be rejected")
	}
}

// Tests that compile-specific aliases do not leak into unrelated tool schemas.
func TestBuildToolParamsKeepsGenericReloadExternalSceneChangesFlagForOtherTools(t *testing.T) {
	tool := toolDefinition{
		Name: "sample-tool",
		InputSchema: inputSchema{
			Properties: map[string]toolProperty{
				reloadExternalSceneChangesPropertyName: {Type: "boolean", Default: true},
			},
		},
	}

	params, _, err := buildToolParams([]string{"--no-reload-external-scene-changes"}, tool)
	if err != nil {
		t.Fatalf("buildToolParams failed: %v", err)
	}
	if params[reloadExternalSceneChangesPropertyName] != false {
		t.Fatalf("ReloadExternalSceneChanges mismatch: %#v", params[reloadExternalSceneChangesPropertyName])
	}

	_, _, err = buildToolParams([]string{"--stop-on-external-scene-changes"}, tool)
	if err == nil {
		t.Fatal("expected compile-specific flag to be rejected")
	}
}

// Tests that run-tests-specific help text does not leak into unrelated tool schemas.
func TestVisibleOptionHelpEntriesKeepsGenericSaveBeforeRunHelpForOtherTools(t *testing.T) {
	tool := toolDefinition{
		Name: "sample-tool",
		InputSchema: inputSchema{
			Properties: map[string]toolProperty{
				"SaveBeforeRun": {Type: "boolean", Default: true},
			},
		},
	}

	entries := visibleOptionHelpEntriesForTool(tool)

	if len(entries) != 1 {
		t.Fatalf("entry count mismatch: %#v", entries)
	}
	if entries[0].name != "--no-save-before-run" {
		t.Fatalf("option name mismatch: %#v", entries[0].name)
	}
	if entries[0].description != "Disable save before run; default: enabled" {
		t.Fatalf("description mismatch: %#v", entries[0].description)
	}
}

func TestBuildToolParamsRejectsCompileWaitForDomainReloadFlag(t *testing.T) {
	// Verifies the removed positive domain-reload wait flag is not accepted by the public CLI parser.
	tool, ok := findTool(loadDefaultTools(), compileCommandName)
	if !ok {
		t.Fatal("compile was not found in default tools")
	}

	_, _, err := buildToolParams([]string{"--wait-for-domain-reload"}, tool)
	if err == nil {
		t.Fatal("expected removed wait flag to be rejected")
	}
}

// Tests that hidden execute-dynamic-code options remain available for internal callers.
func TestBuildToolParamsAcceptsHiddenExecuteDynamicCodeCompileOnlyFlag(t *testing.T) {
	tool, ok := findTool(loadDefaultTools(), executeDynamicCodeCommandName)
	if !ok {
		t.Fatal("execute-dynamic-code was not found in default tools")
	}

	params, _, err := buildToolParams([]string{"--compile-only"}, tool)
	if err != nil {
		t.Fatalf("buildToolParams failed: %v", err)
	}

	if params[dynamicCodeCompileOnlyParam] != true {
		t.Fatalf("CompileOnly mismatch: %#v", params[dynamicCodeCompileOnlyParam])
	}
}

// Tests that boolean tool arguments reject the old explicit true/false value form.
func TestBuildToolParamsRejectsExplicitBooleanValues(t *testing.T) {
	tool := toolDefinition{
		Name: "sample-tool",
		InputSchema: inputSchema{
			Properties: map[string]toolProperty{
				"Enabled": {Type: "boolean"},
			},
		},
	}

	_, _, err := buildToolParams([]string{"--enabled", "true"}, tool)
	if err == nil {
		t.Fatal("expected boolean value error")
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

	cache, err := loadTools(projectRoot)
	if err != nil {
		t.Fatalf("loadTools failed: %v", err)
	}

	if _, ok := findTool(cache, "internal-tool"); ok {
		t.Fatalf("internal tool was not filtered: %#v", cache.Tools)
	}
	if _, ok := findTool(cache, "public-tool"); !ok {
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

	cache, err := loadTools(projectRoot)
	if err != nil {
		t.Fatalf("loadTools failed: %v", err)
	}

	if _, ok := findTool(cache, "internal-tool"); ok {
		t.Fatalf("single-quoted internal tool was not filtered: %#v", cache.Tools)
	}
	if _, ok := findTool(cache, "public-tool"); !ok {
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

	tool, _, ok, err := findToolForCommand(projectRoot, executeDynamicCodeCommandName)
	if err != nil {
		t.Fatalf("findToolForCommand failed: %v", err)
	}

	if !ok {
		t.Fatal("execute-dynamic-code was not loaded from embedded definitions")
	}
	if tool.Name != executeDynamicCodeCommandName {
		t.Fatalf("tool name mismatch: %s", tool.Name)
	}
}

func TestFindToolForCommandSkipsInternalSkillScanForExecuteDynamicCode(t *testing.T) {
	// Verifies that execute-dynamic-code does not scan project skills before using the embedded hot-path definition.
	collectorCalled := false

	tool, _, ok, err := findToolForCommandWithInternalToolNames(
		t.TempDir(),
		executeDynamicCodeCommandName,
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
	if tool.Name != executeDynamicCodeCommandName {
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

	tool, _, ok, err := findToolForCommand(projectRoot, "cached-tool")
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

	cache, err := loadTools(projectRoot)
	if err != nil {
		t.Fatalf("loadTools failed: %v", err)
	}

	if _, ok := findTool(cache, "derived-internal"); ok {
		t.Fatalf("derived internal tool was not filtered: %#v", cache.Tools)
	}
	if _, ok := findTool(cache, "public-tool"); !ok {
		t.Fatalf("public tool was filtered: %#v", cache.Tools)
	}
}

// Tests that cached tools written by the Unity Editor remain usable by the native CLI.
func TestLoadToolsAcceptsEditorParameterSchemaCache(t *testing.T) {
	projectRoot := t.TempDir()
	writeToolCache(t, projectRoot, `{
  "Tools": [
    {
      "name": "get-logs",
      "description": "Retrieve logs from Unity Console",
      "parameterSchema": {
        "Properties": {
          "LogType": {
            "Type": "string",
            "Description": "Log type to filter",
            "DefaultValue": "All"
          },
          "IncludeStackTrace": {
            "Type": "boolean",
            "Description": "Whether to display stack trace",
            "DefaultValue": false
          },
          "IncludeInactive": {
            "Type": "boolean",
            "Description": "Whether to include inactive objects",
            "DefaultValue": true
          }
        },
        "Required": []
      }
    }
  ]
}`)

	cache, err := loadTools(projectRoot)
	if err != nil {
		t.Fatalf("loadTools failed: %v", err)
	}
	tool, ok := findTool(cache, "get-logs")
	if !ok {
		t.Fatalf("cached tool was not loaded: %#v", cache.Tools)
	}

	params, _, err := buildToolParams(
		[]string{"--log-type", "Error", "--include-stack-trace", "--no-include-inactive"},
		tool,
	)
	if err != nil {
		t.Fatalf("buildToolParams failed: %v", err)
	}

	if params["LogType"] != "Error" {
		t.Fatalf("LogType mismatch: %#v", params["LogType"])
	}
	if params["IncludeStackTrace"] != true {
		t.Fatalf("IncludeStackTrace mismatch: %#v", params["IncludeStackTrace"])
	}
	if params["IncludeInactive"] != false {
		t.Fatalf("IncludeInactive mismatch: %#v", params["IncludeInactive"])
	}
}

// Tests that embedded fallback tools do not expose internal-only commands.
func TestLoadDefaultToolsDoesNotExposeInternalSkillTools(t *testing.T) {
	cache := loadDefaultTools()

	for _, toolName := range []string{"get-project-info", "get-version"} {
		if _, ok := findTool(cache, toolName); ok {
			t.Fatalf("internal tool %s was exposed by default tools", toolName)
		}
	}
}

// Tests that numeric tool arguments can be negative values instead of being parsed as flags.
func TestBuildToolParamsAcceptsNegativeNumericValues(t *testing.T) {
	tool := toolDefinition{
		Name: "sample-tool",
		InputSchema: inputSchema{
			Properties: map[string]toolProperty{
				"DeltaX": {Type: "number"},
				"Count":  {Type: "integer"},
			},
		},
	}

	params, _, err := buildToolParams(
		[]string{
			"--delta-x", "-10.5",
			"--count", "-2",
		},
		tool,
	)
	if err != nil {
		t.Fatalf("buildToolParams failed: %v", err)
	}

	if params["DeltaX"] != -10.5 {
		t.Fatalf("DeltaX mismatch: %#v", params["DeltaX"])
	}
	if params["Count"] != -2 {
		t.Fatalf("Count mismatch: %#v", params["Count"])
	}
}

// Tests that the global --project-path option is removed before command-specific parsing.
func TestParseGlobalProjectPathAcceptsLeadingOption(t *testing.T) {
	remaining, projectPath, err := parseGlobalProjectPath(
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
	remaining, projectPath, err := parseGlobalProjectPath([]string{"--project-pathology"})
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

func writeToolCache(t *testing.T, projectRoot string, content string) {
	t.Helper()
	cachePath := filepath.Join(projectRoot, cacheDirectoryName, cacheFileName)
	if err := os.MkdirAll(filepath.Dir(cachePath), 0o755); err != nil {
		t.Fatalf("failed to create tool cache directory: %v", err)
	}
	if err := os.WriteFile(cachePath, []byte(content), 0o644); err != nil {
		t.Fatalf("failed to write tool cache: %v", err)
	}
}
