package clicore

import (
	"os"
	"path/filepath"
	"testing"
)

func enableCliVibeLog(t *testing.T) {
	t.Helper()
	t.Setenv(CLIVibeLogEnvName, "1")
}

// readOnlyCliVibeLog reads the single CLI Vibe log file written for projectRoot.
// Duplicated from internal/cli's test helper of the same name: test helpers
// cannot be shared across packages, and both packages exercise CLI Vibe logging.
func readOnlyCliVibeLog(t *testing.T, projectRoot string) string {
	t.Helper()
	logFiles, err := filepath.Glob(filepath.Join(projectRoot, CLIVibeLogDirectory, CLIVibeLogPrefix+"_*.json"))
	if err != nil {
		t.Fatalf("failed to glob CLI Vibe logs: %v", err)
	}
	if len(logFiles) != 1 {
		t.Fatalf("expected one CLI Vibe log, got %d: %#v", len(logFiles), logFiles)
	}
	content, err := os.ReadFile(logFiles[0])
	if err != nil {
		t.Fatalf("failed to read CLI Vibe log: %v", err)
	}
	return string(content)
}

// Verifies CLI Vibe logs are skipped unless ULOOP_DEBUG is enabled.
func TestWriteCliVibeLogSkipsWhenDebugDisabled(t *testing.T) {
	t.Setenv(CLIVibeLogEnvName, "")
	projectRoot := t.TempDir()

	err := WriteCLIVibeLog(projectRoot, CLIVibeLogEntry{
		Level:     "INFO",
		Operation: "test_operation",
		Message:   "test message",
	})
	if err != nil {
		t.Fatalf("writeCliVibeLog should skip without error: %v", err)
	}
	logFiles, err := filepath.Glob(filepath.Join(projectRoot, CLIVibeLogDirectory, CLIVibeLogPrefix+"_*.json"))
	if err != nil {
		t.Fatalf("failed to glob CLI Vibe logs: %v", err)
	}
	if len(logFiles) != 0 {
		t.Fatalf("expected no CLI Vibe logs, got %d: %#v", len(logFiles), logFiles)
	}
}
