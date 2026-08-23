package dispatcher

import (
	"bytes"
	"os"
	"path/filepath"
	"runtime"
	"strings"
	"testing"
)

// writeDirModeSkillSource seeds a skill source directory with a SKILL.md and one
// reference file, returning the definition used by --output-dir mode tests.
func writeDirModeSkillSource(t *testing.T, root string, name string) skillDefinition {
	t.Helper()
	sourceDir := filepath.Join(root, "source", name, "Skill")
	content := "---\nname: " + name + "\n---\n\n# " + name + "\n"
	writeSkillFile(t, sourceDir, content)
	referencesDir := filepath.Join(sourceDir, "references")
	if err := os.MkdirAll(referencesDir, 0o755); err != nil {
		t.Fatalf("failed to create references dir: %v", err)
	}
	if err := os.WriteFile(filepath.Join(referencesDir, "note.md"), []byte("note\n"), 0o644); err != nil {
		t.Fatalf("failed to write reference: %v", err)
	}
	return skillDefinition{name: name, content: []byte(content), sourceDirectory: sourceDir}
}

// Tests that --output-dir accepts both the space-separated and equals-sign value forms.
func TestParseSkillsOptionsParsesDirFlag(t *testing.T) {
	spaceForm, err := parseSkillsOptions([]string{"--output-dir", "/custom/skills"})
	if err != nil {
		t.Fatalf("space form failed: %v", err)
	}
	if spaceForm.outputDir != "/custom/skills" {
		t.Fatalf("space form dir mismatch: %s", spaceForm.outputDir)
	}

	equalsForm, err := parseSkillsOptions([]string{"--output-dir=/custom/skills"})
	if err != nil {
		t.Fatalf("equals form failed: %v", err)
	}
	if equalsForm.outputDir != "/custom/skills" {
		t.Fatalf("equals form dir mismatch: %s", equalsForm.outputDir)
	}
}

// Tests that --output-dir without a value is rejected as an argument error,
// including when the next token is another flag that must not be swallowed
// as the destination path.
func TestParseSkillsOptionsRejectsDirWithoutValue(t *testing.T) {
	if _, err := parseSkillsOptions([]string{"--output-dir"}); err == nil {
		t.Fatal("expected error for --output-dir without value")
	}
	if _, err := parseSkillsOptions([]string{"--output-dir="}); err == nil {
		t.Fatal("expected error for --output-dir= without value")
	}
	if _, err := parseSkillsOptions([]string{"--output-dir", "--global"}); err == nil {
		t.Fatal("expected error when --output-dir is followed by --global instead of a path")
	}
	if _, err := parseSkillsOptions([]string{"--output-dir", "--claude"}); err == nil {
		t.Fatal("expected error when --output-dir is followed by a target flag instead of a path")
	}
}

// Tests that --output-dir cannot be combined with --global, target flags, or --flat.
func TestParseSkillsOptionsRejectsDirWithGlobalOrTargets(t *testing.T) {
	if _, err := parseSkillsOptions([]string{"--output-dir", "/custom", "--global"}); err == nil {
		t.Fatal("expected error for --output-dir with --global")
	}
	if _, err := parseSkillsOptions([]string{"--claude", "--output-dir", "/custom"}); err == nil {
		t.Fatal("expected error for --output-dir with a target flag")
	}
	if _, err := parseSkillsOptions([]string{"--output-dir", "/custom", "--flat"}); err == nil {
		t.Fatal("expected error for --output-dir with --flat")
	}
}

// Tests that dir-mode install deploys skills flat into <dir>/<name> without
// requiring target flags and without printing target guidance.
func TestRunSkillsSubcommandDirInstallInstallsFlatLayout(t *testing.T) {
	root := t.TempDir()
	skill := writeDirModeSkillSource(t, root, "uloop-sample")
	destinationDir := filepath.Join(root, "apm-skills")
	stdout := &bytes.Buffer{}
	stderr := &bytes.Buffer{}

	code := runSkillsSubcommand(
		"install",
		root,
		[]skillDefinition{skill},
		skillCommandOptions{outputDir: destinationDir},
		stdout,
		stderr,
	)

	if code != 0 {
		t.Fatalf("dir install failed: code=%d stderr=%s", code, stderr.String())
	}
	if strings.Contains(stdout.String(), "Please specify at least one target") {
		t.Fatalf("dir install should not print target guidance:\n%s", stdout.String())
	}
	if !strings.Contains(stdout.String(), destinationDir) {
		t.Fatalf("dir install output should show the destination:\n%s", stdout.String())
	}
	installedSkill := filepath.Join(destinationDir, "uloop-sample", "SKILL.md")
	if _, err := os.Stat(installedSkill); err != nil {
		t.Fatalf("skill file was not installed: %v", err)
	}
	installedReference := filepath.Join(destinationDir, "uloop-sample", "references", "note.md")
	if _, err := os.Stat(installedReference); err != nil {
		t.Fatalf("reference file was not installed: %v", err)
	}
}

// Tests that dir-mode install refreshes source-owned files while leaving
// foreign files such as apm.yml untouched, and drops stale reference files.
func TestRunSkillsDirInstallPreservesForeignFiles(t *testing.T) {
	root := t.TempDir()
	skill := writeDirModeSkillSource(t, root, "uloop-sample")
	destinationDir := filepath.Join(root, "apm-skills")
	installedDir := filepath.Join(destinationDir, "uloop-sample")
	writeRawSkillFile(t, installedDir, "outdated content\n")
	if err := os.WriteFile(filepath.Join(installedDir, "apm.yml"), []byte("name: foreign\n"), 0o644); err != nil {
		t.Fatalf("failed to write foreign file: %v", err)
	}
	staleReferencesDir := filepath.Join(installedDir, "references")
	if err := os.MkdirAll(staleReferencesDir, 0o755); err != nil {
		t.Fatalf("failed to create stale references dir: %v", err)
	}
	if err := os.WriteFile(filepath.Join(staleReferencesDir, "stale.md"), []byte("stale\n"), 0o644); err != nil {
		t.Fatalf("failed to write stale reference: %v", err)
	}
	stdout := &bytes.Buffer{}
	stderr := &bytes.Buffer{}

	code := runSkillsDirInstall(destinationDir, []skillDefinition{skill}, stdout, stderr)

	if code != 0 {
		t.Fatalf("dir install failed: code=%d stderr=%s", code, stderr.String())
	}
	if !strings.Contains(stdout.String(), "Updated: 1") {
		t.Fatalf("outdated skill should be counted as updated:\n%s", stdout.String())
	}
	installedContent, err := os.ReadFile(filepath.Join(installedDir, "SKILL.md"))
	if err != nil {
		t.Fatalf("failed to read installed skill: %v", err)
	}
	if !bytes.Equal(installedContent, skill.content) {
		t.Fatalf("skill file was not refreshed: %s", installedContent)
	}
	if _, err := os.Stat(filepath.Join(installedDir, "apm.yml")); err != nil {
		t.Fatalf("foreign file should be preserved: %v", err)
	}
	if _, err := os.Stat(filepath.Join(staleReferencesDir, "stale.md")); !os.IsNotExist(err) {
		t.Fatalf("stale reference should be removed, stat err=%v", err)
	}
	if _, err := os.Stat(filepath.Join(staleReferencesDir, "note.md")); err != nil {
		t.Fatalf("current reference should be installed: %v", err)
	}
}

// Tests that foreign files in the destination do not mark an up-to-date skill
// outdated, while a stale file inside a source-owned directory does.
func TestGetDirSkillStatusIgnoresForeignFiles(t *testing.T) {
	root := t.TempDir()
	skill := writeDirModeSkillSource(t, root, "uloop-sample")
	destinationDir := filepath.Join(root, "apm-skills")
	stdout := &bytes.Buffer{}
	stderr := &bytes.Buffer{}
	if code := runSkillsDirInstall(destinationDir, []skillDefinition{skill}, stdout, stderr); code != 0 {
		t.Fatalf("setup install failed: code=%d stderr=%s", code, stderr.String())
	}
	installedDir := filepath.Join(destinationDir, "uloop-sample")
	if err := os.WriteFile(filepath.Join(installedDir, "apm.yml"), []byte("name: foreign\n"), 0o644); err != nil {
		t.Fatalf("failed to write foreign file: %v", err)
	}

	state, err := getDirSkillState(destinationDir, skill)
	if err != nil {
		t.Fatalf("status check failed: %v", err)
	}
	if state.status != "installed" {
		t.Fatalf("foreign file should not mark the skill outdated: %s", state.status)
	}

	if err := os.WriteFile(filepath.Join(installedDir, "references", "stale.md"), []byte("stale\n"), 0o644); err != nil {
		t.Fatalf("failed to write stale reference: %v", err)
	}
	state, err = getDirSkillState(destinationDir, skill)
	if err != nil {
		t.Fatalf("status check failed: %v", err)
	}
	if state.status != "outdated" {
		t.Fatalf("stale file in a source-owned directory should mark the skill outdated: %s", state.status)
	}
}

