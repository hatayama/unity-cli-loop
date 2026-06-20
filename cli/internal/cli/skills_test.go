package cli

import (
	"bytes"
	"os"
	"os/exec"
	"path/filepath"
	"reflect"
	"sort"
	"strings"
	"testing"
)

// Tests that CLI-only skill discovery excludes skills marked as internal.
func TestCollectSkillDefinitionsIncludesCliOnlyAndSkipsInternal(t *testing.T) {
	projectRoot := t.TempDir()
	writeTestSkill(t, projectRoot, "Packages/src/Editor/CliOnlyTools~/uloop-launch/Skill", `---
name: uloop-launch
---

# uloop launch
`)
	writeTestSkill(t, projectRoot, "Packages/src/Editor/CliOnlyTools~/uloop-internal/Skill", `---
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

func TestCollectSkillDefinitionsSkipsSingleQuotedInternalSkill(t *testing.T) {
	// Verifies YAML single quotes do not expose internal skills.
	projectRoot := t.TempDir()
	writeTestSkill(t, projectRoot, "Assets/Editor/InternalTool/Skill", `---
name: uloop-internal-tool
internal: 'true'
---

# internal
`)

	skills, err := collectSkillDefinitions(projectRoot)
	if err != nil {
		t.Fatalf("collectSkillDefinitions failed: %v", err)
	}
	if len(skills) != 0 {
		t.Fatalf("internal skill should be hidden: %#v", skills)
	}
}

// Tests that skill discovery includes package, CLI-only, project-local, and cached package skill roots.
func TestCollectSkillDefinitionsIncludesProjectAndPackageRoots(t *testing.T) {
	projectRoot := t.TempDir()
	writeTestSkill(t, projectRoot, "Packages/src/Editor/FirstPartyTools/Compile/Skill", `---
name: uloop-compile
---

# package
`)
	writeTestSkill(t, projectRoot, "Packages/src/Editor/CliOnlyTools~/uloop-launch/Skill", `---
name: uloop-launch
---

# cli-only
`)
	writeTestSkill(t, projectRoot, "Assets/Editor/CustomTools/Hello/Skill", `---
name: uloop-hello
---

# project
`)
	writeTestSkill(t, projectRoot, "Assets/Editor/DirectTool", `---
name: uloop-direct
---

# direct project
`)
	writeTestSkill(t, projectRoot, "Packages/local-package/Editor/LocalTool/Skill", `---
name: uloop-local-package
---

# local package
`)
	manifestLocalPackageRoot := filepath.Join(t.TempDir(), "manifest-local-package")
	writeSkillFile(t, filepath.Join(manifestLocalPackageRoot, "Editor", "ManifestLocalTool", "Skill"), `---
name: uloop-manifest-local-package
---

# manifest local package
`)
	writeManifest(
		t,
		projectRoot,
		`{"dependencies":{"com.example.cached":"1.0.0","com.example.manifest-local":"file:`+filepath.ToSlash(manifestLocalPackageRoot)+`"}}`)
	writeTestSkill(t, projectRoot, "Library/PackageCache/com.example.cached@1.0.0/Editor/CachedTool/Skill", `---
name: uloop-cached-package
---

# cached package
`)

	skills, err := collectSkillDefinitions(projectRoot)
	if err != nil {
		t.Fatalf("collectSkillDefinitions failed: %v", err)
	}

	actualNames := skillNames(skills)
	expectedNames := []string{
		"uloop-cached-package",
		"uloop-compile",
		"uloop-direct",
		"uloop-hello",
		"uloop-launch",
		"uloop-local-package",
		"uloop-manifest-local-package",
	}
	if !reflect.DeepEqual(actualNames, expectedNames) {
		t.Fatalf("skill names mismatch:\nactual:   %#v\nexpected: %#v", actualNames, expectedNames)
	}
}

// Tests that the temporary V3 migration skill stays out of normal skill discovery.
func TestCollectSkillDefinitionsSkipsV3MigrationSkill(t *testing.T) {
	projectRoot := t.TempDir()
	writeTestSkill(t, projectRoot, "Packages/src/TemporarySkills/v3-cli-invocation-migration/Skill", `---
name: v3-cli-invocation-migration
---

# temporary migration
`)

	skills, err := collectSkillDefinitions(projectRoot)
	if err != nil {
		t.Fatalf("collectSkillDefinitions failed: %v", err)
	}

	if len(skills) != 0 {
		t.Fatalf("temporary migration skill should not be discovered normally: %#v", skills)
	}
}

// Tests that the dedicated V3 migration skill discovery reads only the temporary skill source.
func TestCollectV3MigrationSkillDefinitionReadsTemporarySkill(t *testing.T) {
	projectRoot := t.TempDir()
	writePackageRootMarker(t, projectRoot)
	writeTestSkill(t, projectRoot, "Packages/src/TemporarySkills/v3-cli-invocation-migration/Skill", `---
name: v3-cli-invocation-migration
---

# temporary migration
`)

	skills, err := collectV3MigrationSkillDefinition(projectRoot)
	if err != nil {
		t.Fatalf("collectV3MigrationSkillDefinition failed: %v", err)
	}

	actualNames := skillNames(skills)
	expectedNames := []string{"v3-cli-invocation-migration"}
	if !reflect.DeepEqual(actualNames, expectedNames) {
		t.Fatalf("skill names mismatch:\nactual:   %#v\nexpected: %#v", actualNames, expectedNames)
	}
}

// Tests that the dedicated V3 migration skill can be read from a manifest file dependency.
func TestCollectV3MigrationSkillDefinitionReadsManifestFilePackage(t *testing.T) {
	projectRoot := t.TempDir()
	packageRoot := filepath.Join(t.TempDir(), "Packages", "src")
	markerPath := filepath.Join(packageRoot, "Editor", "FirstPartyTools")
	if err := os.MkdirAll(markerPath, 0o755); err != nil {
		t.Fatalf("failed to create marker path: %v", err)
	}
	writeManifest(t, projectRoot, `{"dependencies":{"io.github.hatayama.uloopmcp":"file:`+filepath.ToSlash(packageRoot)+`"}}`)
	writeSkillFile(t, filepath.Join(
		packageRoot,
		"TemporarySkills",
		"v3-cli-invocation-migration",
		"Skill"), `---
name: v3-cli-invocation-migration
---

# temporary migration
`)

	skills, err := collectV3MigrationSkillDefinition(projectRoot)
	if err != nil {
		t.Fatalf("collectV3MigrationSkillDefinition failed: %v", err)
	}

	actualNames := skillNames(skills)
	expectedNames := []string{"v3-cli-invocation-migration"}
	if !reflect.DeepEqual(actualNames, expectedNames) {
		t.Fatalf("skill names mismatch:\nactual:   %#v\nexpected: %#v", actualNames, expectedNames)
	}
}

// Tests that local manifest packages from other dependencies cannot replace the Unity CLI Loop package root.
func TestResolvePackageRootIgnoresOtherManifestFilePackages(t *testing.T) {
	projectRoot := t.TempDir()
	otherPackageRoot := filepath.Join(t.TempDir(), "AOtherPackage")
	packageRoot := filepath.Join(t.TempDir(), "ZUnityCliLoopPackage")
	for _, candidateRoot := range []string{otherPackageRoot, packageRoot} {
		markerPath := filepath.Join(candidateRoot, "Editor", "FirstPartyTools")
		if err := os.MkdirAll(markerPath, 0o755); err != nil {
			t.Fatalf("failed to create marker path: %v", err)
		}
	}
	writeManifest(
		t,
		projectRoot,
		`{"dependencies":{"com.example.other":"file:`+filepath.ToSlash(otherPackageRoot)+`","io.github.hatayama.uloopmcp":"file:`+filepath.ToSlash(packageRoot)+`"}}`)

	actualRoot := resolvePackageRoot(projectRoot)
	if actualRoot != packageRoot {
		t.Fatalf("package root mismatch: actual=%s expected=%s", actualRoot, packageRoot)
	}
}

// Tests that CLI-only and project-local skills win over package-root duplicates.
func TestCollectSkillDefinitionsUsesUnitySideSourcePrecedence(t *testing.T) {
	projectRoot := t.TempDir()
	writeTestSkill(t, projectRoot, "Packages/src/Editor/FirstPartyTools/Compile/Skill", `---
name: uloop-launch
---

# package launch
`)
	writeTestSkill(t, projectRoot, "Packages/src/Editor/FirstPartyTools/ProjectDuplicate/Skill", `---
name: uloop-project
---

# package project
`)
	writeTestSkill(t, projectRoot, "Packages/src/Editor/CliOnlyTools~/uloop-launch/Skill", `---
name: uloop-launch
---

# cli-only launch
`)
	writeTestSkill(t, projectRoot, "Assets/Editor/ProjectDuplicate/Skill", `---
name: uloop-project
---

# asset project
`)

	skills, err := collectSkillDefinitions(projectRoot)
	if err != nil {
		t.Fatalf("collectSkillDefinitions failed: %v", err)
	}

	assertSkillContentContains(t, skills, "uloop-launch", "# cli-only launch")
	assertSkillContentContains(t, skills, "uloop-project", "# asset project")
}

// Tests that package root probing uses the current first-party tool marker.
func TestResolvePackageRootCandidateUsesFirstPartyToolsMarker(t *testing.T) {
	projectRoot := t.TempDir()
	markerPath := filepath.Join(projectRoot, "Packages", "src", "Editor", "FirstPartyTools")
	if err := os.MkdirAll(markerPath, 0o755); err != nil {
		t.Fatalf("failed to create marker path: %v", err)
	}

	actualRoot := resolvePackageRootCandidate(projectRoot)
	expectedRoot := filepath.Join(projectRoot, "Packages", "src")
	if actualRoot != expectedRoot {
		t.Fatalf("package root mismatch: actual=%s expected=%s", actualRoot, expectedRoot)
	}
}

// Tests that direct SKILL.md files without a frontmatter name use their own directory name.
func TestCollectSkillDefinitionsUsesDirectoryNameWhenDirectSkillOmitsName(t *testing.T) {
	projectRoot := t.TempDir()
	writeTestSkill(t, projectRoot, "Assets/Editor/DirectTool", `---
---

# direct project
`)

	skills, err := collectSkillDefinitions(projectRoot)
	if err != nil {
		t.Fatalf("collectSkillDefinitions failed: %v", err)
	}

	actualNames := skillNames(skills)
	expectedNames := []string{"DirectTool"}
	if !reflect.DeepEqual(actualNames, expectedNames) {
		t.Fatalf("skill names mismatch:\nactual:   %#v\nexpected: %#v", actualNames, expectedNames)
	}
}

// Tests that installing and uninstalling a managed skill copies allowed files and removes the skill.
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

// Tests that CRLF-only drift from Windows checkouts does not mark installed skills stale.
func TestSkillStatusIgnoresCRLFLineEndings(t *testing.T) {
	projectRoot := t.TempDir()
	sourceDir := filepath.Join(projectRoot, "source", "Skill")
	writeSkillFile(t, sourceDir, "---\nname: uloop-sample\n---\n\n# sample\n")
	referencesDir := filepath.Join(sourceDir, "references")
	if err := os.MkdirAll(referencesDir, 0o755); err != nil {
		t.Fatalf("failed to create references: %v", err)
	}
	if err := os.WriteFile(filepath.Join(referencesDir, "note.md"), []byte("line1\nline2\n"), 0o644); err != nil {
		t.Fatalf("failed to write source reference: %v", err)
	}

	skill := skillDefinition{
		name:            "uloop-sample",
		content:         []byte("---\nname: uloop-sample\n---\n\n# sample\n"),
		sourceDirectory: sourceDir,
	}
	installedDir := filepath.Join(projectRoot, ".claude", "skills", managedSkillsDir, "uloop-sample")
	writeRawSkillFile(t, installedDir, "---\r\nname: uloop-sample\r\n---\r\n\r\n# sample\r\n")
	if err := os.MkdirAll(filepath.Join(installedDir, "references"), 0o755); err != nil {
		t.Fatalf("failed to create installed references: %v", err)
	}
	if err := os.WriteFile(filepath.Join(installedDir, "references", "note.md"), []byte("line1\r\nline2\r\n"), 0o644); err != nil {
		t.Fatalf("failed to write installed reference: %v", err)
	}
	installedSkillContent, err := os.ReadFile(filepath.Join(installedDir, "SKILL.md"))
	if err != nil {
		t.Fatalf("failed to read installed skill: %v", err)
	}
	if !bytes.Contains(installedSkillContent, []byte("\r\n")) {
		t.Fatal("test setup should keep CRLF line endings in installed SKILL.md")
	}

	status, err := getSkillStatus(filepath.Join(projectRoot, ".claude", "skills"), skill, true)
	if err != nil {
		t.Fatalf("getSkillStatus failed: %v", err)
	}

	if status != "installed" {
		t.Fatalf("status mismatch: %s", status)
	}
}

// Tests that status checks surface inaccessible installed skill directories.
func TestSkillStatusReturnsStatErrors(t *testing.T) {
	projectRoot := t.TempDir()
	baseDir := filepath.Join(projectRoot, ".claude", "skills")
	skill := skillDefinition{name: "uloop-sample"}

	_, err := getSkillStatusWithStat(
		baseDir,
		skill,
		true,
		func(string) (os.FileInfo, error) {
			return nil, os.ErrPermission
		},
	)

	if err == nil {
		t.Fatal("expected status check error")
	}
}

// Tests that installing skills writes deterministic LF line endings.
func TestInstallSkillsNormalizesCRLFLineEndings(t *testing.T) {
	projectRoot := t.TempDir()
	sourceDir := filepath.Join(projectRoot, "source", "Skill")
	writeSkillFile(t, sourceDir, "---\r\nname: uloop-sample\r\n---\r\n\r\n# sample\r\n")
	referencesDir := filepath.Join(sourceDir, "references")
	if err := os.MkdirAll(referencesDir, 0o755); err != nil {
		t.Fatalf("failed to create references: %v", err)
	}
	if err := os.WriteFile(filepath.Join(referencesDir, "note.md"), []byte("line1\r\nline2\r\n"), 0o644); err != nil {
		t.Fatalf("failed to write source reference: %v", err)
	}

	skill := skillDefinition{
		name:            "uloop-sample",
		content:         []byte("---\r\nname: uloop-sample\r\n---\r\n\r\n# sample\r\n"),
		sourceDirectory: sourceDir,
	}

	result, err := installSkillsForTarget(projectRoot, targetConfigs["claude"], []skillDefinition{skill}, false, true)
	if err != nil {
		t.Fatalf("installSkillsForTarget failed: %v", err)
	}
	if result.installed != 1 || result.updated != 0 || result.skipped != 0 {
		t.Fatalf("install result mismatch: %#v", result)
	}

	installedReferencePath := filepath.Join(
		projectRoot,
		".claude",
		"skills",
		managedSkillsDir,
		"uloop-sample",
		"references",
		"note.md")
	content, err := os.ReadFile(installedReferencePath)
	if err != nil {
		t.Fatalf("failed to read installed reference: %v", err)
	}
	if strings.Contains(string(content), "\r") {
		t.Fatalf("installed reference kept CRLF line endings: %q", string(content))
	}
}

// Tests that PowerShell scripts keep their source encoding while line endings are normalized.
func TestNormalizeSkillFileContentPreservesUTF16PowerShellEncoding(t *testing.T) {
	source := utf16LittleEndianWithBOM("line1\r\nline2\r\n")
	expected := utf16LittleEndianWithBOM("line1\nline2\n")

	actual := normalizeSkillFileContent("install.ps1", source)

	if !bytes.Equal(actual, expected) {
		t.Fatalf("normalized UTF-16 content mismatch:\nactual:   % x\nexpected: % x", actual, expected)
	}
}

// Tests that installing skills removes disabled and deprecated skill directories from all layouts.
func TestInstallSkillsForTargetRemovesDisabledAndDeprecatedSkills(t *testing.T) {
	projectRoot := t.TempDir()
	enabledSourceDir := filepath.Join(projectRoot, "source", "Enabled", "Skill")
	writeSkillFile(t, enabledSourceDir, `---
name: uloop-enabled-skill
---

# enabled
`)
	disabledSourceDir := filepath.Join(projectRoot, "source", "Disabled", "Skill")
	writeSkillFile(t, disabledSourceDir, `---
name: uloop-disabled-skill
---

# disabled
`)
	writeToolSettings(t, projectRoot, `{"disabledTools":["disabled-skill"]}`)

	target := targetConfigs["claude"]
	skillsRoot := filepath.Join(projectRoot, target.projectDir, "skills")
	disabledFlatDir := filepath.Join(skillsRoot, "uloop-disabled-skill")
	disabledGroupedDir := filepath.Join(skillsRoot, managedSkillsDir, "uloop-disabled-skill")
	deprecatedFlatDir := filepath.Join(skillsRoot, "uloop-capture-window")
	deprecatedGroupedDir := filepath.Join(skillsRoot, managedSkillsDir, "uloop-capture-window")
	writeSkillFile(t, disabledFlatDir, "---\nname: uloop-disabled-skill\n---\n")
	writeSkillFile(t, disabledGroupedDir, "---\nname: uloop-disabled-skill\n---\n")
	writeSkillFile(t, deprecatedFlatDir, "---\nname: uloop-capture-window\n---\n")
	writeSkillFile(t, deprecatedGroupedDir, "---\nname: uloop-capture-window\n---\n")

	result, err := installSkillsForTarget(projectRoot, target, []skillDefinition{
		{
			name:            "uloop-enabled-skill",
			content:         []byte("---\nname: uloop-enabled-skill\n---\n\n# enabled\n"),
			sourceDirectory: enabledSourceDir,
		},
		{
			name:            "uloop-disabled-skill",
			toolName:        "disabled-skill",
			content:         []byte("---\nname: uloop-disabled-skill\n---\n\n# disabled\n"),
			sourceDirectory: disabledSourceDir,
		},
	}, false, true)
	if err != nil {
		t.Fatalf("installSkillsForTarget failed: %v", err)
	}
	if result.installed != 1 || result.updated != 0 || result.skipped != 0 {
		t.Fatalf("install result mismatch: %#v", result)
	}

	enabledDir := filepath.Join(skillsRoot, managedSkillsDir, "uloop-enabled-skill")
	for _, missingDir := range []string{disabledFlatDir, disabledGroupedDir, deprecatedFlatDir, deprecatedGroupedDir} {
		if _, err := os.Stat(missingDir); err == nil {
			t.Fatalf("stale skill should be removed: %s", missingDir)
		}
	}
	if _, err := os.Stat(filepath.Join(enabledDir, "SKILL.md")); err != nil {
		t.Fatalf("enabled skill should be installed: %v", err)
	}
}

// Tests that grouped installs migrate an existing legacy flat skill instead of duplicating it.
func TestInstallSkillsForTargetMigratesLegacyFlatSkillToGroupedLayout(t *testing.T) {
	projectRoot := t.TempDir()
	sourceDir := filepath.Join(projectRoot, "source", "Skill")
	skillContent := `---
name: uloop-sample
---

# sample
`
	writeSkillFile(t, sourceDir, skillContent)

	skill := skillDefinition{
		name:            "uloop-sample",
		content:         []byte(strings.ReplaceAll(skillContent, "\r\n", "\n")),
		sourceDirectory: sourceDir,
	}
	target := targetConfigs["claude"]
	baseDir, err := getSkillsBaseDir(projectRoot, target, false)
	if err != nil {
		t.Fatalf("getSkillsBaseDir failed: %v", err)
	}
	flatDir := getPreferredSkillDir(baseDir, skill.name, false)
	groupedDir := getPreferredSkillDir(baseDir, skill.name, true)
	writeSkillFile(t, flatDir, skillContent)

	result, err := installSkillsForTarget(projectRoot, target, []skillDefinition{skill}, false, true)
	if err != nil {
		t.Fatalf("installSkillsForTarget failed: %v", err)
	}
	if result.installed != 0 || result.updated != 0 || result.skipped != 1 {
		t.Fatalf("install result mismatch: %#v", result)
	}
	if _, err := os.Stat(flatDir); err == nil {
		t.Fatal("flat skill should be migrated")
	}
	if _, err := os.Stat(filepath.Join(groupedDir, "SKILL.md")); err != nil {
		t.Fatalf("grouped skill should exist: %v", err)
	}
}

// Tests that flat installs remove a grouped copy of the same managed skill.
func TestInstallSkillsForTargetRemovesGroupedSkillWhenInstallingFlatLayout(t *testing.T) {
	projectRoot := t.TempDir()
	sourceDir := filepath.Join(projectRoot, "source", "Skill")
	writeSkillFile(t, sourceDir, `---
name: uloop-sample
---

# sample
`)

	skill := skillDefinition{
		name:            "uloop-sample",
		content:         []byte("---\nname: uloop-sample\n---\n\n# sample\n"),
		sourceDirectory: sourceDir,
	}
	target := targetConfigs["claude"]
	baseDir, err := getSkillsBaseDir(projectRoot, target, false)
	if err != nil {
		t.Fatalf("getSkillsBaseDir failed: %v", err)
	}
	groupedDir := getPreferredSkillDir(baseDir, skill.name, true)
	flatDir := getPreferredSkillDir(baseDir, skill.name, false)
	writeSkillFile(t, groupedDir, "# grouped\n")

	result, err := installSkillsForTarget(projectRoot, target, []skillDefinition{skill}, false, false)
	if err != nil {
		t.Fatalf("installSkillsForTarget failed: %v", err)
	}
	if result.installed != 1 || result.updated != 0 || result.skipped != 0 {
		t.Fatalf("install result mismatch: %#v", result)
	}
	if _, err := os.Stat(groupedDir); err == nil {
		t.Fatal("grouped skill should be removed")
	}
	if _, err := os.Stat(filepath.Join(flatDir, "SKILL.md")); err != nil {
		t.Fatalf("flat skill should be installed: %v", err)
	}
}

// Tests that the public install command writes skills to the flat layout by default.
func TestRunSkillsInstallDefaultsToFlatLayout(t *testing.T) {
	projectRoot := t.TempDir()
	sourceDir := filepath.Join(projectRoot, "source", "Skill")
	writeSkillFile(t, sourceDir, `---
name: uloop-sample
---

# sample
`)

	skill := skillDefinition{
		name:            "uloop-sample",
		content:         []byte("---\nname: uloop-sample\n---\n\n# sample\n"),
		sourceDirectory: sourceDir,
	}
	target := targetConfigs["claude"]
	options := skillCommandOptions{
		targets: []skillTarget{target},
	}
	stdout := &bytes.Buffer{}
	stderr := &bytes.Buffer{}

	code := runSkillsInstall(projectRoot, []skillDefinition{skill}, options, stdout, stderr)

	if code != 0 {
		t.Fatalf("install should succeed: code=%d stderr=%s", code, stderr.String())
	}
	baseDir, err := getSkillsBaseDir(projectRoot, target, false)
	if err != nil {
		t.Fatalf("getSkillsBaseDir failed: %v", err)
	}
	flatDir := getPreferredSkillDir(baseDir, skill.name, false)
	groupedDir := getPreferredSkillDir(baseDir, skill.name, true)
	if _, err := os.Stat(filepath.Join(flatDir, "SKILL.md")); err != nil {
		t.Fatalf("flat skill should be installed: %v", err)
	}
	if _, err := os.Stat(groupedDir); err == nil {
		t.Fatal("grouped skill should not be installed by default")
	}
}

// Tests that the public list command reads the flat layout by default.
func TestRunSkillsListDefaultsToFlatLayout(t *testing.T) {
	projectRoot := t.TempDir()
	skill := skillDefinition{
		name:    "uloop-sample",
		content: []byte("---\nname: uloop-sample\n---\n\n# sample\n"),
	}
	target := targetConfigs["claude"]
	baseDir, err := getSkillsBaseDir(projectRoot, target, false)
	if err != nil {
		t.Fatalf("getSkillsBaseDir failed: %v", err)
	}
	writeSkillFile(t, getPreferredSkillDir(baseDir, skill.name, false), string(skill.content))
	options := skillCommandOptions{
		targets: []skillTarget{target},
	}
	stdout := &bytes.Buffer{}
	stderr := &bytes.Buffer{}

	code := runSkillsList(projectRoot, []skillDefinition{skill}, options, stdout, stderr)

	if code != 0 {
		t.Fatalf("list should succeed: code=%d stderr=%s", code, stderr.String())
	}
	if !strings.Contains(stdout.String(), "uloop-sample (installed)") {
		t.Fatalf("flat skill should be reported as installed: %s", stdout.String())
	}
}

// Tests that the public uninstall command removes only the flat layout by default.
func TestRunSkillsUninstallDefaultsToFlatLayout(t *testing.T) {
	projectRoot := t.TempDir()
	skill := skillDefinition{
		name:    "uloop-sample",
		content: []byte("sample"),
	}
	target := targetConfigs["claude"]
	baseDir, err := getSkillsBaseDir(projectRoot, target, false)
	if err != nil {
		t.Fatalf("getSkillsBaseDir failed: %v", err)
	}
	flatDir := getPreferredSkillDir(baseDir, skill.name, false)
	groupedDir := getPreferredSkillDir(baseDir, skill.name, true)
	writeSkillFile(t, flatDir, "# flat\n")
	writeSkillFile(t, groupedDir, "# grouped\n")
	options := skillCommandOptions{
		targets: []skillTarget{target},
	}
	stdout := &bytes.Buffer{}
	stderr := &bytes.Buffer{}

	code := runSkillsUninstall(projectRoot, []skillDefinition{skill}, options, stdout, stderr)

	if code != 0 {
		t.Fatalf("uninstall should succeed: code=%d stderr=%s", code, stderr.String())
	}
	if _, err := os.Stat(flatDir); err == nil {
		t.Fatal("flat skill should be removed")
	}
	if _, err := os.Stat(groupedDir); err != nil {
		t.Fatalf("grouped skill should remain: %v", err)
	}
}

// Tests that uninstalling uses only the selected layout and leaves the other layout intact.
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
	baseDir, err := getSkillsBaseDir(projectRoot, target, false)
	if err != nil {
		t.Fatalf("getSkillsBaseDir failed: %v", err)
	}
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

// Tests that uninstalling deprecated skills only cleans the selected layout.
func TestUninstallSkillsForTargetRemovesDeprecatedSkillsFromSelectedLayoutOnly(t *testing.T) {
	projectRoot := t.TempDir()
	target := targetConfigs["claude"]
	baseDir, err := getSkillsBaseDir(projectRoot, target, false)
	if err != nil {
		t.Fatalf("getSkillsBaseDir failed: %v", err)
	}
	groupedDeprecatedDir := getPreferredSkillDir(baseDir, "uloop-capture-window", true)
	flatDeprecatedDir := getPreferredSkillDir(baseDir, "uloop-capture-window", false)
	writeSkillFile(t, groupedDeprecatedDir, "# grouped deprecated\n")
	writeSkillFile(t, flatDeprecatedDir, "# flat deprecated\n")

	removed, notFound, err := uninstallSkillsForTarget(projectRoot, target, []skillDefinition{}, false, true)
	if err != nil {
		t.Fatalf("uninstallSkillsForTarget failed: %v", err)
	}
	if removed != 1 || notFound != 0 {
		t.Fatalf("uninstall result mismatch: removed=%d notFound=%d", removed, notFound)
	}
	if _, err := os.Stat(groupedDeprecatedDir); err == nil {
		t.Fatal("grouped deprecated skill should be removed")
	}
	if _, err := os.Stat(flatDeprecatedDir); err != nil {
		t.Fatalf("flat deprecated skill should remain: %v", err)
	}
}

// Tests that global skill paths fail instead of falling back to a relative directory.
func TestGetSkillsBaseDirReturnsHomeLookupErrorForGlobalTargets(t *testing.T) {
	originalUserHomeDir := userHomeDir
	userHomeDir = func() (string, error) {
		return "", os.ErrPermission
	}
	t.Cleanup(func() {
		userHomeDir = originalUserHomeDir
	})

	_, err := getSkillsBaseDir(t.TempDir(), targetConfigs["claude"], true)

	if err == nil {
		t.Fatal("expected home lookup error")
	}
}

// Tests that skills option parsing rejects unknown flags.
func TestParseSkillsOptionsRequiresKnownFlags(t *testing.T) {
	_, err := parseSkillsOptions([]string{"--claude", "--bad-target"})
	if err == nil {
		t.Fatal("expected unknown option error")
	}
}

// Tests that repeated target flags are ignored after their first occurrence.
func TestParseSkillsOptionsDeduplicatesTargets(t *testing.T) {
	options, err := parseSkillsOptions([]string{"--claude", "--claude", "--codex"})
	if err != nil {
		t.Fatalf("parseSkillsOptions failed: %v", err)
	}

	actualIDs := []string{}
	for _, target := range options.targets {
		actualIDs = append(actualIDs, target.id)
	}
	expectedIDs := []string{"claude", "codex"}
	if !reflect.DeepEqual(actualIDs, expectedIDs) {
		t.Fatalf("target ids mismatch: %#v", actualIDs)
	}
}

// Tests that unknown skills subcommands are rejected before project resolution.
func TestTryHandleSkillsRequestRejectsUnknownSubcommandWithoutProject(t *testing.T) {
	stdout := &bytes.Buffer{}
	stderr := &bytes.Buffer{}

	handled, code := tryHandleSkillsRequest(
		[]string{"skills", "unknown"},
		t.TempDir(),
		"",
		stdout,
		stderr,
	)

	if !handled || code != 1 {
		t.Fatalf("unexpected result: handled=%v code=%d", handled, code)
	}
	if !strings.Contains(stderr.String(), "Unknown skills command: unknown") {
		t.Fatalf("stderr mismatch: %s", stderr.String())
	}
	if strings.Contains(stderr.String(), "unity project not found") {
		t.Fatalf("unknown subcommand should not resolve project first: %s", stderr.String())
	}
}

// Tests that install refreshes targets that already hold uloop skills even when
// they were not requested, because stale per-agent copies (e.g. .codex) have
// repeatedly misled agents with retired flags.
func TestRunSkillsInstallAutoRefreshesPreviouslyInstalledTargets(t *testing.T) {
	projectRoot := t.TempDir()
	sourceDir := filepath.Join(projectRoot, "source", "Skill")
	writeSkillFile(t, sourceDir, `---
name: uloop-sample
---

# new content
`)
	skill := skillDefinition{
		name:            "uloop-sample",
		content:         []byte("---\nname: uloop-sample\n---\n\n# new content\n"),
		sourceDirectory: sourceDir,
	}

	codexTarget := targetConfigs["codex"]
	codexBaseDir, err := getSkillsBaseDir(projectRoot, codexTarget, false)
	if err != nil {
		t.Fatalf("getSkillsBaseDir failed: %v", err)
	}
	staleDir := getPreferredSkillDir(codexBaseDir, skill.name, false)
	writeSkillFile(t, staleDir, "---\nname: uloop-sample\n---\n\n# stale content\n")

	stdout := &bytes.Buffer{}
	stderr := &bytes.Buffer{}
	options := skillCommandOptions{targets: []skillTarget{targetConfigs["claude"]}}
	code := runSkillsInstall(projectRoot, []skillDefinition{skill}, options, stdout, stderr)

	if code != 0 {
		t.Fatalf("install should succeed: code=%d stderr=%s", code, stderr.String())
	}
	refreshedContent, err := os.ReadFile(filepath.Join(staleDir, "SKILL.md"))
	if err != nil {
		t.Fatalf("codex skill should still exist: %v", err)
	}
	if !strings.Contains(string(refreshedContent), "# new content") {
		t.Fatalf("codex skill should be refreshed: %s", string(refreshedContent))
	}
	if !strings.Contains(stdout.String(), "Codex CLI") {
		t.Fatalf("output should mention the auto-refreshed target: %s", stdout.String())
	}
}

// Tests that install never creates skill directories for targets that were
// neither requested nor previously installed.
func TestRunSkillsInstallLeavesUninstalledTargetsUntouched(t *testing.T) {
	projectRoot := t.TempDir()
	sourceDir := filepath.Join(projectRoot, "source", "Skill")
	writeSkillFile(t, sourceDir, `---
name: uloop-sample
---

# sample
`)
	skill := skillDefinition{
		name:            "uloop-sample",
		content:         []byte("---\nname: uloop-sample\n---\n\n# sample\n"),
		sourceDirectory: sourceDir,
	}

	stdout := &bytes.Buffer{}
	stderr := &bytes.Buffer{}
	options := skillCommandOptions{targets: []skillTarget{targetConfigs["claude"]}}
	code := runSkillsInstall(projectRoot, []skillDefinition{skill}, options, stdout, stderr)

	if code != 0 {
		t.Fatalf("install should succeed: code=%d stderr=%s", code, stderr.String())
	}
	if _, err := os.Stat(filepath.Join(projectRoot, ".codex")); !os.IsNotExist(err) {
		t.Fatalf(".codex should not be created without a prior install: %v", err)
	}
}

// Tests that the dedicated V3 migration install copies only the temporary skill.
func TestRunV3MigrationSkillInstallAndUninstall(t *testing.T) {
	projectRoot := t.TempDir()
	writePackageRootMarker(t, projectRoot)
	writeTestSkill(t, projectRoot, "Packages/src/TemporarySkills/v3-cli-invocation-migration/Skill", `---
name: v3-cli-invocation-migration
---

# temporary migration
`)
	if err := os.MkdirAll(
		filepath.Join(projectRoot, "Packages", "src", "TemporarySkills", "v3-cli-invocation-migration", "Skill", "scripts"),
		0o755); err != nil {
		t.Fatalf("failed to create script dir: %v", err)
	}
	if err := os.WriteFile(
		filepath.Join(projectRoot, "Packages", "src", "TemporarySkills", "v3-cli-invocation-migration", "Skill", "scripts", "detect.sh"),
		[]byte("#!/bin/sh\n"),
		0o644); err != nil {
		t.Fatalf("failed to write script: %v", err)
	}
	skills, err := collectV3MigrationSkillDefinition(projectRoot)
	if err != nil {
		t.Fatalf("collectV3MigrationSkillDefinition failed: %v", err)
	}
	stdout := &bytes.Buffer{}
	stderr := &bytes.Buffer{}
	options := skillCommandOptions{targets: []skillTarget{targetConfigs["codex"]}}

	installCode := runV3MigrationSkillInstall(projectRoot, skills, options, stdout, stderr)

	if installCode != 0 {
		t.Fatalf("install should succeed: code=%d stderr=%s", installCode, stderr.String())
	}
	installedDir := filepath.Join(projectRoot, ".codex", "skills", "v3-cli-invocation-migration")
	if _, err := os.Stat(filepath.Join(installedDir, "SKILL.md")); err != nil {
		t.Fatalf("migration skill should be installed: %v", err)
	}
	if _, err := os.Stat(filepath.Join(installedDir, "scripts", "detect.sh")); err != nil {
		t.Fatalf("migration skill script should be installed: %v", err)
	}
	unrelatedSkillDir := filepath.Join(projectRoot, ".codex", "skills", "uloop-compile")
	writeSkillFile(t, unrelatedSkillDir, "---\nname: uloop-compile\n---\n")

	uninstallCode := runV3MigrationSkillUninstall(projectRoot, skills, options, stdout, stderr)

	if uninstallCode != 0 {
		t.Fatalf("uninstall should succeed: code=%d stderr=%s", uninstallCode, stderr.String())
	}
	if _, err := os.Stat(installedDir); !os.IsNotExist(err) {
		t.Fatalf("migration skill should be removed: %v", err)
	}
	if _, err := os.Stat(unrelatedSkillDir); err != nil {
		t.Fatalf("unrelated skill should remain: %v", err)
	}
}

// Tests that the POSIX detector reports boolean, first-party, and output-field candidates.
func TestV3MigrationSkillPosixDetectorReportsCandidates(t *testing.T) {
	fixtureRoot := t.TempDir()
	fixtureFile := filepath.Join(fixtureRoot, "docs", "SKILL.md")
	if err := os.MkdirAll(filepath.Dir(fixtureFile), 0o755); err != nil {
		t.Fatalf("failed to create fixture dir: %v", err)
	}
	fixtureContent := strings.Join([]string{
		"uloop compile --wait-for-domain-reload true",
		"uloop some-tool --enabled false",
		"jq '.Success'",
	}, "\n")
	if err := os.WriteFile(fixtureFile, []byte(fixtureContent), 0o644); err != nil {
		t.Fatalf("failed to write fixture: %v", err)
	}
	scriptPath := filepath.Join(
		findRepositoryRootForSkillsTest(t),
		"Packages",
		"src",
		"TemporarySkills",
		"v3-cli-invocation-migration",
		"Skill",
		"scripts",
		"detect-v3-cli-invocation-candidates.sh")

	command := exec.Command("sh", scriptPath, fixtureRoot)
	output, err := command.CombinedOutput()
	if err != nil {
		t.Fatalf("detector failed: %v\n%s", err, string(output))
	}
	text := string(output)
	for _, expected := range []string{"FIRST_PARTY_OPTION", "ARG_BOOL", "OUTPUT_FIELD"} {
		if !strings.Contains(text, expected) {
			t.Fatalf("detector output missing %s:\n%s", expected, text)
		}
	}
}

// Tests that the PowerShell detector reports candidates when PowerShell is available.
func TestV3MigrationSkillPowerShellDetectorReportsCandidates(t *testing.T) {
	pwshPath, err := exec.LookPath("pwsh")
	if err != nil {
		t.Skip("pwsh is not installed")
	}
	fixtureRoot := t.TempDir()
	fixtureFile := filepath.Join(fixtureRoot, "scripts", "migrate.ps1")
	if err := os.MkdirAll(filepath.Dir(fixtureFile), 0o755); err != nil {
		t.Fatalf("failed to create fixture dir: %v", err)
	}
	fixtureContent := strings.Join([]string{
		"uloop run-tests --save-before-run false",
		"uloop some-tool --enabled true",
		"$result.Success",
	}, "\n")
	if err := os.WriteFile(fixtureFile, []byte(fixtureContent), 0o644); err != nil {
		t.Fatalf("failed to write fixture: %v", err)
	}
	scriptPath := filepath.Join(
		findRepositoryRootForSkillsTest(t),
		"Packages",
		"src",
		"TemporarySkills",
		"v3-cli-invocation-migration",
		"Skill",
		"scripts",
		"detect-v3-cli-invocation-candidates.ps1")

	command := exec.Command(pwshPath, "-NoProfile", "-File", scriptPath, "-Root", fixtureRoot)
	output, err := command.CombinedOutput()
	if err != nil {
		t.Fatalf("detector failed: %v\n%s", err, string(output))
	}
	text := string(output)
	for _, expected := range []string{"FIRST_PARTY_OPTION", "ARG_BOOL", "OUTPUT_FIELD"} {
		if !strings.Contains(text, expected) {
			t.Fatalf("detector output missing %s:\n%s", expected, text)
		}
	}
}

func writeTestSkill(t *testing.T, projectRoot string, relativeDir string, content string) {
	t.Helper()
	writeSkillFile(t, filepath.Join(projectRoot, filepath.FromSlash(relativeDir)), content)
}

func writeManifest(t *testing.T, projectRoot string, content string) {
	t.Helper()
	manifestDir := filepath.Join(projectRoot, "Packages")
	if err := os.MkdirAll(manifestDir, 0o755); err != nil {
		t.Fatalf("failed to create manifest dir: %v", err)
	}
	if err := os.WriteFile(filepath.Join(manifestDir, "manifest.json"), []byte(content), 0o644); err != nil {
		t.Fatalf("failed to write manifest: %v", err)
	}
}

func writePackageRootMarker(t *testing.T, projectRoot string) {
	t.Helper()
	markerPath := filepath.Join(projectRoot, "Packages", "src", "Editor", "FirstPartyTools")
	if err := os.MkdirAll(markerPath, 0o755); err != nil {
		t.Fatalf("failed to create package root marker: %v", err)
	}
}

func writeToolSettings(t *testing.T, projectRoot string, content string) {
	t.Helper()
	settingsDir := filepath.Join(projectRoot, ".uloop")
	if err := os.MkdirAll(settingsDir, 0o755); err != nil {
		t.Fatalf("failed to create settings dir: %v", err)
	}
	if err := os.WriteFile(filepath.Join(settingsDir, "settings.tools.json"), []byte(content), 0o644); err != nil {
		t.Fatalf("failed to write tool settings: %v", err)
	}
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

func writeRawSkillFile(t *testing.T, skillDir string, content string) {
	t.Helper()
	if err := os.MkdirAll(skillDir, 0o755); err != nil {
		t.Fatalf("failed to create skill dir: %v", err)
	}
	if err := os.WriteFile(filepath.Join(skillDir, "SKILL.md"), []byte(content), 0o644); err != nil {
		t.Fatalf("failed to write raw skill file: %v", err)
	}
}

func utf16LittleEndianWithBOM(content string) []byte {
	bytes := []byte{0xff, 0xfe}
	for _, char := range content {
		bytes = append(bytes, byte(char), byte(char>>8))
	}
	return bytes
}

func skillNames(skills []skillDefinition) []string {
	names := make([]string, 0, len(skills))
	for _, skill := range skills {
		names = append(names, skill.name)
	}
	sort.Strings(names)
	return names
}

func assertSkillContentContains(t *testing.T, skills []skillDefinition, skillName string, expectedContent string) {
	t.Helper()
	for _, skill := range skills {
		if skill.name != skillName {
			continue
		}
		if !strings.Contains(string(skill.content), expectedContent) {
			t.Fatalf("skill %s content mismatch: %s", skillName, string(skill.content))
		}
		return
	}
	t.Fatalf("skill not found: %s", skillName)
}

func findRepositoryRootForSkillsTest(t *testing.T) string {
	t.Helper()
	currentDir, err := os.Getwd()
	if err != nil {
		t.Fatalf("failed to get working directory: %v", err)
	}
	for {
		if _, err := os.Stat(filepath.Join(currentDir, ".git")); err == nil {
			return currentDir
		}
		parentDir := filepath.Dir(currentDir)
		if parentDir == currentDir {
			t.Fatal("repository root not found")
		}
		currentDir = parentDir
	}
}
