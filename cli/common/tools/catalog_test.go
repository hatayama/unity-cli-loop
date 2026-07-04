package tools

import (
	"os"
	"path/filepath"
	"testing"
)

// Tests that callers can explicitly prefer embedded definitions without tools knowing command names.
func TestFindForCommandUsesEmbeddedDefinitionWhenRequested(t *testing.T) {
	projectRoot := t.TempDir()
	writeToolCache(t, projectRoot, `{"version":"test","tools":[]}`)

	tool, _, ok, err := FindForCommand(projectRoot, "execute-dynamic-code", map[string]bool{}, true)
	if err != nil {
		t.Fatalf("FindForCommand failed: %v", err)
	}

	if !ok {
		t.Fatal("embedded tool should be found")
	}
	if tool.Name != "execute-dynamic-code" {
		t.Fatalf("tool name mismatch: %s", tool.Name)
	}
}

// Tests that project caches are used when callers do not request embedded preference.
func TestFindForCommandUsesProjectCacheWhenEmbeddedPreferenceIsFalse(t *testing.T) {
	projectRoot := t.TempDir()
	writeToolCache(t, projectRoot, `{
  "version": "test",
  "tools": [
    {
      "name": "execute-dynamic-code",
      "description": "cached definition",
      "inputSchema": {"type": "object", "properties": {}}
    }
  ]
}`)

	tool, _, ok, err := FindForCommand(projectRoot, "execute-dynamic-code", map[string]bool{}, false)
	if err != nil {
		t.Fatalf("FindForCommand failed: %v", err)
	}

	if !ok {
		t.Fatal("project cache tool should be found")
	}
	if tool.Description != "cached definition" {
		t.Fatalf("project cache definition was not used: %s", tool.Description)
	}
}

func writeToolCache(t *testing.T, projectRoot string, content string) {
	t.Helper()
	cacheDir := filepath.Join(projectRoot, CacheDirectoryName)
	if err := os.MkdirAll(cacheDir, 0o755); err != nil {
		t.Fatalf("failed to create tool cache dir: %v", err)
	}
	if err := os.WriteFile(filepath.Join(cacheDir, CacheFileName), []byte(content), 0o644); err != nil {
		t.Fatalf("failed to write tool cache: %v", err)
	}
}
