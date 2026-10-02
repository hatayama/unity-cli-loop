package clicore

import (
	"errors"
	"reflect"
	"testing"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
)

// Verifies that --project-path=<value> is consumed in place and a trailing --project-path without a
// value is rejected as a missing-value argument error.
func TestParseGlobalProjectPathHandlesInlineAndMissingValues(t *testing.T) {
	remaining, projectPath, err := ParseGlobalProjectPath([]string{"compile", "--project-path=project-dir", "--force-recompile"})
	if err != nil {
		t.Fatalf("ParseGlobalProjectPath failed: %v", err)
	}
	if projectPath != "project-dir" || !reflect.DeepEqual(remaining, []string{"compile", "--force-recompile"}) {
		t.Fatalf("unexpected parse result: path=%q remaining=%#v", projectPath, remaining)
	}

	_, _, err = ParseGlobalProjectPath([]string{"compile", "--project-path"})
	var argumentError *clierrors.ArgumentError
	if !errors.As(err, &argumentError) || argumentError.Option != "--project-path" {
		t.Fatalf("expected a missing-value error for --project-path, got %v", err)
	}
}

// Verifies how ParseFlagValue splits inline values, consumes the next token, and rejects missing values.
func TestParseFlagValue(t *testing.T) {
	cases := []struct {
		name             string
		args             []string
		expectedName     string
		expectedValue    string
		expectedConsumed bool
		expectedErrorOpt string
		expectedErrorMsg string
	}{
		{name: "bare double dash", args: []string{"--"}, expectedErrorOpt: "--", expectedErrorMsg: "Invalid option: --"},
		{name: "inline value", args: []string{"--count=3"}, expectedName: "count", expectedValue: "3"},
		{name: "inline empty value", args: []string{"--count="}, expectedErrorOpt: "--count"},
		{name: "next token value", args: []string{"--count", "3"}, expectedName: "count", expectedValue: "3", expectedConsumed: true},
		{name: "negative number value", args: []string{"--offset", "-1.5"}, expectedName: "offset", expectedValue: "-1.5", expectedConsumed: true},
		{name: "last argument", args: []string{"--count"}, expectedErrorOpt: "--count"},
		{name: "followed by option", args: []string{"--count", "--verbose"}, expectedErrorOpt: "--count"},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			name, value, consumed, err := ParseFlagValue(testCase.args[0], testCase.args, 0)
			assertParseFlagValueError(t, err, testCase.expectedErrorOpt, testCase.expectedErrorMsg)
			if name != testCase.expectedName || value != testCase.expectedValue || consumed != testCase.expectedConsumed {
				t.Fatalf("ParseFlagValue = (%q, %q, %v), want (%q, %q, %v)",
					name, value, consumed, testCase.expectedName, testCase.expectedValue, testCase.expectedConsumed)
			}
		})
	}
}

func assertParseFlagValueError(t *testing.T, err error, expectedOption string, expectedMessage string) {
	t.Helper()
	if expectedOption == "" {
		if err != nil {
			t.Fatalf("unexpected error: %v", err)
		}
		return
	}
	var argumentError *clierrors.ArgumentError
	if !errors.As(err, &argumentError) || argumentError.Option != expectedOption {
		t.Fatalf("expected an argument error for %q, got %v", expectedOption, err)
	}
	if expectedMessage == "" {
		expectedMessage = expectedOption + " requires a value"
	}
	if argumentError.Message != expectedMessage {
		t.Fatalf("argument error message = %q, want %q", argumentError.Message, expectedMessage)
	}
}

// Verifies that only dash-prefixed tokens that are not numbers count as the next option.
func TestIsNextOptionToken(t *testing.T) {
	cases := map[string]bool{
		"value":     false,
		"-1":        false,
		"-0.5":      false,
		"-v":        true,
		"--verbose": true,
	}
	for value, expected := range cases {
		if actual := IsNextOptionToken(value); actual != expected {
			t.Errorf("IsNextOptionToken(%q) = %v, want %v", value, actual, expected)
		}
	}
}

// Verifies recognition of leading options, version, version JSON, and help requests.
func TestRequestShapePredicates(t *testing.T) {
	cases := []struct {
		name      string
		predicate func([]string) bool
		accepted  [][]string
		rejected  [][]string
	}{
		{
			name:      "version",
			predicate: IsVersionRequest,
			accepted:  [][]string{{"--version"}, {"-v"}},
			rejected:  [][]string{{"--version", "--json"}, {"version"}, {}},
		},
		{
			name:      "version json",
			predicate: IsVersionJSONRequest,
			accepted:  [][]string{{"--version", "--json"}, {"-v", "--json"}},
			rejected:  [][]string{{"--version"}, {"--json", "--version"}, {"--version", "--other"}},
		},
		{
			name:      "help",
			predicate: IsHelpRequest,
			accepted:  [][]string{{"--help"}, {"-h"}},
			rejected:  [][]string{{"compile", "--help"}, {"help"}, {"--help", "compile"}},
		},
		{
			name:      "contains help",
			predicate: ContainsHelpRequest,
			accepted:  [][]string{{"compile", "--help"}, {"-h"}},
			rejected:  [][]string{{"compile"}, {}},
		},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			assertPredicate(t, testCase.predicate, testCase.accepted, true)
			assertPredicate(t, testCase.predicate, testCase.rejected, false)
		})
	}
}

func assertPredicate(t *testing.T, predicate func([]string) bool, inputs [][]string, expected bool) {
	t.Helper()
	for _, input := range inputs {
		if actual := predicate(input); actual != expected {
			t.Errorf("predicate(%#v) = %v, want %v", input, actual, expected)
		}
	}
}

// Verifies that a command token starting with a dash is treated as an unknown leading option.
func TestIsUnknownLeadingOption(t *testing.T) {
	if !IsUnknownLeadingOption("--bogus") || !IsUnknownLeadingOption("-x") || IsUnknownLeadingOption("compile") {
		t.Fatal("leading option detection mismatch")
	}
}
