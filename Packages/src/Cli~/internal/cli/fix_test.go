package cli

import (
	"os"
	"path/filepath"
	"testing"
)

func TestCleanupStaleLockFilesRemovesKnownLocksOnly(t *testing.T) {
	projectRoot := t.TempDir()
	tempDirectory := filepath.Join(projectRoot, "Temp")
	if err := os.MkdirAll(tempDirectory, 0o755); err != nil {
		t.Fatalf("failed to create Temp directory: %v", err)
	}

	for _, lockFileName := range staleLockFileNames {
		if err := os.WriteFile(filepath.Join(tempDirectory, lockFileName), []byte("lock"), 0o644); err != nil {
			t.Fatalf("failed to seed lock file: %v", err)
		}
	}
	keepPath := filepath.Join(tempDirectory, "UnityLockfile")
	if err := os.WriteFile(keepPath, []byte("keep"), 0o644); err != nil {
		t.Fatalf("failed to seed keep file: %v", err)
	}

	cleaned, err := cleanupStaleLockFiles(projectRoot)
	if err != nil {
		t.Fatalf("cleanupStaleLockFiles failed: %v", err)
	}

	if cleaned != len(staleLockFileNames) {
		t.Fatalf("cleaned count mismatch: %d", cleaned)
	}
	for _, lockFileName := range staleLockFileNames {
		if _, err := os.Stat(filepath.Join(tempDirectory, lockFileName)); err == nil {
			t.Fatalf("lock file was not removed: %s", lockFileName)
		}
	}
	if _, err := os.Stat(keepPath); err != nil {
		t.Fatalf("unrelated lock file was removed: %v", err)
	}
}

func TestCleanupStaleLockFilesAllowsMissingTempDirectory(t *testing.T) {
	cleaned, err := cleanupStaleLockFiles(t.TempDir())
	if err != nil {
		t.Fatalf("cleanupStaleLockFiles failed: %v", err)
	}
	if cleaned != 0 {
		t.Fatalf("cleaned count mismatch: %d", cleaned)
	}
}

func TestCleanupStaleLockFilesReturnsUnexpectedStatError(t *testing.T) {
	projectRoot := t.TempDir()
	tempPath := filepath.Join(projectRoot, "Temp")
	if err := os.WriteFile(tempPath, []byte("not a directory"), 0o644); err != nil {
		t.Fatalf("failed to seed Temp file: %v", err)
	}

	_, err := cleanupStaleLockFiles(projectRoot)
	if err == nil {
		t.Fatal("expected stat error")
	}
}
