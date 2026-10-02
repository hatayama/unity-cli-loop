package dispatcher

import (
	"os"
	"path/filepath"
	"strings"
	"testing"
)

func TestSyncSkillDirectorySkipsMetaDirectories(t *testing.T) {
	// Verifies Unity .meta directories in the source are not copied into the installed skill.
	sourceDir := filepath.Join(t.TempDir(), "Skill")
	writeSkillFile(t, sourceDir, sampleSkillContent)
	writeDispatcherTestFile(t, filepath.Join(sourceDir, "references.meta", "inner.md"), "meta")
	writeDispatcherTestFile(t, filepath.Join(sourceDir, "references", "guide.md"), "guide")
	destinationDir := filepath.Join(t.TempDir(), "skills", "uloop-sample")

	if err := syncSkillDirectory(sourceDir, destinationDir); err != nil {
		t.Fatalf("syncSkillDirectory failed: %v", err)
	}

	assertFileContent(t, filepath.Join(destinationDir, "references", "guide.md"), "guide")
	if fileExists(filepath.Join(destinationDir, "references.meta")) {
		t.Fatal(".meta directories must not be copied")
	}
}

func TestSyncSkillDirectoryReportsFailures(t *testing.T) {
	// Verifies sync failures leave the destination untouched and no temp directory behind.
	cases := []struct {
		name        string
		setup       func(t *testing.T, sourceDir string, destinationDir string)
		wantMessage string
	}{
		{
			name:        "source missing",
			wantMessage: string(filepath.Separator) + "Skill: ",
			setup: func(t *testing.T, sourceDir string, destinationDir string) {
				if err := os.RemoveAll(sourceDir); err != nil {
					t.Fatalf("failed to remove source: %v", err)
				}
			},
		},
		{
			name:        "source file unreadable",
			wantMessage: "SKILL.md: permission denied",
			setup: func(t *testing.T, sourceDir string, destinationDir string) {
				lockSkillsTestFile(t, filepath.Join(sourceDir, "SKILL.md"))
			},
		},
		{
			name:        "parent not writable",
			wantMessage: string(filepath.Separator) + "uloop-sample" + skillSyncTempSuffix,
			setup: func(t *testing.T, sourceDir string, destinationDir string) {
				lockSkillsTestDirectory(t, filepath.Dir(destinationDir), 0o555)
			},
		},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			sourceDir := filepath.Join(t.TempDir(), "Skill")
			writeSkillFile(t, sourceDir, sampleSkillContent)
			destinationDir := filepath.Join(t.TempDir(), "skills", "uloop-sample")
			testCase.setup(t, sourceDir, destinationDir)

			err := syncSkillDirectory(sourceDir, destinationDir)
			if err == nil || !strings.Contains(err.Error(), testCase.wantMessage) {
				t.Fatalf("expected an error containing %q, got %v", testCase.wantMessage, err)
			}
			assertNoSkillSyncDebris(t, filepath.Dir(destinationDir))
		})
	}
}

// lockSkillsTestFile removes every permission from a file and restores it when the test ends.
func lockSkillsTestFile(t *testing.T, filePath string) {
	t.Helper()
	if os.Geteuid() == 0 {
		t.Skip("root can read files without permissions.")
	}
	lockSkillsTestDirectory(t, filepath.Dir(filePath), 0o755)
	if err := os.Chmod(filePath, 0o000); err != nil {
		t.Fatalf("failed to chmod %s: %v", filePath, err)
	}
	t.Cleanup(func() {
		_ = os.Chmod(filePath, 0o644)
	})
}

func assertNoSkillSyncDebris(t *testing.T, directory string) {
	t.Helper()
	entries, err := os.ReadDir(directory)
	if os.IsNotExist(err) {
		return
	}
	if err != nil {
		t.Fatalf("failed to read %s: %v", directory, err)
	}
	if len(entries) != 0 {
		t.Fatalf("sync must not leave debris in %s: %v", directory, entries)
	}
}

