package cli

import (
	"os"
	"path/filepath"
	"strings"
	"testing"
)

// Verifies --code-file is extracted from execute-dynamic-code args in both flag forms.
func TestExtractDynamicCodeFileFlagParsesBothForms(t *testing.T) {
	remaining, path, err := extractDynamicCodeFileFlag(
		executeDynamicCodeCommandName,
		[]string{"--code-file", "/tmp/snippet.cs", "--timeout-seconds", "30"},
	)
	if err != nil {
		t.Fatalf("extract failed: %v", err)
	}
	if path != "/tmp/snippet.cs" {
		t.Fatalf("path mismatch: %s", path)
	}
	if len(remaining) != 2 || remaining[0] != "--timeout-seconds" {
		t.Fatalf("remaining args mismatch: %#v", remaining)
	}

	remaining, path, err = extractDynamicCodeFileFlag(
		executeDynamicCodeCommandName,
		[]string{"--code-file=/tmp/snippet.cs"},
	)
	if err != nil {
		t.Fatalf("equals-form extract failed: %v", err)
	}
	if path != "/tmp/snippet.cs" || len(remaining) != 0 {
		t.Fatalf("equals-form mismatch: %s %#v", path, remaining)
	}
}

// Verifies the flag is reserved for execute-dynamic-code and ignored for other tools.
func TestExtractDynamicCodeFileFlagIgnoresOtherCommands(t *testing.T) {
	args := []string{"--code-file", "/tmp/snippet.cs"}
	remaining, path, err := extractDynamicCodeFileFlag("get-logs", args)
	if err != nil {
		t.Fatalf("extract failed: %v", err)
	}
	if path != "" || len(remaining) != 2 {
		t.Fatalf("other commands must pass args through: %s %#v", path, remaining)
	}
}

// Verifies the file content lands in the Code parameter, so long C# avoids shell quoting.
func TestApplyDynamicCodeFileParamReadsFile(t *testing.T) {
	codePath := filepath.Join(t.TempDir(), "snippet.cs")
	source := "using UnityEngine;\nreturn \"ok\";\n"
	if err := os.WriteFile(codePath, []byte(source), 0o644); err != nil {
		t.Fatalf("failed to write snippet: %v", err)
	}

	params := map[string]any{}
	if err := applyDynamicCodeFileParam(params, codePath); err != nil {
		t.Fatalf("apply failed: %v", err)
	}
	if params["Code"] != source {
		t.Fatalf("Code param mismatch: %#v", params["Code"])
	}
}

// Verifies --code and --code-file cannot silently shadow each other.
func TestApplyDynamicCodeFileParamRejectsConflictingCode(t *testing.T) {
	codePath := filepath.Join(t.TempDir(), "snippet.cs")
	if err := os.WriteFile(codePath, []byte("return 1;"), 0o644); err != nil {
		t.Fatalf("failed to write snippet: %v", err)
	}

	params := map[string]any{"Code": "return 2;"}
	err := applyDynamicCodeFileParam(params, codePath)
	if err == nil {
		t.Fatal("expected conflict error")
	}
	if !strings.Contains(err.Error(), "--code-file") {
		t.Fatalf("error should name the conflicting option: %v", err)
	}
}

// Verifies an unreadable file fails fast with the path in the message.
func TestApplyDynamicCodeFileParamReportsUnreadableFile(t *testing.T) {
	missingPath := filepath.Join(t.TempDir(), "missing.cs")

	err := applyDynamicCodeFileParam(map[string]any{}, missingPath)
	if err == nil {
		t.Fatal("expected unreadable file error")
	}
	if !strings.Contains(err.Error(), missingPath) {
		t.Fatalf("error should include the path: %v", err)
	}
}

// Verifies no path means no change, keeping plain --code calls untouched.
func TestApplyDynamicCodeFileParamIsNoOpWithoutPath(t *testing.T) {
	params := map[string]any{"Code": "return 3;"}
	if err := applyDynamicCodeFileParam(params, ""); err != nil {
		t.Fatalf("no-op apply failed: %v", err)
	}
	if params["Code"] != "return 3;" {
		t.Fatalf("params must stay untouched: %#v", params)
	}
}