// Tests that dir-mode uninstall removes only source-owned entries, keeps the
// directory when foreign files remain, and removes it when it becomes empty.
func TestRunSkillsDirUninstallRemovesOnlyOwnedFiles(t *testing.T) {
	root := t.TempDir()
	skillWithForeign := writeDirModeSkillSource(t, root, "uloop-with-foreign")
	skillClean := writeDirModeSkillSource(t, root, "uloop-clean")
	destinationDir := filepath.Join(root, "apm-skills")
	stdout := &bytes.Buffer{}
	stderr := &bytes.Buffer{}
	skills := []skillDefinition{skillClean, skillWithForeign}
	if code := runSkillsDirInstall(destinationDir, skills, stdout, stderr); code != 0 {
		t.Fatalf("setup install failed: code=%d stderr=%s", code, stderr.String())
	}
	foreignPath := filepath.Join(destinationDir, "uloop-with-foreign", "apm.yml")
	if err := os.WriteFile(foreignPath, []byte("name: foreign\n"), 0o644); err != nil {
		t.Fatalf("failed to write foreign file: %v", err)
	}

	stdout.Reset()
	code := runSkillsDirUninstall(destinationDir, skills, stdout, stderr)

	if code != 0 {
		t.Fatalf("dir uninstall failed: code=%d stderr=%s", code, stderr.String())
	}
	if !strings.Contains(stdout.String(), "Removed: 2") {
		t.Fatalf("uninstall should report removed skills:\n%s", stdout.String())
	}
	if _, err := os.Stat(filepath.Join(destinationDir, "uloop-with-foreign", "SKILL.md")); !os.IsNotExist(err) {
		t.Fatalf("owned skill file should be removed, stat err=%v", err)
	}
	if _, err := os.Stat(foreignPath); err != nil {
		t.Fatalf("foreign file should survive uninstall: %v", err)
	}
	if _, err := os.Stat(filepath.Join(destinationDir, "uloop-clean")); !os.IsNotExist(err) {
		t.Fatalf("emptied skill directory should be removed, stat err=%v", err)
	}
}

// Tests that a skill directory holding owned files but no SKILL.md is reported
// outdated, and that install repairs it as an update, so list, install, and
// uninstall agree on the partially removed state.
func TestGetDirSkillStatusReportsOrphanedOwnedFilesAsOutdated(t *testing.T) {
	root := t.TempDir()
	skill := writeDirModeSkillSource(t, root, "uloop-sample")
	destinationDir := filepath.Join(root, "apm-skills")
	stdout := &bytes.Buffer{}
	stderr := &bytes.Buffer{}
	if code := runSkillsDirInstall(destinationDir, []skillDefinition{skill}, stdout, stderr); code != 0 {
		t.Fatalf("setup install failed: code=%d stderr=%s", code, stderr.String())
	}
	installedDir := filepath.Join(destinationDir, "uloop-sample")
	if err := os.Remove(filepath.Join(installedDir, "SKILL.md")); err != nil {
		t.Fatalf("failed to remove installed skill file: %v", err)
	}

	state, err := getDirSkillState(destinationDir, skill)
	if err != nil {
		t.Fatalf("status check failed: %v", err)
	}
	if state.status != "outdated" {
		t.Fatalf("orphaned owned files should read as outdated: %s", state.status)
	}

	stdout.Reset()
	if code := runSkillsDirInstall(destinationDir, []skillDefinition{skill}, stdout, stderr); code != 0 {
		t.Fatalf("repair install failed: code=%d stderr=%s", code, stderr.String())
	}
	if !strings.Contains(stdout.String(), "Updated: 1") {
		t.Fatalf("repairing an orphaned skill should count as updated:\n%s", stdout.String())
	}
	if _, err := os.Stat(filepath.Join(installedDir, "SKILL.md")); err != nil {
		t.Fatalf("repair should restore the skill file: %v", err)
	}
}

// Tests that a foreign top-level file occupying a skill's name fails install
// with a clear error on every platform instead of a raw ENOTDIR, and that
// uninstall preserves the file and reports the skill as not found.
func TestRunSkillsDirInstallRejectsFileOccupyingSkillName(t *testing.T) {
	root := t.TempDir()
	skill := writeDirModeSkillSource(t, root, "uloop-sample")
	destinationDir := filepath.Join(root, "apm-skills")
	if err := os.MkdirAll(destinationDir, 0o755); err != nil {
		t.Fatalf("failed to create destination: %v", err)
	}
	occupyingFile := filepath.Join(destinationDir, "uloop-sample")
	if err := os.WriteFile(occupyingFile, []byte("foreign\n"), 0o644); err != nil {
		t.Fatalf("failed to write occupying file: %v", err)
	}
	stdout := &bytes.Buffer{}
	stderr := &bytes.Buffer{}

	code := runSkillsDirInstall(destinationDir, []skillDefinition{skill}, stdout, stderr)

	if code != 1 {
		t.Fatalf("install onto an occupying file should fail: code=%d", code)
	}
	if !strings.Contains(stderr.String(), "cannot manage skill") || !strings.Contains(stderr.String(), "not a directory") {
		t.Fatalf("error should be the crafted cross-platform message, not a raw readdir error:\n%s", stderr.String())
	}

	stdout.Reset()
	stderr.Reset()
	code = runSkillsDirUninstall(destinationDir, []skillDefinition{skill}, stdout, stderr)
	if code != 0 {
		t.Fatalf("uninstall should not fail on an occupying file: code=%d stderr=%s", code, stderr.String())
	}
	if !strings.Contains(stdout.String(), "Not found: 1") {
		t.Fatalf("occupying file should not count as an install:\n%s", stdout.String())
	}
	if _, err := os.Stat(occupyingFile); err != nil {
		t.Fatalf("occupying foreign file should be preserved: %v", err)
	}
}

// Tests that a symlink occupying a skill's name is never followed: install
// fails with a clear error and uninstall preserves the symlink target's
// contents, so operations cannot reach outside the store.
func TestRunSkillsDirDoesNotFollowSymlinkAtSkillName(t *testing.T) {
	if runtime.GOOS == "windows" {
		t.Skip("symlink creation requires elevated privileges on Windows")
	}
	root := t.TempDir()
	skill := writeDirModeSkillSource(t, root, "uloop-sample")
	destinationDir := filepath.Join(root, "apm-skills")
	if err := os.MkdirAll(destinationDir, 0o755); err != nil {
		t.Fatalf("failed to create destination: %v", err)
	}
	curatedDir := filepath.Join(root, "curated", "uloop-sample")
	curatedFile := filepath.Join(curatedDir, "SKILL.md")
	if err := os.MkdirAll(curatedDir, 0o755); err != nil {
		t.Fatalf("failed to create curated dir: %v", err)
	}
	if err := os.WriteFile(curatedFile, []byte("user content\n"), 0o644); err != nil {
		t.Fatalf("failed to write curated file: %v", err)
	}
	// An artifact-named entry inside the symlink target guards against cleanup
	// reading through the symlink and deleting outside the store.
	curatedArtifact := filepath.Join(curatedDir, "SKILL.md.uloop-tmp-1234")
	if err := os.WriteFile(curatedArtifact, []byte("theirs\n"), 0o644); err != nil {
		t.Fatalf("failed to write curated artifact-named file: %v", err)
	}
	if err := os.Symlink(curatedDir, filepath.Join(destinationDir, "uloop-sample")); err != nil {
		t.Fatalf("failed to create symlink: %v", err)
	}
	stdout := &bytes.Buffer{}
	stderr := &bytes.Buffer{}

	if code := runSkillsDirInstall(destinationDir, []skillDefinition{skill}, stdout, stderr); code != 1 {
		t.Fatalf("install through a symlink should fail: code=%d", code)
	}

	stdout.Reset()
	stderr.Reset()
	code := runSkillsDirUninstall(destinationDir, []skillDefinition{skill}, stdout, stderr)
	if code != 0 {
		t.Fatalf("dir uninstall failed: code=%d stderr=%s", code, stderr.String())
	}
	if !strings.Contains(stdout.String(), "Not found: 1") {
		t.Fatalf("symlinked skill name should not count as an install:\n%s", stdout.String())
	}
	if _, err := os.Stat(curatedFile); err != nil {
		t.Fatalf("symlink target contents must survive uninstall: %v", err)
	}
	if _, err := os.Stat(curatedArtifact); err != nil {
		t.Fatalf("artifact-named file inside the symlink target must survive: %v", err)
	}
}

