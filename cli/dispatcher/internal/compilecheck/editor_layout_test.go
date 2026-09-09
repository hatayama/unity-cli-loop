package compilecheck

import (
	"os"
	"path/filepath"
	"strings"
	"testing"
)

// writeFileAt creates a file and every parent directory it needs.
func writeFileAt(t *testing.T, path string, content string) {
	t.Helper()
	if err := os.MkdirAll(filepath.Dir(path), 0o755); err != nil {
		t.Fatalf("failed to create directory for %s: %v", path, err)
	}
	if err := os.WriteFile(path, []byte(content), 0o644); err != nil {
		t.Fatalf("failed to write %s: %v", path, err)
	}
}

// makeDirAt creates a directory and every parent directory it needs.
func makeDirAt(t *testing.T, path string) {
	t.Helper()
	if err := os.MkdirAll(path, 0o755); err != nil {
		t.Fatalf("failed to create directory %s: %v", path, err)
	}
}

// writeCompilerFiles fills a compiler directory with the files the resolver requires.
func writeCompilerFiles(t *testing.T, compilerDirectoryPath string, runtimeConfigContent string) {
	t.Helper()
	writeFileAt(t, filepath.Join(compilerDirectoryPath, compilerDllFileName), "")
	writeFileAt(t, filepath.Join(compilerDirectoryPath, compilerRuntimeConfigFileName), runtimeConfigContent)
	writeFileAt(t, filepath.Join(compilerDirectoryPath, compilerDepsFileName), "")
	writeFileAt(t, filepath.Join(compilerDirectoryPath, codeAnalysisDllFileName), "")
	writeFileAt(t, filepath.Join(compilerDirectoryPath, codeAnalysisCSharpDllFileName), "")
}

// runtimeConfigWithVersion builds a csc.runtimeconfig.json that requires the given framework version.
func runtimeConfigWithVersion(version string) string {
	return `{"runtimeOptions":{"framework":{"name":"Microsoft.NETCore.App","version":"` + version + `"}}}`
}

// writeSharedRuntime creates a dotnet host plus one installed shared framework version.
func writeSharedRuntime(t *testing.T, runtimeRootPath string, version string) {
	t.Helper()
	writeFileAt(t, filepath.Join(runtimeRootPath, hostFileName()), "")
	makeDirAt(t, filepath.Join(runtimeRootPath, sharedDirectoryName, sharedFrameworkName, version))
}

// Verifies the 2022-era layout resolves the compiler under Contents and pairs it with NetCoreRuntime.
func TestResolveEditorCompilerPathsForContentsRootLayout(t *testing.T) {
	contentsPath := filepath.Join(t.TempDir(), "Unity.app", "Contents")
	compilerDirectoryPath := filepath.Join(contentsPath, dotNetSdkRoslynDirectoryName)
	writeCompilerFiles(t, compilerDirectoryPath, "{}")
	writeSharedRuntime(t, filepath.Join(contentsPath, netCoreRuntimeDirectoryName), "6.0.21")

	paths, err := ResolveEditorCompilerPaths(filepath.Join(contentsPath, "MacOS", "Unity"))
	if err != nil {
		t.Fatalf("expected the layout to resolve, got error: %v", err)
	}

	wantHost := filepath.Join(contentsPath, netCoreRuntimeDirectoryName, hostFileName())
	if paths.DotnetHostPath != wantHost {
		t.Errorf("host path = %s, want %s", paths.DotnetHostPath, wantHost)
	}
	wantShared := filepath.Join(
		contentsPath, netCoreRuntimeDirectoryName, sharedDirectoryName, sharedFrameworkName, "6.0.21")
	if paths.SharedFrameworkPath != wantShared {
		t.Errorf("shared framework path = %s, want %s", paths.SharedFrameworkPath, wantShared)
	}
	if paths.CompilerDllPath != filepath.Join(compilerDirectoryPath, compilerDllFileName) {
		t.Errorf("compiler dll path = %s, want it under %s", paths.CompilerDllPath, compilerDirectoryPath)
	}
}

