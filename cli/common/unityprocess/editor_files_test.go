package unityprocess

import (
	"os"
	"path/filepath"
	"strings"
	"testing"
)

// Verifies the lockfile is looked up where Unity keeps it, under the project's Temp directory.
func TestLockfilePathIsUnderTheProjectTempDirectory(t *testing.T) {
	projectRoot := filepath.Join("projects", "sample")

	if got, want := LockfilePath(projectRoot), filepath.Join(projectRoot, "Temp", "UnityLockfile"); got != want {
		t.Fatalf("LockfilePath() = %q, want %q", got, want)
	}
}

// Verifies the default Editor log location for each platform Unity documents, and that a missing base
// directory yields no path instead of one rooted in the working directory.
func TestEditorLogPathForEachPlatform(t *testing.T) {
	const homeDir = "/home/<USER_NAME>"
	const localAppData = `C:\Users\<USER_NAME>\AppData\Local`
	cases := []struct {
		name         string
		goos         string
		homeDir      string
		localAppData string
		want         string
	}{
		{name: "macOS", goos: "darwin", homeDir: homeDir, want: filepath.Join(homeDir, "Library", "Logs", "Unity", "Editor.log")},
		{name: "Windows", goos: "windows", localAppData: localAppData, want: filepath.Join(localAppData, "Unity", "Editor", "Editor.log")},
		{name: "Linux", goos: "linux", homeDir: homeDir, want: filepath.Join(homeDir, ".config", "unity3d", "Editor.log")},
		{name: "macOS without a home directory", goos: "darwin", want: ""},
		{name: "Windows without LOCALAPPDATA", goos: "windows", homeDir: homeDir, want: ""},
		{name: "Linux without a home directory", goos: "linux", want: ""},
		{name: "other platform", goos: "plan9", homeDir: homeDir, want: ""},
	}

	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			got := editorLogPathFor(testCase.goos, testCase.homeDir, testCase.localAppData)
			if got != testCase.want {
				t.Fatalf("editorLogPathFor() = %q, want %q", got, testCase.want)
			}
		})
	}
}

// writeEditorLogForTest writes an Editor log whose head records projectPath as Unity does at startup,
// with the given line ending.
func writeEditorLogForTest(t *testing.T, logPath string, projectPath string, lineEnding string) {
	t.Helper()
	lines := []string{
		"COMMAND LINE ARGUMENTS:",
		"Unity",
		"-projectPath",
		projectPath,
		"Successfully changed project path to: " + projectPath,
		"Loading GUID <-> Path mappings...",
	}
	if err := os.WriteFile(logPath, []byte(strings.Join(lines, lineEnding)+lineEnding), 0o644); err != nil {
		t.Fatalf("writing %s failed: %v", logPath, err)
	}
}

// Verifies the Editor log that names the project is picked whether it is still Editor.log or a later
// Editor start already renamed it to Editor-prev.log, and that logs of other projects, which every later
// start leaves at both names, are never picked.
func TestProjectEditorLogPath(t *testing.T) {
	projectRoot := t.TempDir()
	otherProject := t.TempDir()
	cases := []struct {
		name         string
		currentLog   string
		previousLog  string
		lineEnding   string
		wantPrevious bool
		wantNone     bool
	}{
		{name: "Editor.log is the project's", currentLog: projectRoot, previousLog: otherProject, lineEnding: "\n"},
		{name: "renamed to Editor-prev.log", currentLog: otherProject, previousLog: projectRoot, lineEnding: "\n", wantPrevious: true},
		{name: "Windows line endings", currentLog: projectRoot, lineEnding: "\r\n"},
		{name: "both are other projects'", currentLog: otherProject, previousLog: otherProject, lineEnding: "\n", wantNone: true},
		{name: "no log at all", lineEnding: "\n", wantNone: true},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			logDirectory := t.TempDir()
			currentLogPath := filepath.Join(logDirectory, "Editor.log")
			previousLogPath := filepath.Join(logDirectory, "Editor-prev.log")
			if testCase.currentLog != "" {
				writeEditorLogForTest(t, currentLogPath, testCase.currentLog, testCase.lineEnding)
			}
			if testCase.previousLog != "" {
				writeEditorLogForTest(t, previousLogPath, testCase.previousLog, testCase.lineEnding)
			}
			want := currentLogPath
			if testCase.wantPrevious {
				want = previousLogPath
			}
			if testCase.wantNone {
				want = ""
			}

			if got := projectEditorLogPath(currentLogPath, projectRoot); got != want {
				t.Fatalf("projectEditorLogPath() = %q, want %q", got, want)
			}
		})
	}
}

// Verifies no log is picked when the default log location could not be resolved, instead of looking in
// the working directory.
func TestProjectEditorLogPathWithoutADefaultLocation(t *testing.T) {
	if got := projectEditorLogPath("", t.TempDir()); got != "" {
		t.Fatalf("projectEditorLogPath() = %q, want no log", got)
	}
}