// Tests that an --output-dir destination that exists as a regular file is
// rejected with a clear error on every platform instead of diverging between
// a raw ENOTDIR and bogus not-installed statuses.
func TestRunSkillsDirSubcommandRejectsFileDestination(t *testing.T) {
	root := t.TempDir()
	skill := writeDirModeSkillSource(t, root, "uloop-sample")
	destinationPath := filepath.Join(root, "apm-skills")
	if err := os.WriteFile(destinationPath, []byte("not a directory\n"), 0o644); err != nil {
		t.Fatalf("failed to write destination file: %v", err)
	}
	stdout := &bytes.Buffer{}
	stderr := &bytes.Buffer{}

	code := runSkillsDirSubcommand("install", []skillDefinition{skill}, destinationPath, stdout, stderr)

	if code != 1 {
		t.Fatalf("file destination should fail: code=%d", code)
	}
	if !strings.Contains(stderr.String(), "not a directory") {
		t.Fatalf("error should explain the file destination:\n%s", stderr.String())
	}
}

// Tests that uninstall leaves a user-created empty directory bearing a skill
// name in place when nothing owned was found inside it.
func TestRunSkillsDirUninstallPreservesEmptyForeignDir(t *testing.T) {
	root := t.TempDir()
	skill := writeDirModeSkillSource(t, root, "uloop-sample")
	destinationDir := filepath.Join(root, "apm-skills")
	scaffoldDir := filepath.Join(destinationDir, "uloop-sample")
	if err := os.MkdirAll(scaffoldDir, 0o755); err != nil {
		t.Fatalf("failed to create scaffold dir: %v", err)
	}
	stdout := &bytes.Buffer{}
	stderr := &bytes.Buffer{}

	code := runSkillsDirUninstall(destinationDir, []skillDefinition{skill}, stdout, stderr)

	if code != 0 {
		t.Fatalf("dir uninstall failed: code=%d stderr=%s", code, stderr.String())
	}
	if !strings.Contains(stdout.String(), "Not found: 1") {
		t.Fatalf("empty foreign directory should not count as an install:\n%s", stdout.String())
	}
	if _, err := os.Stat(scaffoldDir); err != nil {
		t.Fatalf("empty foreign directory should be preserved: %v", err)
	}
}

// Tests that leftover temp and backup artifacts from an interrupted sync are
// cleaned up by install and uninstall instead of lingering as foreign files.
func TestRunSkillsDirCleansStaleSyncArtifacts(t *testing.T) {
	root := t.TempDir()
	skill := writeDirModeSkillSource(t, root, "uloop-sample")
	destinationDir := filepath.Join(root, "apm-skills")
	stdout := &bytes.Buffer{}
	stderr := &bytes.Buffer{}
	if code := runSkillsDirInstall(destinationDir, []skillDefinition{skill}, stdout, stderr); code != 0 {
		t.Fatalf("setup install failed: code=%d stderr=%s", code, stderr.String())
	}
	installedDir := filepath.Join(destinationDir, "uloop-sample")
	staleTemp := filepath.Join(installedDir, "SKILL.md.uloop-tmp-1234")
	staleBackup := filepath.Join(installedDir, "references.uloop-backup-5678")
	if err := os.WriteFile(staleTemp, []byte("partial\n"), 0o644); err != nil {
		t.Fatalf("failed to write stale temp file: %v", err)
	}
	if err := os.MkdirAll(staleBackup, 0o755); err != nil {
		t.Fatalf("failed to create stale backup dir: %v", err)
	}

	stdout.Reset()
	if code := runSkillsDirInstall(destinationDir, []skillDefinition{skill}, stdout, stderr); code != 0 {
		t.Fatalf("dir install failed: code=%d stderr=%s", code, stderr.String())
	}
	if _, err := os.Stat(staleTemp); !os.IsNotExist(err) {
		t.Fatalf("stale temp file should be cleaned by install, stat err=%v", err)
	}
	if _, err := os.Stat(staleBackup); !os.IsNotExist(err) {
		t.Fatalf("stale backup dir should be cleaned by install, stat err=%v", err)
	}

	if err := os.MkdirAll(staleBackup, 0o755); err != nil {
		t.Fatalf("failed to recreate stale backup dir: %v", err)
	}
	stdout.Reset()
	if code := runSkillsDirUninstall(destinationDir, []skillDefinition{skill}, stdout, stderr); code != 0 {
		t.Fatalf("dir uninstall failed: code=%d stderr=%s", code, stderr.String())
	}
	if _, err := os.Stat(installedDir); !os.IsNotExist(err) {
		t.Fatalf("skill directory should be fully removed after cleanup, stat err=%v", err)
	}
}

// Tests that a leftover artifact minted for an entry the current skill source
// no longer owns (a renamed or dropped directory) is still cleaned up: the
// uloop namespace marker alone identifies the debris.
func TestRunSkillsDirCleansArtifactsOfFormerlyOwnedEntries(t *testing.T) {
	root := t.TempDir()
	skill := writeDirModeSkillSource(t, root, "uloop-sample")
	destinationDir := filepath.Join(root, "apm-skills")
	stdout := &bytes.Buffer{}
	stderr := &bytes.Buffer{}
	if code := runSkillsDirInstall(destinationDir, []skillDefinition{skill}, stdout, stderr); code != 0 {
		t.Fatalf("setup install failed: code=%d stderr=%s", code, stderr.String())
	}
	installedDir := filepath.Join(destinationDir, "uloop-sample")
	formerBackup := filepath.Join(installedDir, "docs.uloop-backup-1234")
	if err := os.MkdirAll(formerBackup, 0o755); err != nil {
		t.Fatalf("failed to create former-entry backup dir: %v", err)
	}

	stdout.Reset()
	if code := runSkillsDirInstall(destinationDir, []skillDefinition{skill}, stdout, stderr); code != 0 {
		t.Fatalf("dir install failed: code=%d stderr=%s", code, stderr.String())
	}
	if _, err := os.Stat(formerBackup); !os.IsNotExist(err) {
		t.Fatalf("artifact of a formerly owned entry should be cleaned, stat err=%v", err)
	}
}

// Tests that uninstall removes the skill directory when only ignorable OS/tool
// debris (.DS_Store) remains after the owned entries are deleted, but keeps
// the directory when genuinely foreign content is also present.
func TestRunSkillsDirUninstallRemovesIgnorableDebrisWithSkillDir(t *testing.T) {
	root := t.TempDir()
	skill := writeDirModeSkillSource(t, root, "uloop-sample")
	destinationDir := filepath.Join(root, "apm-skills")
	stdout := &bytes.Buffer{}
	stderr := &bytes.Buffer{}
	if code := runSkillsDirInstall(destinationDir, []skillDefinition{skill}, stdout, stderr); code != 0 {
		t.Fatalf("setup install failed: code=%d stderr=%s", code, stderr.String())
	}
	installedDir := filepath.Join(destinationDir, "uloop-sample")
	if err := os.WriteFile(filepath.Join(installedDir, ".DS_Store"), []byte("junk"), 0o644); err != nil {
		t.Fatalf("failed to write debris file: %v", err)
	}

	stdout.Reset()
	if code := runSkillsDirUninstall(destinationDir, []skillDefinition{skill}, stdout, stderr); code != 0 {
		t.Fatalf("dir uninstall failed: code=%d stderr=%s", code, stderr.String())
	}
	if _, err := os.Stat(installedDir); !os.IsNotExist(err) {
		t.Fatalf("debris-only skill directory should be removed, stat err=%v", err)
	}

	stdout.Reset()
	if code := runSkillsDirInstall(destinationDir, []skillDefinition{skill}, stdout, stderr); code != 0 {
		t.Fatalf("reinstall failed: code=%d stderr=%s", code, stderr.String())
	}
	foreignFile := filepath.Join(installedDir, "apm.yml")
	if err := os.WriteFile(foreignFile, []byte("manifest\n"), 0o644); err != nil {
		t.Fatalf("failed to write foreign file: %v", err)
	}
	if err := os.WriteFile(filepath.Join(installedDir, ".DS_Store"), []byte("junk"), 0o644); err != nil {
		t.Fatalf("failed to write debris file again: %v", err)
	}
	stdout.Reset()
	if code := runSkillsDirUninstall(destinationDir, []skillDefinition{skill}, stdout, stderr); code != 0 {
		t.Fatalf("dir uninstall failed: code=%d stderr=%s", code, stderr.String())
	}
	if _, err := os.Stat(foreignFile); err != nil {
		t.Fatalf("foreign file should keep the directory and survive: %v", err)
	}
}

