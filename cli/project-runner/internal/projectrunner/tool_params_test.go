package projectrunner

import (
	"errors"
	"strings"
	"testing"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"

	"github.com/hatayama/unity-cli-loop/common/clicore"
)

// Verifies that an unknown option error guides users to `<tool> --help` and never to the non-existent `--list-options`.
func TestBuildToolParamsUnknownOptionNextActionsGuideToHelp(t *testing.T) {
	tool := clicore.ToolDefinition{
		Name: "sample-tool",
		InputSchema: clicore.InputSchema{
			Properties: map[string]clicore.ToolProperty{
				"Enabled": {Type: "boolean"},
			},
		},
	}

	_, _, err := buildToolParams([]string{"--unknown-flag"}, tool)
	if err == nil {
		t.Fatal("expected an error for an unknown option")
	}

	var argumentError *clierrors.ArgumentError
	if !errors.As(err, &argumentError) {
		t.Fatalf("expected an *ArgumentError, got %T: %v", err, err)
	}
	if len(argumentError.NextActions) != 1 {
		t.Fatalf("expected exactly one NextAction, got %#v", argumentError.NextActions)
	}
	want := "Run `uloop sample-tool --help` to inspect supported options."
	if argumentError.NextActions[0] != want {
		t.Fatalf("NextActions mismatch:\nwant: %q\ngot:  %q", want, argumentError.NextActions[0])
	}
	if strings.Contains(argumentError.NextActions[0], "--list-options") {
		t.Fatalf("NextActions must not mention --list-options: %#v", argumentError.NextActions)
	}
}

// Verifies that a string property with an enum accepts an exact-case valid value.
func TestConvertValueAcceptsExactCaseEnumValue(t *testing.T) {
	property := clicore.ToolProperty{Type: "string", Enum: []string{"Play", "Stop", "Pause"}}

	converted, err := convertValue("Stop", property, "--action")
	if err != nil {
		t.Fatalf("expected no error, got: %v", err)
	}
	if converted != "Stop" {
		t.Fatalf("expected converted value %q, got %q", "Stop", converted)
	}
}

// Verifies that enum matching ignores case, mirroring the C# CaseInsensitiveStringEnumConverter.
func TestConvertValueAcceptsCaseInsensitiveEnumValue(t *testing.T) {
	property := clicore.ToolProperty{Type: "string", Enum: []string{"Play", "Stop", "Pause"}}

	converted, err := convertValue("stop", property, "--action")
	if err != nil {
		t.Fatalf("expected no error, got: %v", err)
	}
	if converted != "stop" {
		t.Fatalf("expected the original value to be passed through unchanged, got %q", converted)
	}
}

// Verifies that a value outside the enum is rejected with the valid value list included.
func TestConvertValueRejectsInvalidEnumValue(t *testing.T) {
	property := clicore.ToolProperty{Type: "string", Enum: []string{"Play", "Stop", "Pause"}}

	_, err := convertValue("bogus", property, "--action")

	if err == nil {
		t.Fatal("expected an error for a value outside the enum")
	}
	var argumentError *clierrors.ArgumentError
	if !errors.As(err, &argumentError) {
		t.Fatalf("expected an *ArgumentError, got %T: %v", err, err)
	}
	if argumentError.Received != "bogus" {
		t.Fatalf("expected Received to be %q, got %q", "bogus", argumentError.Received)
	}
	for _, want := range property.Enum {
		if !strings.Contains(argumentError.ExpectedType, want) {
			t.Fatalf("expected error to list valid value %q, got: %s", want, argumentError.ExpectedType)
		}
	}
}

// Verifies that a string property without an enum passes any value through unchanged.
func TestConvertValuePassesThroughStringWithoutEnum(t *testing.T) {
	property := clicore.ToolProperty{Type: "string"}

	converted, err := convertValue("anything", property, "--label")
	if err != nil {
		t.Fatalf("expected no error, got: %v", err)
	}
	if converted != "anything" {
		t.Fatalf("expected converted value %q, got %q", "anything", converted)
	}
}

func toolParamsSuggestionTool() clicore.ToolDefinition {
	return clicore.ToolDefinition{
		Name: "sample-tool",
		InputSchema: clicore.InputSchema{
			Properties: map[string]clicore.ToolProperty{
				"Action": {
					Type: "string",
					Enum: []string{"Play", "Stop", "Pause", "Status"},
				},
				"OutputDirectory": {
					Type: "string",
				},
			},
		},
	}
}

