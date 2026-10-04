package project

import (
	stderrors "errors"
	"os"
	"path/filepath"
	"runtime"
	"strconv"
	"strings"
	"testing"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
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

	expectedPrefix := filepath.Join(
		"/tmp/uloop-"+strconv.Itoa(os.Geteuid()),
		"UnityCliLoop-",
	)
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

// Verifies parents-first resolution prefers the enclosing Unity project over a nested
// Unity-shaped child folder (for example a CI fixture) when starting from a subdirectory.
func TestFindUnityProjectRootPreferringParentsPrefersEnclosingProject(t *testing.T) {
	projectRoot := t.TempDir()
	createUnityProject(t, projectRoot)
	createUnityProject(t, filepath.Join(projectRoot, "ci", "fixtures", "NestedApp"))

	resolved, err := FindUnityProjectRootPreferringParents(filepath.Join(projectRoot, "ci"), 3)
	if err != nil {
		t.Fatalf("FindUnityProjectRootPreferringParents failed: %v", err)
	}
	if resolved != projectRoot {
		t.Fatalf("must resolve enclosing project, got: %s", resolved)
	}
}

// Verifies parents-first resolution falls back to the child search when no ancestor is a Unity project.
func TestFindUnityProjectRootPreferringParentsFallsBackToChildSearch(t *testing.T) {
	workspaceRoot := t.TempDir()
	projectRoot := filepath.Join(workspaceRoot, "nested", "Game")
	createUnityProject(t, projectRoot)

	resolved, err := FindUnityProjectRootPreferringParents(workspaceRoot, 3)
	if err != nil {
		t.Fatalf("FindUnityProjectRootPreferringParents failed: %v", err)
	}
	if resolved != projectRoot {
		t.Fatalf("project root mismatch: %s", resolved)
	}
}

// Verifies the ambiguous child fallback classifies as MultipleProjectsFoundError instead of an internal error.
func TestFindUnityProjectRootPreferringParentsRejectsAmbiguousChildren(t *testing.T) {
	workspaceRoot := t.TempDir()
	createUnityProject(t, filepath.Join(workspaceRoot, "first", "Game"))
	createUnityProject(t, filepath.Join(workspaceRoot, "second", "Game"))

	_, err := FindUnityProjectRootPreferringParents(workspaceRoot, 3)

	var multipleErr clierrors.MultipleProjectsFoundError
	if !stderrors.As(err, &multipleErr) {
		t.Fatalf("expected MultipleProjectsFoundError, got %T: %v", err, err)
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
	var projectNotFoundErr clierrors.ProjectNotFoundError
	if !stderrors.As(err, &projectNotFoundErr) {
		t.Fatalf("expected ProjectNotFoundError, got %T", err)
	}
}

func TestResolveConnection_WhenSettingsFileIsMissing_ShouldUseProjectPathEndpoint(t *testing.T) {
	projectRoot := t.TempDir()
	createUnityProject(t, projectRoot)

	connection, err := resolveConnection(projectRoot, "")
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

	connection, err := resolveConnection(projectRoot, "")
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

func TestNotUnityProjectErrorReturnsTypedError(t *testing.T) {
	// Verifies explicit project path failures are type-classifiable by CLI error handling.
	err := notUnityProjectError("/tmp/not-unity", "")

	var notUnityErr clierrors.NotUnityProjectError
	if !stderrors.As(err, &notUnityErr) {
		t.Fatalf("expected NotUnityProjectError, got %T", err)
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

func TestResolveConnection_WhenExplicitPathIsUnityProject_ShouldUseThatProject(t *testing.T) {
	// Verifies the public entry point resolves an explicit project path instead of searching from the start path.
	startPath := createGitBoundedDir(t)
	projectRoot := filepath.Join(t.TempDir(), "Game")
	createUnityProject(t, projectRoot)

	connection, err := ResolveConnection(startPath, projectRoot)
	if err != nil {
		t.Fatalf("ResolveConnection failed: %v", err)
	}
	assertProjectConnection(t, connection, projectRoot)
}

func TestResolveConnection_WhenExplicitPathIsNotUnityProject_ShouldReturnNotUnityProjectError(t *testing.T) {
	// Verifies an explicit non-Unity path is rejected with a typed error naming the absolute path.
	notProjectRoot := t.TempDir()

	_, err := ResolveConnection(notProjectRoot, notProjectRoot)

	var notUnityErr clierrors.NotUnityProjectError
	if !stderrors.As(err, &notUnityErr) {
		t.Fatalf("expected NotUnityProjectError, got %T: %v", err, err)
	}
	if notUnityErr.ProjectRoot != notProjectRoot {
		t.Fatalf("project root mismatch: %s", notUnityErr.ProjectRoot)
	}
}

func TestResolveConnection_WhenNoProjectEnclosesStartPath_ShouldReturnProjectNotFoundError(t *testing.T) {
	// Verifies implicit resolution fails with ProjectNotFoundError when no Unity project is found before the git root.
	startPath := filepath.Join(createGitBoundedDir(t), "sub")
	mkdirAll(t, startPath)

	_, err := ResolveConnection(startPath, "")

	assertProjectNotFound(t, err)
}

func TestFindProjectRoot_WhenStartedInsideProject_ShouldReturnEnclosingProject(t *testing.T) {
	// Verifies the upward search returns the nearest enclosing Unity project root.
	projectRoot := filepath.Join(createGitBoundedDir(t), "Game")
	createUnityProject(t, projectRoot)
	startPath := filepath.Join(projectRoot, "Assets", "Scripts")
	mkdirAll(t, startPath)

	resolved, err := FindProjectRoot(startPath)
	if err != nil {
		t.Fatalf("FindProjectRoot failed: %v", err)
	}
	if resolved != projectRoot {
		t.Fatalf("project root mismatch: %s", resolved)
	}
}

func TestFindProjectRoot_WhenGitRootIsReachedFirst_ShouldStopSearching(t *testing.T) {
	// Verifies the upward search stops at a .git boundary even when a Unity project exists above it.
	outerProject := t.TempDir()
	createUnityProject(t, outerProject)
	repositoryRoot := filepath.Join(outerProject, "repo")
	mkdirAll(t, filepath.Join(repositoryRoot, ".git"))
	startPath := filepath.Join(repositoryRoot, "src")
	mkdirAll(t, startPath)

	_, err := FindProjectRoot(startPath)

	assertProjectNotFound(t, err)
}

func TestFindProjectRoot_WhenFilesystemRootIsReached_ShouldReturnProjectNotFoundError(t *testing.T) {
	// Verifies the upward search terminates at the filesystem root instead of looping forever.
	root := filesystemRootWithoutProject(t)

	_, err := FindProjectRoot(root)

	assertProjectNotFound(t, err)
}

func TestFindUnityProjectRoot_WhenStartedInsideProject_ShouldReturnEnclosingProject(t *testing.T) {
	// Verifies parent-only resolution finds the enclosing Unity project from a nested directory.
	projectRoot := filepath.Join(createGitBoundedDir(t), "Game")
	createUnityProject(t, projectRoot)
	startPath := filepath.Join(projectRoot, "Packages")
	mkdirAll(t, startPath)

	resolved, err := FindUnityProjectRoot(startPath)
	if err != nil {
		t.Fatalf("FindUnityProjectRoot failed: %v", err)
	}
	if resolved != projectRoot {
		t.Fatalf("project root mismatch: %s", resolved)
	}
}

func TestFindUnityProjectRoot_WhenOnlyChildProjectExists_ShouldNotSearchChildren(t *testing.T) {
	// Verifies parent-only resolution ignores Unity projects below the start path and stops at the git root.
	startPath := createGitBoundedDir(t)
	createUnityProject(t, filepath.Join(startPath, "Game"))

	_, err := FindUnityProjectRoot(startPath)

	assertProjectNotFound(t, err)
}

func TestFindUnityProjectRoot_WhenFilesystemRootIsReached_ShouldReturnProjectNotFoundError(t *testing.T) {
	// Verifies parent-only resolution terminates at the filesystem root.
	root := filesystemRootWithoutProject(t)

	_, err := FindUnityProjectRoot(root)

	assertProjectNotFound(t, err)
}

func TestFindUnityProjectRootWithin_WhenStartIsProject_ShouldReturnStartPath(t *testing.T) {
	// Verifies the start directory wins over nested Unity-shaped children when it is itself a project.
	projectRoot := t.TempDir()
	createUnityProject(t, projectRoot)
	createUnityProject(t, filepath.Join(projectRoot, "nested", "Game"))

	resolved, err := FindUnityProjectRootWithin(projectRoot, 3)
	if err != nil {
		t.Fatalf("FindUnityProjectRootWithin failed: %v", err)
	}
	if resolved != projectRoot {
		t.Fatalf("project root mismatch: %s", resolved)
	}
}

func TestFindUnityProjectRootWithin_WhenNoChildProject_ShouldFallBackToParents(t *testing.T) {
	// Verifies the child search falls back to the enclosing project when no child project exists.
	projectRoot := filepath.Join(createGitBoundedDir(t), "Game")
	createUnityProject(t, projectRoot)
	startPath := filepath.Join(projectRoot, "Assets", "Scripts")
	mkdirAll(t, startPath)

	resolved, err := FindUnityProjectRootWithin(startPath, 3)
	if err != nil {
		t.Fatalf("FindUnityProjectRootWithin failed: %v", err)
	}
	if resolved != projectRoot {
		t.Fatalf("project root mismatch: %s", resolved)
	}
}

func TestFindUnityProjectRootWithin_WhenChildDirectoryIsUnreadable_ShouldSkipIt(t *testing.T) {
	// Verifies an unreadable child directory is skipped and the remaining readable child project is still found.
	workspaceRoot := createGitBoundedDir(t)
	projectRoot := filepath.Join(workspaceRoot, "readable", "Game")
	createUnityProject(t, projectRoot)
	// Named to be scanned before "readable", so stopping the scan at an unreadable child would fail.
	unreadableDir := filepath.Join(workspaceRoot, "a-unreadable")
	mkdirAll(t, unreadableDir)
	if err := os.Chmod(unreadableDir, 0o000); err != nil {
		t.Fatalf("failed to make directory unreadable: %v", err)
	}
	t.Cleanup(func() {
		_ = os.Chmod(unreadableDir, 0o755)
	})
	// Root, and Windows where Chmod only toggles the read-only attribute, can still read the directory.
	if _, err := os.ReadDir(unreadableDir); err == nil {
		t.Skip("the directory is still readable, so the unreadable branch is not reached")
	}

	resolved, err := FindUnityProjectRootWithin(workspaceRoot, 3)
	if err != nil {
		t.Fatalf("FindUnityProjectRootWithin failed: %v", err)
	}
	if resolved != projectRoot {
		t.Fatalf("project root mismatch: %s", resolved)
	}
}

func TestFindUnityProjectRootPreferringParents_WhenNoProjectAnywhere_ShouldReturnParentError(t *testing.T) {
	// Verifies the parents-first search reports ProjectNotFoundError when neither ancestors nor children are projects.
	startPath := createGitBoundedDir(t)
	mkdirAll(t, filepath.Join(startPath, "docs"))

	_, err := FindUnityProjectRootPreferringParents(startPath, 3)

	assertProjectNotFound(t, err)
}

func TestResolveExplicitProjectRoot_WhenPathIsUnityProject_ShouldReturnAbsolutePath(t *testing.T) {
	// Verifies a valid explicit project path resolves to its absolute form.
	projectRoot := t.TempDir()
	createUnityProject(t, projectRoot)

	resolved, err := ResolveExplicitProjectRoot(projectRoot)
	if err != nil {
		t.Fatalf("ResolveExplicitProjectRoot failed: %v", err)
	}
	if resolved != projectRoot {
		t.Fatalf("project root mismatch: %s", resolved)
	}
}

func TestResolveExplicitProjectRoot_WhenPathIsNotUnityProject_ShouldReturnNotUnityProjectError(t *testing.T) {
	// Verifies an explicit directory without Assets and ProjectSettings is rejected with a typed error.
	notProjectRoot := t.TempDir()
	mkdirAll(t, filepath.Join(notProjectRoot, "Assets"))

	_, err := ResolveExplicitProjectRoot(notProjectRoot)

	var notUnityErr clierrors.NotUnityProjectError
	if !stderrors.As(err, &notUnityErr) {
		t.Fatalf("expected NotUnityProjectError, got %T: %v", err, err)
	}
	if notUnityErr.ProjectRoot != notProjectRoot {
		t.Fatalf("project root mismatch: %s", notUnityErr.ProjectRoot)
	}
}

func TestWindowsPosixProjectPathCandidate_ShouldConvertOnlyDriveShapedPaths(t *testing.T) {
	// Verifies Git Bash and WSL drive paths convert to Win32 paths while other shapes are rejected.
	cases := []struct {
		input     string
		expected  string
		converted bool
	}{
		{input: "", expected: "", converted: false},
		{input: "relative/Game", expected: "", converted: false},
		{input: "/c", expected: `C:\`, converted: true},
		{input: "/c/x", expected: `C:\x`, converted: true},
		{input: "/C/x/y", expected: `C:\x\y`, converted: true},
		{input: "/z/x", expected: `Z:\x`, converted: true},
		{input: "/mnt/a/x", expected: `A:\x`, converted: true},
		{input: `/c\x`, expected: `C:\x`, converted: true},
		{input: "/mnt/d", expected: `D:\`, converted: true},
		{input: "/mnt/d/x", expected: `D:\x`, converted: true},
		{input: "/MNT/d/x", expected: `D:\x`, converted: true},
		{input: "/mnt/dx", expected: "", converted: false},
		{input: "/mnt/1/x", expected: "", converted: false},
		{input: "/1/x", expected: "", converted: false},
	}

	for _, testCase := range cases {
		t.Run(testCase.input, func(t *testing.T) {
			candidate, converted := windowsPosixProjectPathCandidate(testCase.input)
			if converted != testCase.converted || candidate != testCase.expected {
				t.Fatalf("got (%q, %v), want (%q, %v)", candidate, converted, testCase.expected, testCase.converted)
			}
		})
	}
}

func TestTrimTrailingSeparators_ShouldKeepRootAndRemoveTrailingSeparators(t *testing.T) {
	// Verifies the POSIX root survives trimming while other paths lose only their trailing separators.
	cases := map[string]string{
		"/":          "/",
		"///":        "/",
		"":           "",
		"a/":         "a",
		"abc":        "abc",
		"/tmp/Game/": "/tmp/Game",
	}

	for input, expected := range cases {
		if actual := trimTrailingSeparators(input); actual != expected {
			t.Fatalf("trimTrailingSeparators(%q) = %q, want %q", input, actual, expected)
		}
	}
}

// createGitBoundedDir returns a temp directory containing .git so upward searches stop there.
func createGitBoundedDir(t *testing.T) string {
	t.Helper()

	dir := t.TempDir()
	mkdirAll(t, filepath.Join(dir, ".git"))
	return dir
}

func mkdirAll(t *testing.T, path string) {
	t.Helper()

	if err := os.MkdirAll(path, 0o755); err != nil {
		t.Fatalf("failed to create %s: %v", path, err)
	}
}

// filesystemRootWithoutProject returns the filesystem root of the temp directory, skipping when
// that root itself would end the search early (a Unity project or .git at the root).
func filesystemRootWithoutProject(t *testing.T) string {
	t.Helper()

	tempDir := t.TempDir()
	root := filepath.VolumeName(tempDir) + string(filepath.Separator)
	if IsUnityProject(root) || exists(filepath.Join(root, ".git")) {
		t.Skip("filesystem root is a Unity project or git repository")
	}
	return root
}

func assertProjectNotFound(t *testing.T, err error) {
	t.Helper()

	var projectNotFoundErr clierrors.ProjectNotFoundError
	if !stderrors.As(err, &projectNotFoundErr) {
		t.Fatalf("expected ProjectNotFoundError, got %T: %v", err, err)
	}
}
