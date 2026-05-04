package project

import (
	"os"
	"path/filepath"
	"runtime"
	"strings"
	"testing"

	"github.com/hatayama/unity-cli-loop/Packages/src/GoCli/internal/domain"
)

func TestCreateEndpointUsesStableProjectHash(t *testing.T) {
	endpoint := CreateEndpoint("/tmp/MyProject")

	if runtime.GOOS == "windows" {
		if !strings.HasPrefix(endpoint.Address, `\\.\pipe\uloop-UnityCliLoop-`) {
			t.Fatalf("unexpected windows pipe endpoint: %s", endpoint.Address)
		}
		return
	}

	expectedPrefix := filepath.Join("/tmp/uloop", "UnityCliLoop-")
	if !strings.HasPrefix(endpoint.Address, expectedPrefix) {
		t.Fatalf("unexpected unix endpoint: %s", endpoint.Address)
	}
	if !strings.HasSuffix(endpoint.Address, ".sock") {
		t.Fatalf("unix endpoint should end with .sock: %s", endpoint.Address)
	}
}

func TestTrimTrailingSeparators_WhenWindowsPathIsNotRoot_ShouldRemoveTrailingSeparator(t *testing.T) {
	if runtime.GOOS != "windows" {
		t.Skip("Windows path roots are platform-specific")
	}

	// Verifies that a normal Windows project path matches the Editor endpoint input.
	path := trimTrailingSeparators(`C:\Users\booql\oss\unity-cli-loop\`)

	if path != `C:\Users\booql\oss\unity-cli-loop` {
		t.Fatalf("path should not keep trailing separator: %s", path)
	}
}

func TestTrimTrailingSeparators_WhenWindowsPathIsDriveRoot_ShouldKeepRootSeparator(t *testing.T) {
	if runtime.GOOS != "windows" {
		t.Skip("Windows path roots are platform-specific")
	}

	// Verifies that a Windows drive root remains a valid root path.
	path := trimTrailingSeparators(`C:\`)

	if path != `C:\` {
		t.Fatalf("drive root should keep trailing separator: %s", path)
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

func TestResolveConnection_WhenSettingsFileIsMissing_ShouldUseProjectPathEndpoint(t *testing.T) {
	projectRoot := t.TempDir()
	createUnityProject(t, projectRoot)

	connection, err := ResolveConnection(projectRoot, "")
	if err != nil {
		t.Fatalf("ResolveConnection failed: %v", err)
	}
	assertProjectConnection(t, connection, projectRoot)
}

func TestResolveConnection_WhenSettingsFileContainsStaleRuntimeState_ShouldIgnoreIt(t *testing.T) {
	projectRoot := t.TempDir()
	createUnityProject(t, projectRoot)
	userSettingsPath := filepath.Join(projectRoot, "UserSettings")
	if err := os.MkdirAll(userSettingsPath, 0o755); err != nil {
		t.Fatalf("failed to create UserSettings: %v", err)
	}
	if err := os.WriteFile(
		filepath.Join(userSettingsPath, "UnityMcpSettings.json"),
		[]byte(`{"projectRootPath":"/stale/project","serverSessionId":"stale-session"}`),
		0o644); err != nil {
		t.Fatalf("failed to write stale settings: %v", err)
	}

	connection, err := ResolveConnection(projectRoot, "")
	if err != nil {
		t.Fatalf("ResolveConnection failed: %v", err)
	}
	assertProjectConnection(t, connection, projectRoot)
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

func assertProjectConnection(t *testing.T, connection domain.Connection, projectRoot string) {
	t.Helper()

	canonicalProjectRoot, err := filepath.EvalSymlinks(projectRoot)
	if err != nil {
		t.Fatalf("failed to canonicalize project root: %v", err)
	}
	canonicalProjectRoot = trimTrailingSeparators(canonicalProjectRoot)
	if connection.ProjectRoot != canonicalProjectRoot {
		t.Fatalf("project root mismatch: %s", connection.ProjectRoot)
	}
	if connection.RequestMetadata == nil {
		t.Fatal("request metadata should be present")
	}
	if connection.RequestMetadata.ExpectedProjectRoot != canonicalProjectRoot {
		t.Fatalf("expected project root mismatch: %s", connection.RequestMetadata.ExpectedProjectRoot)
	}
	if connection.Endpoint != CreateEndpoint(canonicalProjectRoot) {
		t.Fatalf("endpoint mismatch: %#v", connection.Endpoint)
	}
}