func requireArgumentError(t *testing.T, err error) *clierrors.ArgumentError {
	t.Helper()
	if err == nil {
		t.Fatal("expected an error")
	}
	var argumentError *clierrors.ArgumentError
	if !errors.As(err, &argumentError) {
		t.Fatalf("expected an *ArgumentError, got %T: %v", err, err)
	}
	return argumentError
}

func requireNextActions(t *testing.T, err error, want []string) {
	t.Helper()
	argumentError := requireArgumentError(t, err)
	if len(argumentError.NextActions) != len(want) {
		t.Fatalf("NextActions length mismatch:\nwant: %#v\ngot:  %#v", want, argumentError.NextActions)
	}
	for index := range want {
		if argumentError.NextActions[index] != want[index] {
			t.Fatalf("NextActions mismatch at %d:\nwant: %#v\ngot:  %#v", index, want, argumentError.NextActions)
		}
	}
}

// Verifies a positional token that matches an option enum value suggests the --option Value form.
func TestBuildToolParamsUnexpectedArgumentSuggestsMatchingEnumOption(t *testing.T) {
	_, _, err := buildToolParams([]string{"status"}, toolParamsSuggestionTool())
	requireNextActions(t, err, []string{
		"Did you mean: uloop sample-tool --action Status",
		"Pass tool inputs as `--option value` pairs.",
	})
}

// Verifies a positional token that matches no enum keeps only the original NextAction.
func TestBuildToolParamsUnexpectedArgumentWithoutEnumMatchKeepsOriginalNextAction(t *testing.T) {
	_, _, err := buildToolParams([]string{"not-an-enum"}, toolParamsSuggestionTool())
	requireNextActions(t, err, []string{
		"Pass tool inputs as `--option value` pairs.",
	})
}

// Verifies a leftover token after an array option suggests a comma-separated list for that option.
func TestBuildToolParamsUnexpectedArgumentAfterArraySuggestsCommaSeparatedList(t *testing.T) {
	tool := clicore.ToolDefinition{
		Name: "sample-tool",
		InputSchema: clicore.InputSchema{
			Properties: map[string]clicore.ToolProperty{
				"Tags": {Type: "array"},
			},
		},
	}

	_, _, err := buildToolParams([]string{"--tags", "alpha", "beta"}, tool)
	requireNextActions(t, err, []string{
		"Pass multiple values as one comma-separated list: --tags value1,value2",
		"Pass tool inputs as `--option value` pairs.",
	})
}

// Verifies a leftover token after a non-array option does not suggest a comma-separated list.
func TestBuildToolParamsUnexpectedArgumentAfterStringOmitsCommaSeparatedList(t *testing.T) {
	_, _, err := buildToolParams([]string{"--output-directory", "out", "extra"}, toolParamsSuggestionTool())
	requireNextActions(t, err, []string{
		"Pass tool inputs as `--option value` pairs.",
	})
}

// Verifies an unknown flag whose name matches an option enum value suggests that option and value.
func TestBuildToolParamsUnknownOptionSuggestsMatchingEnumValue(t *testing.T) {
	_, _, err := buildToolParams([]string{"--status"}, toolParamsSuggestionTool())
	requireNextActions(t, err, []string{
		"Did you mean: uloop sample-tool --action Status",
		"Run `uloop sample-tool --help` to inspect supported options.",
	})
}

// Verifies a close typo of a known option name suggests that option via edit distance.
func TestBuildToolParamsUnknownOptionSuggestsCloseTypo(t *testing.T) {
	_, _, err := buildToolParams([]string{"--output-directry"}, toolParamsSuggestionTool())
	requireNextActions(t, err, []string{
		"Did you mean: uloop sample-tool --output-directory",
		"Run `uloop sample-tool --help` to inspect supported options.",
	})
}

// Verifies an unknown flag that shares a kebab first token with a known option suggests that option.
func TestBuildToolParamsUnknownOptionSuggestsSharedKebabToken(t *testing.T) {
	_, _, err := buildToolParams([]string{"--output-path"}, toolParamsSuggestionTool())
	requireNextActions(t, err, []string{
		"Did you mean: uloop sample-tool --output-directory",
		"Run `uloop sample-tool --help` to inspect supported options.",
	})
}

