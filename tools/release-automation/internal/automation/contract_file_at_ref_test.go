package automation

import (
	"context"
	"os"
	"path/filepath"
	"strings"
	"testing"
)

// Verifies the legacy fallback runs when the primary path is absent at an
// existing ref, so releases published before the directory split remain
// readable through the pre-split contract file.
func TestContractFileAtRefWithLegacyFallback_WhenPrimaryMissingAtExistingRef_FallsBackToLegacy(t *testing.T) {
	fixture := setupContractFileAtRefMockGit(t, contractFileAtRefFixture{
		refExists:           true,
		primaryExists:       false,
		primaryShowSucceeds: false,
		legacyContent:       "legacy-body",
		legacyShowSucceeds:  true,
	})

	content, err := contractFileAtRefWithLegacyFallback(
		context.Background(),
		fixture.repoRoot,
		"missing-primary-ref",
		"common/clicontract/contract.json",
		"cli/contract.json")
	if err != nil {
		t.Fatalf("expected legacy fallback to succeed, got err: %v", err)
	}
	if content != "legacy-body" {
		t.Fatalf("expected legacy body, got %q", content)
	}
}

// Verifies a show failure on a primary file that actually exists at the ref
// propagates the original error instead of masquerading as "missing" and
// falling back to the legacy path.
func TestContractFileAtRefWithLegacyFallback_WhenPrimaryExistsButShowFails_ReturnsShowError(t *testing.T) {
	fixture := setupContractFileAtRefMockGit(t, contractFileAtRefFixture{
		refExists:           true,
		primaryExists:       true,
		primaryShowSucceeds: false,
		primaryShowStderr:   "fatal: unable to read tree",
		legacyShowSucceeds:  true,
		legacyContent:       "legacy-body",
	})

	content, err := contractFileAtRefWithLegacyFallback(
		context.Background(),
		fixture.repoRoot,
		"broken-primary-ref",
		"common/clicontract/contract.json",
		"cli/contract.json")

	if err == nil {
		t.Fatalf("expected show error to propagate, got content %q", content)
	}
	if !strings.Contains(err.Error(), "unable to read tree") {
		t.Fatalf("expected propagated show error message, got %v", err)
	}
	if content != "" {
		t.Fatalf("expected no content when propagating, got %q", content)
	}
}

// Verifies a missing base ref is not misclassified as a missing file:
// the original show error propagates and the dispatcher guard therefore
// does NOT treat it as an initial (no-contract) base.
func TestContractFileAtRefWithLegacyFallback_WhenRefMissing_PropagatesShowError(t *testing.T) {
	fixture := setupContractFileAtRefMockGit(t, contractFileAtRefFixture{
		refExists:           false,
		primaryShowSucceeds: false,
		primaryShowStderr:   "fatal: bad revision 'no-such-ref'",
		legacyShowSucceeds:  false,
	})

	content, err := contractFileAtRefWithLegacyFallback(
		context.Background(),
		fixture.repoRoot,
		"no-such-ref",
		"common/clicontract/contract.json",
		"cli/contract.json")

	if err == nil {
		t.Fatalf("expected missing-ref error to propagate, got content %q", content)
	}
	if content != "" {
		t.Fatalf("expected no content on missing ref, got %q", content)
	}

	// Confirm the dispatcher guard does not treat a missing ref as initial.
	missing, guardErr := isMissingDispatcherContractAtRefError(
		context.Background(),
		fixture.repoRoot,
		"no-such-ref")
	if guardErr != nil {
		t.Fatalf("expected guard to run without executor error, got %v", guardErr)
	}
	if missing {
		t.Fatal("expected missing-ref to NOT be treated as an initial contract by the guard")
	}
}

// Verifies a non-ExitError from the presence probe after a show failure
// preserves the original show error text alongside the classification
// failure so operators see both signals.
func TestContractFileAtRefWithLegacyFallback_WhenProbeFailsWithNonExitError_PreservesShowError(t *testing.T) {
	workDir := t.TempDir()
	emptyBin := filepath.Join(workDir, "empty-bin")
	if err := os.MkdirAll(emptyBin, 0o755); err != nil {
		t.Fatalf("failed to create empty bin: %v", err)
	}
	// Shadow PATH with a directory that has no git so both the show call and
	// the follow-up presence probe fail with a non-ExitError startup failure.
	t.Setenv("PATH", emptyBin)

	_, err := contractFileAtRefWithLegacyFallback(
		context.Background(),
		workDir,
		"some-ref",
		"common/clicontract/contract.json",
		"cli/contract.json")

	if err == nil {
		t.Fatal("expected an error when both show and probe fail")
	}
	if !strings.Contains(err.Error(), "show some-ref:common/clicontract/contract.json") {
		t.Fatalf("expected the original show error text to be preserved, got: %v", err)
	}
	if !strings.Contains(err.Error(), "cat-file") {
		t.Fatalf("expected the classification failure detail to be included, got: %v", err)
	}
}

type contractFileAtRefFixture struct {
	refExists           bool
	primaryExists       bool
	primaryShowSucceeds bool
	primaryShowStderr   string
	legacyShowSucceeds  bool
	legacyContent       string
}

type contractFileAtRefFixtureRun struct {
	repoRoot string
}

func setupContractFileAtRefMockGit(t *testing.T, fixture contractFileAtRefFixture) contractFileAtRefFixtureRun {
	t.Helper()

	workDir, binDir := setupMockGitBin(t)

	legacyPath := filepath.Join(workDir, "legacy.txt")
	writeFile(t, legacyPath, fixture.legacyContent)

	// The legacy path is treated as absent by cat-file unless a legacy show
	// is set up, preserving the pre-refactor mock behavior where the legacy
	// existence and show signals were tied together via MOCK_LEGACY_SHOW_OK.
	writeExistenceMockGit(t, binDir, mockGitExistenceFixture{
		refResolves: fixture.refExists,
		paths: map[string]mockGitPathBehavior{
			cliContractFile: {
				exists:      fixture.primaryExists,
				showOK:      fixture.primaryShowSucceeds,
				showContent: "primary-body",
				showStderr:  fixture.primaryShowStderr,
			},
			legacyRunnerContractFile: {
				exists:          fixture.legacyShowSucceeds,
				showOK:          fixture.legacyShowSucceeds,
				showContentPath: legacyPath,
				showStderr:      "fatal: legacy show failed",
			},
			dispatcherContractFile: {exists: false},
		},
	})

	return contractFileAtRefFixtureRun{repoRoot: workDir}
}
