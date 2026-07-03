package automation

import (
	"context"
	"errors"
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"
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
		cliContractFile,
		legacyRunnerContractFile)
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
		cliContractFile,
		legacyRunnerContractFile)

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
		cliContractFile,
		legacyRunnerContractFile)

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
		cliContractFile,
		legacyRunnerContractFile)

	if err == nil {
		t.Fatal("expected an error when both show and probe fail")
	}
	if !strings.Contains(err.Error(), "show some-ref:"+cliContractFile) {
		t.Fatalf("expected the original show error text to be preserved, got: %v", err)
	}
	if !strings.Contains(err.Error(), "cat-file") {
		t.Fatalf("expected the classification failure detail to be included, got: %v", err)
	}
}

// Verifies a probe killed by context cancellation reports an error instead of
// classifying the killed git process as "file absent": the kill produces an
// *exec.ExitError just like a genuine non-zero exit, and misreading it as
// absence would let the dispatcher guard treat a base as an initial contract.
func TestFileExistsAtRef_WhenContextCanceledMidProbe_ReturnsErrorInsteadOfAbsence(t *testing.T) {
	_, binDir := setupMockGitBin(t)
	writeSleepingMockGit(t, binDir)

	ctx, cancel := context.WithTimeout(context.Background(), 200*time.Millisecond)
	defer cancel()

	exists, err := fileExistsAtRef(ctx, t.TempDir(), "some-ref", cliContractFile)
	if err == nil {
		t.Fatalf("expected an error for an interrupted probe, got exists=%v", exists)
	}
	if !errors.Is(err, context.DeadlineExceeded) {
		t.Fatalf("expected the context error to be wrapped, got: %v", err)
	}
}

// Verifies the ref-existence probe applies the same cancellation handling as
// the file-existence probe, so a canceled rev-parse cannot masquerade as a
// missing ref.
func TestRefExistsAtRef_WhenContextCanceledMidProbe_ReturnsErrorInsteadOfAbsence(t *testing.T) {
	_, binDir := setupMockGitBin(t)
	writeSleepingMockGit(t, binDir)

	ctx, cancel := context.WithTimeout(context.Background(), 200*time.Millisecond)
	defer cancel()

	exists, err := refExistsAtRef(ctx, t.TempDir(), "some-ref")
	if err == nil {
		t.Fatalf("expected an error for an interrupted probe, got exists=%v", exists)
	}
	if !errors.Is(err, context.DeadlineExceeded) {
		t.Fatalf("expected the context error to be wrapped, got: %v", err)
	}
}

// Verifies the variadic fallback resolves the first existing legacy path when
// several are supplied, so newer generations take precedence over older ones.
func TestContractFileAtRefWithLegacyFallback_WithTwoLegacies_WhenFirstLegacyExists_UsesFirstLegacy(t *testing.T) {
	workDir, binDir := setupMockGitBin(t)
	firstLegacyPath := filepath.Join(workDir, "first-legacy.txt")
	writeFile(t, firstLegacyPath, "first-legacy-body")
	secondLegacyPath := filepath.Join(workDir, "second-legacy.txt")
	writeFile(t, secondLegacyPath, "second-legacy-body")

	writeExistenceMockGit(t, binDir, mockGitExistenceFixture{
		refResolves: true,
		paths: map[string]mockGitPathBehavior{
			"generation/primary.json": {exists: false, showOK: false, showStderr: "fatal: primary missing"},
			"generation/first.json":   {exists: true, showOK: true, showContentPath: firstLegacyPath},
			"generation/second.json":  {exists: true, showOK: true, showContentPath: secondLegacyPath},
		},
	})

	content, err := contractFileAtRefWithLegacyFallback(
		context.Background(),
		workDir,
		"some-ref",
		"generation/primary.json",
		"generation/first.json",
		"generation/second.json")
	if err != nil {
		t.Fatalf("expected first legacy to resolve, got err: %v", err)
	}
	if content != "first-legacy-body" {
		t.Fatalf("expected first legacy body, got %q", content)
	}
}

