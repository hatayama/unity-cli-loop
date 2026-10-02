package compilecheck

import (
	"path/filepath"
	"strings"
	"testing"
)

// newMacEditorContents returns a fresh Unity.app/Contents path and the executable inside it.
func newMacEditorContents(t *testing.T) (string, string) {
	t.Helper()
	contentsPath := filepath.Join(t.TempDir(), "Unity.app", "Contents")
	makeDirAt(t, filepath.Join(contentsPath, "MacOS"))
	return contentsPath, filepath.Join(contentsPath, "MacOS", "Unity")
}

// Verifies an Editor whose data holds no compiler layout is rejected with the layout message.
func TestResolveEditorCompilerPathsRejectsEditorWithoutCompiler(t *testing.T) {
	contentsPath, executablePath := newMacEditorContents(t)

	_, err := ResolveEditorCompilerPaths(executablePath)

	if err == nil || err.Error() != "no Unity-bundled C# compiler layout found under "+contentsPath {
		t.Fatalf("err = %v", err)
	}
}

// Verifies the breadth-first scan finds a compiler layout nested below the known locations, and
// gives up on one nested deeper than the scan depth limit.
func TestResolveEditorCompilerPathsScansForNestedLayouts(t *testing.T) {
	t.Run("within the depth limit", func(t *testing.T) {
		contentsPath, executablePath := newMacEditorContents(t)
		scriptingRootPath := filepath.Join(contentsPath, "Frameworks", "Scripting")
		writeCompilerFiles(t, filepath.Join(scriptingRootPath, dotNetSdkRoslynDirectoryName), "{}")
		writeSharedRuntime(t, filepath.Join(scriptingRootPath, netCoreRuntimeDirectoryName), "6.0.21")

		paths, err := ResolveEditorCompilerPaths(executablePath)
		if err != nil {
			t.Fatalf("expected the nested layout to resolve, got error: %v", err)
		}
		if paths.CompilerDirectory != filepath.Join(scriptingRootPath, dotNetSdkRoslynDirectoryName) {
			t.Fatalf("compiler directory = %s", paths.CompilerDirectory)
		}
	})
	t.Run("beyond the depth limit", func(t *testing.T) {
		contentsPath, executablePath := newMacEditorContents(t)
		scriptingRootPath := filepath.Join(contentsPath, "a", "b", "c", "d", "e")
		writeCompilerFiles(t, filepath.Join(scriptingRootPath, dotNetSdkRoslynDirectoryName), "{}")
		writeSharedRuntime(t, filepath.Join(scriptingRootPath, netCoreRuntimeDirectoryName), "6.0.21")

		_, err := ResolveEditorCompilerPaths(executablePath)
		if err == nil || !strings.HasPrefix(err.Error(), "no Unity-bundled C# compiler layout found under ") {
			t.Fatalf("err = %v, want the layout to stay unfound", err)
		}
	})
}

// Verifies a NetCoreRuntime without any installed shared framework is reported as the missing
// shared framework directory.
func TestResolveEditorCompilerPathsReportsMissingSharedFramework(t *testing.T) {
	contentsPath, executablePath := newMacEditorContents(t)
	writeCompilerFiles(t, filepath.Join(contentsPath, dotNetSdkRoslynDirectoryName), "{}")
	writeFileAt(t, filepath.Join(contentsPath, netCoreRuntimeDirectoryName, hostFileName()), "")

	_, err := ResolveEditorCompilerPaths(executablePath)

	want := "required compiler file missing: " +
		filepath.Join(contentsPath, netCoreRuntimeDirectoryName, sharedDirectoryName, sharedFrameworkName)
	if err == nil || err.Error() != want {
		t.Fatalf("err = %v, want %q", err, want)
	}
}

// Verifies an SDK-only layout (no NetCoreRuntime) counts as a compiler layout only when its SDK root
// ships a shared framework, and then compiles with the SDK's own runtime.
func TestResolveEditorCompilerPathsForSdkOnlyLayout(t *testing.T) {
	writeSdkCompiler := func(t *testing.T, contentsPath string) string {
		dotNetSdkRootPath := filepath.Join(contentsPath, "Resources", "Scripting", dotNetSdkDirectoryName)
		writeCompilerFiles(t, filepath.Join(
			dotNetSdkRootPath, sdkDirectoryName, "10.0.301", roslynDirectoryName, bincoreDirectoryName),
			runtimeConfigWithVersion("10.0.9"))
		return dotNetSdkRootPath
	}
	t.Run("with a shared framework", func(t *testing.T) {
		contentsPath, executablePath := newMacEditorContents(t)
		dotNetSdkRootPath := writeSdkCompiler(t, contentsPath)
		writeSharedRuntime(t, dotNetSdkRootPath, "10.0.9")

		paths, err := ResolveEditorCompilerPaths(executablePath)
		if err != nil {
			t.Fatalf("expected the SDK-only layout to resolve, got error: %v", err)
		}
		if paths.DotnetHostPath != filepath.Join(dotNetSdkRootPath, hostFileName()) {
			t.Fatalf("host path = %s", paths.DotnetHostPath)
		}
	})
	t.Run("without a shared framework", func(t *testing.T) {
		contentsPath, executablePath := newMacEditorContents(t)
		dotNetSdkRootPath := writeSdkCompiler(t, contentsPath)
		writeFileAt(t, filepath.Join(dotNetSdkRootPath, hostFileName()), "")

		_, err := ResolveEditorCompilerPaths(executablePath)
		if err == nil || !strings.HasPrefix(err.Error(), "no Unity-bundled C# compiler layout found under ") {
			t.Fatalf("err = %v, want no layout", err)
		}
	})
}