// Verifies a Resources/Scripting SDK layout keeps NetCoreRuntime when it satisfies the required major.
func TestResolveEditorCompilerPathsKeepsNetCoreRuntimeWhenMajorIsSatisfied(t *testing.T) {
	contentsPath := filepath.Join(t.TempDir(), "Unity.app", "Contents")
	scriptingRootPath := filepath.Join(contentsPath, "Resources", "Scripting")
	compilerDirectoryPath := filepath.Join(
		scriptingRootPath, dotNetSdkDirectoryName, sdkDirectoryName, "8.0.318",
		roslynDirectoryName, bincoreDirectoryName)
	writeCompilerFiles(t, compilerDirectoryPath, runtimeConfigWithVersion("8.0.21"))
	writeSharedRuntime(t, filepath.Join(scriptingRootPath, netCoreRuntimeDirectoryName), "8.0.21")
	// Both runtimes satisfy the required major here, so only a resolver that prefers NetCoreRuntime
	// passes this test; without the second runtime the fallback would produce the same answer.
	writeSharedRuntime(t, filepath.Join(scriptingRootPath, dotNetSdkDirectoryName), "8.0.21")

	paths, err := ResolveEditorCompilerPaths(filepath.Join(contentsPath, "MacOS", "Unity"))
	if err != nil {
		t.Fatalf("expected the layout to resolve, got error: %v", err)
	}

	wantHost := filepath.Join(scriptingRootPath, netCoreRuntimeDirectoryName, hostFileName())
	if paths.DotnetHostPath != wantHost {
		t.Errorf("host path = %s, want %s", paths.DotnetHostPath, wantHost)
	}
	wantShared := filepath.Join(
		scriptingRootPath, netCoreRuntimeDirectoryName, sharedDirectoryName, sharedFrameworkName, "8.0.21")
	if paths.SharedFrameworkPath != wantShared {
		t.Errorf("shared framework path = %s, want %s", paths.SharedFrameworkPath, wantShared)
	}
	if paths.CompilerDirectory != compilerDirectoryPath {
		t.Errorf("compiler directory = %s, want %s", paths.CompilerDirectory, compilerDirectoryPath)
	}
}

// Verifies the resolver switches to the DotNetSdk runtime when NetCoreRuntime is too old for csc.
func TestResolveEditorCompilerPathsSwitchesToDotNetSdkRuntime(t *testing.T) {
	contentsPath := filepath.Join(t.TempDir(), "Unity.app", "Contents")
	scriptingRootPath := filepath.Join(contentsPath, "Resources", "Scripting")
	dotNetSdkRootPath := filepath.Join(scriptingRootPath, dotNetSdkDirectoryName)
	compilerDirectoryPath := filepath.Join(
		dotNetSdkRootPath, sdkDirectoryName, "10.0.301", roslynDirectoryName, bincoreDirectoryName)
	writeCompilerFiles(t, compilerDirectoryPath, runtimeConfigWithVersion("10.0.9"))
	writeSharedRuntime(t, filepath.Join(scriptingRootPath, netCoreRuntimeDirectoryName), "8.0.21")
	writeSharedRuntime(t, dotNetSdkRootPath, "10.0.9")

	paths, err := ResolveEditorCompilerPaths(filepath.Join(contentsPath, "MacOS", "Unity"))
	if err != nil {
		t.Fatalf("expected the layout to resolve, got error: %v", err)
	}

	wantHost := filepath.Join(dotNetSdkRootPath, hostFileName())
	if paths.DotnetHostPath != wantHost {
		t.Errorf("host path = %s, want %s", paths.DotnetHostPath, wantHost)
	}
	wantShared := filepath.Join(dotNetSdkRootPath, sharedDirectoryName, sharedFrameworkName, "10.0.9")
	if paths.SharedFrameworkPath != wantShared {
		t.Errorf("shared framework path = %s, want %s", paths.SharedFrameworkPath, wantShared)
	}
}

// Verifies a Windows-shaped install resolves its compiler from the Data directory beside Unity.exe.
func TestResolveEditorCompilerPathsForWindowsDataLayout(t *testing.T) {
	editorDirectoryPath := filepath.Join(t.TempDir(), "Editor")
	dataPath := filepath.Join(editorDirectoryPath, windowsEditorDataDirectoryName)
	compilerDirectoryPath := filepath.Join(dataPath, dotNetSdkRoslynDirectoryName)
	writeCompilerFiles(t, compilerDirectoryPath, "{}")
	writeSharedRuntime(t, filepath.Join(dataPath, netCoreRuntimeDirectoryName), "6.0.21")

	paths, err := ResolveEditorCompilerPaths(filepath.Join(editorDirectoryPath, "Unity.exe"))
	if err != nil {
		t.Fatalf("expected the layout to resolve, got error: %v", err)
	}

	if paths.CompilerDirectory != compilerDirectoryPath {
		t.Errorf("compiler directory = %s, want %s", paths.CompilerDirectory, compilerDirectoryPath)
	}
}