// Tests that user files whose names merely resemble sync artifacts (temp or
// backup naming without the uloop namespace marker, digit-suffixed dated
// backups included) are preserved as foreign by install and uninstall instead
// of being deleted as uloop debris.
func TestRunSkillsDirPreservesHumanNamedArtifactLookalikes(t *testing.T) {
	root := t.TempDir()
	skill := writeDirModeSkillSource(t, root, "uloop-sample")
	destinationDir := filepath.Join(root, "apm-skills")
	stdout := &bytes.Buffer{}
	stderr := &bytes.Buffer{}
	if code := runSkillsDirInstall(destinationDir, []skillDefinition{skill}, stdout, stderr); code != 0 {
		t.Fatalf("setup install failed: code=%d stderr=%s", code, stderr.String())
	}
	installedDir := filepath.Join(destinationDir, "uloop-sample")
	manualBackup := filepath.Join(installedDir, "references.backup-manual")
	datedBackup := filepath.Join(installedDir, "references.backup-20240115")
	draftNote := filepath.Join(installedDir, "SKILL.md.tmp-notes")
	// Carries the artifact marker but a human-chosen tail: only the all-digit
	// tails that os.CreateTemp/MkdirTemp mint may be cleaned up.
	markerLookalike := filepath.Join(installedDir, "my-archive.uloop-tmp-keepme")
	if err := os.MkdirAll(manualBackup, 0o755); err != nil {
		t.Fatalf("failed to create manual backup dir: %v", err)
	}
	if err := os.MkdirAll(datedBackup, 0o755); err != nil {
		t.Fatalf("failed to create dated backup dir: %v", err)
	}
	if err := os.WriteFile(draftNote, []byte("draft\n"), 0o644); err != nil {
		t.Fatalf("failed to write draft note: %v", err)
	}
	if err := os.WriteFile(markerLookalike, []byte("keep\n"), 0o644); err != nil {
		t.Fatalf("failed to write marker lookalike: %v", err)
	}

	stdout.Reset()
	if code := runSkillsDirInstall(destinationDir, []skillDefinition{skill}, stdout, stderr); code != 0 {
		t.Fatalf("dir install failed: code=%d stderr=%s", code, stderr.String())
	}
	if _, err := os.Stat(manualBackup); err != nil {
		t.Fatalf("human-named backup dir should survive install: %v", err)
	}
	if _, err := os.Stat(datedBackup); err != nil {
		t.Fatalf("dated backup dir should survive install: %v", err)
	}
	if _, err := os.Stat(draftNote); err != nil {
		t.Fatalf("human-named draft file should survive install: %v", err)
	}
	if _, err := os.Stat(markerLookalike); err != nil {
		t.Fatalf("marker file with a non-digit tail should survive install: %v", err)
	}

	stdout.Reset()
	if code := runSkillsDirUninstall(destinationDir, []skillDefinition{skill}, stdout, stderr); code != 0 {
		t.Fatalf("dir uninstall failed: code=%d stderr=%s", code, stderr.String())
	}
	if _, err := os.Stat(manualBackup); err != nil {
		t.Fatalf("human-named backup dir should survive uninstall: %v", err)
	}
	if _, err := os.Stat(datedBackup); err != nil {
		t.Fatalf("dated backup dir should survive uninstall: %v", err)
	}
	if _, err := os.Stat(draftNote); err != nil {
		t.Fatalf("human-named draft file should survive uninstall: %v", err)
	}
}

// Tests that install repairs a skill whose source-owned directory name is
// occupied by an entry of the wrong type: a foreign regular file and a
// dangling symlink both give way to the source directory instead of failing
// with a raw rename error or silently corrupting the store.
func TestRunSkillsDirInstallReplacesWrongTypeEntryAtOwnedName(t *testing.T) {
	root := t.TempDir()
	skill := writeDirModeSkillSource(t, root, "uloop-sample")
	destinationDir := filepath.Join(root, "apm-skills")
	stdout := &bytes.Buffer{}
	stderr := &bytes.Buffer{}
	if code := runSkillsDirInstall(destinationDir, []skillDefinition{skill}, stdout, stderr); code != 0 {
		t.Fatalf("setup install failed: code=%d stderr=%s", code, stderr.String())
	}
	installedReferences := filepath.Join(destinationDir, "uloop-sample", "references")
	if err := os.RemoveAll(installedReferences); err != nil {
		t.Fatalf("failed to remove installed references: %v", err)
	}
	if err := os.WriteFile(installedReferences, []byte("foreign\n"), 0o644); err != nil {
		t.Fatalf("failed to write occupying file: %v", err)
	}

	stdout.Reset()
	if code := runSkillsDirInstall(destinationDir, []skillDefinition{skill}, stdout, stderr); code != 0 {
		t.Fatalf("install over an occupying file failed: code=%d stderr=%s", code, stderr.String())
	}
	if !strings.Contains(stdout.String(), "Updated: 1") {
		t.Fatalf("replacing the occupant should count as updated:\n%s", stdout.String())
	}
	if _, err := os.Stat(filepath.Join(installedReferences, "note.md")); err != nil {
		t.Fatalf("references should be restored as a directory: %v", err)
	}
}

// Tests that install replaces a symlink occupying a source-owned entry name by
// removing the link itself, never writing through it, so the symlink target's
// contents survive untouched.
func TestRunSkillsDirInstallReplacesSymlinkAtOwnedNameWithoutFollowing(t *testing.T) {
	if runtime.GOOS == "windows" {
		t.Skip("symlink creation requires elevated privileges on Windows")
	}
	root := t.TempDir()
	skill := writeDirModeSkillSource(t, root, "uloop-sample")
	destinationDir := filepath.Join(root, "apm-skills")
	stdout := &bytes.Buffer{}
	stderr := &bytes.Buffer{}
	if code := runSkillsDirInstall(destinationDir, []skillDefinition{skill}, stdout, stderr); code != 0 {
		t.Fatalf("setup install failed: code=%d stderr=%s", code, stderr.String())
	}
	installedReferences := filepath.Join(destinationDir, "uloop-sample", "references")
	if err := os.RemoveAll(installedReferences); err != nil {
		t.Fatalf("failed to remove installed references: %v", err)
	}
	curatedDir := filepath.Join(root, "curated-refs")
	curatedFile := filepath.Join(curatedDir, "mine.md")
	if err := os.MkdirAll(curatedDir, 0o755); err != nil {
		t.Fatalf("failed to create curated dir: %v", err)
	}
	if err := os.WriteFile(curatedFile, []byte("user content\n"), 0o644); err != nil {
		t.Fatalf("failed to write curated file: %v", err)
	}
	if err := os.Symlink(curatedDir, installedReferences); err != nil {
		t.Fatalf("failed to create symlink: %v", err)
	}

	stdout.Reset()
	if code := runSkillsDirInstall(destinationDir, []skillDefinition{skill}, stdout, stderr); code != 0 {
		t.Fatalf("install over a symlink failed: code=%d stderr=%s", code, stderr.String())
	}
	if _, err := os.Stat(curatedFile); err != nil {
		t.Fatalf("symlink target contents must survive install: %v", err)
	}
	if _, err := os.Stat(filepath.Join(installedReferences, "note.md")); err != nil {
		t.Fatalf("references should be restored as a real directory: %v", err)
	}

	danglingTarget := filepath.Join(root, "gone")
	if err := os.RemoveAll(installedReferences); err != nil {
		t.Fatalf("failed to remove references for dangling case: %v", err)
	}
	if err := os.Symlink(danglingTarget, installedReferences); err != nil {
		t.Fatalf("failed to create dangling symlink: %v", err)
	}
	stdout.Reset()
	if code := runSkillsDirInstall(destinationDir, []skillDefinition{skill}, stdout, stderr); code != 0 {
		t.Fatalf("install over a dangling symlink failed: code=%d stderr=%s", code, stderr.String())
	}
	if _, err := os.Stat(filepath.Join(installedReferences, "note.md")); err != nil {
		t.Fatalf("references should be restored over the dangling symlink: %v", err)
	}
}

// Tests that uninstall removes a dangling symlink sitting at an owned entry
// name instead of skipping it as missing.
func TestRunSkillsDirUninstallRemovesDanglingSymlink(t *testing.T) {
	if runtime.GOOS == "windows" {
		t.Skip("symlink creation requires elevated privileges on Windows")
	}
	root := t.TempDir()
	skill := writeDirModeSkillSource(t, root, "uloop-sample")
	destinationDir := filepath.Join(root, "apm-skills")
	stdout := &bytes.Buffer{}
	stderr := &bytes.Buffer{}
	if code := runSkillsDirInstall(destinationDir, []skillDefinition{skill}, stdout, stderr); code != 0 {
		t.Fatalf("setup install failed: code=%d stderr=%s", code, stderr.String())
	}
	installedDir := filepath.Join(destinationDir, "uloop-sample")
	referencesPath := filepath.Join(installedDir, "references")
	if err := os.RemoveAll(referencesPath); err != nil {
		t.Fatalf("failed to remove references dir: %v", err)
	}
	if err := os.Symlink(filepath.Join(root, "missing-target"), referencesPath); err != nil {
		t.Fatalf("failed to create dangling symlink: %v", err)
	}

	stdout.Reset()
	code := runSkillsDirUninstall(destinationDir, []skillDefinition{skill}, stdout, stderr)

	if code != 0 {
		t.Fatalf("dir uninstall failed: code=%d stderr=%s", code, stderr.String())
	}
	if !strings.Contains(stdout.String(), "Removed: 1") {
		t.Fatalf("uninstall should count the skill as removed:\n%s", stdout.String())
	}
	if _, err := os.Stat(installedDir); !os.IsNotExist(err) {
		t.Fatalf("emptied skill directory should be removed, stat err=%v", err)
	}
}

