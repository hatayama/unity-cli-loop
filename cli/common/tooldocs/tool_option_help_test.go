package tooldocs

import (
	"reflect"
	"testing"

	"github.com/hatayama/unity-cli-loop/common/tools"
)

// Verifies a negated boolean whose description came from a skill parameter table prints that text
// verbatim. Those rows are written from the flag's point of view, and the summary this replaced
// discarded them in favor of a synthesized "Disable <Name>".
func TestOptionSummaryKeepsASkillSourcedNegatedBooleanDescription(t *testing.T) {
	summary := OptionSummary("get-hierarchy", "IncludeComponents", tools.ToolProperty{
		Type:                    "boolean",
		Default:                 true,
		Description:             "Exclude component information",
		SkillSourcedDescription: true,
	})

	if summary != "Exclude component information" {
		t.Errorf("a skill-sourced description must be printed as written: %q", summary)
	}
}

// Verifies a negated boolean with no skill behind it still gets a synthesized summary. A custom
// command's author writes the property in the positive sense, so printing "Show my overlay" against
// --no-show-my-overlay would state the opposite of what the flag does.
func TestOptionSummarySynthesizesForANegatedBooleanWithNoSkill(t *testing.T) {
	summary := OptionSummary("my-custom-command", "ShowMyOverlay", tools.ToolProperty{
		Type:        "boolean",
		Default:     true,
		Description: "Show my overlay",
	})

	if summary != "Disable show my overlay" {
		t.Errorf("a description with no skill behind it must be synthesized: %q", summary)
	}
}

// Verifies the branch is on where the text came from, not on how it is worded: a description that
// happens to start with "Disable" is no longer what decides the outcome.
func TestOptionSummaryIgnoresTheWordingOfTheDescription(t *testing.T) {
	summary := OptionSummary("my-custom-command", "WaitForThing", tools.ToolProperty{
		Type:        "boolean",
		Default:     true,
		Description: "Disable the wait that this custom command performs",
	})

	if summary != "Disable wait for thing" {
		t.Errorf("wording must not decide the branch: %q", summary)
	}
}

// Verifies a description filled in from the embedded catalog is treated as skill-sourced too. The
// catalog is generated from the same parameter tables, so a cache carrying Unity's placeholder must end
// up with the table's wording rather than a synthesized summary.
func TestOptionSummaryKeepsANegatedBooleanDescriptionFilledFromTheEmbeddedCatalog(t *testing.T) {
	catalog := tools.ApplyEmbeddedDescriptionFallback(tools.ToolCatalog{Tools: []tools.ToolDefinition{{
		Name: "get-hierarchy",
		ParameterSchema: tools.ToolInputSchema{Properties: map[string]tools.ToolProperty{
			"IncludeComponents": {Type: "boolean", Default: true, Description: "Parameter: IncludeComponents"},
		}},
	}}})

	property := catalog.Tools[0].EffectiveInputSchema().Properties["IncludeComponents"]
	if property.Description == "" || property.Description == "Parameter: IncludeComponents" {
		t.Fatalf("the embedded catalog did not supply a description: %q", property.Description)
	}
	summary := OptionSummary("get-hierarchy", "IncludeComponents", property)

	if summary != property.Description {
		t.Errorf("the filled-in description was not printed as written: %q", summary)
	}
	if summary == "Disable include components" {
		t.Errorf("the synthesized summary replaced the embedded description: %q", summary)
	}
}

// Verifies a plain (non-negated) option is unaffected by provenance, since its description already
// reads correctly against its own flag name.
func TestOptionSummaryKeepsPlainOptionDescriptions(t *testing.T) {
	summary := OptionSummary("my-custom-command", "Amount", tools.ToolProperty{
		Type:        "number",
		Description: "How much to apply",
	})

	if summary != "How much to apply" {
		t.Errorf("a plain option's description must be printed as written: %q", summary)
	}
}

