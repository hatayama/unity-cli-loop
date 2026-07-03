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

	workDir := t.TempDir()
	mockBin := filepath.Join(workDir, "bin")
	if err := os.MkdirAll(mockBin, 0o755); err != nil {
		t.Fatalf("failed to create mock bin: %v", err)
	}

	legacyPath := filepath.Join(workDir, "legacy.txt")
	writeFile(t, legacyPath, fixture.legacyContent)

	// The mock script inspects env vars to decide exit codes so tests can
	// assemble each failure mode without editing shell logic per test.
	setBool := func(name string, value bool) {
		if value {
			t.Setenv(name, "1")
		} else {
			t.Setenv(name, "")
		}
	}
	setBool("MOCK_REF_EXISTS", fixture.refExists)
	setBool("MOCK_PRIMARY_EXISTS", fixture.primaryExists)
	setBool("MOCK_PRIMARY_SHOW_OK", fixture.primaryShowSucceeds)
	setBool("MOCK_LEGACY_SHOW_OK", fixture.legacyShowSucceeds)
	t.Setenv("MOCK_PRIMARY_SHOW_STDERR", fixture.primaryShowStderr)
	t.Setenv("MOCK_LEGACY_CONTENT_PATH", legacyPath)

	writeContractFileAtRefMockGit(t, filepath.Join(mockBin, "git"))
	t.Setenv("PATH", mockBin+string(os.PathListSeparator)+os.Getenv("PATH"))

	return contractFileAtRefFixtureRun{repoRoot: workDir}
}

func writeContractFileAtRefMockGit(t *testing.T, path string) {
	t.Helper()

	content := `#!/bin/sh
set -eu

if [ "$1" = "-C" ]; then
  shift 2
fi

case "$1" in
  show)
    case "$2" in
      *:common/clicontract/contract.json)
        if [ -n "${MOCK_PRIMARY_SHOW_OK:-}" ]; then
          echo "primary-body"
          exit 0
        fi
        [ -n "${MOCK_PRIMARY_SHOW_STDERR:-}" ] && echo "$MOCK_PRIMARY_SHOW_STDERR" >&2
        exit 1
        ;;
      *:cli/contract.json)
        if [ -n "${MOCK_LEGACY_SHOW_OK:-}" ]; then
          cat "$MOCK_LEGACY_CONTENT_PATH"
          exit 0
        fi
        echo "fatal: legacy show failed" >&2
        exit 1
        ;;
      *)
        echo "unexpected git show ref: $2" >&2
        exit 1
        ;;
    esac
    ;;
  cat-file)
    # cat-file -e <ref>:<file>
    case "$3" in
      *:common/clicontract/contract.json)
        [ -n "${MOCK_PRIMARY_EXISTS:-}" ] && exit 0
        exit 1
        ;;
      *:cli/contract.json)
        # legacy path is treated as absent unless a legacy show is set up
        [ -n "${MOCK_LEGACY_SHOW_OK:-}" ] && exit 0
        exit 1
        ;;
      *:dispatcher/dispatcher-contract.json)
        exit 1
        ;;
      *)
        echo "unexpected cat-file target: $3" >&2
        exit 1
        ;;
    esac
    ;;
  rev-parse)
    # rev-parse --verify --quiet <ref>^{commit}
    if [ "$2" = "--verify" ]; then
      [ -n "${MOCK_REF_EXISTS:-}" ] && exit 0
      exit 1
    fi
    echo "unexpected rev-parse: $*" >&2
    exit 1
    ;;
  *)
    echo "unexpected git command: $*" >&2
    exit 1
    ;;
esac
`
	writeFile(t, path, content)
	if err := os.Chmod(path, 0o755); err != nil {
		t.Fatalf("failed to chmod mock git: %v", err)
	}
}