// Tests that dir-mode uninstall cleans up owned files left behind after
// SKILL.md was removed, instead of reporting the skill as not found.
func TestRunSkillsDirUninstallRemovesOrphanedOwnedFiles(t *testing.T) {
	root := t.TempDir()
	skill := writeDirModeSkillSource(t, root, "uloop-sample")
	destinationDir := filepath.Join(root, "apm-skills")
	stdout := &bytes.Buffer{}
	stderr := &bytes.Buffer{}
	if code := runSkillsDirInstall(destinationDir, []skillDefinition{skill}, stdout, stderr); code != 0 {
		t.Fatalf("setup install failed: code=%d stderr=%s", code, stderr.String())
	}
	installedDir := filepath.Join(destinationDir, "uloop-sample")
	if err := os.Remove(filepath.Join(installedDir, "SKILL.md")); err != nil {
		t.Fatalf("failed to remove installed skill file: %v", err)
	}

	stdout.Reset()
	code := runSkillsDirUninstall(destinationDir, []skillDefinition{skill}, stdout, stderr)

	if code != 0 {
		t.Fatalf("dir uninstall failed: code=%d stderr=%s", code, stderr.String())
	}
	if !strings.Contains(stdout.String(), "Removed: 1") {
		t.Fatalf("orphaned owned files should count as removed:\n%s", stdout.String())
	}
	if _, err := os.Stat(installedDir); !os.IsNotExist(err) {
		t.Fatalf("emptied skill directory should be removed, stat err=%v", err)
	}
}

// Tests that dir-mode uninstall counts skills that were never installed.
func TestRunSkillsDirUninstallCountsMissingSkills(t *testing.T) {
	root := t.TempDir()
	skill := writeDirModeSkillSource(t, root, "uloop-sample")
	destinationDir := filepath.Join(root, "apm-skills")
	stdout := &bytes.Buffer{}
	stderr := &bytes.Buffer{}

	code := runSkillsDirUninstall(destinationDir, []skillDefinition{skill}, stdout, stderr)

	if code != 0 {
		t.Fatalf("dir uninstall failed: code=%d stderr=%s", code, stderr.String())
	}
	if !strings.Contains(stdout.String(), "Not found: 1") {
		t.Fatalf("uninstall should report missing skills:\n%s", stdout.String())
	}
}

// Tests that dir-mode list shows the destination and per-skill statuses.
func TestRunSkillsDirListShowsStatuses(t *testing.T) {
	root := t.TempDir()
	installedSkill := writeDirModeSkillSource(t, root, "uloop-installed")
	missingSkill := writeDirModeSkillSource(t, root, "uloop-missing")
	destinationDir := filepath.Join(root, "apm-skills")
	stdout := &bytes.Buffer{}
	stderr := &bytes.Buffer{}
	if code := runSkillsDirInstall(destinationDir, []skillDefinition{installedSkill}, stdout, stderr); code != 0 {
		t.Fatalf("setup install failed: code=%d stderr=%s", code, stderr.String())
	}

	stdout.Reset()
	code := runSkillsDirList(destinationDir, []skillDefinition{installedSkill, missingSkill}, stdout, stderr)

	if code != 0 {
		t.Fatalf("dir list failed: code=%d stderr=%s", code, stderr.String())
	}
	output := stdout.String()
	if !strings.Contains(output, destinationDir) {
		t.Fatalf("list should show the destination:\n%s", output)
	}
	if !strings.Contains(output, "uloop-installed (installed)") {
		t.Fatalf("list should show installed status:\n%s", output)
	}
	if !strings.Contains(output, "uloop-missing (not installed)") {
		t.Fatalf("list should show not installed status:\n%s", output)
	}
}

// Tests that the v3 migration subcommands reject the --output-dir option.
func TestTryHandleSkillsRequestRejectsDirForV3Migration(t *testing.T) {
	stdout := &bytes.Buffer{}
	stderr := &bytes.Buffer{}

	handled, code := tryHandleSkillsRequest(
		[]string{"skills", "install-v3-migration", "--output-dir", t.TempDir()},
		t.TempDir(),
		"",
		stdout,
		stderr,
	)

	if !handled {
		t.Fatal("skills request should be handled")
	}
	if code != 1 {
		t.Fatalf("v3 migration with --output-dir should fail: code=%d", code)
	}
	if !strings.Contains(stderr.String(), "--output-dir") {
		t.Fatalf("error should name the rejected option:\n%s", stderr.String())
	}
}

// Tests that subcommand help advertises --output-dir for standard subcommands only.
func TestPrintSkillsSubcommandHelpShowsDirOptionForStandardSubcommands(t *testing.T) {
	stdout := &bytes.Buffer{}
	printSkillsSubcommandHelp("install", stdout)
	if !strings.Contains(stdout.String(), "--output-dir <path>") {
		t.Fatalf("install help should list --output-dir:\n%s", stdout.String())
	}

	stdout.Reset()
	printSkillsSubcommandHelp("install-v3-migration", stdout)
	if strings.Contains(stdout.String(), "--output-dir") {
		t.Fatalf("v3 migration help should not list --output-dir:\n%s", stdout.String())
	}
}

// Tests that a hand-authored directory using a skill's name and an owned entry
// name, in a store uloop never installed into, is preserved: uninstall reports
// not found and install blocks the skill instead of replacing the content.
func TestRunSkillsDirPreservesNeverInstalledOwnedNames(t *testing.T) {
	root := t.TempDir()
	skill := writeDirModeSkillSource(t, root, "uloop-sample")
	destinationDir := filepath.Join(root, "apm-skills")
	handAuthoredDir := filepath.Join(destinationDir, "uloop-sample", "references")
	if err := os.MkdirAll(handAuthoredDir, 0o755); err != nil {
		t.Fatalf("failed to create hand-authored dir: %v", err)
	}
	userNotes := filepath.Join(handAuthoredDir, "mynotes.md")
	if err := os.WriteFile(userNotes, []byte("my curated notes\n"), 0o644); err != nil {
		t.Fatalf("failed to write user notes: %v", err)
	}
	manifest := filepath.Join(destinationDir, "uloop-sample", "package.json")
	if err := os.WriteFile(manifest, []byte("{}\n"), 0o644); err != nil {
		t.Fatalf("failed to write manifest: %v", err)
	}
	stdout := &bytes.Buffer{}
	stderr := &bytes.Buffer{}

	code := runSkillsDirUninstall(destinationDir, []skillDefinition{skill}, stdout, stderr)

	if code != 0 {
		t.Fatalf("dir uninstall failed: code=%d stderr=%s", code, stderr.String())
	}
	if !strings.Contains(stdout.String(), "Not found: 1") {
		t.Fatalf("never-installed content should not count as removed:\n%s", stdout.String())
	}
	if _, err := os.Stat(userNotes); err != nil {
		t.Fatalf("hand-authored notes must survive uninstall: %v", err)
	}

	stdout.Reset()
	stderr.Reset()
	code = runSkillsDirInstall(destinationDir, []skillDefinition{skill}, stdout, stderr)

	if code != 1 {
		t.Fatalf("install onto never-installed owned names should report a failure: code=%d", code)
	}
	if !strings.Contains(stderr.String(), "cannot confirm it installed") {
		t.Fatalf("error should state the content is not a confirmed uloop install:\n%s", stderr.String())
	}
	if !strings.Contains(stdout.String(), "Blocked: 1") || !strings.Contains(stdout.String(), "Installed: 0") {
		t.Fatalf("summary should count the skill as blocked:\n%s", stdout.String())
	}
	if _, err := os.Stat(userNotes); err != nil {
		t.Fatalf("hand-authored notes must survive install: %v", err)
	}
	if _, err := os.Stat(filepath.Join(destinationDir, "uloop-sample", "SKILL.md")); !os.IsNotExist(err) {
		t.Fatalf("blocked skill must not be written, stat err=%v", err)
	}
}

// Tests that one skill blocked by a conflict does not abort the run: the
// remaining skills still install and the summary still prints.
func TestRunSkillsDirInstallContinuesPastBlockedSkill(t *testing.T) {
	root := t.TempDir()
	blockedSkill := writeDirModeSkillSource(t, root, "uloop-blocked")
	healthySkill := writeDirModeSkillSource(t, root, "uloop-healthy")
	destinationDir := filepath.Join(root, "apm-skills")
	if err := os.MkdirAll(destinationDir, 0o755); err != nil {
		t.Fatalf("failed to create destination: %v", err)
	}
	if err := os.WriteFile(filepath.Join(destinationDir, "uloop-blocked"), []byte("foreign\n"), 0o644); err != nil {
		t.Fatalf("failed to write occupying file: %v", err)
	}
	stdout := &bytes.Buffer{}
	stderr := &bytes.Buffer{}

	code := runSkillsDirInstall(destinationDir, []skillDefinition{blockedSkill, healthySkill}, stdout, stderr)

	if code != 1 {
		t.Fatalf("a blocked skill should fail the run: code=%d", code)
	}
	if !strings.Contains(stdout.String(), "Installed: 1") || !strings.Contains(stdout.String(), "Blocked: 1") {
		t.Fatalf("summary should show the healthy skill installed and the conflict counted:\n%s", stdout.String())
	}
	if _, err := os.Stat(filepath.Join(destinationDir, "uloop-healthy", "SKILL.md")); err != nil {
		t.Fatalf("the healthy skill should still be installed: %v", err)
	}
	if !strings.Contains(stderr.String(), "cannot manage skill") {
		t.Fatalf("the conflict should be reported per skill:\n%s", stderr.String())
	}
}

