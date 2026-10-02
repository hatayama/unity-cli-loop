package dispatcher

import (
	"os"
	"path/filepath"
	"runtime"
	"strings"
	"testing"
)

func TestSkillDirCleanupHelpersTreatMissingDirectoryAsDone(t *testing.T) {
	// Verifies cleanup of an already removed skill directory succeeds without creating anything.
	missingDir := filepath.Join(t.TempDir(), "uloop-sample")

	if err := removeSkillDirWithIgnorableDebris(missingDir, nil); err != nil {
		t.Fatalf("removeSkillDirWithIgnorableDebris failed: %v", err)
	}
	if err := removeSkillDirIfEmpty(missingDir); err != nil {
		t.Fatalf("removeSkillDirIfEmpty failed: %v", err)
	}
	if removed, err := removeStaleSyncArtifacts(missingDir, nil); err != nil || removed {
		t.Fatalf("unexpected result: removed=%t err=%v", removed, err)
	}
	if fileExists(missingDir) {
		t.Fatal("cleanup must not create the missing directory")
	}
}

func TestSkillDirCleanupHelpersReportUnlistableDirectory(t *testing.T) {
	// Verifies cleanup reports a skill path that exists but cannot be listed instead of treating it as removed.
	if runtime.GOOS == "windows" {
		t.Skip("Windows reports listing a file differently from ENOTDIR.")
	}
	skillFile := filepath.Join(t.TempDir(), "uloop-sample")
	writeDispatcherTestFile(t, skillFile, "not a directory")
	wantSuffix := "uloop-sample: not a directory"

	if err := removeSkillDirWithIgnorableDebris(skillFile, nil); err == nil || !strings.HasSuffix(err.Error(), wantSuffix) {
		t.Fatalf("removeSkillDirWithIgnorableDebris: expected %q, got %v", wantSuffix, err)
	}
	if err := removeSkillDirIfEmpty(skillFile); err == nil || !strings.HasSuffix(err.Error(), wantSuffix) {
		t.Fatalf("removeSkillDirIfEmpty: expected %q, got %v", wantSuffix, err)
	}
	if _, err := removeStaleSyncArtifacts(skillFile, nil); err == nil || !strings.HasSuffix(err.Error(), wantSuffix) {
		t.Fatalf("removeStaleSyncArtifacts: expected %q, got %v", wantSuffix, err)
	}
	if !fileExists(skillFile) {
		t.Fatal("the unlistable path must be left in place")
	}
}

func TestRemoveSkillDirIfEmptyRemovesOnlyEmptyDirectory(t *testing.T) {
	// Verifies an empty skill directory is removed while one with any entry is kept.
	emptyDir := filepath.Join(t.TempDir(), "empty")
	if err := os.MkdirAll(emptyDir, 0o755); err != nil {
		t.Fatalf("failed to create %s: %v", emptyDir, err)
	}
	keptDir := filepath.Join(t.TempDir(), "kept")
	writeDispatcherTestFile(t, filepath.Join(keptDir, "user.txt"), "mine")

	if err := removeSkillDirIfEmpty(emptyDir); err != nil || fileExists(emptyDir) {
		t.Fatalf("expected the empty directory to be removed: err=%v", err)
	}
	if err := removeSkillDirIfEmpty(keptDir); err != nil || !fileExists(filepath.Join(keptDir, "user.txt")) {
		t.Fatalf("expected the non-empty directory to be kept: err=%v", err)
	}
}

func TestRemoveEntryOfWrongTypeReplacesDirectoryOccupant(t *testing.T) {
	// Verifies a directory at a name the source owns as a file is removed with its contents.
	occupant := filepath.Join(t.TempDir(), "notes.md")
	writeDispatcherTestFile(t, filepath.Join(occupant, "nested", "file.txt"), "x")

	if err := removeEntryOfWrongType(occupant, false); err != nil {
		t.Fatalf("removeEntryOfWrongType failed: %v", err)
	}
	if fileExists(occupant) {
		t.Fatal("the directory occupant must be removed")
	}
}

func TestRemoveEntryOfWrongTypeReportsUninspectableOccupant(t *testing.T) {
	// Verifies an occupant that cannot be inspected is reported instead of being treated as absent.
	if runtime.GOOS == "windows" {
		t.Skip("Windows reports a path below a file as not found rather than ENOTDIR.")
	}
	parentFile := filepath.Join(t.TempDir(), "file")
	writeDispatcherTestFile(t, parentFile, "x")

	err := removeEntryOfWrongType(filepath.Join(parentFile, "notes.md"), false)

	if err == nil || !strings.HasPrefix(err.Error(), "lstat "+parentFile) {
		t.Fatalf("expected the lstat error, got %v", err)
	}
}

func TestRemoveStaleSyncArtifactsReportsUnremovableArtifact(t *testing.T) {
	// Verifies a stale sync artifact that cannot be removed is reported instead of being counted as removed.
	skillDir := filepath.Join(t.TempDir(), "uloop-sample")
	artifactDir := filepath.Join(skillDir, "references"+skillSyncBackupSuffix+"123")
	writeDispatcherTestFile(t, filepath.Join(artifactDir, "note.md"), "note")
	lockSkillsTestDirectory(t, artifactDir, 0o555)

	removed, err := removeStaleSyncArtifacts(skillDir, nil)

	if removed || err == nil || !strings.HasPrefix(err.Error(), "unlinkat "+filepath.Join(artifactDir, "note.md")) {
		t.Fatalf("expected the removal failure: removed=%t err=%v", removed, err)
	}
}

func TestIsStaleSyncArtifactNameRequiresDigitTail(t *testing.T) {
	// Verifies only the marker followed by digits is treated as a sync artifact.
	cases := map[string]bool{
		"SKILL.md" + skillSyncTempSuffix + "42":     true,
		"references" + skillSyncBackupSuffix + "7":  true,
		"SKILL.md" + skillSyncTempSuffix:            false,
		"archive" + skillSyncTempSuffix + "keepme":  false,
		"plain-notes.md":                            false,
		"references" + skillSyncBackupSuffix + "7a": false,
	}
	for name, want := range cases {
		if got := isStaleSyncArtifactName(name); got != want {
			t.Fatalf("isStaleSyncArtifactName(%q) = %t, want %t", name, got, want)
		}
	}
}
