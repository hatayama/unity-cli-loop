package dispatcher

import (
	"path/filepath"
	"reflect"
	"testing"
)

func TestReadUnityEditorVersionRejectsMissingOrEmptyVersion(t *testing.T) {
	// Verifies a ProjectVersion.txt without an editor version line, or with an empty one, is reported instead of launching an unknown Editor.
	cases := map[string]string{
		"no version line": "m_EditorVersionWithRevision: 2022.3.0f1 (abc)\n",
		"empty version":   "m_EditorVersion:   \n",
	}
	wants := map[string]string{
		"no version line": "unity editor version not found in " + projectVersionFilePath,
		"empty version":   "unity editor version is empty in " + projectVersionFilePath,
	}
	for name, content := range cases {
		t.Run(name, func(t *testing.T) {
			projectRoot := t.TempDir()
			writeDispatcherTestFile(t, filepath.Join(projectRoot, projectVersionFilePath), content)

			version, err := readUnityEditorVersion(projectRoot)

			if err == nil || err.Error() != wants[name] || version != "" {
				t.Fatalf("expected %q, got version=%q err=%v", wants[name], version, err)
			}
		})
	}
}

func TestWindowsUnityExecutableCandidatesSkipsUnsetBases(t *testing.T) {
	// Verifies each set install base yields a Hub Editor path and unset bases are skipped, ending with the fixed default.
	t.Setenv("ProgramFiles", "<PROGRAM_FILES>")
	t.Setenv("ProgramFiles(x86)", "")
	t.Setenv("LOCALAPPDATA", "<LOCAL_APP_DATA>")

	candidates := windowsUnityExecutableCandidates("2022.3.0f1")

	want := []string{
		filepath.Join("<PROGRAM_FILES>", "Unity", "Hub", "Editor", "2022.3.0f1", "Editor", "Unity.exe"),
		filepath.Join("<LOCAL_APP_DATA>", "Unity", "Hub", "Editor", "2022.3.0f1", "Editor", "Unity.exe"),
		filepath.Join(`C:\Program Files`, "Unity", "Hub", "Editor", "2022.3.0f1", "Editor", "Unity.exe"),
	}
	if !reflect.DeepEqual(candidates, want) {
		t.Fatalf("candidates mismatch:\n got %v\nwant %v", candidates, want)
	}
}
