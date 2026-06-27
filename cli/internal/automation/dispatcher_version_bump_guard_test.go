package automation

import (
	"errors"
	"strings"
	"testing"
)

// Verifies dispatcher-only changes do not require a dispatcher release bump.
func TestDispatcherVersionBumpGuardPassesWhenDispatcherInputsAreUnchanged(t *testing.T) {
	result := AnalyzeDispatcherVersionBumpGuard(
		[]string{"cli/internal/cli/compile_wait.go"},
		DispatcherVersionBumpValues{
			HasContract:               true,
			DispatcherVersion:         "1.0.0",
			DispatcherContractVersion: 1,
		},
		DispatcherVersionBumpValues{
			HasContract:               true,
			DispatcherVersion:         "1.0.0",
			DispatcherContractVersion: 1,
		})

	if dispatcherVersionBumpGuardNeedsAction(result) {
		t.Fatalf("expected unchanged dispatcher inputs to pass: %#v", result)
	}
}

// Verifies dispatcher release automation changes do not require a user-facing dispatcher bump.
func TestDispatcherVersionBumpGuardPassesWhenOnlyReleaseAutomationChanges(t *testing.T) {
	result := AnalyzeDispatcherVersionBumpGuard(
		[]string{"scripts/package-dispatcher.sh"},
		DispatcherVersionBumpValues{
			HasContract:               true,
			DispatcherVersion:         "1.0.0",
			DispatcherContractVersion: 1,
		},
		DispatcherVersionBumpValues{
			HasContract:               true,
			DispatcherVersion:         "1.0.0",
			DispatcherContractVersion: 1,
		})

	if dispatcherVersionBumpGuardNeedsAction(result) {
		t.Fatalf("expected release automation-only changes to pass: %#v", result)
	}
}

// Verifies dispatcher release inputs require a new dispatcherVersion.
func TestDispatcherVersionBumpGuardRequiresDispatcherVersionIncrease(t *testing.T) {
	result := AnalyzeDispatcherVersionBumpGuard(
		[]string{"cli/internal/cli/dispatcher.go"},
		DispatcherVersionBumpValues{
			HasContract:               true,
			DispatcherVersion:         "1.0.0",
			DispatcherContractVersion: 1,
		},
		DispatcherVersionBumpValues{
			HasContract:               true,
			DispatcherVersion:         "1.0.0",
			DispatcherContractVersion: 1,
		})

	if !dispatcherVersionBumpGuardNeedsAction(result) {
		t.Fatal("expected dispatcher input changes without a version increase to fail")
	}
	if result.DispatcherVersionIncreased {
		t.Fatalf("expected dispatcher version not to be marked increased: %#v", result)
	}
}

// Verifies the first dispatcher contract can be introduced without comparing against a missing base.
func TestDispatcherVersionBumpGuardAcceptsInitialDispatcherContract(t *testing.T) {
	result := AnalyzeDispatcherVersionBumpGuard(
		[]string{"cli/dispatcher-contract.json"},
		DispatcherVersionBumpValues{},
		DispatcherVersionBumpValues{
			HasContract:               true,
			DispatcherVersion:         "1.0.0",
			DispatcherContractVersion: 1,
		})

	if dispatcherVersionBumpGuardNeedsAction(result) {
		t.Fatalf("expected initial dispatcher contract introduction to pass: %#v", result)
	}
}

// Verifies only a missing base dispatcher contract is treated as initial introduction.
func TestDispatcherVersionBumpGuardAcceptsMissingBaseContractReadError(t *testing.T) {
	_, err := parseDispatcherVersionBumpBaseValues(
		"",
		errors.New("git show failed: fatal: path 'cli/dispatcher-contract.json' exists on disk, but not in 'origin/v3-beta'"))
	if err != nil {
		t.Fatalf("expected missing base contract to be accepted: %v", err)
	}
}

// Verifies base contract read failures other than missing files do not get silently ignored.
func TestDispatcherVersionBumpGuardRejectsUnexpectedBaseContractReadError(t *testing.T) {
	_, err := parseDispatcherVersionBumpBaseValues("", errors.New("git show failed: gh auth failed"))

	if err == nil {
		t.Fatal("expected unexpected base contract read error")
	}
	if !strings.Contains(err.Error(), "failed to read base cli/dispatcher-contract.json") {
		t.Fatalf("unexpected error: %v", err)
	}
}

// Verifies dispatcher contract generations cannot move backwards.
func TestDispatcherVersionBumpGuardRejectsDispatcherContractVersionDecrease(t *testing.T) {
	result := AnalyzeDispatcherVersionBumpGuard(
		[]string{"cli/internal/cli/dispatcher.go"},
		DispatcherVersionBumpValues{
			HasContract:               true,
			DispatcherVersion:         "1.0.0",
			DispatcherContractVersion: 2,
		},
		DispatcherVersionBumpValues{
			HasContract:               true,
			DispatcherVersion:         "1.0.1",
			DispatcherContractVersion: 1,
		})

	if !dispatcherVersionBumpGuardNeedsAction(result) {
		t.Fatal("expected dispatcher contract version decrease to fail")
	}
	if !result.DispatcherContractVersionDecreased {
		t.Fatalf("expected dispatcher contract version decrease flag: %#v", result)
	}
}