// Verifies the resolver keeps NetCoreRuntime when the SDK runtime cannot satisfy the compiler:
// a missing SDK host or an SDK shared framework older than the required major.
func TestResolveEditorCompilerPathsFallsBackToNetCoreRuntime(t *testing.T) {
	cases := []struct {
		name     string
		writeSdk func(t *testing.T, dotNetSdkRootPath string)
	}{
		{name: "missing SDK host", writeSdk: func(t *testing.T, dotNetSdkRootPath string) {
			makeDirAt(t, filepath.Join(dotNetSdkRootPath, sharedDirectoryName, sharedFrameworkName, "10.0.9"))
		}},
		{name: "SDK framework too old", writeSdk: func(t *testing.T, dotNetSdkRootPath string) {
			writeSharedRuntime(t, dotNetSdkRootPath, "9.0.1")
		}},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			contentsPath, executablePath := newMacEditorContents(t)
			scriptingRootPath := filepath.Join(contentsPath, "Resources", "Scripting")
			dotNetSdkRootPath := filepath.Join(scriptingRootPath, dotNetSdkDirectoryName)
			writeCompilerFiles(t, filepath.Join(
				dotNetSdkRootPath, sdkDirectoryName, "10.0.301", roslynDirectoryName, bincoreDirectoryName),
				runtimeConfigWithVersion("10.0.9"))
			writeSharedRuntime(t, filepath.Join(scriptingRootPath, netCoreRuntimeDirectoryName), "8.0.21")
			testCase.writeSdk(t, dotNetSdkRootPath)

			paths, err := ResolveEditorCompilerPaths(executablePath)
			if err != nil {
				t.Fatalf("expected the layout to resolve, got error: %v", err)
			}
			if paths.DotnetHostPath != filepath.Join(scriptingRootPath, netCoreRuntimeDirectoryName, hostFileName()) {
				t.Fatalf("host path = %s, want the NetCoreRuntime host", paths.DotnetHostPath)
			}
		})
	}
}

// Verifies version-shaped directory names sort newest first and ahead of other names, other names
// sort in reverse ordinal order, and an absent version component counts as lower.
func TestCompareVersionDirectoryNames(t *testing.T) {
	cases := []struct {
		left  string
		right string
		want  int
	}{
		{left: "9.0.1", right: "10.0.0", want: 1},
		{left: "10.0.0", right: "9.0.1", want: -1},
		{left: "8.0", right: "8.0.1", want: 1},
		{left: "8.0.1", right: "8.0", want: -1},
		{left: "8.0.1", right: "preview", want: -1},
		{left: "preview", right: "8.0.1", want: 1},
		{left: "alpha", right: "beta", want: 1},
		{left: "8.0", right: "8.0", want: 0},
	}
	for _, testCase := range cases {
		if got := compareVersionDirectoryNames(testCase.left, testCase.right); got != testCase.want {
			t.Fatalf("compareVersionDirectoryNames(%q, %q) = %d, want %d", testCase.left, testCase.right, got, testCase.want)
		}
	}
}

// Verifies the version parser rejects negative components the way the .NET Version parser does.
func TestParseVersionPartsRejectsNegativeComponents(t *testing.T) {
	if parts, ok := parseVersionParts("8.-1"); ok {
		t.Fatalf("parseVersionParts(8.-1) = %v, want rejection", parts)
	}
}

// Verifies the SDK-root walk stops with no result at the top of a relative path and for an empty path.
func TestFindDirectoryContainingSdkStopsWithoutMatch(t *testing.T) {
	for _, start := range []string{filepath.Join("no-such-directory", "child"), ""} {
		if got := findDirectoryContainingSdk(start); got != "" {
			t.Fatalf("findDirectoryContainingSdk(%q) = %q, want empty", start, got)
		}
	}
}
