package unityprocess

import (
	"path/filepath"
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