// Tests that dir-mode list reports a conflicted skill as a status row and
// completes the listing instead of aborting midway.
func TestRunSkillsDirListReportsConflict(t *testing.T) {
	root := t.TempDir()
	blockedSkill := writeDirModeSkillSource(t, root, "uloop-blocked")
	installedSkill := writeDirModeSkillSource(t, root, "uloop-installed")
	destinationDir := filepath.Join(root, "apm-skills")
	stdout := &bytes.Buffer{}
	stderr := &bytes.Buffer{}
	if code := runSkillsDirInstall(destinationDir, []skillDefinition{installedSkill}, stdout, stderr); code != 0 {
		t.Fatalf("setup install failed: code=%d stderr=%s", code, stderr.String())
	}
	if err := os.WriteFile(filepath.Join(destinationDir, "uloop-blocked"), []byte("foreign\n"), 0o644); err != nil {
		t.Fatalf("failed to write occupying file: %v", err)
	}

	stdout.Reset()
	code := runSkillsDirList(destinationDir, []skillDefinition{blockedSkill, installedSkill}, stdout, stderr)

	if code != 0 {
		t.Fatalf("dir list failed: code=%d stderr=%s", code, stderr.String())
	}
	output := stdout.String()
	if !strings.Contains(output, "uloop-blocked (conflict)") {
		t.Fatalf("list should show the conflict status:\n%s", output)
	}
	if !strings.Contains(output, "exists but is not a directory") {
		t.Fatalf("list should print the conflict reason under the status row:\n%s", output)
	}
	if !strings.Contains(output, "uloop-installed (installed)") {
		t.Fatalf("list should keep reporting the skills after a conflict:\n%s", output)
	}
	if !strings.Contains(output, "Total: 2 skills") {
		t.Fatalf("list should complete instead of aborting midway:\n%s", output)
	}
}

// Tests that an entry differing from SKILL.md only by letter case is never
// claimed: uninstall preserves it and reports not found on every filesystem,
// case-insensitive ones (where a path probe would resolve it) included.
func TestRunSkillsDirUninstallPreservesCaseVariantSkillFile(t *testing.T) {
	root := t.TempDir()
	skill := writeDirModeSkillSource(t, root, "uloop-sample")
	destinationDir := filepath.Join(root, "apm-skills")
	skillDir := filepath.Join(destinationDir, "uloop-sample")
	if err := os.MkdirAll(skillDir, 0o755); err != nil {
		t.Fatalf("failed to create skill dir: %v", err)
	}
	lowercaseSkillFile := filepath.Join(skillDir, "skill.md")
	if err := os.WriteFile(lowercaseSkillFile, []byte("hand-authored\n"), 0o644); err != nil {
		t.Fatalf("failed to write lowercase skill file: %v", err)
	}
	stdout := &bytes.Buffer{}
	stderr := &bytes.Buffer{}

	code := runSkillsDirUninstall(destinationDir, []skillDefinition{skill}, stdout, stderr)

	if code != 0 {
		t.Fatalf("dir uninstall failed: code=%d stderr=%s", code, stderr.String())
	}
	if !strings.Contains(stdout.String(), "Not found: 1") {
		t.Fatalf("a case-variant file should not count as an install:\n%s", stdout.String())
	}
	content, err := os.ReadFile(lowercaseSkillFile)
	if err != nil || string(content) != "hand-authored\n" {
		t.Fatalf("the case-variant file must survive untouched: content=%q err=%v", content, err)
	}
}

// Tests that install blocks a skill whose owned entry name is present only as
// a case variant (References/), instead of letting a case-insensitive
// filesystem replace the foreign entry; the refusal is uniform across
// platforms so store behavior does not depend on where it is synced.
func TestRunSkillsDirInstallBlocksCaseVariantOwnedEntry(t *testing.T) {
	root := t.TempDir()
	skill := writeDirModeSkillSource(t, root, "uloop-sample")
	destinationDir := filepath.Join(root, "apm-skills")
	stdout := &bytes.Buffer{}
	stderr := &bytes.Buffer{}
	if code := runSkillsDirInstall(destinationDir, []skillDefinition{skill}, stdout, stderr); code != 0 {
		t.Fatalf("setup install failed: code=%d stderr=%s", code, stderr.String())
	}
	installedDir := filepath.Join(destinationDir, "uloop-sample")
	if err := os.Rename(filepath.Join(installedDir, "references"), filepath.Join(installedDir, "References")); err != nil {
		t.Fatalf("failed to rename references dir: %v", err)
	}

	stdout.Reset()
	code := runSkillsDirInstall(destinationDir, []skillDefinition{skill}, stdout, stderr)

	if code != 1 {
		t.Fatalf("a case-variant owned entry should block the skill: code=%d", code)
	}
	if !strings.Contains(stderr.String(), "only by letter case") {
		t.Fatalf("error should call out the case mismatch:\n%s", stderr.String())
	}
	if _, err := os.Stat(filepath.Join(installedDir, "References", "note.md")); err != nil {
		t.Fatalf("the case-variant directory must survive untouched: %v", err)
	}
}

// Tests that a broken entry inside an owned directory (a dangling symlink
// where a reference file should be) marks the skill outdated instead of
// installed, and that install repairs it.
func TestRunSkillsDirDetectsBrokenOwnedEntryAsOutdated(t *testing.T) {
	if runtime.GOOS == "windows" {
		t.Skip("symlink creation requires elevated privileges on Windows")
	}
	root := t.TempDir()
	skill := writeDirModeSkillSource(t, root, "uloop-sample")
	destinationDir := filepath.Join(root, "apm-skills")
	stdout := &bytes.Buffer{}
	stderr := &bytes.Buffer{}
	if code := runSkillsDirInstall(destinationDir, []skillDefinition{skill}, stdout, stderr); code != 0 {
		t.Fatalf("setup install failed: code=%d stderr=%s", code, stderr.String())
	}
	notePath := filepath.Join(destinationDir, "uloop-sample", "references", "note.md")
	if err := os.Remove(notePath); err != nil {
		t.Fatalf("failed to remove reference: %v", err)
	}
	if err := os.Symlink(filepath.Join(root, "missing-target"), notePath); err != nil {
		t.Fatalf("failed to create dangling symlink: %v", err)
	}

	state, err := getDirSkillState(destinationDir, skill)
	if err != nil {
		t.Fatalf("status check failed: %v", err)
	}
	if state.status != "outdated" {
		t.Fatalf("a broken owned entry should read as outdated, got: %s", state.status)
	}

	stdout.Reset()
	if code := runSkillsDirInstall(destinationDir, []skillDefinition{skill}, stdout, stderr); code != 0 {
		t.Fatalf("repair install failed: code=%d stderr=%s", code, stderr.String())
	}
	if !strings.Contains(stdout.String(), "Updated: 1") {
		t.Fatalf("repairing a broken owned entry should count as updated:\n%s", stdout.String())
	}
	content, err := os.ReadFile(notePath)
	if err != nil || string(content) != "note\n" {
		t.Fatalf("repair should restore the reference file: content=%q err=%v", content, err)
	}
}

// Tests that passing --output-dir more than once is rejected instead of
// silently letting the last destination win, matching `uloop install --dir`.
func TestParseSkillsOptionsRejectsDuplicateOutputDir(t *testing.T) {
	if _, err := parseSkillsOptions([]string{"--output-dir", "/first", "--output-dir", "/second"}); err == nil {
		t.Fatal("expected error for repeated --output-dir")
	}
	if _, err := parseSkillsOptions([]string{"--output-dir=/first", "--output-dir=/second"}); err == nil {
		t.Fatal("expected error for repeated --output-dir=<path>")
	}
	_, err := parseSkillsOptions([]string{"--output-dir", "/first", "--output-dir=/second"})
	if err == nil {
		t.Fatal("expected error for mixed-form repeated --output-dir")
	}
	if !strings.Contains(err.Error(), "Duplicate") {
		t.Fatalf("error should call out the duplicate option: %v", err)
	}
}

