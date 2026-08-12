package projectrunner

import (
	"strings"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"

	"github.com/hatayama/unity-cli-loop/common/tooldocs"
)

// pausePointExpectation is one --expect 'Name=value' assertion parsed from the CLI args.
type pausePointExpectation struct {
	Name     string
	Expected string
}

// pausePointExpectationResult is one entry of the Expectations array the CLI adds to a hit
// response when --expect was passed.
type pausePointExpectationResult struct {
	Name     string `json:"Name"`
	Expected string `json:"Expected"`
	Actual   string `json:"Actual,omitempty"`
	Passed   bool   `json:"Passed"`
	Found    bool   `json:"Found"`
}

// parsePausePointExpectFlagValue splits a raw --expect value into Name and Expected on the
// first "=" only, so an expected value that itself contains "=" (for example a connection
// string) still round-trips instead of being truncated at an inner "=".
func parsePausePointExpectFlagValue(value string) (pausePointExpectation, error) {
	name, expected, found := strings.Cut(value, "=")
	if !found || name == "" {
		return pausePointExpectation{}, &clierrors.ArgumentError{
			Message:      "Invalid --expect value: " + value,
			Option:       "--" + tooldocs.PausePointExpectFlagName,
			ExpectedType: "Name=value",
			NextActions:  []string{"Pass `--expect 'Name=value'`, for example `--expect 'Health=100'`."},
		}
	}
	return pausePointExpectation{Name: name, Expected: expected}, nil
}

// evaluatePausePointExpectations checks each expectation against variables — the hit's raw
// CapturedVariables, evaluated before --captured-variable-names/--captured-variables narrow or
// strip the response, so an --expect target is never silently dropped just because it was not
// also requested via --captured-variable-names. Matching is not scoped to a particular kind of
// variable: it searches Local, Parameter, InstanceField, and This entries alike by Name, since
// --expect callers care about the variable's name, not which kind of variable it is.
func evaluatePausePointExpectations(
	variables []pausePointCapturedVariable,
	expectations []pausePointExpectation,
) []pausePointExpectationResult {
	if len(expectations) == 0 {
		return nil
	}

	results := make([]pausePointExpectationResult, 0, len(expectations))
	for _, expectation := range expectations {
		result := pausePointExpectationResult{
			Name:     expectation.Name,
			Expected: expectation.Expected,
		}
		if variable, ok := findPausePointCapturedVariableByName(variables, expectation.Name); ok {
			result.Found = true
			if variable.Value != nil {
				result.Actual = *variable.Value
				result.Passed = result.Actual == expectation.Expected
			}
		}
		results = append(results, result)
	}
	return results
}

func findPausePointCapturedVariableByName(
	variables []pausePointCapturedVariable,
	name string,
) (pausePointCapturedVariable, bool) {
	for _, variable := range variables {
		if variable.Name == name {
			return variable, true
		}
	}
	return pausePointCapturedVariable{}, false
}

// allPausePointExpectationsPassed reports whether every expectation passed.
func allPausePointExpectationsPassed(results []pausePointExpectationResult) bool {
	for _, result := range results {
		if !result.Passed {
			return false
		}
	}
	return true
}

// buildPausePointExpectNotFoundWarning returns a hit-time warning when at least one --expect
// target was absent from CapturedVariables (Found=false). Empty when every expectation was found.
func buildPausePointExpectNotFoundWarning(results []pausePointExpectationResult) string {
	missingNames := make([]string, 0)
	for _, result := range results {
		if result.Found {
			continue
		}
		missingNames = append(missingNames, result.Name)
	}
	if len(missingNames) == 0 {
		return ""
	}

	return "Expected variable(s) not present in CapturedVariables: " +
		strings.Join(missingNames, ", ") +
		". This is a not-found result, not a value mismatch — check the variable name, and note that locals can be missing from hot-reload patched bodies compiled before this fix."
}
