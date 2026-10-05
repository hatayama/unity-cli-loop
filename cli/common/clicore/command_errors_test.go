package clicore

import (
	"testing"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
)

func TestAvailableCommandNamesIncludesBuiltIns(t *testing.T) {
	// Verifies unknown-command suggestions include visible built-in CLI commands before cached tools.
	names := availableCommandNames(ToolsCache{})
	expectedBuiltIns := []string{"launch", "verify-project", "list", "sync", "focus-window", "await-pause-point", "pause-point-status", "set-code-optimization", "status", "skills", "package", "install", "update", "uninstall"}
	for index, expected := range expectedBuiltIns {
		if names[index] != expected {
			t.Fatalf("built-in command mismatch: %#v", names)
		}
	}
}

// Verifies that the unknown-command error names the command and suggests a close cached tool name,
// and that a cached tool sharing a built-in name is not listed twice.
func TestUnknownCommandErrorSuggestsCachedTools(t *testing.T) {
	cache := ToolsCache{Tools: []ToolDefinition{{Name: "launch"}, {Name: "get-hierarchy"}}}

	names := availableCommandNames(cache)
	launchCount := 0
	for _, name := range names {
		if name == "launch" {
			launchCount++
		}
	}
	if launchCount != 1 || names[len(names)-1] != "get-hierarchy" {
		t.Fatalf("unexpected available command names: %#v", names)
	}

	cliError := UnknownCommandError("get-hierarchi", cache, clierrors.ErrorContext{ProjectRoot: "project"})

	if cliError.Command != "get-hierarchi" || cliError.ProjectRoot != "project" {
		t.Fatalf("unexpected error identity: %#v", cliError)
	}
	suggestions, ok := cliError.Details["SuggestedCommands"].([]string)
	if !ok || len(suggestions) == 0 || suggestions[0] != "get-hierarchy" {
		t.Fatalf("expected get-hierarchy suggestion, got %#v", cliError.Details["SuggestedCommands"])
	}
}