// Tests that a POSIX-style absolute --output-dir is rejected on Windows, where
// filepath.Abs would silently anchor it under the current drive, and accepted
// on other platforms and for Windows-style paths.
func TestPosixStyleOutputDirError(t *testing.T) {
	if err := posixStyleOutputDirError("windows", "/c/apm/skills"); err == nil {
		t.Fatal("a POSIX-style path should be rejected on Windows")
	}
	if err := posixStyleOutputDirError("windows", `C:\apm\skills`); err != nil {
		t.Fatalf("a Windows path should be accepted on Windows: %v", err)
	}
	if err := posixStyleOutputDirError("linux", "/tmp/skills"); err != nil {
		t.Fatalf("a POSIX path should be accepted on non-Windows platforms: %v", err)
	}
	// A double-slash prefix is a UNC path on Windows, which carries its own
	// volume and is therefore not ambiguous.
	if err := posixStyleOutputDirError("windows", "//server/share/skills"); err != nil {
		t.Fatalf("a UNC path should be accepted on Windows: %v", err)
	}
}

// Tests that a store directory matching the skill name only by letter case is
// never adopted: list reports a conflict, install blocks, and uninstall
// preserves the directory, uniformly on every filesystem.
func TestRunSkillsDirRejectsCaseVariantSkillDirectory(t *testing.T) {
	root := t.TempDir()
	skill := writeDirModeSkillSource(t, root, "uloop-sample")
	destinationDir := filepath.Join(root, "apm-skills")
	variantDir := filepath.Join(destinationDir, "Uloop-Sample")
	if err := os.MkdirAll(filepath.Join(variantDir, "references"), 0o755); err != nil {
		t.Fatalf("failed to create case-variant dir: %v", err)
	}
	userNotes := filepath.Join(variantDir, "references", "mynotes.md")
	if err := os.WriteFile(userNotes, []byte("my curated notes\n"), 0o644); err != nil {
		t.Fatalf("failed to write user notes: %v", err)
	}
	if err := os.WriteFile(filepath.Join(variantDir, "SKILL.md"), []byte("hand-authored\n"), 0o644); err != nil {
		t.Fatalf("failed to write hand-authored skill file: %v", err)
	}
	stdout := &bytes.Buffer{}
	stderr := &bytes.Buffer{}

	code := runSkillsDirList(destinationDir, []skillDefinition{skill}, stdout, stderr)
	if code != 0 {
		t.Fatalf("dir list failed: code=%d stderr=%s", code, stderr.String())
	}
	if !strings.Contains(stdout.String(), "uloop-sample (conflict)") {
		t.Fatalf("a case-variant store directory should read as a conflict:\n%s", stdout.String())
	}

	stdout.Reset()
	code = runSkillsDirInstall(destinationDir, []skillDefinition{skill}, stdout, stderr)
	if code != 1 {
		t.Fatalf("install onto a case-variant store directory should be blocked: code=%d", code)
	}
	if !strings.Contains(stderr.String(), "only by letter case") {
		t.Fatalf("error should call out the case mismatch:\n%s", stderr.String())
	}

	stdout.Reset()
	stderr.Reset()
	code = runSkillsDirUninstall(destinationDir, []skillDefinition{skill}, stdout, stderr)
	if code != 0 {
		t.Fatalf("dir uninstall failed: code=%d stderr=%s", code, stderr.String())
	}
	if !strings.Contains(stdout.String(), "Not found: 1") {
		t.Fatalf("a case-variant store directory should not count as an install:\n%s", stdout.String())
	}
	content, err := os.ReadFile(userNotes)
	if err != nil || string(content) != "my curated notes\n" {
		t.Fatalf("the case-variant directory content must survive untouched: content=%q err=%v", content, err)
	}
}

// Tests that an orphan whose source was updated after the partial removal is
// preserved as a recoverable conflict: the message explains the manual remedy
// and uninstall does not delete the drifted content.
func TestRunSkillsDirReportsDriftedOrphanAsRecoverableConflict(t *testing.T) {
	root := t.TempDir()
	skill := writeDirModeSkillSource(t, root, "uloop-sample")
	destinationDir := filepath.Join(root, "apm-skills")
	stdout := &bytes.Buffer{}
	stderr := &bytes.Buffer{}
	if code := runSkillsDirInstall(destinationDir, []skillDefinition{skill}, stdout, stderr); code != 0 {
		t.Fatalf("setup install failed: code=%d stderr=%s", code, stderr.String())
	}
	installedDir := filepath.Join(destinationDir, "uloop-sample")
	if err := os.Remove(filepath.Join(installedDir, "SKILL.md")); err != nil {
		t.Fatalf("failed to remove installed skill file: %v", err)
	}
	// Simulates a uloop upgrade between the partial removal and the next run.
	if err := os.WriteFile(filepath.Join(skill.sourceDirectory, "references", "note.md"), []byte("note v2\n"), 0o644); err != nil {
		t.Fatalf("failed to update source reference: %v", err)
	}

	state, err := getDirSkillState(destinationDir, skill)
	if err != nil {
		t.Fatalf("status check failed: %v", err)
	}
	if state.status != "conflict" {
		t.Fatalf("a drifted orphan should read as a conflict, got: %s", state.status)
	}
	if !strings.Contains(state.conflictReason, "remove or rename them") {
		t.Fatalf("the conflict must explain the manual remedy: %s", state.conflictReason)
	}

	stdout.Reset()
	code := runSkillsDirUninstall(destinationDir, []skillDefinition{skill}, stdout, stderr)
	if code != 0 {
		t.Fatalf("dir uninstall failed: code=%d stderr=%s", code, stderr.String())
	}
	if !strings.Contains(stdout.String(), "Not found: 1") {
		t.Fatalf("a drifted orphan should not count as removed:\n%s", stdout.String())
	}
	if _, err := os.Stat(filepath.Join(installedDir, "references", "note.md")); err != nil {
		t.Fatalf("drifted orphan content must be preserved: %v", err)
	}
}

// Tests that an owned directory holding no comparable files grants no install
// evidence: an empty match proves nothing, so a user's directory of the same
// name is preserved instead of being claimed and removed.
func TestRunSkillsDirEmptyOwnedDirGrantsNoEvidence(t *testing.T) {
	root := t.TempDir()
	sourceDir := filepath.Join(root, "source", "uloop-sample", "Skill")
	content := "---\nname: uloop-sample\n---\n\n# uloop-sample\n"
	writeSkillFile(t, sourceDir, content)
	// The owned directory contains only skipped names, so the comparable set
	// is empty on the source side.
	if err := os.MkdirAll(filepath.Join(sourceDir, "references"), 0o755); err != nil {
		t.Fatalf("failed to create source references dir: %v", err)
	}
	if err := os.WriteFile(filepath.Join(sourceDir, "references", "note.md.meta"), []byte("meta\n"), 0o644); err != nil {
		t.Fatalf("failed to write skipped source file: %v", err)
	}
	skill := skillDefinition{name: "uloop-sample", content: []byte(content), sourceDirectory: sourceDir}
	destinationDir := filepath.Join(root, "apm-skills")
	userDir := filepath.Join(destinationDir, "uloop-sample")
	if err := os.MkdirAll(filepath.Join(userDir, "references"), 0o755); err != nil {
		t.Fatalf("failed to create user dir: %v", err)
	}
	userNotes := filepath.Join(userDir, "notes.md")
	if err := os.WriteFile(userNotes, []byte("mine\n"), 0o644); err != nil {
		t.Fatalf("failed to write user notes: %v", err)
	}
	stdout := &bytes.Buffer{}
	stderr := &bytes.Buffer{}

	code := runSkillsDirUninstall(destinationDir, []skillDefinition{skill}, stdout, stderr)

	if code != 0 {
		t.Fatalf("dir uninstall failed: code=%d stderr=%s", code, stderr.String())
	}
	if !strings.Contains(stdout.String(), "Not found: 1") {
		t.Fatalf("an empty owned directory should not count as an install:\n%s", stdout.String())
	}
	if _, err := os.Stat(userNotes); err != nil {
		t.Fatalf("user content must survive: %v", err)
	}
	if _, err := os.Stat(filepath.Join(userDir, "references")); err != nil {
		t.Fatalf("the user's empty references dir must survive: %v", err)
	}
}

// Tests that an empty owned top-level file grants no install evidence: a
// vacuous empty-file match must not authorize uninstall to delete the skill
// directory or the user's files.
func TestRunSkillsDirEmptyOwnedFileGrantsNoEvidence(t *testing.T) {
	root := t.TempDir()
	skill := writeDirModeSkillSource(t, root, "uloop-sample")
	if err := os.WriteFile(filepath.Join(skill.sourceDirectory, "notes.md"), []byte{}, 0o644); err != nil {
		t.Fatalf("failed to write empty source file: %v", err)
	}
	destinationDir := filepath.Join(root, "apm-skills")
	storeDir := filepath.Join(destinationDir, "uloop-sample")
	if err := os.MkdirAll(storeDir, 0o755); err != nil {
		t.Fatalf("failed to create store skill dir: %v", err)
	}
	emptyNotes := filepath.Join(storeDir, "notes.md")
	if err := os.WriteFile(emptyNotes, []byte{}, 0o644); err != nil {
		t.Fatalf("failed to write empty store file: %v", err)
	}
	userFile := filepath.Join(storeDir, "mine.md")
	if err := os.WriteFile(userFile, []byte("mine\n"), 0o644); err != nil {
		t.Fatalf("failed to write user file: %v", err)
	}
	stdout := &bytes.Buffer{}
	stderr := &bytes.Buffer{}

	code := runSkillsDirUninstall(destinationDir, []skillDefinition{skill}, stdout, stderr)

	if code != 0 {
		t.Fatalf("dir uninstall failed: code=%d stderr=%s", code, stderr.String())
	}
	if !strings.Contains(stdout.String(), "Not found: 1") {
		t.Fatalf("an empty owned file should not count as an install:\n%s", stdout.String())
	}
	if _, err := os.Stat(emptyNotes); err != nil {
		t.Fatalf("the matching empty file must survive: %v", err)
	}
	if _, err := os.Stat(userFile); err != nil {
		t.Fatalf("user content must survive: %v", err)
	}
}

