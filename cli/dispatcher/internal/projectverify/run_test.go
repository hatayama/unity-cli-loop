package projectverify_test

import (
	"path/filepath"
	"testing"
)

// Verifies a relative project root is refused instead of being resolved against the working directory.
func TestRunRejectsRelativeProjectRoot(t *testing.T) {
	assertRunFails(t, "relative/path", "relative/path")
}

// Verifies a project whose Assets entry is a file is refused with the offending path.
func TestRunRejectsProjectWhoseAssetsIsNotADirectory(t *testing.T) {
	root := filepath.Join(t.TempDir(), "Project")
	writeFileAt(t, filepath.Join(root, "Assets"), "")
	makeDirectory(t, filepath.Join(root, "ProjectSettings"))

	assertRunFails(t, root, filepath.Join(root, "Assets"))
}

// Verifies a project without a ProjectSettings directory is refused with the missing path.
func TestRunRejectsProjectWithoutProjectSettingsDirectory(t *testing.T) {
	root := filepath.Join(t.TempDir(), "Project")
	makeDirectory(t, filepath.Join(root, "Assets"))

	assertRunFails(t, root, filepath.Join(root, "ProjectSettings"))
}