// Verifies the usage placeholder for each schema type and the default/values text in each description,
// and that hidden properties are left out of the listing.
func TestVisibleOptionHelpEntriesForToolRendersUsageAndDefaults(t *testing.T) {
	tool := toolWithProperties("my-custom-command", map[string]tools.ToolProperty{
		"Count":   {Type: "integer", Description: "How many", Default: 3},
		"Ratio":   {Type: "number", Description: "Scale"},
		"Items":   {Type: "array", Description: "Entries"},
		"Payload": {Type: "object", Description: "Extra data"},
		"Label":   {Type: "string", Description: "Text label", Default: ""},
		"Mode":    {Type: "string", Description: "Mode to use", DefaultValue: float64(1), Enum: []string{"Fast", "Slow"}},
		"Verbose": {Type: "boolean", Description: "Print more", Default: false},
		"Secret":  {Type: "string", Hidden: true},
	})

	actual := VisibleOptionHelpEntriesForTool(tool)

	expected := []OptionHelpEntry{
		{Name: "--count", Usage: "--count <integer>", Description: "How many; default: 3"},
		{Name: "--items", Usage: "--items <value[,value]>", Description: "Entries"},
		{Name: "--label", Usage: "--label <value>", Description: "Text label"},
		{Name: "--mode", Usage: "--mode <value>", Description: "Mode to use; default: Slow; values: Fast|Slow"},
		{Name: "--payload", Usage: "--payload <json>", Description: "Extra data"},
		{Name: "--ratio", Usage: "--ratio <number>", Description: "Scale"},
		{Name: "--verbose", Usage: "--verbose", Description: "Print more; default: disabled"},
	}
	if !reflect.DeepEqual(actual, expected) {
		t.Fatalf("help entries mismatch:\nactual:   %#v\nexpected: %#v", actual, expected)
	}
}

// Verifies that the CLI-only --code-file and --skip-compile help rows are added when the schema lacks
// them and that a schema-declared row is kept instead of being duplicated.
func TestVisibleOptionHelpEntriesForToolAddsCLIOnlyRowsOnce(t *testing.T) {
	cases := []struct {
		name     string
		tool     tools.ToolDefinition
		expected []OptionHelpEntry
	}{
		{
			name: "code-file added",
			tool: toolWithProperties(executeDynamicCodeCommandName, map[string]tools.ToolProperty{}),
			expected: []OptionHelpEntry{
				{Name: DynamicCodeFileOptionName, Usage: DynamicCodeFileOptionUsage, Description: DynamicCodeFileOptionDescription},
			},
		},
		{
			name: "code-file already declared",
			tool: toolWithProperties(executeDynamicCodeCommandName, map[string]tools.ToolProperty{
				"CodeFile": {Type: "string", Description: "Schema code file"},
			}),
			expected: []OptionHelpEntry{
				{Name: DynamicCodeFileOptionName, Usage: DynamicCodeFileOptionName + " <value>", Description: "Schema code file"},
			},
		},
		{
			name: "skip-compile added",
			tool: toolWithProperties(runTestsCommandName, map[string]tools.ToolProperty{}),
			expected: []OptionHelpEntry{
				{Name: RunTestsSkipCompileOptionName, Usage: RunTestsSkipCompileOptionUsage, Description: RunTestsSkipCompileOptionDescription},
			},
		},
		{
			name: "skip-compile already declared",
			tool: toolWithProperties(runTestsCommandName, map[string]tools.ToolProperty{
				"SkipCompile": {Type: "boolean", Description: "Schema skip compile"},
			}),
			expected: []OptionHelpEntry{
				{Name: RunTestsSkipCompileOptionName, Usage: RunTestsSkipCompileOptionName, Description: "Schema skip compile"},
			},
		},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			actual := VisibleOptionHelpEntriesForTool(testCase.tool)
			if !reflect.DeepEqual(actual, testCase.expected) {
				t.Fatalf("help entries mismatch:\nactual:   %#v\nexpected: %#v", actual, testCase.expected)
			}
		})
	}
}
