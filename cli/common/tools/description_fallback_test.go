package tools

import (
	"os"
	"path/filepath"
	"strings"
	"testing"
)

// Verifies a cached property whose description is Unity's generated placeholder is replaced with the
// embedded catalog's real description, since the placeholder carries no information at all.
func TestApplyEmbeddedDescriptionFallbackReplacesPlaceholders(t *testing.T) {
	catalog := ToolCatalog{Tools: []ToolDefinition{{
		Name: "simulate-keyboard",
		ParameterSchema: ToolInputSchema{Properties: map[string]ToolProperty{
			"Duration": {Type: "number", Description: "Parameter: Duration"},
		}},
	}}}

	result := ApplyEmbeddedDescriptionFallback(catalog)

	description := result.Tools[0].EffectiveInputSchema().Properties["Duration"].Description
	if description == "Parameter: Duration" || description == "" {
		t.Fatalf("placeholder description was not replaced: %q", description)
	}
}

// Verifies an empty description is treated as a placeholder too, since it is equally uninformative.
func TestApplyEmbeddedDescriptionFallbackReplacesEmptyDescriptions(t *testing.T) {
	catalog := ToolCatalog{Tools: []ToolDefinition{{
		Name: "simulate-keyboard",
		ParameterSchema: ToolInputSchema{Properties: map[string]ToolProperty{
			"Duration": {Type: "number", Description: ""},
		}},
	}}}

	result := ApplyEmbeddedDescriptionFallback(catalog)

	if result.Tools[0].EffectiveInputSchema().Properties["Duration"].Description == "" {
		t.Fatal("empty description was not replaced")
	}
}

// Verifies a description the schema author actually wrote is never overwritten: the cache is
// authoritative whenever it carries real information.
func TestApplyEmbeddedDescriptionFallbackKeepsAuthoredDescriptions(t *testing.T) {
	catalog := ToolCatalog{Tools: []ToolDefinition{{
		Name:        "simulate-keyboard",
		Description: "Cached tool description",
		ParameterSchema: ToolInputSchema{Properties: map[string]ToolProperty{
			"Duration": {Type: "number", Description: "Authored duration description"},
		}},
	}}}

	result := ApplyEmbeddedDescriptionFallback(catalog)

	if result.Tools[0].Description != "Cached tool description" {
		t.Errorf("tool description was overwritten: %q", result.Tools[0].Description)
	}
	if got := result.Tools[0].EffectiveInputSchema().Properties["Duration"].Description; got != "Authored duration description" {
		t.Errorf("authored property description was overwritten: %q", got)
	}
}

// Verifies a tool absent from the embedded catalog passes through untouched, so a project's custom
// commands are not silently emptied or matched against an unrelated tool.
func TestApplyEmbeddedDescriptionFallbackLeavesCustomToolsAlone(t *testing.T) {
	catalog := ToolCatalog{Tools: []ToolDefinition{{
		Name: "my-custom-command",
		ParameterSchema: ToolInputSchema{Properties: map[string]ToolProperty{
			"Value": {Type: "string", Description: "Parameter: Value"},
		}},
	}}}

	result := ApplyEmbeddedDescriptionFallback(catalog)

	if result.Tools[0].Description != "" {
		t.Errorf("custom tool gained a description: %q", result.Tools[0].Description)
	}
	if got := result.Tools[0].EffectiveInputSchema().Properties["Value"].Description; got != "Parameter: Value" {
		t.Errorf("custom tool property description changed: %q", got)
	}
}

// Verifies a property the embedded catalog does not know keeps its placeholder rather than picking
// up an unrelated description.
func TestApplyEmbeddedDescriptionFallbackKeepsUnknownProperties(t *testing.T) {
	catalog := ToolCatalog{Tools: []ToolDefinition{{
		Name: "simulate-keyboard",
		ParameterSchema: ToolInputSchema{Properties: map[string]ToolProperty{
			"NewlyAddedOption": {Type: "string", Description: "Parameter: NewlyAddedOption"},
		}},
	}}}

	result := ApplyEmbeddedDescriptionFallback(catalog)

	if got := result.Tools[0].EffectiveInputSchema().Properties["NewlyAddedOption"].Description; got != "Parameter: NewlyAddedOption" {
		t.Errorf("unknown property description changed: %q", got)
	}
}

