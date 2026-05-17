package cli

import (
	"os"
	"path/filepath"
	"testing"
)

// Verifies that fix cleanup no longer treats project Temp lock hints as recovery state.
func TestCleanupStaleRecoveryStateIgnoresProjectTempLockHints(t *testing.T) {
	projectRoot := t.TempDir()
	tempDirectory := filepath.Join(projectRoot, "Temp")
	if err := os.MkdirAll(tempDirectory, 0o755); err != nil {
		t.Fatalf("failed to create Temp directory: %v", err)
	}
	lockPath := filepath.Join(tempDirectory, "domainreload.lock")
	if err := os.WriteFile(lockPath, []byte("lock"), 0o644); err != nil {
		t.Fatalf("failed to seed lock hint: %v", err)
	}

	cleaned, err := cleanupStaleRecoveryState(projectRoot)
	if err != nil {
		t.Fatalf("cleanupStaleRecoveryState failed: %v", err)
	}

	if cleaned != 0 {
		t.Fatalf("cleaned count mismatch: %d", cleaned)
	}
	if _, err := os.Stat(lockPath); err != nil {
		t.Fatalf("project Temp lock hint was touched: %v", err)
	}
}

// Verifies that fix cleanup removes server readiness state files.
func TestCleanupStaleRecoveryStateRemovesServerStateFiles(t *testing.T) {
	projectRoot := t.TempDir()
	statePath := filepath.Join(projectRoot, serverStateRelativePath)
	if err := os.MkdirAll(filepath.Dir(statePath), 0o755); err != nil {
		t.Fatalf("failed to create state directory: %v", err)
	}
	if err := os.WriteFile(statePath, []byte(`{"phase":"failed"}`), 0o644); err != nil {
		t.Fatalf("failed to seed state file: %v", err)
	}
	if err := os.WriteFile(statePath+serverStateCompletedTempSuffix, []byte(`{"phase":"starting"}`), 0o644); err != nil {
		t.Fatalf("failed to seed temp state file: %v", err)
	}
	if err := os.WriteFile(statePath+serverStateInProgressTempSuffix, []byte(`{"phase":"recovering"}`), 0o644); err != nil {
		t.Fatalf("failed to seed in-progress temp state file: %v", err)
	}

	cleaned, err := cleanupStaleRecoveryState(projectRoot)
	if err != nil {
		t.Fatalf("cleanupStaleRecoveryState failed: %v", err)
	}

	if cleaned != 3 {
		t.Fatalf("cleaned count mismatch: %d", cleaned)
	}
	if _, err := os.Stat(statePath); err == nil {
		t.Fatal("server state file was not removed")
	}
	if _, err := os.Stat(statePath + serverStateCompletedTempSuffix); err == nil {
		t.Fatal("temporary server state file was not removed")
	}
	if _, err := os.Stat(statePath + serverStateInProgressTempSuffix); err == nil {
		t.Fatal("in-progress temporary server state file was not removed")
	}
}
