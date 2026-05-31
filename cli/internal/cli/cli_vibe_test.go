package cli

import (
	"path/filepath"
	"testing"
)

func enableCliVibeLog(t *testing.T) {
	t.Helper()
	t.Setenv(cliVibeLogEnvName, "1")
}

// Verifies CLI Vibe logs are skipped unless ULOOP_DEBUG is enabled.
func TestWriteCliVibeLogSkipsWhenDebugDisabled(t *testing.T) {
	t.Setenv(cliVibeLogEnvName, "")
	projectRoot := t.TempDir()

	err := writeCliVibeLog(projectRoot, cliVibeLogEntry{
		Level:     "INFO",
		Operation: "test_operation",
		Message:   "test message",
	})
	if err != nil {
		t.Fatalf("writeCliVibeLog should skip without error: %v", err)
	}
	logFiles, err := filepath.Glob(filepath.Join(projectRoot, cliVibeLogDirectory, cliVibeLogPrefix+"_*.json"))
	if err != nil {
		t.Fatalf("failed to glob CLI Vibe logs: %v", err)
	}
	if len(logFiles) != 0 {
		t.Fatalf("expected no CLI Vibe logs, got %d: %#v", len(logFiles), logFiles)
	}
}
