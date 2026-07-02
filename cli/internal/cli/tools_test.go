package cli

import (
	"os"
	"path/filepath"
	"testing"

	"github.com/hatayama/unity-cli-loop/cli/internal/clicore"
)

// Tests that tool arguments are converted according to their schema types.
func TestBuildToolParamsConvertsSchemaTypes(t *testing.T) {
	tool := clicore.ToolDefinition{
		Name: "sample-tool",
		InputSchema: clicore.InputSchema{
			Properties: map[string]clicore.ToolProperty{
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

func TestBuildToolParamsRejectsNullObjectValue(t *testing.T) {
	// Tests that object schema arguments must parse to JSON objects rather than null.
	tool := clicore.ToolDefinition{
		Name: "sample-tool",
		InputSchema: clicore.InputSchema{
			Properties: map[string]clicore.ToolProperty{
				"Payload": {Type: "object"},
			},
		},
	}

	_, _, err := buildToolParams([]string{"--payload", "null"}, tool)

	if err == nil {
		t.Fatal("expected null object value to be rejected")
	}
}

// Tests that default-enabled boolean tool arguments are disabled through --no-* flags.
func TestBuildToolParamsConvertsDefaultTrueBooleanToNegatedFlag(t *testing.T) {
	tool := clicore.ToolDefinition{
		Name: "sample-tool",
		InputSchema: clicore.InputSchema{
			Properties: map[string]clicore.ToolProperty{
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
	tool, ok := clicore.FindTool(clicore.LoadDefaultTools(), clicore.ExecuteDynamicCodeCommandName)
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

// Tests that execute-dynamic-code preserves multiline inline code as one argument.
func TestBuildToolParamsPreservesExecuteDynamicCodeMultilineCode(t *testing.T) {
	tool, ok := clicore.FindTool(clicore.LoadDefaultTools(), clicore.ExecuteDynamicCodeCommandName)
	if !ok {
		t.Fatal("execute-dynamic-code was not found in default tools")
	}
	source := "using UnityEngine;\nGameObject obj = GameObject.Find(\"Player\");\nif (obj == null) return \"Player not found\";\nreturn obj.name;"

	params, _, err := buildToolParams([]string{"--code", source}, tool)
	if err != nil {
		t.Fatalf("buildToolParams failed: %v", err)
	}

	code, ok := params[dynamicCodeCodePropertyName].(string)
	if !ok {
		t.Fatalf("Code param type mismatch: %#v", params[dynamicCodeCodePropertyName])
	}
	if code != source {
		t.Fatalf("Code param was not preserved:\nexpected: %q\nactual:   %q", source, code)
	}
}

// Tests that run-tests accepts --fail-on-unsaved-changes from embedded tools.
func TestBuildToolParamsConvertsRunTestsFailOnUnsavedChangesFlag(t *testing.T) {
	tool, ok := clicore.FindTool(clicore.LoadDefaultTools(), clicore.RunTestsCommandName)
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
	tool, ok := clicore.FindTool(clicore.LoadDefaultTools(), clicore.CompileCommandName)
	if !ok {
		t.Fatal("compile was not found in default tools")
	}

	params, _, err := buildToolParams([]string{"--stop-on-external-scene-changes"}, tool)
	if err != nil {
		t.Fatalf("buildToolParams failed: %v", err)
	}

	if params[clicore.ReloadExternalSceneChangesPropertyName] != false {
		t.Fatalf("ReloadExternalSceneChanges mismatch: %#v", params[clicore.ReloadExternalSceneChangesPropertyName])
	}
}

// Tests that run-tests-specific aliases do not leak into unrelated tool schemas.
func TestBuildToolParamsKeepsGenericSaveBeforeRunFlagForOtherTools(t *testing.T) {
	tool := clicore.ToolDefinition{
		Name: "sample-tool",
		InputSchema: clicore.InputSchema{
			Properties: map[string]clicore.ToolProperty{
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
	tool := clicore.ToolDefinition{
		Name: "sample-tool",
		InputSchema: clicore.InputSchema{
			Properties: map[string]clicore.ToolProperty{
				clicore.ReloadExternalSceneChangesPropertyName: {Type: "boolean", Default: true},
			},
		},
	}

	params, _, err := buildToolParams([]string{"--no-reload-external-scene-changes"}, tool)
	if err != nil {
		t.Fatalf("buildToolParams failed: %v", err)
	}
	if params[clicore.ReloadExternalSceneChangesPropertyName] != false {
		t.Fatalf("ReloadExternalSceneChanges mismatch: %#v", params[clicore.ReloadExternalSceneChangesPropertyName])
	}

	_, _, err = buildToolParams([]string{"--stop-on-external-scene-changes"}, tool)
	if err == nil {
		t.Fatal("expected compile-specific flag to be rejected")
	}
}

func TestBuildToolParamsRejectsCompileWaitForDomainReloadFlag(t *testing.T) {
	// Verifies the removed positive domain-reload wait flag is not accepted by the public CLI parser.
	tool, ok := clicore.FindTool(clicore.LoadDefaultTools(), clicore.CompileCommandName)
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
	tool, ok := clicore.FindTool(clicore.LoadDefaultTools(), clicore.ExecuteDynamicCodeCommandName)
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
	tool := clicore.ToolDefinition{
		Name: "sample-tool",
		InputSchema: clicore.InputSchema{
			Properties: map[string]clicore.ToolProperty{
				"Enabled": {Type: "boolean"},
			},
		},
	}

	_, _, err := buildToolParams([]string{"--enabled", "true"}, tool)
	if err == nil {
		t.Fatal("expected boolean value error")
	}
}

// Tests that cached tools written by the Unity Editor remain usable by the native CLI.
func TestLoadToolsAcceptsEditorParameterSchemaCache(t *testing.T) {
	projectRoot := t.TempDir()
	writeToolCache(t, projectRoot, `{
  "tools": [
    {
      "name": "get-logs",
      "description": "Retrieve logs from Unity Console",
      "parameterSchema": {
        "properties": {
          "LogType": {
            "type": "string",
            "description": "Log type to filter",
            "defaultValue": "All"
          },
          "IncludeStackTrace": {
            "type": "boolean",
            "description": "Whether to display stack trace",
            "defaultValue": false
          },
          "IncludeInactive": {
            "type": "boolean",
            "description": "Whether to include inactive objects",
            "defaultValue": true
          }
        },
        "required": []
      }
    }
  ]
}`)

	cache, err := clicore.LoadTools(projectRoot)
	if err != nil {
		t.Fatalf("loadTools failed: %v", err)
	}
	tool, ok := clicore.FindTool(cache, "get-logs")
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

// Tests that numeric tool arguments can be negative values instead of being parsed as flags.
func TestBuildToolParamsAcceptsNegativeNumericValues(t *testing.T) {
	tool := clicore.ToolDefinition{
		Name: "sample-tool",
		InputSchema: clicore.InputSchema{
			Properties: map[string]clicore.ToolProperty{
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

func writeToolCache(t *testing.T, projectRoot string, content string) {
	t.Helper()
	cachePath := filepath.Join(projectRoot, clicore.CacheDirectoryName, clicore.CacheFileName)
	if err := os.MkdirAll(filepath.Dir(cachePath), 0o755); err != nil {
		t.Fatalf("failed to create tool cache directory: %v", err)
	}
	if err := os.WriteFile(cachePath, []byte(content), 0o644); err != nil {
		t.Fatalf("failed to write tool cache: %v", err)
	}
}
