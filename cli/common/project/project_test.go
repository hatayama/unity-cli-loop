package project

import (
	"os"
	"path/filepath"
	"runtime"
	"strings"
	"testing"

	"github.com/hatayama/unity-cli-loop/common/unityipc"
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
	path := trimTrailingSeparators(`C:\Users\ExampleUser\Projects\unity-cli-loop\`)

	if path != `C:\Users\ExampleUser\Projects\unity-cli-loop` {
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

func TestFindUnityProjectRootWithinRejectsAmbiguousNestedProjects(t *testing.T) {
	// Verifies launch discovery never silently chooses one Unity project from an ambiguous workspace.
	workspaceRoot := t.TempDir()
	createUnityProject(t, filepath.Join(workspaceRoot, "first", "Game"))
	createUnityProject(t, filepath.Join(workspaceRoot, "second", "Game"))

	_, err := FindUnityProjectRootWithin(workspaceRoot, 3)

	if err == nil {
		t.Fatal("expected ambiguous project error")
	}
	if !strings.Contains(err.Error(), "--project-path") {
		t.Fatalf("error should ask for --project-path: %v", err)
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

func TestResolveExplicitProjectRoot_WhenWindowsWslPathTargetsExistingProject_ShouldResolveProject(t *testing.T) {
	if runtime.GOOS != "windows" {
		t.Skip("Windows WSL path conversion is platform-specific")
	}

	// Verifies the Windows CLI accepts a WSL /mnt/<drive> path for an existing Unity project.
	projectRoot := t.TempDir()
	createUnityProject(t, projectRoot)
	wslPath := windowsPathToWslMountPath(t, projectRoot)

	resolved, err := ResolveExplicitProjectRoot(wslPath)
	if err != nil {
		t.Fatalf("ResolveExplicitProjectRoot failed: %v", err)
	}

	if resolved != projectRoot {
		t.Fatalf("project root mismatch: %s", resolved)
	}
}

func TestResolveExplicitProjectRoot_WhenWindowsGitBashPathTargetsExistingProject_ShouldResolveProject(t *testing.T) {
	if runtime.GOOS != "windows" {
		t.Skip("Windows Git Bash path conversion is platform-specific")
	}

	// Verifies the Windows CLI accepts a Git Bash /<drive> path for an existing Unity project.
	projectRoot := t.TempDir()
	createUnityProject(t, projectRoot)
	gitBashPath := windowsPathToGitBashPath(t, projectRoot)

	resolved, err := ResolveExplicitProjectRoot(gitBashPath)
	if err != nil {
		t.Fatalf("ResolveExplicitProjectRoot failed: %v", err)
	}

	if resolved != projectRoot {
		t.Fatalf("project root mismatch: %s", resolved)
	}
}

func TestNormalizeExplicitProjectPathForOS_WhenWindowsWslPathExists_ShouldUseWin32Path(t *testing.T) {
	// Verifies that WSL /mnt/<drive> project paths become Win32 paths before Windows file APIs see them.
	result := normalizeExplicitProjectPathForOS(
		"/mnt/c/Users/ExampleUser/Game",
		"windows",
		existsOnly(`C:\Users\ExampleUser\Game`),
	)

	if result.path != `C:\Users\ExampleUser\Game` {
		t.Fatalf("path mismatch: %s", result.path)
	}
	if result.suggestion != "" {
		t.Fatalf("suggestion should be empty for an accepted conversion: %s", result.suggestion)
	}
}

func TestNormalizeExplicitProjectPathForOS_WhenWindowsGitBashPathExists_ShouldUseWin32Path(t *testing.T) {
	// Verifies that Git Bash /<drive> project paths become Win32 paths before Windows file APIs see them.
	result := normalizeExplicitProjectPathForOS(
		"/d/Projects/My Game",
		"windows",
		existsOnly(`D:\Projects\My Game`),
	)

	if result.path != `D:\Projects\My Game` {
		t.Fatalf("path mismatch: %s", result.path)
	}
}

func TestNormalizeExplicitProjectPathForOS_WhenConvertedWindowsPathIsMissing_ShouldKeepOriginalWithSuggestion(t *testing.T) {
	// Verifies missing converted paths are not silently adopted, but still produce a diagnostic suggestion.
	result := normalizeExplicitProjectPathForOS(
		"/mnt/c/Users/ExampleUser/MissingGame",
		"windows",
		existsOnly(),
	)

	if result.path != "/mnt/c/Users/ExampleUser/MissingGame" {
		t.Fatalf("original path should be preserved: %s", result.path)
	}
	if result.suggestion != `C:\Users\ExampleUser\MissingGame` {
		t.Fatalf("suggestion mismatch: %s", result.suggestion)
	}
}

func TestNormalizeExplicitProjectPathForOS_WhenWindowsPathIsNotPosixDrive_ShouldKeepOriginal(t *testing.T) {
	// Verifies non-drive POSIX paths are not guessed as Windows project paths.
	for _, input := range []string{
		"/home/example/Game",
		"/help",
		"relative/Game",
		`C:\Users\ExampleUser\Game`,
		`\c\Game`,
		`\\server\share\Game`,
	} {
		result := normalizeExplicitProjectPathForOS(input, "windows", existsOnly(`C:\Game`))
		if result.path != input {
			t.Fatalf("path %q should be unchanged, got %q", input, result.path)
		}
		if result.suggestion != "" {
			t.Fatalf("path %q should not have suggestion %q", input, result.suggestion)
		}
	}
}

func TestNormalizeExplicitProjectPathForOS_WhenNotWindows_ShouldKeepPosixPath(t *testing.T) {
	// Verifies POSIX platforms do not reinterpret WSL-looking paths.
	result := normalizeExplicitProjectPathForOS(
		"/mnt/c/Users/ExampleUser/Game",
		"linux",
		existsOnly(`C:\Users\ExampleUser\Game`),
	)

	if result.path != "/mnt/c/Users/ExampleUser/Game" {
		t.Fatalf("non-Windows path should be unchanged: %s", result.path)
	}
	if result.suggestion != "" {
		t.Fatalf("non-Windows path should not have suggestion: %s", result.suggestion)
	}
}

func TestNotUnityProjectError_WhenSuggestionExists_ShouldIncludeConvertedPath(t *testing.T) {
	// Verifies path diagnostics show the safer Win32 candidate when WSL or Git Bash conversion was not adopted.
	err := notUnityProjectError(`C:\mnt\c\Users\ExampleUser\Game`, `C:\Users\ExampleUser\Game`)

	message := err.Error()
	for _, expected := range []string{
		`not a Unity project: C:\mnt\c\Users\ExampleUser\Game`,
		"This looks like a WSL or Git Bash path",
		`Did you mean: C:\Users\ExampleUser\Game`,
	} {
		if !strings.Contains(message, expected) {
			t.Fatalf("message %q should contain %q", message, expected)
		}
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

func existsOnly(paths ...string) func(string) bool {
	accepted := map[string]bool{}
	for _, path := range paths {
		accepted[path] = true
	}
	return func(path string) bool {
		return accepted[path]
	}
}

func windowsPathToWslMountPath(t *testing.T, path string) string {
	t.Helper()

	driveLetter, rest := splitWindowsDrivePath(t, path)
	return "/mnt/" + strings.ToLower(driveLetter) + "/" + rest
}

func windowsPathToGitBashPath(t *testing.T, path string) string {
	t.Helper()

	driveLetter, rest := splitWindowsDrivePath(t, path)
	return "/" + strings.ToLower(driveLetter) + "/" + rest
}

func splitWindowsDrivePath(t *testing.T, path string) (string, string) {
	t.Helper()

	volumeName := filepath.VolumeName(path)
	if len(volumeName) != 2 || volumeName[1] != ':' {
		t.Fatalf("expected drive-qualified Windows path, got %q", path)
	}
	rest := strings.TrimLeft(strings.TrimPrefix(path, volumeName), `\/`)
	rest = strings.ReplaceAll(rest, `\`, "/")
	return volumeName[:1], rest
}

func assertProjectConnection(t *testing.T, connection unityipc.Connection, projectRoot string) {
	t.Helper()

	canonicalProjectRoot, err := filepath.EvalSymlinks(projectRoot)
	if err != nil {
		t.Fatalf("failed to canonicalize project root: %v", err)
	}
	canonicalProjectRoot = trimTrailingSeparators(canonicalProjectRoot)
	if connection.ProjectRoot != canonicalProjectRoot {
		t.Fatalf("project root mismatch: %s", connection.ProjectRoot)
	}
	if connection.Endpoint != CreateEndpoint(canonicalProjectRoot) {
		t.Fatalf("endpoint mismatch: %#v", connection.Endpoint)
	}
}
