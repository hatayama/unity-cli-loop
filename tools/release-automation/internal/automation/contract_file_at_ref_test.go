package automation

import (
	"errors"
	"testing"
)

// Verifies the lowercase form git currently emits when the file exists nowhere is treated as missing.
func TestIsMissingFileAtRefErrorMatchesLowercaseDoesNotExist(t *testing.T) {
	err := errors.New("git show failed: fatal: path 'common/clicontract/contract.json' does not exist in 'uloop-project-runner-v3.0.0-beta.40'")
	if !isMissingFileAtRefError(err, "common/clicontract/contract.json") {
		t.Fatal("expected lowercase does-not-exist git error to be treated as a missing file")
	}
}

// Verifies the capitalized does-not-exist form emitted by older git versions stays covered.
func TestIsMissingFileAtRefErrorMatchesCapitalizedDoesNotExist(t *testing.T) {
	err := errors.New("git show failed: fatal: Path 'common/clicontract/contract.json' does not exist in 'uloop-project-runner-v3.0.0-beta.40'")
	if !isMissingFileAtRefError(err, "common/clicontract/contract.json") {
		t.Fatal("expected capitalized does-not-exist git error to be treated as a missing file")
	}
}

// Verifies the exists-on-disk-but-not-in-ref form is treated as missing at the ref.
func TestIsMissingFileAtRefErrorMatchesExistsOnDiskForm(t *testing.T) {
	err := errors.New("git show failed: fatal: path 'common/clicontract/contract.json' exists on disk, but not in 'uloop-project-runner-v3.0.0-beta.40'")
	if !isMissingFileAtRefError(err, "common/clicontract/contract.json") {
		t.Fatal("expected exists-on-disk git error to be treated as a missing file")
	}
}

// Verifies unrelated git failures are never classified as a missing file, so they propagate.
func TestIsMissingFileAtRefErrorRejectsUnrelatedErrors(t *testing.T) {
	err := errors.New("git show failed: fatal: unable to read tree")
	if isMissingFileAtRefError(err, "common/clicontract/contract.json") {
		t.Fatal("expected unrelated git error to propagate instead of being treated as missing")
	}
}

// Verifies a missing-file error for a different path is not attributed to the requested file.
func TestIsMissingFileAtRefErrorRejectsOtherFilePaths(t *testing.T) {
	err := errors.New("git show failed: fatal: path 'other/file.json' does not exist in 'HEAD'")
	if isMissingFileAtRefError(err, "common/clicontract/contract.json") {
		t.Fatal("expected missing-file error for another path to propagate")
	}
}
