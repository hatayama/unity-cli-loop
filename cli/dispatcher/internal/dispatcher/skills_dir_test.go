package dispatcher

import (
	"bytes"
	"os"
	"path/filepath"
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

// Tests that --output-dir without a value is rejected as an argument error.
func TestParseSkillsOptionsRejectsDirWithoutValue(t *testing.T) {
	if _, err := parseSkillsOptions([]string{"--output-dir"}); err == nil {
		t.Fatal("expected error for --output-dir without value")
	}
	if _, err := parseSkillsOptions([]string{"--output-dir="}); err == nil {
		t.Fatal("expected error for --output-dir= without value")
	}
}

// Tests that --output-dir cannot be combined with --global or target flags.
func TestParseSkillsOptionsRejectsDirWithGlobalOrTargets(t *testing.T) {
	if _, err := parseSkillsOptions([]string{"--output-dir", "/custom", "--global"}); err == nil {
		t.Fatal("expected error for --output-dir with --global")
	}
	if _, err := parseSkillsOptions([]string{"--claude", "--output-dir", "/custom"}); err == nil {
		t.Fatal("expected error for --output-dir with a target flag")
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

	status, err := getDirSkillStatus(destinationDir, skill)
	if err != nil {
		t.Fatalf("status check failed: %v", err)
	}
	if status != "installed" {
		t.Fatalf("foreign file should not mark the skill outdated: %s", status)
	}

	if err := os.WriteFile(filepath.Join(installedDir, "references", "stale.md"), []byte("stale\n"), 0o644); err != nil {
		t.Fatalf("failed to write stale reference: %v", err)
	}
	status, err = getDirSkillStatus(destinationDir, skill)
	if err != nil {
		t.Fatalf("status check failed: %v", err)
	}
	if status != "outdated" {
		t.Fatalf("stale file in a source-owned directory should mark the skill outdated: %s", status)
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
