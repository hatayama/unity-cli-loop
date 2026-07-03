package automation

import (
	"errors"
	"strings"
	"testing"
)

// Verifies dispatcher contract generations cannot move backwards.
func TestDispatcherContractGuardRejectsContractVersionDecrease(t *testing.T) {
	result := AnalyzeDispatcherContractGuard(
		DispatcherContractValues{HasContract: true, DispatcherVersion: "3.0.1", DispatcherContractVersion: 2},
		DispatcherContractValues{HasContract: true, DispatcherVersion: "3.0.2", DispatcherContractVersion: 1},
	)

	if !result.DispatcherContractVersionDecreased {
		t.Fatal("expected a contract version decrease to be detected")
	}
	if !dispatcherContractGuardNeedsAction(result) {
		t.Fatal("expected the guard to fail on a contract version decrease")
	}
}

// Verifies unchanged and increased contract versions pass the guard.
func TestDispatcherContractGuardAcceptsSameAndIncreasedContractVersions(t *testing.T) {
	base := DispatcherContractValues{HasContract: true, DispatcherVersion: "3.0.1", DispatcherContractVersion: 1}
	for _, headContractVersion := range []int{1, 2} {
		head := DispatcherContractValues{HasContract: true, DispatcherVersion: "3.0.2", DispatcherContractVersion: headContractVersion}
		result := AnalyzeDispatcherContractGuard(base, head)
		if dispatcherContractGuardNeedsAction(result) {
			t.Fatalf("expected head contract version %d to pass", headContractVersion)
		}
	}
}

// Verifies the first dispatcher contract can be introduced without comparing against a missing base.
func TestDispatcherContractGuardAcceptsInitialContract(t *testing.T) {
	result := AnalyzeDispatcherContractGuard(
		DispatcherContractValues{},
		DispatcherContractValues{HasContract: true, DispatcherVersion: "3.0.1", DispatcherContractVersion: 1},
	)

	if dispatcherContractGuardNeedsAction(result) {
		t.Fatal("expected an initial contract introduction to pass")
	}
}

// Verifies a base ref without the contract at the split or legacy path is treated as initial introduction.
func TestDispatcherContractGuardTreatsMissingBaseContractAsInitial(t *testing.T) {
	missingErr := errors.New("fatal: path 'cli/dispatcher-contract.json' does not exist in 'origin/main'")

	values, err := parseDispatcherContractBaseValues("", missingErr)
	if err != nil {
		t.Fatalf("expected a missing base contract to be tolerated, got %v", err)
	}
	if values.HasContract {
		t.Fatal("expected a missing base contract to produce zero values")
	}
}

// Verifies base contract read failures other than missing files do not get silently ignored.
func TestDispatcherContractGuardRejectsUnexpectedBaseReadError(t *testing.T) {
	readErr := errors.New("fatal: not a git repository")

	_, err := parseDispatcherContractBaseValues("", readErr)

	if err == nil {
		t.Fatal("expected an unexpected read error to propagate")
	}
}

// Verifies the head contract must parse as a valid dispatcher contract.
func TestParseDispatcherContractValuesValidation(t *testing.T) {
	cases := []struct {
		name    string
		content string
	}{
		{name: "invalid JSON", content: "{"},
		{name: "missing dispatcherVersion", content: `{"dispatcherContractVersion": 1}`},
		{name: "non-semver dispatcherVersion", content: `{"dispatcherVersion": "next", "dispatcherContractVersion": 1}`},
		{name: "contract version below 1", content: `{"dispatcherVersion": "3.0.1", "dispatcherContractVersion": 0}`},
	}

	for _, testCase := range cases {
		_, err := ParseDispatcherContractValues([]byte(testCase.content))
		if err == nil {
			t.Fatalf("expected %s to be rejected", testCase.name)
		}
	}
}

// Verifies a valid contract parses into its version values.
func TestParseDispatcherContractValuesReadsContract(t *testing.T) {
	values, err := ParseDispatcherContractValues([]byte(`{"dispatcherVersion": "3.0.1-beta.11", "dispatcherContractVersion": 1}`))
	if err != nil {
		t.Fatalf("expected a valid contract to parse, got %v", err)
	}
	if !values.HasContract || values.DispatcherVersion != "3.0.1-beta.11" || values.DispatcherContractVersion != 1 {
		t.Fatalf("unexpected parsed values: %+v", values)
	}
}

// Verifies the warning explains that the contract generation moved backwards.
func TestFormatDispatcherContractWarningExplainsDecrease(t *testing.T) {
	result := AnalyzeDispatcherContractGuard(
		DispatcherContractValues{HasContract: true, DispatcherVersion: "3.0.1", DispatcherContractVersion: 2},
		DispatcherContractValues{HasContract: true, DispatcherVersion: "3.0.2", DispatcherContractVersion: 1},
	)

	warning := FormatDispatcherContractWarning(result)

	for _, expected := range []string{"dispatcherContractVersion", "moved backwards", "`2`", "`1`"} {
		if !strings.Contains(warning, expected) {
			t.Fatalf("expected warning to contain %q, got:\n%s", expected, warning)
		}
	}
}
