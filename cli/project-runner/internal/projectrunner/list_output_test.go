package projectrunner

import (
	"encoding/json"
	"testing"

	"github.com/hatayama/unity-cli-loop/common/tooldocs"

	"github.com/hatayama/unity-cli-loop/common/clicore"
)

// Tests that list output exposes the actual CLI option names instead of schema property names.
func TestFormatToolListResultUsesCliOptionNames(t *testing.T) {
	result := formatToolListResult([]byte(`{
  "tools": [
    {
      "name": "screenshot",
      "parameterSchema": {
        "properties": {
          "CaptureMode": {
            "type": "string",
            "description": "Capture mode",
            "defaultValue": "window",
            "enum": ["window", "rendering"]
          },
          "AnnotateElements": {
            "type": "boolean",
            "description": "Annotate elements",
            "defaultValue": false
          }
        }
      }
    }
  ]
}`))

	catalog := decodeListCatalog(t, result)
	tool := findListTool(t, catalog, "screenshot")

	captureMode := findListOption(t, tool, "--capture-mode")
	if captureMode.Type != "string" {
		t.Fatalf("capture mode type mismatch: %#v", captureMode)
	}
	if captureMode.Default != "window" {
		t.Fatalf("capture mode default mismatch: %#v", captureMode)
	}
	if len(captureMode.Values) != 2 || captureMode.Values[0] != "window" || captureMode.Values[1] != "rendering" {
		t.Fatalf("capture mode values mismatch: %#v", captureMode)
	}
	findListOption(t, tool, "--annotate-elements")
	assertListOptionMissing(t, tool, "CaptureMode")
}

// Tests that list output uses command-specific option aliases.
func TestNewListCatalogUsesSpecialOptionAliases(t *testing.T) {
	cache := clicore.ToolsCache{
		Tools: []clicore.ToolDefinition{
			{
				Name: clicore.RunTestsCommandName,
				InputSchema: clicore.InputSchema{
					Properties: map[string]clicore.ToolProperty{
						"SaveBeforeRun": {Type: "boolean", Default: true},
					},
				},
			},
			{
				Name: clicore.CompileCommandName,
				InputSchema: clicore.InputSchema{
					Properties: map[string]clicore.ToolProperty{
						tooldocs.ReloadExternalSceneChangesPropertyName: {Type: "boolean", Default: true},
					},
				},
			},
		},
	}

	catalog := newListCatalog(cache)

	runTestsTool := findListTool(t, catalog, clicore.RunTestsCommandName)
	failOnUnsavedChanges := findListOption(t, runTestsTool, "--fail-on-unsaved-changes")
	if failOnUnsavedChanges.Default != false {
		t.Fatalf("fail-on-unsaved-changes default mismatch: %#v", failOnUnsavedChanges)
	}
	assertListOptionMissing(t, runTestsTool, "--no-save-before-run")

	compileTool := findListTool(t, catalog, clicore.CompileCommandName)
	stopOnExternalSceneChanges := findListOption(t, compileTool, "--stop-on-external-scene-changes")
	if stopOnExternalSceneChanges.Default != false {
		t.Fatalf("stop-on-external-scene-changes default mismatch: %#v", stopOnExternalSceneChanges)
	}
	assertListOptionMissing(t, compileTool, "--no-reload-external-scene-changes")
}

// Tests that list output renders numeric enum defaults as their public value names.
func TestNewListCatalogUsesEnumNameForNumericDefault(t *testing.T) {
	cache := clicore.ToolsCache{
		Tools: []clicore.ToolDefinition{
			{
				Name: "screenshot",
				InputSchema: clicore.InputSchema{
					Properties: map[string]clicore.ToolProperty{
						"CaptureMode": {
							Type:    "string",
							Default: float64(0),
							Enum:    []string{"window", "rendering"},
						},
					},
				},
			},
		},
	}

	catalog := newListCatalog(cache)

	tool := findListTool(t, catalog, "screenshot")
	captureMode := findListOption(t, tool, "--capture-mode")
	if captureMode.Default != "window" {
		t.Fatalf("capture mode default mismatch: %#v", captureMode)
	}
}

// Tests that list output includes CLI-side options that are not Unity schema properties.
func TestNewListCatalogIncludesExecuteDynamicCodeCodeFile(t *testing.T) {
	tool, ok := clicore.FindTool(clicore.LoadDefaultTools(), clicore.ExecuteDynamicCodeCommandName)
	if !ok {
		t.Fatal("execute-dynamic-code was not found in default tools")
	}

	catalog := newListCatalog(clicore.ToolsCache{Tools: []clicore.ToolDefinition{tool}})
	executeDynamicCode := findListTool(t, catalog, clicore.ExecuteDynamicCodeCommandName)

	findListOption(t, executeDynamicCode, tooldocs.DynamicCodeFileOptionName)
}

