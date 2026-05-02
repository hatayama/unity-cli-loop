package project

import (
	"os"
	"path/filepath"
	"runtime"
	"strings"
	"testing"
)

func TestCreateEndpointUsesStableProjectHash(t *testing.T) {
	endpoint := CreateEndpoint("/tmp/MyProject")

	if runtime.GOOS == "windows" {
		if !strings.HasPrefix(endpoint.Address, `\\.\pipe\uloop-uLoopMCP-`) {
			t.Fatalf("unexpected windows pipe endpoint: %s", endpoint.Address)
		}
		return
	}

	expectedPrefix := filepath.Join("/tmp/uloop", "uLoopMCP-")
	if !strings.HasPrefix(endpoint.Address, expectedPrefix) {
		t.Fatalf("unexpected unix endpoint: %s", endpoint.Address)
	}
	if !strings.HasSuffix(endpoint.Address, ".sock") {
		t.Fatalf("unix endpoint should end with .sock: %s", endpoint.Address)
	}
}

func TestFindUnityProjectRootWithinFindsNestedProject(t *testing.T) {
	workspaceRoot := t.TempDir()
	projectRoot := filepath.Join(workspaceRoot, "nested", "Game")
	createUnityProject(t, projectRoot)

	resolved, err := FindUnityProjectRootWithin(workspaceRoot, 3)
	if err != nil {
		t.Fatalf("FindUnityProjectRootWithin failed: %v", err)
	}
	if resolved != projectRoot {
		t.Fatalf("project root mismatch: %s", resolved)
	}
}

func TestFindUnityProjectRootWithinHonorsMaxDepth(t *testing.T) {
	workspaceRoot := t.TempDir()
	projectRoot := filepath.Join(workspaceRoot, "nested", "Game")
	createUnityProject(t, projectRoot)

	_, err := FindUnityProjectRootWithin(workspaceRoot, 1)
	if err == nil {
		t.Fatal("expected max depth search to miss nested project")
	}
}

func createUnityProject(t *testing.T, projectRoot string) {
	t.Helper()

	if err := os.MkdirAll(filepath.Join(projectRoot, "Assets"), 0o755); err != nil {
		t.Fatalf("failed to create Assets: %v", err)
	}
	if err := os.MkdirAll(filepath.Join(projectRoot, "ProjectSettings"), 0o755); err != nil {
		t.Fatalf("failed to create ProjectSettings: %v", err)
	}
}
