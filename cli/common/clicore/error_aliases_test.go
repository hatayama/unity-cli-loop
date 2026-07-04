package clicore

import "testing"

func TestAvailableCommandNamesIncludesBuiltIns(t *testing.T) {
	// Verifies the clicore compatibility wrapper still contributes built-in commands.
	names := availableCommandNames(ToolsCache{})
	expectedBuiltIns := []string{"launch", "list", "sync", "focus-window", "wait-for-pause-point", "pause-point-status", "skills", "completion", "install", "update", "uninstall"}
	for index, expected := range expectedBuiltIns {
		if names[index] != expected {
			t.Fatalf("built-in command mismatch: %#v", names)
		}
	}
}