// Tests that list output includes the CLI-side --await orchestration flags for
// enable-pause-point, which are not part of the Unity-side EnablePausePointSchema.
func TestNewListCatalogIncludesEnablePausePointAwaitOptions(t *testing.T) {
	tool, ok := clicore.FindTool(clicore.LoadDefaultTools(), pausePointEnableCommandName)
	if !ok {
		t.Fatal("enable-pause-point was not found in default tools")
	}

	catalog := newListCatalog(clicore.ToolsCache{Tools: []clicore.ToolDefinition{tool}})
	enablePausePoint := findListTool(t, catalog, pausePointEnableCommandName)

	findListOption(t, enablePausePoint, "--"+tooldocs.PausePointEnableAwaitFlagName)
	findListOption(t, enablePausePoint, "--"+tooldocs.PausePointCapturedVariablesFlagName)
	findListOption(t, enablePausePoint, "--"+tooldocs.PausePointCapturedVariableNamesFlagName)
	findListOption(t, enablePausePoint, "--"+tooldocs.PausePointExpectFlagName)
	findListOption(t, enablePausePoint, "--"+tooldocs.PausePointTriggerFlagName)
	findListOption(t, enablePausePoint, "--"+tooldocs.PausePointResumePlayFlagName)
}

// Tests that list replaces Unity's generated placeholder descriptions with the embedded catalog's
// real text. list formats the raw get-tool-details response, so it does not inherit the fallback the
// project-cache loader applies for `--help`.
func TestFormatToolListResultFillsPlaceholderDescriptions(t *testing.T) {
	content := formatToolListResult([]byte(`{
  "tools": [
    {
      "name": "simulate-keyboard",
      "parameterSchema": {
        "Properties": {
          "Duration": {"Type": "number", "Description": "Parameter: Duration"}
        }
      }
    }
  ]
}`))

	catalog := decodeListCatalog(t, content)
	simulateKeyboard := findListTool(t, catalog, "simulate-keyboard")

	if simulateKeyboard.Description == "" {
		t.Error("tool description was not filled from the embedded catalog")
	}
	option := findListOption(t, simulateKeyboard, "--duration")
	if option.Description == "Parameter: Duration" || option.Description == "" {
		t.Errorf("option placeholder description was not replaced: %q", option.Description)
	}
}

// Tests that an unset string parameter reports no default at all, matching --help. Unity reports an
// empty string for these, which would otherwise claim the option defaults to "".
func TestNewListCatalogOmitsEmptyStringDefaults(t *testing.T) {
	catalog := newListCatalog(clicore.ToolsCache{Tools: []clicore.ToolDefinition{{
		Name: "screenshot",
		ParameterSchema: clicore.InputSchema{Properties: map[string]clicore.ToolProperty{
			"OutputPath": {Type: "string", Description: "Where to write the file", DefaultValue: ""},
		}},
	}}})

	option := findListOption(t, findListTool(t, catalog, "screenshot"), "--output-path")
	if option.Default != nil {
		t.Errorf("empty-string default was reported: %#v", option.Default)
	}
}

func decodeListCatalog(t *testing.T, content []byte) listCatalog {
	t.Helper()

	var catalog listCatalog
	if err := json.Unmarshal(content, &catalog); err != nil {
		t.Fatalf("failed to decode list catalog: %v\n%s", err, content)
	}
	return catalog
}

func findListTool(t *testing.T, catalog listCatalog, name string) listTool {
	t.Helper()

	for _, tool := range catalog.Tools {
		if tool.Name == name {
			return tool
		}
	}
	t.Fatalf("tool %q was not listed: %#v", name, catalog.Tools)
	return listTool{}
}

func findListOption(t *testing.T, tool listTool, name string) listOption {
	t.Helper()

	for _, option := range tool.Options {
		if option.Name == name {
			return option
		}
	}
	t.Fatalf("option %q was not listed: %#v", name, tool.Options)
	return listOption{}
}

func assertListOptionMissing(t *testing.T, tool listTool, name string) {
	t.Helper()

	for _, option := range tool.Options {
		if option.Name == name {
			t.Fatalf("option %q should not be listed: %#v", name, tool.Options)
		}
	}
}