// Verifies a missing required compiler file is reported by name instead of resolving silently.
func TestResolveEditorCompilerPathsReportsMissingRequiredFile(t *testing.T) {
	contentsPath := filepath.Join(t.TempDir(), "Unity.app", "Contents")
	compilerDirectoryPath := filepath.Join(contentsPath, dotNetSdkRoslynDirectoryName)
	writeCompilerFiles(t, compilerDirectoryPath, "{}")
	if err := os.Remove(filepath.Join(compilerDirectoryPath, compilerDepsFileName)); err != nil {
		t.Fatalf("failed to remove %s: %v", compilerDepsFileName, err)
	}
	writeSharedRuntime(t, filepath.Join(contentsPath, netCoreRuntimeDirectoryName), "6.0.21")

	_, err := ResolveEditorCompilerPaths(filepath.Join(contentsPath, "MacOS", "Unity"))
	if err == nil {
		t.Fatal("expected an error when a required compiler file is missing")
	}
	if !strings.Contains(err.Error(), compilerDepsFileName) {
		t.Errorf("error %q does not name the missing file %s", err.Error(), compilerDepsFileName)
	}
}

// Verifies the newest SDK directory wins when an Editor ships several Roslyn SDKs.
func TestResolveEditorCompilerPathsPicksNewestSdkDirectory(t *testing.T) {
	contentsPath := filepath.Join(t.TempDir(), "Unity.app", "Contents")
	scriptingRootPath := filepath.Join(contentsPath, "Resources", "Scripting")
	sdkRootPath := filepath.Join(scriptingRootPath, dotNetSdkDirectoryName, sdkDirectoryName)
	olderCompilerDirectoryPath := filepath.Join(
		sdkRootPath, "8.0.318", roslynDirectoryName, bincoreDirectoryName)
	newerCompilerDirectoryPath := filepath.Join(
		sdkRootPath, "9.0.100", roslynDirectoryName, bincoreDirectoryName)
	writeCompilerFiles(t, olderCompilerDirectoryPath, runtimeConfigWithVersion("8.0.21"))
	writeCompilerFiles(t, newerCompilerDirectoryPath, runtimeConfigWithVersion("8.0.21"))
	writeSharedRuntime(t, filepath.Join(scriptingRootPath, netCoreRuntimeDirectoryName), "8.0.21")

	paths, err := ResolveEditorCompilerPaths(filepath.Join(contentsPath, "MacOS", "Unity"))
	if err != nil {
		t.Fatalf("expected the layout to resolve, got error: %v", err)
	}

	if paths.CompilerDirectory != newerCompilerDirectoryPath {
		t.Errorf("compiler directory = %s, want %s", paths.CompilerDirectory, newerCompilerDirectoryPath)
	}
}

// Verifies an unrecognized Editor executable location is rejected instead of guessed at.
func TestResolveEditorCompilerPathsRejectsUnknownEditorLayout(t *testing.T) {
	_, err := ResolveEditorCompilerPaths(filepath.Join(t.TempDir(), "Unity"))
	if err == nil {
		t.Fatal("expected an error for an executable with no Editor layout around it")
	}
}

// Verifies the resolver finds a real bundled compiler when an installed Editor is pointed at.
func TestResolveEditorCompilerPathsAgainstInstalledEditor(t *testing.T) {
	editorExecutablePath := os.Getenv("ULOOP_EDITOR_EXE")
	if editorExecutablePath == "" {
		t.Skip("set ULOOP_EDITOR_EXE to a Unity Editor executable to run this check")
	}

	paths, err := ResolveEditorCompilerPaths(editorExecutablePath)
	if err != nil {
		t.Fatalf("failed to resolve the bundled compiler for %s: %v", editorExecutablePath, err)
	}

	t.Logf("host: %s", paths.DotnetHostPath)
	t.Logf("compiler: %s", paths.CompilerDllPath)
	t.Logf("shared framework: %s", paths.SharedFrameworkPath)
}