func TestSyncSkillDirectoryReportsUncreatableParent(t *testing.T) {
	// Verifies a destination below a regular file fails before any copy starts.
	parentFile := filepath.Join(t.TempDir(), "file")
	writeDispatcherTestFile(t, parentFile, "x")
	sourceDir := filepath.Join(t.TempDir(), "Skill")
	writeSkillFile(t, sourceDir, sampleSkillContent)

	err := syncSkillDirectory(sourceDir, filepath.Join(parentFile, "skills", "uloop-sample"))
	if err == nil || !strings.HasPrefix(err.Error(), "mkdir "+parentFile) {
		t.Fatalf("expected a parent directory failure, got %v", err)
	}
}

func TestReplaceSkillDirectoryRestoresDestinationWhenReplacementFails(t *testing.T) {
	// Verifies a failed swap moves the previous install back instead of leaving the skill missing.
	destinationDir := filepath.Join(t.TempDir(), "uloop-sample")
	writeSkillFile(t, destinationDir, "previous")

	missingSource := filepath.Join(t.TempDir(), "missing")
	err := replaceSkillDirectory(missingSource, destinationDir)

	if err == nil || !strings.HasPrefix(err.Error(), "rename "+missingSource) {
		t.Fatalf("expected the replacement to fail, got %v", err)
	}
	assertFileContent(t, filepath.Join(destinationDir, "SKILL.md"), "previous")
	assertNoSkillSyncDebrisExcept(t, filepath.Dir(destinationDir), "uloop-sample")
}

func assertNoSkillSyncDebrisExcept(t *testing.T, directory string, keep string) {
	t.Helper()
	entries, err := os.ReadDir(directory)
	if err != nil {
		t.Fatalf("failed to read %s: %v", directory, err)
	}
	for _, entry := range entries {
		if entry.Name() != keep {
			t.Fatalf("unexpected leftover %s in %s", entry.Name(), directory)
		}
	}
}

func TestReplaceSkillDirectoryReportsBlockedBackup(t *testing.T) {
	// Verifies an existing install whose parent forbids a backup is left in place and the error is returned.
	sourceDir := filepath.Join(t.TempDir(), "staged")
	writeSkillFile(t, sourceDir, "new")
	parentDir := t.TempDir()
	destinationDir := filepath.Join(parentDir, "uloop-sample")
	writeSkillFile(t, destinationDir, "previous")
	lockSkillsTestDirectory(t, parentDir, 0o555)

	if err := replaceSkillDirectory(sourceDir, destinationDir); err == nil || !strings.HasPrefix(err.Error(), "mkdir "+destinationDir+".uloop-backup-") {
		t.Fatalf("expected the backup to fail, got %v", err)
	}
	assertFileContent(t, filepath.Join(destinationDir, "SKILL.md"), "previous")
}

func TestReplaceSkillDirectoryReportsUninspectableDestination(t *testing.T) {
	// Verifies a destination that cannot be inspected is reported instead of being overwritten.
	parentFile := filepath.Join(t.TempDir(), "file")
	blockSkillsDirWithFile(t, filepath.Dir(parentFile), "file")

	if err := replaceSkillDirectory(t.TempDir(), filepath.Join(parentFile, "uloop-sample")); err == nil || !strings.HasPrefix(err.Error(), "stat "+parentFile) {
		t.Fatalf("expected a stat failure, got %v", err)
	}
}

func TestGetSkillStatusTreatsUnreadableFilesAsOutdated(t *testing.T) {
	// Verifies an installed SKILL.md or reference that cannot be read makes the skill outdated so install rewrites it.
	for _, lockedFile := range []string{"SKILL.md", filepath.Join("references", "guide.md")} {
		t.Run(lockedFile, func(t *testing.T) {
			sourceDir := filepath.Join(t.TempDir(), "Skill")
			writeSkillFile(t, sourceDir, sampleSkillContent)
			writeDispatcherTestFile(t, filepath.Join(sourceDir, "references", "guide.md"), "guide")
			baseDir := t.TempDir()
			installedDir := getPreferredSkillDir(baseDir, "uloop-sample", true)
			if err := syncSkillDirectory(sourceDir, installedDir); err != nil {
				t.Fatalf("syncSkillDirectory failed: %v", err)
			}
			skill := skillDefinition{name: "uloop-sample", content: []byte(sampleSkillContent), sourceDirectory: sourceDir}
			assertSkillStatus(t, baseDir, skill, "installed")
			lockSkillsTestFile(t, filepath.Join(installedDir, lockedFile))

			assertSkillStatus(t, baseDir, skill, "outdated")
		})
	}
}
