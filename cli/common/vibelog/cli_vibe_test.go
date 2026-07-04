package vibelog

import (
	"path/filepath"
	"testing"
)

// Verifies CLI Vibe logs are skipped unless ULOOP_DEBUG is enabled.
func TestWriteCLIVibeLogSkipsWhenDebugDisabled(t *testing.T) {
	t.Setenv(CLIVibeLogEnvName, "")
	projectRoot := t.TempDir()

	err := WriteCLIVibeLog(projectRoot, CLIVibeLogEntry{
		Level:     "INFO",
		Operation: "test_operation",
		Message:   "test message",
	})
	if err != nil {
		t.Fatalf("WriteCLIVibeLog should skip without error: %v", err)
	}
	logFiles, err := filepath.Glob(filepath.Join(projectRoot, CLIVibeLogDirectory, CLIVibeLogPrefix+"_*.json"))
	if err != nil {
		t.Fatalf("failed to glob CLI Vibe logs: %v", err)
	}
	if len(logFiles) != 0 {
		t.Fatalf("expected no CLI Vibe logs, got %d: %#v", len(logFiles), logFiles)
	}
}
