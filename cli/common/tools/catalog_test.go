package tools

import (
	"os"
	"path/filepath"
	"testing"
)

// Tests that callers can explicitly prefer embedded definitions without tools knowing command names.
func TestFindForCommandUsesEmbeddedDefinitionWhenRequested(t *testing.T) {
	projectRoot := t.TempDir()
	writeToolCache(t, projectRoot, `{"tools":[]}`)

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

// Tests that without a project cache the embedded catalog is used and internal tools are filtered out of it.
func TestLoadFiltersInternalToolsFromEmbeddedCatalog(t *testing.T) {
	projectRoot := t.TempDir()

	cache, err := Load(projectRoot, map[string]bool{"execute-dynamic-code": true})
	if err != nil {
		t.Fatalf("Load failed: %v", err)
	}

	if _, ok := Find(cache, "execute-dynamic-code"); ok {
		t.Fatal("internal tool should be filtered from the embedded catalog")
	}
	if _, ok := Find(cache, "compile"); !ok {
		t.Fatal("public embedded tool should remain in the catalog")
	}
}

// Tests that an unparsable project cache is ignored so callers fall back to the embedded catalog.
func TestLoadProjectCacheRejectsInvalidJSON(t *testing.T) {
	projectRoot := t.TempDir()
	writeToolCache(t, projectRoot, `{"tools":`)

	if _, ok := LoadProjectCache(projectRoot, nil); ok {
		t.Fatal("invalid project cache should not load")
	}
	cache, err := Load(projectRoot, nil)
	if err != nil {
		t.Fatalf("Load failed: %v", err)
	}
	if _, ok := Find(cache, "compile"); !ok {
		t.Fatal("Load should fall back to the embedded catalog")
	}
}

// Tests that a project cache drops internal tools and keeps the other tools in order.
func TestLoadProjectCacheFiltersInternalTools(t *testing.T) {
	projectRoot := t.TempDir()
	writeToolCache(t, projectRoot, `{"serverVersion":"1.2.3","tools":[{"name":"first"},{"name":"hidden-tool"},{"name":"second"}]}`)

	cache, ok := LoadProjectCache(projectRoot, map[string]bool{"hidden-tool": true})

	if !ok {
		t.Fatal("project cache should load")
	}
	if cache.ServerVersion != "1.2.3" || len(cache.Tools) != 2 || cache.Tools[0].Name != "first" || cache.Tools[1].Name != "second" {
		t.Fatalf("unexpected filtered cache: %#v", cache)
	}
}

// Tests that a project lookup without a cache reports a miss for a command the catalog does not define.
func TestFindForCommandReportsUnknownCommand(t *testing.T) {
	_, cache, ok, err := FindForCommand(t.TempDir(), "no-such-command", nil, false)
	if err != nil {
		t.Fatalf("FindForCommand failed: %v", err)
	}

	if ok {
		t.Fatal("unknown command should not be found")
	}
	if len(cache.Tools) == 0 {
		t.Fatal("the loaded catalog should still be returned")
	}
}
