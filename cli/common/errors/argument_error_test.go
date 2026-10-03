package clierrors

import (
	"reflect"
	"testing"
)

// Verifies an ArgumentError reports its message as the error text and maps every
// populated field into the CLI error envelope, keeping its own command and actions.
func TestArgumentErrorToCLIErrorCarriesPopulatedFields(t *testing.T) {
	err := &ArgumentError{
		Message:      "bad value",
		Option:       "--count",
		Received:     "abc",
		ExpectedType: "integer",
		Command:      "run-tests",
		NextActions:  []string{"Pass a number."},
	}

	cliErr := err.ToCLIError(ErrorContext{ProjectRoot: "<PROJECT_ROOT>", Command: "compile"})

	if err.Error() != "bad value" {
		t.Fatalf("unexpected Error(): %q", err.Error())
	}
	want := CLIError{
		ErrorCode:   ErrorCodeInvalidArgument,
		Phase:       ErrorPhaseArgumentParsing,
		Message:     "bad value",
		ProjectRoot: "<PROJECT_ROOT>",
		Command:     "run-tests",
		NextActions: []string{"Pass a number."},
		Details: map[string]any{
			"Option":       "--count",
			"Received":     "abc",
			"ExpectedType": "integer",
		},
	}
	if !reflect.DeepEqual(cliErr, want) {
		t.Fatalf("unexpected CLI error:\n got: %#v\nwant: %#v", cliErr, want)
	}
}

// Verifies an ArgumentError without optional fields falls back to the context command,
// the default retry guidance, and empty details.
func TestArgumentErrorToCLIErrorFallsBackWhenFieldsAreEmpty(t *testing.T) {
	err := &ArgumentError{Message: "unknown flag"}

	cliErr := err.ToCLIError(ErrorContext{Command: "compile"})

	if cliErr.Command != "compile" {
		t.Fatalf("expected context command fallback, got %q", cliErr.Command)
	}
	if !reflect.DeepEqual(cliErr.NextActions, []string{"Correct the command arguments and retry."}) {
		t.Fatalf("unexpected default next actions: %#v", cliErr.NextActions)
	}
	if len(cliErr.Details) != 0 {
		t.Fatalf("expected no details for empty fields, got %#v", cliErr.Details)
	}
	if cliErr.Retryable || cliErr.SafeToRetry {
		t.Fatalf("argument errors must not be retryable: %#v", cliErr)
	}
}

// Verifies the missing-value constructor names the option and explains both value syntaxes.
func TestMissingValueArgumentErrorDescribesOption(t *testing.T) {
	err := MissingValueArgumentError("--filter")

	if err.Message != "--filter requires a value" || err.Option != "--filter" {
		t.Fatalf("unexpected message or option: %#v", err)
	}
	expectedActions := []string{"Pass a value after `--filter` or use `--filter=<value>`."}
	if !reflect.DeepEqual(err.NextActions, expectedActions) {
		t.Fatalf("unexpected next actions: %#v", err.NextActions)
	}
}

// Verifies the invalid-value constructor reports the received value and expected type.
func TestInvalidValueArgumentErrorDescribesReceivedValue(t *testing.T) {
	err := InvalidValueArgumentError("--count", "abc", "integer")

	want := &ArgumentError{
		Message:      "Invalid integer value for --count: abc",
		Option:       "--count",
		Received:     "abc",
		ExpectedType: "integer",
		NextActions:  []string{"Pass a valid integer value for `--count`."},
	}
	if !reflect.DeepEqual(err, want) {
		t.Fatalf("unexpected error:\n got: %#v\nwant: %#v", err, want)
	}
}
