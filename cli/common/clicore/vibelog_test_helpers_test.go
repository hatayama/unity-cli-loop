package clicore

import (
	"os"
	"path/filepath"
	"testing"

	"github.com/hatayama/unity-cli-loop/common/vibelog"
)

func enableCliVibeLog(t *testing.T) {
	t.Helper()
	t.Setenv(vibelog.CLIVibeLogEnvName, "1")
}

func readOnlyCliVibeLog(t *testing.T, projectRoot string) string {
	t.Helper()
	logFiles, err := filepath.Glob(filepath.Join(projectRoot, vibelog.CLIVibeLogDirectory, vibelog.CLIVibeLogPrefix+"_*.json"))
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
