package cli

import (
	"os"
	"path/filepath"
	"strings"
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

// Verifies project-level Unity debug defines enable CLI Vibe logs without a shell environment override.
func TestWriteCliVibeLogUsesUnityProjectDebugDefine(t *testing.T) {
	t.Setenv(cliVibeLogEnvName, "")
	projectRoot := t.TempDir()
	writeUnityProjectSettings(t, projectRoot, "Standalone: ULOOP_DEBUG;EXAMPLE_SYMBOL")

	err := writeCliVibeLog(projectRoot, cliVibeLogEntry{
		Level:     "INFO",
		Operation: "test_project_debug_operation",
		Message:   "test message",
	})
	if err != nil {
		t.Fatalf("writeCliVibeLog failed: %v", err)
	}

	logContent := readOnlyCliVibeLog(t, projectRoot)
	if !strings.Contains(logContent, `"operation":"test_project_debug_operation"`) {
		t.Fatalf("CLI Vibe log missing project-debug entry:\n%s", logContent)
	}
}

// Verifies debug source resolution distinguishes shell, project, and combined sources.
func TestResolveCliVibeDebugModeReportsSource(t *testing.T) {
	projectRoot := t.TempDir()
	writeUnityProjectSettings(t, projectRoot, "Standalone: ULOOP_DEBUG")

	t.Setenv(cliVibeLogEnvName, "")
	projectOnly := resolveCliVibeDebugMode(projectRoot)
	if !projectOnly.enabled || projectOnly.source != cliVibeDebugSourceUnityProject {
		t.Fatalf("project debug source mismatch: %#v", projectOnly)
	}

	t.Setenv(cliVibeLogEnvName, "1")
	both := resolveCliVibeDebugMode(projectRoot)
	if !both.enabled || both.source != cliVibeDebugSourceBoth {
		t.Fatalf("combined debug source mismatch: %#v", both)
	}

	noDebugRoot := t.TempDir()
	t.Setenv(cliVibeLogEnvName, "0")
	none := resolveCliVibeDebugMode(noDebugRoot)
	if none.enabled || none.source != cliVibeDebugSourceNone {
		t.Fatalf("disabled debug source mismatch: %#v", none)
	}
}

func writeUnityProjectSettings(t *testing.T, projectRoot string, scriptingDefineLine string) {
	t.Helper()
	projectSettingsDirectory := filepath.Join(projectRoot, "ProjectSettings")
	if err := os.MkdirAll(projectSettingsDirectory, 0o755); err != nil {
		t.Fatalf("failed to create ProjectSettings: %v", err)
	}
	content := "PlayerSettings:\n  scriptingDefineSymbols:\n    " + scriptingDefineLine + "\n"
	if err := os.WriteFile(filepath.Join(projectSettingsDirectory, "ProjectSettings.asset"), []byte(content), 0o644); err != nil {
		t.Fatalf("failed to write ProjectSettings.asset: %v", err)
	}
}
