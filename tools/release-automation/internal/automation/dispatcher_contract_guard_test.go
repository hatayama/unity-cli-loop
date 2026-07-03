package automation

import (
	"context"
	"errors"
	"os"
	"path/filepath"
	"strings"
	"testing"
)

// Verifies dispatcher contract generations cannot move backwards.
func TestDispatcherContractGuardRejectsContractVersionDecrease(t *testing.T) {
	result := AnalyzeDispatcherContractGuard(
		DispatcherContractValues{HasContract: true, DispatcherContractVersion: 2},
		DispatcherContractValues{HasContract: true, DispatcherContractVersion: 1},
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
	base := DispatcherContractValues{HasContract: true, DispatcherContractVersion: 1}
	for _, headContractVersion := range []int{1, 2} {
		head := DispatcherContractValues{HasContract: true, DispatcherContractVersion: headContractVersion}
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
		DispatcherContractValues{HasContract: true, DispatcherContractVersion: 1},
	)

	if dispatcherContractGuardNeedsAction(result) {
		t.Fatal("expected an initial contract introduction to pass")
	}
}

// Verifies a base ref without the contract at either the split or legacy path
// is treated as an initial introduction (zero values, no error).
func TestDispatcherContractGuardTreatsMissingBaseContractAsInitial(t *testing.T) {
	repoRoot := setupDispatcherContractGuardMockGit(t, dispatcherContractGuardMockState{
		refExists:     true,
		primaryExists: false,
		legacyExists:  false,
	})
	readErr := errors.New("fatal: dispatcher/dispatcher-contract.json missing at origin/main")

	values, err := parseDispatcherContractBaseValues(
		context.Background(),
		repoRoot,
		"origin/main",
		"",
		readErr)
	if err != nil {
		t.Fatalf("expected a missing base contract to be tolerated, got %v", err)
	}
	if values.HasContract {
		t.Fatal("expected a missing base contract to produce zero values")
	}
}

// Verifies base contract read failures other than "both paths missing at an
// existing ref" propagate; a real read failure must not be silently ignored.
func TestDispatcherContractGuardRejectsUnexpectedBaseReadError(t *testing.T) {
	repoRoot := setupDispatcherContractGuardMockGit(t, dispatcherContractGuardMockState{
		refExists:     true,
		primaryExists: true,
		legacyExists:  false,
	})
	readErr := errors.New("fatal: unable to read tree")

	_, err := parseDispatcherContractBaseValues(
		context.Background(),
		repoRoot,
		"origin/main",
		"",
		readErr)

	if err == nil {
		t.Fatal("expected an unexpected read error to propagate")
	}
}

// Verifies a base ref that is not resolvable is not silently treated as
// initial; the read error propagates instead.
func TestDispatcherContractGuardPropagatesMissingBaseRef(t *testing.T) {
	repoRoot := setupDispatcherContractGuardMockGit(t, dispatcherContractGuardMockState{
		refExists: false,
	})
	readErr := errors.New("fatal: bad revision 'no-such-ref'")

	_, err := parseDispatcherContractBaseValues(
		context.Background(),
		repoRoot,
		"no-such-ref",
		"",
		readErr)

	if err == nil {
		t.Fatal("expected a missing ref to propagate the read error")
	}
}

// Verifies that when the existence probe used to classify a base read
// failure itself fails with a non-ExitError, the original read error text is
// still present in the returned error so operators see the real failure.
func TestDispatcherContractGuardPreservesReadErrWhenProbeFailsWithNonExitError(t *testing.T) {
	workDir := t.TempDir()
	emptyBin := filepath.Join(workDir, "empty-bin")
	if err := os.MkdirAll(emptyBin, 0o755); err != nil {
		t.Fatalf("failed to create empty bin: %v", err)
	}
	// Shadow PATH with a directory that has no git so exec.LookPath fails
	// at command startup, producing a non-ExitError from the probe.
	t.Setenv("PATH", emptyBin)

	readErr := errors.New("fatal: original show failure sentinel")

	_, err := parseDispatcherContractBaseValues(
		context.Background(),
		workDir,
		"origin/main",
		"",
		readErr)

	if err == nil {
		t.Fatal("expected an error when the classifier probe itself fails")
	}
	if !strings.Contains(err.Error(), "fatal: original show failure sentinel") {
		t.Fatalf("expected original read error text to be preserved, got: %v", err)
	}
}

type dispatcherContractGuardMockState struct {
	refExists     bool
	primaryExists bool
	legacyExists  bool
}

func setupDispatcherContractGuardMockGit(t *testing.T, state dispatcherContractGuardMockState) string {
	t.Helper()

	workDir, binDir := setupMockGitBin(t)
	writeExistenceMockGit(t, binDir, mockGitExistenceFixture{
		refResolves: state.refExists,
		paths: map[string]mockGitPathBehavior{
			dispatcherContractFile:       {exists: state.primaryExists},
			legacyDispatcherContractFile: {exists: state.legacyExists},
		},
	})
	return workDir
}

// Verifies the head contract must parse as a valid dispatcher contract.
// dispatcherVersion format is not validated here: this guard never consumes
// it, and TestDispatcherContractProvidesRuntimeVersion in the dispatcher
// module already pins the semver format on every PR.
func TestParseDispatcherContractValuesValidation(t *testing.T) {
	cases := []struct {
		name    string
		content string
	}{
		{name: "invalid JSON", content: "{"},
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
	if !values.HasContract || values.DispatcherContractVersion != 1 {
		t.Fatalf("unexpected parsed values: %+v", values)
	}
}

// Verifies the warning explains that the contract generation moved backwards.
func TestFormatDispatcherContractWarningExplainsDecrease(t *testing.T) {
	result := AnalyzeDispatcherContractGuard(
		DispatcherContractValues{HasContract: true, DispatcherContractVersion: 2},
		DispatcherContractValues{HasContract: true, DispatcherContractVersion: 1},
	)

	warning := FormatDispatcherContractWarning(result)

	for _, expected := range []string{"dispatcherContractVersion", "moved backwards", "`2`", "`1`"} {
		if !strings.Contains(warning, expected) {
			t.Fatalf("expected warning to contain %q, got:\n%s", expected, warning)
		}
	}
}