// Tests that a working symlink inside a source-owned directory does not wedge
// dir mode: the sync copies the target's content as a regular file, and the
// status check compares the same content, so the skill reads as installed.
func TestRunSkillsDirHandlesSymlinkedSourceReference(t *testing.T) {
	if runtime.GOOS == "windows" {
		t.Skip("symlink creation requires elevated privileges on Windows")
	}
	root := t.TempDir()
	skill := writeDirModeSkillSource(t, root, "uloop-sample")
	sharedFile := filepath.Join(root, "shared.md")
	if err := os.WriteFile(sharedFile, []byte("shared\n"), 0o644); err != nil {
		t.Fatalf("failed to write shared file: %v", err)
	}
	if err := os.Symlink(sharedFile, filepath.Join(skill.sourceDirectory, "references", "linked.md")); err != nil {
		t.Fatalf("failed to create source symlink: %v", err)
	}
	destinationDir := filepath.Join(root, "apm-skills")
	stdout := &bytes.Buffer{}
	stderr := &bytes.Buffer{}
	if code := runSkillsDirInstall(destinationDir, []skillDefinition{skill}, stdout, stderr); code != 0 {
		t.Fatalf("dir install failed: code=%d stderr=%s", code, stderr.String())
	}

	stdout.Reset()
	code := runSkillsDirList(destinationDir, []skillDefinition{skill}, stdout, stderr)

	if code != 0 {
		t.Fatalf("dir list failed: code=%d stderr=%s", code, stderr.String())
	}
	if !strings.Contains(stdout.String(), "uloop-sample (installed)") {
		t.Fatalf("a symlinked source reference should not wedge the status:\n%s", stdout.String())
	}
}

// Tests that uninstall in a directory without install evidence removes only
// uloop's own artifacts: a user's .meta file neither authorizes deleting the
// directory nor is deleted itself.
func TestRunSkillsDirUninstallKeepsNoEvidenceDirWithUserMeta(t *testing.T) {
	root := t.TempDir()
	skill := writeDirModeSkillSource(t, root, "uloop-sample")
	destinationDir := filepath.Join(root, "apm-skills")
	skillDir := filepath.Join(destinationDir, "uloop-sample")
	if err := os.MkdirAll(skillDir, 0o755); err != nil {
		t.Fatalf("failed to create skill dir: %v", err)
	}
	artifact := filepath.Join(skillDir, "leftover.uloop-backup-1234")
	if err := os.WriteFile(artifact, []byte("partial\n"), 0o644); err != nil {
		t.Fatalf("failed to write artifact: %v", err)
	}
	userMeta := filepath.Join(skillDir, "dataset.meta")
	if err := os.WriteFile(userMeta, []byte("guid: 1\n"), 0o644); err != nil {
		t.Fatalf("failed to write user meta file: %v", err)
	}
	stdout := &bytes.Buffer{}
	stderr := &bytes.Buffer{}

	code := runSkillsDirUninstall(destinationDir, []skillDefinition{skill}, stdout, stderr)

	if code != 0 {
		t.Fatalf("dir uninstall failed: code=%d stderr=%s", code, stderr.String())
	}
	if _, err := os.Stat(artifact); !os.IsNotExist(err) {
		t.Fatalf("uloop's artifact should be cleaned, stat err=%v", err)
	}
	if _, err := os.Stat(userMeta); err != nil {
		t.Fatalf("the user's .meta file must survive: %v", err)
	}
	if _, err := os.Stat(skillDir); err != nil {
		t.Fatalf("a directory holding user content must not be removed: %v", err)
	}
}

// Tests that uninstall of a real install removes .meta stubs of owned entries
// along with the directory but keeps a directory holding a user's own .meta
// data file.
func TestRunSkillsDirUninstallDistinguishesMetaDebrisFromUserMeta(t *testing.T) {
	root := t.TempDir()
	skill := writeDirModeSkillSource(t, root, "uloop-sample")
	destinationDir := filepath.Join(root, "apm-skills")
	stdout := &bytes.Buffer{}
	stderr := &bytes.Buffer{}
	if code := runSkillsDirInstall(destinationDir, []skillDefinition{skill}, stdout, stderr); code != 0 {
		t.Fatalf("setup install failed: code=%d stderr=%s", code, stderr.String())
	}
	installedDir := filepath.Join(destinationDir, "uloop-sample")
	// Unity mints .meta stubs for the entries uloop installed; those are
	// debris, while dataset.meta is the user's own data file.
	if err := os.WriteFile(filepath.Join(installedDir, "references.meta"), []byte("guid: 1\n"), 0o644); err != nil {
		t.Fatalf("failed to write owned-entry meta stub: %v", err)
	}
	userMeta := filepath.Join(installedDir, "dataset.meta")
	if err := os.WriteFile(userMeta, []byte("guid: 2\n"), 0o644); err != nil {
		t.Fatalf("failed to write user meta file: %v", err)
	}

	stdout.Reset()
	code := runSkillsDirUninstall(destinationDir, []skillDefinition{skill}, stdout, stderr)

	if code != 0 {
		t.Fatalf("dir uninstall failed: code=%d stderr=%s", code, stderr.String())
	}
	if !strings.Contains(stdout.String(), "Removed: 1") {
		t.Fatalf("the installed skill should be removed:\n%s", stdout.String())
	}
	if _, err := os.Stat(userMeta); err != nil {
		t.Fatalf("the user's .meta data file must survive: %v", err)
	}
	if _, err := os.Stat(installedDir); err != nil {
		t.Fatalf("a directory holding a user's .meta file must be kept: %v", err)
	}
}

// Tests that uninstall does not treat a user directory named like ignorable
// debris (references.meta) as deletable: only regular files qualify, so the
// directory, its contents, and the skill directory itself survive.
func TestRunSkillsDirUninstallPreservesUserDirectoryNamedAsDebris(t *testing.T) {
	root := t.TempDir()
	skill := writeDirModeSkillSource(t, root, "uloop-sample")
	destinationDir := filepath.Join(root, "apm-skills")
	stdout := &bytes.Buffer{}
	stderr := &bytes.Buffer{}
	if code := runSkillsDirInstall(destinationDir, []skillDefinition{skill}, stdout, stderr); code != 0 {
		t.Fatalf("setup install failed: code=%d stderr=%s", code, stderr.String())
	}
	installedDir := filepath.Join(destinationDir, "uloop-sample")
	userDebrisDir := filepath.Join(installedDir, "references.meta")
	if err := os.MkdirAll(userDebrisDir, 0o755); err != nil {
		t.Fatalf("failed to create user debris-named dir: %v", err)
	}
	keptFile := filepath.Join(userDebrisDir, "keep.txt")
	if err := os.WriteFile(keptFile, []byte("keep me\n"), 0o644); err != nil {
		t.Fatalf("failed to write file inside debris-named dir: %v", err)
	}

	stdout.Reset()
	code := runSkillsDirUninstall(destinationDir, []skillDefinition{skill}, stdout, stderr)

	if code != 0 {
		t.Fatalf("dir uninstall failed: code=%d stderr=%s", code, stderr.String())
	}
	if _, err := os.Stat(userDebrisDir); err != nil {
		t.Fatalf("user directory named like debris must survive: %v", err)
	}
	if _, err := os.Stat(keptFile); err != nil {
		t.Fatalf("contents of the debris-named directory must survive: %v", err)
	}
	if _, err := os.Stat(installedDir); err != nil {
		t.Fatalf("skill directory holding a user directory must be kept: %v", err)
	}
}

// Tests that a whitespace-only --output-dir value (a typical unset shell
// variable) is rejected as a missing value in both option forms.
func TestParseSkillsOptionsRejectsWhitespaceOutputDir(t *testing.T) {
	if _, err := parseSkillsOptions([]string{"--output-dir", " "}); err == nil {
		t.Fatal("expected error for a whitespace-only --output-dir value")
	}
	if _, err := parseSkillsOptions([]string{"--output-dir= "}); err == nil {
		t.Fatal("expected error for a whitespace-only --output-dir= value")
	}
}
