package tooldocs

import (
	"reflect"
	"testing"

	"github.com/hatayama/unity-cli-loop/common/tools"
)

func toolWithProperties(name string, properties map[string]tools.ToolProperty) tools.ToolDefinition {
	return tools.ToolDefinition{
		Name:        name,
		InputSchema: tools.ToolInputSchema{Type: "object", Properties: properties},
	}
}

// Verifies that FindProperty maps a kebab-case flag back to its schema property, reports negated
// booleans, and reports a miss for an unknown flag.
func TestFindProperty(t *testing.T) {
	tool := toolWithProperties("get-hierarchy", map[string]tools.ToolProperty{
		"IncludeComponents": {Type: "boolean", Default: true},
		"MaxDepth":          {Type: "integer"},
	})

	cases := []struct {
		flag            string
		expectedName    string
		expectedNegated bool
		expectedFound   bool
	}{
		{flag: "no-include-components", expectedName: "IncludeComponents", expectedNegated: true, expectedFound: true},
		{flag: "max-depth", expectedName: "MaxDepth", expectedFound: true},
		{flag: "include-components"},
	}
	for _, testCase := range cases {
		t.Run(testCase.flag, func(t *testing.T) {
			name, property, negated, found := FindProperty(tool, testCase.flag)
			if name != testCase.expectedName || negated != testCase.expectedNegated || found != testCase.expectedFound {
				t.Fatalf("FindProperty(%q) = (%q, %v, %v), want (%q, %v, %v)",
					testCase.flag, name, negated, found, testCase.expectedName, testCase.expectedNegated, testCase.expectedFound)
			}
			if found && property.Type == "" {
				t.Fatalf("FindProperty(%q) returned an empty property", testCase.flag)
			}
		})
	}
}

// Verifies that visible option names skip hidden properties, are sorted, and use the
// compile-specific name for the negated ReloadExternalSceneChanges flag.
func TestVisibleOptionNamesForToolSkipsHiddenAndSorts(t *testing.T) {
	tool := toolWithProperties(compileCommandName, map[string]tools.ToolProperty{
		"ForceRecompile":                       {Type: "boolean"},
		ReloadExternalSceneChangesPropertyName: {Type: "boolean", Default: true},
		"InternalOnly":                         {Type: "string", Hidden: true},
	})

	actual := VisibleOptionNamesForTool(tool)

	expected := []string{"--force-recompile", "--stop-on-external-scene-changes"}
	if !reflect.DeepEqual(actual, expected) {
		t.Fatalf("option names mismatch:\nactual:   %#v\nexpected: %#v", actual, expected)
	}
}

// Verifies that the CLI-only --code-file and --skip-compile flags are added to their tools exactly once,
// whether or not the tool schema already declares them.
func TestVisibleOptionNamesForToolAddsCLIOnlyFlagsOnce(t *testing.T) {
	cases := []struct {
		name     string
		tool     tools.ToolDefinition
		expected []string
	}{
		{
			name:     "code-file added",
			tool:     toolWithProperties(executeDynamicCodeCommandName, map[string]tools.ToolProperty{"Code": {Type: "string"}}),
			expected: []string{"--code", DynamicCodeFileOptionName},
		},
		{
			name:     "code-file already declared",
			tool:     toolWithProperties(executeDynamicCodeCommandName, map[string]tools.ToolProperty{"CodeFile": {Type: "string"}}),
			expected: []string{DynamicCodeFileOptionName},
		},
		{
			name:     "skip-compile added",
			tool:     toolWithProperties(runTestsCommandName, map[string]tools.ToolProperty{"FilterValue": {Type: "string"}}),
			expected: []string{"--filter-value", RunTestsSkipCompileOptionName},
		},
		{
			name:     "skip-compile already declared",
			tool:     toolWithProperties(runTestsCommandName, map[string]tools.ToolProperty{"SkipCompile": {Type: "boolean"}}),
			expected: []string{RunTestsSkipCompileOptionName},
		},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			actual := VisibleOptionNamesForTool(testCase.tool)
			if !reflect.DeepEqual(actual, testCase.expected) {
				t.Fatalf("option names mismatch:\nactual:   %#v\nexpected: %#v", actual, testCase.expected)
			}
		})
	}
}

// Verifies that an empty property name converts to an empty flag name.
func TestPascalToKebabKeepsEmptyValue(t *testing.T) {
	if actual := pascalToKebab(""); actual != "" {
		t.Fatalf("pascalToKebab(\"\") = %q, want empty", actual)
	}
}
