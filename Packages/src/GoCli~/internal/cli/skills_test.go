package cli

import (
	"os"
	"path/filepath"
	"strings"
	"testing"
)

func TestCollectSkillDefinitionsIncludesCliOnlyAndSkipsInternal(t *testing.T) {
	projectRoot := t.TempDir()
	writeTestSkill(t, projectRoot, "Packages/src/GoCli~/internal/cli/skill-definitions/cli-only/uloop-launch/Skill", `---
name: uloop-launch
---

# uloop launch
`)
	writeTestSkill(t, projectRoot, "Packages/src/GoCli~/internal/cli/skill-definitions/cli-only/uloop-internal/Skill", `---
name: uloop-internal
internal: true
---

# internal
`)

	skills, err := collectSkillDefinitions(projectRoot)
	if err != nil {
		t.Fatalf("collectSkillDefinitions failed: %v", err)
	}

	if len(skills) != 1 {
		t.Fatalf("skill count mismatch: %#v", skills)
	}
	if skills[0].name != "uloop-launch" {
		t.Fatalf("skill name mismatch: %s", skills[0].name)
	}
}

func TestInstallAndUninstallSkillsForTarget(t *testing.T) {
	projectRoot := t.TempDir()
	sourceDir := filepath.Join(projectRoot, "source", "Skill")
	writeSkillFile(t, sourceDir, `---
name: uloop-sample
---

# sample
`)
	if err := os.MkdirAll(filepath.Join(sourceDir, "references"), 0o755); err != nil {
		t.Fatalf("failed to create references: %v", err)
	}
	if err := os.WriteFile(filepath.Join(sourceDir, "references", "note.md"), []byte("note"), 0o644); err != nil {
		t.Fatalf("failed to write reference: %v", err)
	}
	if err := os.WriteFile(filepath.Join(sourceDir, "SKILL.md.meta"), []byte("meta"), 0o644); err != nil {
		t.Fatalf("failed to write meta: %v", err)
	}

	skill := skillDefinition{
		name:            "uloop-sample",
		content:         []byte("sample"),
		sourceDirectory: sourceDir,
	}
	target := targetConfigs["claude"]

	result, err := installSkillsForTarget(projectRoot, target, []skillDefinition{skill}, false, true)
	if err != nil {
		t.Fatalf("installSkillsForTarget failed: %v", err)
	}
	if result.installed != 1 || result.updated != 0 || result.skipped != 0 {
		t.Fatalf("install result mismatch: %#v", result)
	}

	installedDir := filepath.Join(projectRoot, ".claude", "skills", "unity-cli-loop", "uloop-sample")
	if _, err := os.Stat(filepath.Join(installedDir, "SKILL.md")); err != nil {
		t.Fatalf("skill file was not installed: %v", err)
	}
	if _, err := os.Stat(filepath.Join(installedDir, "SKILL.md.meta")); err == nil {
		t.Fatal("meta file should not be installed")
	}

	removed, notFound, err := uninstallSkillsForTarget(projectRoot, target, []skillDefinition{skill}, false, true)
	if err != nil {
		t.Fatalf("uninstallSkillsForTarget failed: %v", err)
	}
	if removed != 1 || notFound != 0 {
		t.Fatalf("uninstall result mismatch: removed=%d notFound=%d", removed, notFound)
	}
}

func TestUninstallSkillsForTargetUsesSelectedLayoutOnly(t *testing.T) {
	projectRoot := t.TempDir()
	sourceDir := filepath.Join(projectRoot, "source", "Skill")
	writeSkillFile(t, sourceDir, `---
name: uloop-sample
---

# sample
`)

	skill := skillDefinition{
		name:            "uloop-sample",
		content:         []byte("sample"),
		sourceDirectory: sourceDir,
	}
	target := targetConfigs["claude"]
	baseDir := getSkillsBaseDir(projectRoot, target, false)
	groupedDir := getPreferredSkillDir(baseDir, skill.name, true)
	flatDir := getPreferredSkillDir(baseDir, skill.name, false)
	writeSkillFile(t, groupedDir, "# grouped\n")
	writeSkillFile(t, flatDir, "# flat\n")

	removed, notFound, err := uninstallSkillsForTarget(projectRoot, target, []skillDefinition{skill}, false, true)
	if err != nil {
		t.Fatalf("uninstallSkillsForTarget failed: %v", err)
	}
	if removed != 1 || notFound != 0 {
		t.Fatalf("uninstall result mismatch: removed=%d notFound=%d", removed, notFound)
	}
	if _, err := os.Stat(groupedDir); err == nil {
		t.Fatal("grouped skill should be removed")
	}
	if _, err := os.Stat(flatDir); err != nil {
		t.Fatalf("flat skill should remain: %v", err)
	}
}

func TestParseSkillsOptionsRequiresKnownFlags(t *testing.T) {
	_, err := parseSkillsOptions([]string{"--claude", "--bad-target"})
	if err == nil {
		t.Fatal("expected unknown option error")
	}
}

func writeTestSkill(t *testing.T, projectRoot string, relativeDir string, content string) {
	t.Helper()
	writeSkillFile(t, filepath.Join(projectRoot, filepath.FromSlash(relativeDir)), content)
}

func writeSkillFile(t *testing.T, skillDir string, content string) {
	t.Helper()
	if err := os.MkdirAll(skillDir, 0o755); err != nil {
		t.Fatalf("failed to create skill dir: %v", err)
	}
	normalizedContent := strings.ReplaceAll(content, "\r\n", "\n")
	if err := os.WriteFile(filepath.Join(skillDir, "SKILL.md"), []byte(normalizedContent), 0o644); err != nil {
		t.Fatalf("failed to write skill file: %v", err)
	}
}
