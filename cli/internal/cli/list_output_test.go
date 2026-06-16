package cli

import (
	"encoding/json"
	"testing"
)

// Tests that list output exposes the actual CLI option names instead of schema property names.
func TestFormatToolListResultUsesCliOptionNames(t *testing.T) {
	result := formatToolListResult([]byte(`{
  "Tools": [
    {
      "name": "screenshot",
      "parameterSchema": {
        "Properties": {
          "CaptureMode": {
            "Type": "string",
            "Description": "Capture mode",
            "DefaultValue": "window",
            "Enum": ["window", "rendering"]
          },
          "AnnotateElements": {
            "Type": "boolean",
            "Description": "Annotate elements",
            "DefaultValue": false
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
	cache := toolsCache{
		Tools: []toolDefinition{
			{
				Name: runTestsCommandName,
				InputSchema: inputSchema{
					Properties: map[string]toolProperty{
						"SaveBeforeRun": {Type: "boolean", Default: true},
					},
				},
			},
			{
				Name: compileCommandName,
				InputSchema: inputSchema{
					Properties: map[string]toolProperty{
						reloadExternalSceneChangesPropertyName: {Type: "boolean", Default: true},
					},
				},
			},
		},
	}

	catalog := newListCatalog(cache)

	runTestsTool := findListTool(t, catalog, runTestsCommandName)
	findListOption(t, runTestsTool, "--fail-on-unsaved-changes")
	assertListOptionMissing(t, runTestsTool, "--no-save-before-run")

	compileTool := findListTool(t, catalog, compileCommandName)
	findListOption(t, compileTool, "--stop-on-external-scene-changes")
	assertListOptionMissing(t, compileTool, "--no-reload-external-scene-changes")
}

// Tests that list output includes CLI-side options that are not Unity schema properties.
func TestNewListCatalogIncludesExecuteDynamicCodeCodeFile(t *testing.T) {
	tool, ok := findTool(loadDefaultTools(), executeDynamicCodeCommandName)
	if !ok {
		t.Fatal("execute-dynamic-code was not found in default tools")
	}

	catalog := newListCatalog(toolsCache{Tools: []toolDefinition{tool}})
	executeDynamicCode := findListTool(t, catalog, executeDynamicCodeCommandName)

	findListOption(t, executeDynamicCode, dynamicCodeFileOptionName)
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