// Verifies an unrelated unknown flag keeps only the --help NextAction.
func TestBuildToolParamsUnknownOptionWithoutSuggestionKeepsHelpNextAction(t *testing.T) {
	_, _, err := buildToolParams([]string{"--zzzzzzzz"}, toolParamsSuggestionTool())
	requireNextActions(t, err, []string{
		"Run `uloop sample-tool --help` to inspect supported options.",
	})
}

// Verifies a 2-character shared kebab prefix such as "no" does not pair unrelated flags.
func TestBuildToolParamsUnknownOptionIgnoresShortSharedKebabToken(t *testing.T) {
	tool := clicore.ToolDefinition{
		Name: "sample-tool",
		InputSchema: clicore.InputSchema{
			Properties: map[string]clicore.ToolProperty{
				"Cache": {Type: "boolean", Default: true},
			},
		},
	}

	_, _, err := buildToolParams([]string{"--no-foo"}, tool)
	requireNextActions(t, err, []string{
		"Run `uloop sample-tool --help` to inspect supported options.",
	})
}

// Verifies an enum-value match beats a close option-name typo so --status suggests --action Status, not --stat.
func TestBuildToolParamsUnknownOptionPrefersEnumMatchOverCloseOptionName(t *testing.T) {
	tool := clicore.ToolDefinition{
		Name: "sample-tool",
		InputSchema: clicore.InputSchema{
			Properties: map[string]clicore.ToolProperty{
				"Action": {Type: "string", Enum: []string{"Status"}},
				"Stat":   {Type: "string"},
			},
		},
	}

	_, _, err := buildToolParams([]string{"--status"}, tool)
	requireNextActions(t, err, []string{
		"Did you mean: uloop sample-tool --action Status",
		"Run `uloop sample-tool --help` to inspect supported options.",
	})
}

// Verifies a close typo beats a shared kebab token so --time-out suggests --timeout, not --time-scale.
func TestBuildToolParamsUnknownOptionPrefersCloseTypoOverSharedKebabToken(t *testing.T) {
	tool := clicore.ToolDefinition{
		Name: "sample-tool",
		InputSchema: clicore.InputSchema{
			Properties: map[string]clicore.ToolProperty{
				"Timeout":   {Type: "integer"},
				"TimeScale": {Type: "number"},
			},
		},
	}

	_, _, err := buildToolParams([]string{"--time-out"}, tool)
	requireNextActions(t, err, []string{
		"Did you mean: uloop sample-tool --timeout",
		"Run `uloop sample-tool --help` to inspect supported options.",
	})
}

// Verifies an unknown flag at edit distance 2 still suggests the nearest option (threshold inclusive).
func TestBuildToolParamsUnknownOptionSuggestsAtEditDistanceTwo(t *testing.T) {
	tool := clicore.ToolDefinition{
		Name: "sample-tool",
		InputSchema: clicore.InputSchema{
			Properties: map[string]clicore.ToolProperty{
				"Action": {Type: "string"},
			},
		},
	}

	_, _, err := buildToolParams([]string{"--acxxon"}, tool)
	requireNextActions(t, err, []string{
		"Did you mean: uloop sample-tool --action",
		"Run `uloop sample-tool --help` to inspect supported options.",
	})
}

// Verifies an unknown flag at edit distance 3 does not suggest an option past the threshold.
func TestBuildToolParamsUnknownOptionDoesNotSuggestAtEditDistanceThree(t *testing.T) {
	tool := clicore.ToolDefinition{
		Name: "sample-tool",
		InputSchema: clicore.InputSchema{
			Properties: map[string]clicore.ToolProperty{
				"Action": {Type: "string"},
			},
		},
	}

	_, _, err := buildToolParams([]string{"--acxxxn"}, tool)
	requireNextActions(t, err, []string{
		"Run `uloop sample-tool --help` to inspect supported options.",
	})
}

// Verifies equal edit-distance option names suggest the sorted-first property name only.
func TestBuildToolParamsUnknownOptionTieBreaksToSortedFirstOptionName(t *testing.T) {
	tool := clicore.ToolDefinition{
		Name: "sample-tool",
		InputSchema: clicore.InputSchema{
			Properties: map[string]clicore.ToolProperty{
				"OutputPile": {Type: "string"},
				"OutputFile": {Type: "string"},
			},
		},
	}

	_, _, err := buildToolParams([]string{"--output-bile"}, tool)
	requireNextActions(t, err, []string{
		"Did you mean: uloop sample-tool --output-file",
		"Run `uloop sample-tool --help` to inspect supported options.",
	})
}