// Verifies enum, default, type, and required stay as the cache reported them: Unity is the truth for
// everything except the description text.
func TestApplyEmbeddedDescriptionFallbackKeepsCachedSchemaFacts(t *testing.T) {
	catalog := ToolCatalog{Tools: []ToolDefinition{{
		Name: "simulate-keyboard",
		ParameterSchema: ToolInputSchema{
			Properties: map[string]ToolProperty{
				"Action": {
					Type:         "string",
					Description:  "Parameter: Action",
					DefaultValue: float64(0),
					Enum:         []string{"Press", "OnlyCachedMember"},
				},
			},
			Required: []string{"Action"},
		},
	}}}

	result := ApplyEmbeddedDescriptionFallback(catalog)

	property := result.Tools[0].EffectiveInputSchema().Properties["Action"]
	if len(property.Enum) != 2 || property.Enum[1] != "OnlyCachedMember" {
		t.Errorf("cached enum was replaced: %v", property.Enum)
	}
	if property.EffectiveDefault() != float64(0) {
		t.Errorf("cached default was replaced: %v", property.EffectiveDefault())
	}
	if required := result.Tools[0].EffectiveInputSchema().Required; len(required) != 1 || required[0] != "Action" {
		t.Errorf("cached required list was replaced: %v", required)
	}
}

// Verifies the fallback is applied when a project cache is loaded, which is the path both `--help`
// and `uloop list` read in a synced project.
func TestLoadProjectCacheAppliesDescriptionFallback(t *testing.T) {
	projectRoot := t.TempDir()
	cacheDirectory := filepath.Join(projectRoot, CacheDirectoryName)
	if err := os.MkdirAll(cacheDirectory, 0o755); err != nil {
		t.Fatalf("failed to create cache directory: %v", err)
	}
	content := `{"Tools":[{"name":"simulate-keyboard","parameterSchema":{"Properties":{"Duration":{"Type":"number","Description":"Parameter: Duration"}}}}]}`
	if err := os.WriteFile(filepath.Join(cacheDirectory, CacheFileName), []byte(content), 0o644); err != nil {
		t.Fatalf("failed to write cache: %v", err)
	}

	catalog, ok := LoadProjectCache(projectRoot, nil)
	if !ok {
		t.Fatal("project cache was not loaded")
	}
	if catalog.Tools[0].Description == "" {
		t.Error("tool description was not filled from the embedded catalog")
	}
	if got := catalog.Tools[0].EffectiveInputSchema().Properties["Duration"].Description; got == "Parameter: Duration" {
		t.Errorf("property placeholder was not replaced: %q", got)
	}
}

// Verifies the embedded catalog itself has no placeholder descriptions, since it is the source the
// fallback reads from.
func TestEmbeddedCatalogHasNoPlaceholderDescriptions(t *testing.T) {
	for _, tool := range LoadDefault().Tools {
		if tool.Description == "" {
			t.Errorf("embedded tool %s has no description", tool.Name)
		}
		for propertyName, property := range tool.EffectiveInputSchema().Properties {
			if isPlaceholderDescription(property.Description, propertyName) {
				t.Errorf("embedded tool %s property %s has a placeholder description", tool.Name, propertyName)
			}
		}
	}
}

// Verifies the documented digit-key rule reaches simulate-keyboard's --key description, so a caller
// reading only `--help` learns that bare digits are rejected.
func TestEmbeddedSimulateKeyboardKeyDescriptionDocumentsDigitKeys(t *testing.T) {
	tool, ok := Find(LoadDefault(), "simulate-keyboard")
	if !ok {
		t.Fatal("embedded catalog has no simulate-keyboard tool")
	}

	// The text is generated from the skill's parameter table, which states the rule as the accepted
	// ranges rather than one example digit; the guarantee this pins - a caller learns bare digits are
	// rejected - is unchanged.
	description := tool.EffectiveInputSchema().Properties["Key"].Description
	for _, expected := range []string{"Digit0-Digit9", "Numpad0-Numpad9", "not bare 0-9"} {
		if !strings.Contains(description, expected) {
			t.Errorf("--key description does not mention %q: %q", expected, description)
		}
	}
	if strings.Contains(description, "\"Return\"") {
		t.Errorf("--key description still offers the rejected Return example: %q", description)
	}
}

// Verifies enable-pause-point documents that --max-preview-elements also shapes later
// pause-point-status responses, which is not discoverable from the option name.
func TestEmbeddedEnablePausePointDocumentsMaxPreviewElementsCarryOver(t *testing.T) {
	tool, ok := Find(LoadDefault(), "enable-pause-point")
	if !ok {
		t.Fatal("embedded catalog has no enable-pause-point tool")
	}

	description := tool.EffectiveInputSchema().Properties["MaxPreviewElements"].Description
	if !strings.Contains(description, "pause-point-status") {
		t.Errorf("--max-preview-elements description does not mention pause-point-status: %q", description)
	}
}

// Verifies enable-pause-point documents that --max-caller-frames also shapes later
// pause-point-status responses, which is not discoverable from the option name.
func TestEmbeddedEnablePausePointDocumentsMaxCallerFramesCarryOver(t *testing.T) {
	tool, ok := Find(LoadDefault(), "enable-pause-point")
	if !ok {
		t.Fatal("embedded catalog has no enable-pause-point tool")
	}

	description := tool.EffectiveInputSchema().Properties["MaxCallerFrames"].Description
	if !strings.Contains(description, "pause-point-status") {
		t.Errorf("--max-caller-frames description does not mention pause-point-status: %q", description)
	}
}
