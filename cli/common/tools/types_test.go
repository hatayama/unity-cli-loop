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

// Tests that the input schema wins whenever it carries a type, properties, or required names, and
// that the parameter schema is used only when the input schema is empty.
func TestToolDefinitionEffectiveInputSchema(t *testing.T) {
	parameterSchema := ToolInputSchema{Type: "parameter"}
	cases := []struct {
		name         string
		inputSchema  ToolInputSchema
		expectsInput bool
	}{
		{name: "type only", inputSchema: ToolInputSchema{Type: "object"}, expectsInput: true},
		{name: "properties only", inputSchema: ToolInputSchema{Properties: map[string]ToolProperty{"Name": {}}}, expectsInput: true},
		{name: "required only", inputSchema: ToolInputSchema{Required: []string{"Name"}}, expectsInput: true},
		{name: "empty", inputSchema: ToolInputSchema{}, expectsInput: false},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			tool := ToolDefinition{InputSchema: testCase.inputSchema, ParameterSchema: parameterSchema}

			usedParameterSchema := tool.EffectiveInputSchema().Type == "parameter"

			if usedParameterSchema == testCase.expectsInput {
				t.Fatalf("expected input schema=%v, got %#v", testCase.expectsInput, tool.EffectiveInputSchema())
			}
		})
	}
}