// Verifies the fallback skips an absent legacy generation and continues to the
// next entry, so multi-step directory moves stay traversable.
func TestContractFileAtRefWithLegacyFallback_WithTwoLegacies_WhenFirstLegacyAbsent_UsesSecondLegacy(t *testing.T) {
	workDir, binDir := setupMockGitBin(t)
	secondLegacyPath := filepath.Join(workDir, "second-legacy.txt")
	writeFile(t, secondLegacyPath, "second-legacy-body")

	writeExistenceMockGit(t, binDir, mockGitExistenceFixture{
		refResolves: true,
		paths: map[string]mockGitPathBehavior{
			"generation/primary.json": {exists: false, showOK: false, showStderr: "fatal: primary missing"},
			"generation/first.json":   {exists: false, showOK: false, showStderr: "fatal: first missing"},
			"generation/second.json":  {exists: true, showOK: true, showContentPath: secondLegacyPath},
		},
	})

	content, err := contractFileAtRefWithLegacyFallback(
		context.Background(),
		workDir,
		"some-ref",
		"generation/primary.json",
		"generation/first.json",
		"generation/second.json")
	if err != nil {
		t.Fatalf("expected second legacy to resolve, got err: %v", err)
	}
	if content != "second-legacy-body" {
		t.Fatalf("expected second legacy body, got %q", content)
	}
}

// Verifies a successful primary show returns immediately without probing any
// legacy path, so the common happy path incurs no unnecessary git calls.
func TestContractFileAtRefWithLegacyFallback_WhenPrimaryReadSucceeds_DoesNotConsultLegacyChain(t *testing.T) {
	workDir, binDir := setupMockGitBin(t)

	// Unlisted legacy paths would exit 1 as "unexpected" in the mock, so if
	// the fallback tried to probe them the test would fail loudly.
	writeExistenceMockGit(t, binDir, mockGitExistenceFixture{
		refResolves: true,
		paths: map[string]mockGitPathBehavior{
			"generation/primary.json": {exists: true, showOK: true, showContent: "primary-body"},
		},
	})

	content, err := contractFileAtRefWithLegacyFallback(
		context.Background(),
		workDir,
		"some-ref",
		"generation/primary.json",
		"generation/first.json",
		"generation/second.json")
	if err != nil {
		t.Fatalf("expected primary to resolve without touching legacies, got err: %v", err)
	}
	if content != "primary-body" {
		t.Fatalf("expected primary body, got %q", content)
	}
}

// Verifies the "no path found at an existing ref" outcome still classifies as
// missing through the sentinel-based classifier, so the bootstrap exemption
// keeps working when the path list grows.
func TestContractFileAtRefWithLegacyFallback_WhenAllPathsAbsentAtExistingRef_ClassifiesAsMissingByClassifier(t *testing.T) {
	workDir, binDir := setupMockGitBin(t)

	writeExistenceMockGit(t, binDir, mockGitExistenceFixture{
		refResolves: true,
		paths: map[string]mockGitPathBehavior{
			"generation/primary.json": {exists: false, showOK: false, showStderr: "fatal: primary missing"},
			"generation/first.json":   {exists: false, showOK: false, showStderr: "fatal: first missing"},
			"generation/second.json":  {exists: false, showOK: false, showStderr: "fatal: second missing"},
		},
	})

	_, err := contractFileAtRefWithLegacyFallback(
		context.Background(),
		workDir,
		"missing-ref",
		"generation/primary.json",
		"generation/first.json",
		"generation/second.json")
	if err == nil {
		t.Fatal("expected an error when every path is absent")
	}

	// The runner-side caller wraps this outcome with the sentinel message. The
	// classifier must accept it regardless of the checked-path count.
	sentinelErr := errors.New(runnerContractMissingAtReleaseMessage("uloop-project-runner-v0.0.0"))
	if !protocolMinimumVersionReleaseContractIsMissing(sentinelErr) {
		t.Fatalf("expected classifier to recognize sentinel message, got: %v", sentinelErr)
	}
}

