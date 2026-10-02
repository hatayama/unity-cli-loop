package tools

import "testing"

// Tests that EffectiveDefault prefers Default and falls back to DefaultValue when Default is unset.
func TestToolPropertyEffectiveDefault(t *testing.T) {
	cases := []struct {
		name     string
		property ToolProperty
		expected any
	}{
		{name: "default wins", property: ToolProperty{Default: "a", DefaultValue: "b"}, expected: "a"},
		{name: "defaultValue fallback", property: ToolProperty{DefaultValue: "b"}, expected: "b"},
		{name: "neither", property: ToolProperty{}, expected: nil},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			if actual := testCase.property.EffectiveDefault(); actual != testCase.expected {
				t.Fatalf("EffectiveDefault() = %#v, want %#v", actual, testCase.expected)
			}
		})
	}
}

// Tests that the parameter schema is used only when the input schema carries no values.
func TestToolDefinitionEffectiveInputSchema(t *testing.T) {
	parameterSchema := ToolInputSchema{Required: []string{"Name"}}
	withInput := ToolDefinition{InputSchema: ToolInputSchema{Type: "object"}, ParameterSchema: parameterSchema}
	withoutInput := ToolDefinition{ParameterSchema: parameterSchema}

	if schema := withInput.EffectiveInputSchema(); schema.Type != "object" {
		t.Fatalf("input schema should win, got %#v", schema)
	}
	if schema := withoutInput.EffectiveInputSchema(); len(schema.Required) != 1 || schema.Required[0] != "Name" {
		t.Fatalf("parameter schema should be used, got %#v", schema)
	}
}
