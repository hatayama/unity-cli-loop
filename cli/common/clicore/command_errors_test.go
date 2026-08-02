package clicore

import "testing"

func TestAvailableCommandNamesIncludesBuiltIns(t *testing.T) {
	// Verifies unknown-command suggestions include built-in CLI commands before cached tools.
	names := availableCommandNames(ToolsCache{})
	expectedBuiltIns := []string{"launch", "list", "sync", "focus-window", "await-pause-point", "pause-point-status", "skills", "package", "completion", "install", "update", "uninstall"}
	for index, expected := range expectedBuiltIns {
		if names[index] != expected {
			t.Fatalf("built-in command mismatch: %#v", names)
		}
	}
}
