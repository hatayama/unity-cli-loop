package dispatcher

import (
	"os"
	"path/filepath"
	"runtime"
	"strings"
	"testing"
)

// installDirModeSkillWithNotes installs a dir-mode skill whose source also owns a
// top-level notes.md, so per-file comparisons beyond SKILL.md can be exercised.
func installDirModeSkillWithNotes(t *testing.T) (skillDefinition, string) {
	t.Helper()
	root := t.TempDir()
	skill := writeDirModeSkillSource(t, root, "uloop-sample")
	writeDispatcherTestFile(t, filepath.Join(skill.sourceDirectory, "notes.md"), "notes\n")
	store := filepath.Join(root, "store")
	if err := syncSkillDirectoryPreservingForeignFiles(skill.sourceDirectory, filepath.Join(store, skill.name)); err != nil {
		t.Fatalf("setup sync failed: %v", err)
	}
	assertDirSkillStatus(t, store, skill, "installed")
	return skill, store
}

func assertDirSkillStatus(t *testing.T, store string, skill skillDefinition, want string) {
	t.Helper()
	state, err := getDirSkillState(store, skill)
	if err != nil {
		t.Fatalf("getDirSkillState failed: %v", err)
	}
	if state.status != want {
		t.Fatalf("status mismatch: got %q want %q (reason %q)", state.status, want, state.conflictReason)
	}
}

func TestGetDirSkillStateMarksDivergedOwnedFilesOutdated(t *testing.T) {
	// Verifies an installed owned file that differs, cannot be read, or is no longer a regular file makes the skill outdated.
	cases := []struct {
		name   string
		modify func(t *testing.T, installedDir string)
	}{
		{
			name: "different content",
			modify: func(t *testing.T, installedDir string) {
				writeDispatcherTestFile(t, filepath.Join(installedDir, "notes.md"), "edited\n")
			},
		},
		{
			name: "unreadable file",
			modify: func(t *testing.T, installedDir string) {
				lockSkillsTestFile(t, filepath.Join(installedDir, "notes.md"))
			},
		},
		{
			// The symlink points at identical content, so only the regular-file check can report it.
			name: "symlink with identical content",
			modify: func(t *testing.T, installedDir string) {
				replaceWithSymlinkToSameContent(t, filepath.Join(installedDir, "notes.md"), "notes\n")
			},
		},
		{
			name: "unreadable owned directory",
			modify: func(t *testing.T, installedDir string) {
				lockSkillsTestDirectory(t, filepath.Join(installedDir, "references"), 0o000)
			},
		},
		{
			name: "unreadable file in owned directory",
			modify: func(t *testing.T, installedDir string) {
				lockSkillsTestFile(t, filepath.Join(installedDir, "references", "note.md"))
			},
		},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			skill, store := installDirModeSkillWithNotes(t)
			testCase.modify(t, filepath.Join(store, skill.name))

			assertDirSkillStatus(t, store, skill, "outdated")
		})
	}
}

func replaceWithSymlinkToSameContent(t *testing.T, path string, content string) {
	t.Helper()
	if runtime.GOOS == "windows" {
		t.Skip("creating symlinks requires extra privileges on Windows.")
	}
	target := filepath.Join(t.TempDir(), "target.md")
	writeDispatcherTestFile(t, target, content)
	if err := os.Remove(path); err != nil {
		t.Fatalf("failed to remove %s: %v", path, err)
	}
	if err := os.Symlink(target, path); err != nil {
		t.Fatalf("failed to create symlink: %v", err)
	}
}

func TestGetDirSkillStateReportsUnreadableInstalledSkillFileAsConflict(t *testing.T) {
	// Verifies an unreadable installed SKILL.md is reported per skill as a conflict instead of aborting the run.
	skill, store := installDirModeSkillWithNotes(t)
	lockSkillsTestFile(t, filepath.Join(store, skill.name, "SKILL.md"))

	state, err := getDirSkillState(store, skill)
	if err != nil {
		t.Fatalf("getDirSkillState failed: %v", err)
	}
	if state.status != "conflict" || !strings.Contains(state.conflictReason, `cannot read SKILL.md for skill "uloop-sample"`) {
		t.Fatalf("unexpected state: %+v", state)
	}
}

func TestGetDirSkillStateTreatsDirectoryWithOnlyForeignFilesAsNotInstalled(t *testing.T) {
	// Verifies a skill directory holding nothing at source-owned names is not installed rather than a conflict.
	root := t.TempDir()
	skill := writeDirModeSkillSource(t, root, "uloop-sample")
	store := filepath.Join(root, "store")
	writeDispatcherTestFile(t, filepath.Join(store, skill.name, "user-notes.txt"), "mine")

	assertDirSkillStatus(t, store, skill, "not_installed")
}

func TestGetDirSkillStateReportsUnreadableSources(t *testing.T) {
	// Verifies a source-side read failure is returned as an error instead of being treated as a mismatch.
	cases := []struct {
		name          string
		removeSkill   bool
		lock          func(t *testing.T, sourceDir string)
		wantErrSuffix string
	}{
		{
			name:          "source file",
			lock:          func(t *testing.T, sourceDir string) { lockSkillsTestFile(t, filepath.Join(sourceDir, "notes.md")) },
			wantErrSuffix: "notes.md: permission denied",
		},
		{
			name: "source directory",
			lock: func(t *testing.T, sourceDir string) {
				lockSkillsTestDirectory(t, filepath.Join(sourceDir, "references"), 0o000)
			},
			wantErrSuffix: "references: permission denied",
		},
		{
			name: "file in source directory",
			lock: func(t *testing.T, sourceDir string) {
				lockSkillsTestFile(t, filepath.Join(sourceDir, "references", "note.md"))
			},
			wantErrSuffix: "note.md: permission denied",
		},
		{
			// Without SKILL.md the install evidence check reads the source instead of the staleness check.
			name:          "source file during evidence check",
			removeSkill:   true,
			lock:          func(t *testing.T, sourceDir string) { lockSkillsTestFile(t, filepath.Join(sourceDir, "notes.md")) },
			wantErrSuffix: "notes.md: permission denied",
		},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			skill, store := installDirModeSkillWithNotes(t)
			if testCase.removeSkill {
				if err := os.Remove(filepath.Join(store, skill.name, "SKILL.md")); err != nil {
					t.Fatalf("failed to remove SKILL.md: %v", err)
				}
			}
			testCase.lock(t, skill.sourceDirectory)

			_, err := getDirSkillState(store, skill)

			if err == nil || !strings.HasSuffix(err.Error(), testCase.wantErrSuffix) {
				t.Fatalf("expected an error ending with %q, got %v", testCase.wantErrSuffix, err)
			}
		})
	}
}

func TestGetDirSkillStateReportsUnreadableStore(t *testing.T) {
	// Verifies a store path that exists but cannot be listed is an error, not "not installed".
	if runtime.GOOS == "windows" {
		t.Skip("Windows reports listing a file differently from ENOTDIR.")
	}
	root := t.TempDir()
	skill := writeDirModeSkillSource(t, root, "uloop-sample")
	storeFile := filepath.Join(root, "store-file")
	writeDispatcherTestFile(t, storeFile, "not a directory")

	_, err := getDirSkillState(storeFile, skill)

	if err == nil || !strings.Contains(err.Error(), "store-file: not a directory") {
		t.Fatalf("expected the store listing error, got %v", err)
	}
}
