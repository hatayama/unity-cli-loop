package dispatcher

import (
	"bytes"
	"os"
	"path/filepath"
	"strings"
	"testing"
)

// Tests that dir-mode install skips a skill whose tool the project disabled
// while still installing the enabled ones.
func TestRunSkillsDirInstallSkipsDisabledToolSkill(t *testing.T) {
	root := t.TempDir()
	enabledSkill := writeDirModeSkillSource(t, root, "uloop-enabled")
	disabledSkill := writeDirModeSkillSource(t, root, "uloop-disabled")
	writeToolSettings(t, root, `{"disabledTools":["disabled"]}`)
	destinationDir := filepath.Join(root, "apm-skills")
	stdout := &bytes.Buffer{}
	stderr := &bytes.Buffer{}

	code := runSkillsSubcommand(
		"install",
		root,
		[]skillDefinition{enabledSkill, disabledSkill},
		skillCommandOptions{outputDir: destinationDir},
		stdout,
		stderr,
	)

	if code != 0 {
		t.Fatalf("dir install failed: code=%d stderr=%s", code, stderr.String())
	}
	if _, err := os.Stat(filepath.Join(destinationDir, "uloop-enabled", "SKILL.md")); err != nil {
		t.Fatalf("enabled skill should be installed: %v", err)
	}
	if _, err := os.Stat(filepath.Join(destinationDir, "uloop-disabled")); !os.IsNotExist(err) {
		t.Fatalf("disabled skill should not be installed: err=%v", err)
	}
	if !strings.Contains(stdout.String(), "Installed: 1\n") {
		t.Fatalf("summary should count only the enabled skill:\n%s", stdout.String())
	}
}

// Tests that dir-mode install removes a previously installed skill whose tool
// is now disabled, while foreign files next to it survive.
func TestRunSkillsDirInstallRemovesInstalledDisabledToolSkill(t *testing.T) {
	root := t.TempDir()
	skill := writeDirModeSkillSource(t, root, "uloop-disabled")
	destinationDir := filepath.Join(root, "apm-skills")
	installedDir := filepath.Join(destinationDir, "uloop-disabled")
	installOptions := skillCommandOptions{outputDir: destinationDir}
	if code := runSkillsSubcommand("install", root, []skillDefinition{skill}, installOptions, &bytes.Buffer{}, &bytes.Buffer{}); code != 0 {
		t.Fatalf("initial dir install failed: code=%d", code)
	}
	foreignFile := filepath.Join(installedDir, "apm.yml")
	if err := os.WriteFile(foreignFile, []byte("name: foreign\n"), 0o644); err != nil {
		t.Fatalf("failed to write foreign file: %v", err)
	}
	writeToolSettings(t, root, `{"disabledTools":["disabled"]}`)
	stdout := &bytes.Buffer{}
	stderr := &bytes.Buffer{}

	code := runSkillsSubcommand("install", root, []skillDefinition{skill}, installOptions, stdout, stderr)

	if code != 0 {
		t.Fatalf("dir install failed: code=%d stderr=%s", code, stderr.String())
	}
	if _, err := os.Stat(filepath.Join(installedDir, "SKILL.md")); !os.IsNotExist(err) {
		t.Fatalf("disabled skill file should be removed: err=%v", err)
	}
	if _, err := os.Stat(filepath.Join(installedDir, "references")); !os.IsNotExist(err) {
		t.Fatalf("disabled skill references should be removed: err=%v", err)
	}
	if _, err := os.Stat(foreignFile); err != nil {
		t.Fatalf("foreign file should survive: %v", err)
	}
	if !strings.Contains(stdout.String(), "Disabled removed: 1\n") {
		t.Fatalf("summary should count the removed disabled skill:\n%s", stdout.String())
	}
}

// Tests that dir-mode install leaves a hand-authored directory named after a
// disabled tool's skill untouched, because it carries no install evidence.
func TestRunSkillsDirInstallPreservesUnownedDirOfDisabledToolSkill(t *testing.T) {
	root := t.TempDir()
	skill := writeDirModeSkillSource(t, root, "uloop-disabled")
	writeToolSettings(t, root, `{"disabledTools":["disabled"]}`)
	destinationDir := filepath.Join(root, "apm-skills")
	handAuthoredFile := filepath.Join(destinationDir, "uloop-disabled", "notes.md")
	if err := os.MkdirAll(filepath.Dir(handAuthoredFile), 0o755); err != nil {
		t.Fatalf("failed to create hand-authored dir: %v", err)
	}
	if err := os.WriteFile(handAuthoredFile, []byte("mine\n"), 0o644); err != nil {
		t.Fatalf("failed to write hand-authored file: %v", err)
	}
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
	if _, err := os.Stat(handAuthoredFile); err != nil {
		t.Fatalf("hand-authored file should survive: %v", err)
	}
	if strings.Contains(stdout.String(), "Disabled removed:") {
		t.Fatalf("nothing uloop-owned was removed, so no removal should be reported:\n%s", stdout.String())
	}
}

// Tests that dir-mode list reports a disabled tool's skill as disabled rather
// than as missing.
func TestRunSkillsDirListReportsDisabledToolSkill(t *testing.T) {
	root := t.TempDir()
	skill := writeDirModeSkillSource(t, root, "uloop-disabled")
	writeToolSettings(t, root, `{"disabledTools":["disabled"]}`)
	destinationDir := filepath.Join(root, "apm-skills")
	stdout := &bytes.Buffer{}
	stderr := &bytes.Buffer{}

	code := runSkillsSubcommand(
		"list",
		root,
		[]skillDefinition{skill},
		skillCommandOptions{outputDir: destinationDir},
		stdout,
		stderr,
	)

	if code != 0 {
		t.Fatalf("dir list failed: code=%d stderr=%s", code, stderr.String())
	}
	if !strings.Contains(stdout.String(), "uloop-disabled (disabled)") {
		t.Fatalf("list should report the skill as disabled:\n%s", stdout.String())
	}
}