// Verifies that when execution reaches the legacy loop and the first legacy
// probe fails as an execution error (its git process is killed by a context
// timeout while the primary show and probe plus rev-parse have already
// completed), the returned error names the failing legacy path AND still
// carries the original primary show error text so operators keep both signals.
func TestContractFileAtRefWithLegacyFallback_WhenLegacyChainProbeFails_PreservesOriginalShowError(t *testing.T) {
	workDir, binDir := setupMockGitBin(t)

	// Only the first legacy probe blocks; every other invocation (primary
	// show/probe, rev-parse verify) returns immediately so the control flow
	// deterministically reaches the legacy loop before the timeout fires.
	writeExistenceMockGit(t, binDir, mockGitExistenceFixture{
		refResolves: true,
		paths: map[string]mockGitPathBehavior{
			"generation/primary.json": {exists: false, showOK: false, showStderr: "fatal: primary missing"},
			"generation/first.json":   {probeSleeps: true},
			"generation/second.json":  {exists: false, showOK: false, showStderr: "fatal: second missing"},
		},
	})

	ctx, cancel := context.WithTimeout(context.Background(), 500*time.Millisecond)
	defer cancel()

	_, err := contractFileAtRefWithLegacyFallback(
		ctx,
		workDir,
		"some-ref",
		"generation/primary.json",
		"generation/first.json",
		"generation/second.json")
	if err == nil {
		t.Fatal("expected an error when the mid-chain probe is killed")
	}
	if !strings.Contains(err.Error(), "failed to check generation/first.json") {
		t.Fatalf("expected the wrap to name the failing legacy path, got: %v", err)
	}
	if !strings.Contains(err.Error(), "(original show error:") {
		t.Fatalf("expected the original show error to be preserved, got: %v", err)
	}
	if !strings.Contains(err.Error(), "some-ref:generation/primary.json") {
		t.Fatalf("expected the primary path to appear in the preserved show error, got: %v", err)
	}
	if !errors.Is(err, context.DeadlineExceeded) {
		t.Fatalf("expected the context error to be wrapped, got: %v", err)
	}
}

// Verifies anyOfFilesExistsAtRef returns true as soon as a later path in the
// list is present, so the classifier does not need to know which generation
// won.
func TestAnyOfFilesExistsAtRef_WhenSecondFileExists_ReturnsTrue(t *testing.T) {
	workDir, binDir := setupMockGitBin(t)
	writeExistenceMockGit(t, binDir, mockGitExistenceFixture{
		refResolves: true,
		paths: map[string]mockGitPathBehavior{
			"generation/first.json":  {exists: false},
			"generation/second.json": {exists: true},
		},
	})

	found, err := anyOfFilesExistsAtRef(
		context.Background(),
		workDir,
		"some-ref",
		[]string{"generation/first.json", "generation/second.json"})
	if err != nil {
		t.Fatalf("expected no error, got %v", err)
	}
	if !found {
		t.Fatal("expected any-of to be true when a later path exists")
	}
}

// Verifies anyOfFilesExistsAtRef returns false without error when no path is
// present at the ref.
func TestAnyOfFilesExistsAtRef_WhenAllFilesAbsent_ReturnsFalse(t *testing.T) {
	workDir, binDir := setupMockGitBin(t)
	writeExistenceMockGit(t, binDir, mockGitExistenceFixture{
		refResolves: true,
		paths: map[string]mockGitPathBehavior{
			"generation/first.json":  {exists: false},
			"generation/second.json": {exists: false},
		},
	})

	found, err := anyOfFilesExistsAtRef(
		context.Background(),
		workDir,
		"some-ref",
		[]string{"generation/first.json", "generation/second.json"})
	if err != nil {
		t.Fatalf("expected no error, got %v", err)
	}
	if found {
		t.Fatal("expected any-of to be false when every path is absent")
	}
}

// Verifies anyOfFilesExistsAtRef propagates a probe execution error instead of
// silently misreading it as absence.
func TestAnyOfFilesExistsAtRef_WhenProbeErrors_PropagatesError(t *testing.T) {
	workDir := t.TempDir()
	emptyBin := filepath.Join(workDir, "empty-bin")
	if err := os.MkdirAll(emptyBin, 0o755); err != nil {
		t.Fatalf("failed to create empty bin: %v", err)
	}
	t.Setenv("PATH", emptyBin)

	_, err := anyOfFilesExistsAtRef(
		context.Background(),
		workDir,
		"some-ref",
		[]string{"generation/first.json"})
	if err == nil {
		t.Fatal("expected the probe execution failure to propagate")
	}
}

// writeSleepingMockGit installs a git mock that blocks far longer than the
// test timeouts so context cancellation reliably kills it mid-run.
func writeSleepingMockGit(t *testing.T, binDir string) {
	t.Helper()

	path := filepath.Join(binDir, "git")
	writeFile(t, path, "#!/bin/sh\nsleep 10\n")
	if err := os.Chmod(path, 0o755); err != nil {
		t.Fatalf("failed to chmod mock git: %v", err)
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
			// Only the paths these tests actually probe are registered; the
			// mock fails loudly on any other path, so a future caller that
			// probes an unregistered generation surfaces as a test failure
			// instead of a silent "absent" result.
			legacyRunnerContractFile: {
				exists:          fixture.legacyShowSucceeds,
				showOK:          fixture.legacyShowSucceeds,
				showContentPath: legacyPath,
				showStderr:      "fatal: legacy show failed",
			},
		},
	})

	return contractFileAtRefFixtureRun{repoRoot: workDir}
}
