package dispatcher

import (
	"bytes"
	"os"
	"path/filepath"
	"strings"
	"testing"
)

// Tests that both value forms of --root are accepted.
func TestParseSkillsOptionsAcceptsRootValues(t *testing.T) {
	separate, err := parseSkillsOptions([]string{"--claude", "--root", "git"})
	if err != nil {
		t.Fatalf("--root git should parse: %v", err)
	}
	if separate.root != skillsInstallRootGit {
		t.Fatalf("root = %q, want git", separate.root)
	}

	joined, err := parseSkillsOptions([]string{"--root=unity", "--claude"})
	if err != nil {
		t.Fatalf("--root=unity should parse: %v", err)
	}
	if joined.root != skillsInstallRootUnity {
		t.Fatalf("root = %q, want unity", joined.root)
	}
}

// Tests that --root rejects values other than unity and git, including other casings.
func TestParseSkillsOptionsRejectsUnknownRootValue(t *testing.T) {
	for _, args := range [][]string{
		{"--root", "repo"},
		{"--root=Git"},
		{"--root="},
	} {
		if _, err := parseSkillsOptions(args); err == nil {
			t.Fatalf("%v should be rejected", args)
		}
	}
}

// Tests that --root without a value, or followed by another flag, is rejected instead of swallowing the flag.
func TestParseSkillsOptionsRejectsMissingRootValue(t *testing.T) {
	for _, args := range [][]string{
		{"--root"},
		{"--root", "--claude"},
	} {
		if _, err := parseSkillsOptions(args); err == nil {
			t.Fatalf("%v should be rejected", args)
		}
	}
}

// Tests that passing --root twice is rejected even when both values agree.
func TestParseSkillsOptionsRejectsDuplicateRoot(t *testing.T) {
	if _, err := parseSkillsOptions([]string{"--root", "git", "--root", "git"}); err == nil {
		t.Fatal("duplicate --root should be rejected")
	}
}

// Tests that --root conflicts with --global and with --output-dir, which already name a destination.
func TestParseSkillsOptionsRejectsRootWithOtherDestinations(t *testing.T) {
	if _, err := parseSkillsOptions([]string{"--root", "git", "--global", "--claude"}); err == nil {
		t.Fatal("--root with --global should be rejected")
	}
	if _, err := parseSkillsOptions([]string{"--root", "git", "--output-dir", "store"}); err == nil {
		t.Fatal("--root with --output-dir should be rejected")
	}
}

// Tests that the Git root is the nearest ancestor holding a .git directory, starting at the Unity project root.
func TestFindGitRepositoryRootFindsAncestorWithGitDirectory(t *testing.T) {
	repositoryRoot := t.TempDir()
	mustMkdir(t, filepath.Join(repositoryRoot, ".git"))
	projectRoot := filepath.Join(repositoryRoot, "client", "unity")
	mustMkdir(t, projectRoot)

	found, err := findGitRepositoryRoot(projectRoot)
	if err != nil {
		t.Fatalf("git root should be found: %v", err)
	}
	if found != repositoryRoot {
		t.Fatalf("git root = %q, want %q", found, repositoryRoot)
	}
}

// Tests that a .git file (a worktree or submodule checkout) also marks the Git root.
func TestFindGitRepositoryRootAcceptsGitFile(t *testing.T) {
	repositoryRoot := t.TempDir()
	if err := os.WriteFile(filepath.Join(repositoryRoot, ".git"), []byte("gitdir: elsewhere\n"), 0o644); err != nil {
		t.Fatalf("failed to write .git file: %v", err)
	}
	projectRoot := filepath.Join(repositoryRoot, "unity")
	mustMkdir(t, projectRoot)

	found, err := findGitRepositoryRoot(projectRoot)
	if err != nil {
		t.Fatalf("git root should be found: %v", err)
	}
	if found != repositoryRoot {
		t.Fatalf("git root = %q, want %q", found, repositoryRoot)
	}
}

// Tests that a Unity project that is itself the repository root resolves to itself.
func TestFindGitRepositoryRootReturnsProjectRootWhenItHoldsGit(t *testing.T) {
	projectRoot := t.TempDir()
	mustMkdir(t, filepath.Join(projectRoot, ".git"))

	found, err := findGitRepositoryRoot(projectRoot)
	if err != nil {
		t.Fatalf("git root should be found: %v", err)
	}
	if found != projectRoot {
		t.Fatalf("git root = %q, want %q", found, projectRoot)
	}
}

// Tests that a project outside any Git repository reports an error instead of falling back to the Unity root.
func TestFindGitRepositoryRootFailsOutsideRepository(t *testing.T) {
	projectRoot := filepath.Join(t.TempDir(), "unity")
	mustMkdir(t, projectRoot)

	if _, err := findGitRepositoryRoot(projectRoot); err == nil {
		t.Fatal("a project outside any repository should be rejected")
	}
}

// Tests that a project without a saved skills setting installs at the Unity project root.
func TestLoadSavedSkillsInstallRootDefaultsToUnity(t *testing.T) {
	root, err := loadSavedSkillsInstallRoot(t.TempDir())
	if err != nil {
		t.Fatalf("missing settings should not fail: %v", err)
	}
	if root != skillsInstallRootUnity {
		t.Fatalf("root = %q, want unity", root)
	}
}

// Tests that the install root saved by the Editor is read back.
func TestLoadSavedSkillsInstallRootReadsSavedValue(t *testing.T) {
	projectRoot := t.TempDir()
	writeSkillSettings(t, projectRoot, `{"installRoot":"git"}`)

	root, err := loadSavedSkillsInstallRoot(projectRoot)
	if err != nil {
		t.Fatalf("saved settings should load: %v", err)
	}
	if root != skillsInstallRootGit {
		t.Fatalf("root = %q, want git", root)
	}
}

// Tests that an unreadable or unknown saved install root fails instead of silently installing at the Unity root.
func TestLoadSavedSkillsInstallRootRejectsInvalidSettings(t *testing.T) {
	for _, content := range []string{`{"installRoot":"repo"}`, `{not json`} {
		projectRoot := t.TempDir()
		writeSkillSettings(t, projectRoot, content)

		if _, err := loadSavedSkillsInstallRoot(projectRoot); err == nil {
			t.Fatalf("settings %q should be rejected", content)
		}
	}
}

// Tests that install with --root git writes into the Git root while tool settings still come from the Unity project.
func TestTryHandleSkillsRequestInstallsAtGitRoot(t *testing.T) {
	repositoryRoot, projectRoot := setUpNestedUnityProjectWithSkills(t)

	stdout := &bytes.Buffer{}
	stderr := &bytes.Buffer{}
	handled, code := tryHandleSkillsRequest(
		[]string{"skills", "install", "--claude", "--root", "git"},
		"",
		projectRoot,
		stdout,
		stderr)

	if !handled || code != 0 {
		t.Fatalf("install should succeed: handled=%v code=%d stderr=%s", handled, code, stderr.String())
	}
	assertSkillInstalledAt(t, repositoryRoot, "uloop-enabled-skill")
	assertSkillAbsentAt(t, repositoryRoot, "uloop-disabled-skill")
	assertSkillAbsentAt(t, projectRoot, "uloop-enabled-skill")
	if !strings.Contains(stdout.String(), filepath.Join(repositoryRoot, ".claude", "skills")) {
		t.Fatalf("output should report the Git root location:\n%s", stdout.String())
	}
}

// Tests that install without --root follows the install root saved by the Editor.
func TestTryHandleSkillsRequestFollowsSavedInstallRoot(t *testing.T) {
	repositoryRoot, projectRoot := setUpNestedUnityProjectWithSkills(t)
	writeSkillSettings(t, projectRoot, `{"installRoot":"git"}`)

	stderr := &bytes.Buffer{}
	_, code := tryHandleSkillsRequest(
		[]string{"skills", "install", "--claude"},
		"",
		projectRoot,
		&bytes.Buffer{},
		stderr)

	if code != 0 {
		t.Fatalf("install should succeed: code=%d stderr=%s", code, stderr.String())
	}
	assertSkillInstalledAt(t, repositoryRoot, "uloop-enabled-skill")
	assertSkillAbsentAt(t, projectRoot, "uloop-enabled-skill")
}

// Tests that --root unity overrides a saved Git install root for one invocation.
func TestTryHandleSkillsRequestRootFlagOverridesSavedInstallRoot(t *testing.T) {
	repositoryRoot, projectRoot := setUpNestedUnityProjectWithSkills(t)
	writeSkillSettings(t, projectRoot, `{"installRoot":"git"}`)

	stderr := &bytes.Buffer{}
	_, code := tryHandleSkillsRequest(
		[]string{"skills", "install", "--claude", "--root", "unity"},
		"",
		projectRoot,
		&bytes.Buffer{},
		stderr)

	if code != 0 {
		t.Fatalf("install should succeed: code=%d stderr=%s", code, stderr.String())
	}
	assertSkillInstalledAt(t, projectRoot, "uloop-enabled-skill")
	assertSkillAbsentAt(t, repositoryRoot, "uloop-enabled-skill")
}

// Tests that --root git fails without writing anything when the project is not inside a Git repository.
func TestTryHandleSkillsRequestRejectsGitRootOutsideRepository(t *testing.T) {
	projectRoot := filepath.Join(t.TempDir(), "unity")
	writeUnityProjectWithSkills(t, projectRoot)

	stderr := &bytes.Buffer{}
	_, code := tryHandleSkillsRequest(
		[]string{"skills", "install", "--claude", "--root", "git"},
		"",
		projectRoot,
		&bytes.Buffer{},
		stderr)

	if code != 1 {
		t.Fatalf("install should fail: code=%d", code)
	}
	if !strings.Contains(stderr.String(), "Git repository") {
		t.Fatalf("error should explain the missing Git repository:\n%s", stderr.String())
	}
	assertSkillAbsentAt(t, projectRoot, "uloop-enabled-skill")
}

// Tests that list with --root git reports skills installed at the Git root.
func TestTryHandleSkillsRequestListsGitRootInstall(t *testing.T) {
	repositoryRoot, projectRoot := setUpNestedUnityProjectWithSkills(t)
	_, installCode := tryHandleSkillsRequest(
		[]string{"skills", "install", "--claude", "--root", "git"},
		"",
		projectRoot,
		&bytes.Buffer{},
		&bytes.Buffer{})
	if installCode != 0 {
		t.Fatalf("install should succeed: code=%d", installCode)
	}

	stdout := &bytes.Buffer{}
	stderr := &bytes.Buffer{}
	_, code := tryHandleSkillsRequest(
		[]string{"skills", "list", "--claude", "--root", "git"},
		"",
		projectRoot,
		stdout,
		stderr)

	if code != 0 {
		t.Fatalf("list should succeed: code=%d stderr=%s", code, stderr.String())
	}
	output := stdout.String()
	if !strings.Contains(output, "Claude Code (Git root):") {
		t.Fatalf("list should label the Git root location:\n%s", output)
	}
	if !strings.Contains(output, filepath.Join(repositoryRoot, ".claude", "skills")) {
		t.Fatalf("list should report the Git root location:\n%s", output)
	}
	if !strings.Contains(output, "uloop-enabled-skill (installed)") {
		t.Fatalf("list should report the Git root install:\n%s", output)
	}
}

// Tests that the V3 migration skill honors --root git like the regular skills.
func TestTryHandleSkillsRequestInstallsV3MigrationAtGitRoot(t *testing.T) {
	repositoryRoot := t.TempDir()
	mustMkdir(t, filepath.Join(repositoryRoot, ".git"))
	projectRoot := filepath.Join(repositoryRoot, "unity")
	writeUnityProjectWithSkills(t, projectRoot)
	writeV3MigrationSkillFixture(t, projectRoot, "---\nname: v3-cli-invocation-migration\n---\n")

	stderr := &bytes.Buffer{}
	_, code := tryHandleSkillsRequest(
		[]string{"skills", "install-v3-migration", "--claude", "--root", "git"},
		"",
		projectRoot,
		&bytes.Buffer{},
		stderr)

	if code != 0 {
		t.Fatalf("install-v3-migration should succeed: code=%d stderr=%s", code, stderr.String())
	}
	assertSkillInstalledAt(t, repositoryRoot, v3MigrationSkillName)
	assertSkillAbsentAt(t, projectRoot, v3MigrationSkillName)
}

// setUpNestedUnityProjectWithSkills builds a Git repository whose Unity project
// sits one level below the repository root, with one enabled and one disabled skill.
func setUpNestedUnityProjectWithSkills(t *testing.T) (string, string) {
	t.Helper()
	repositoryRoot := t.TempDir()
	mustMkdir(t, filepath.Join(repositoryRoot, ".git"))
	projectRoot := filepath.Join(repositoryRoot, "unity")
	writeUnityProjectWithSkills(t, projectRoot)
	return repositoryRoot, projectRoot
}

func writeUnityProjectWithSkills(t *testing.T, projectRoot string) {
	t.Helper()
	mustMkdir(t, filepath.Join(projectRoot, "Assets"))
	mustMkdir(t, filepath.Join(projectRoot, "ProjectSettings"))
	writeTestSkill(t, projectRoot, "Assets/Editor/Enabled/Skill", "---\nname: uloop-enabled-skill\n---\n")
	writeTestSkill(t, projectRoot, "Assets/Editor/Disabled/Skill", "---\nname: uloop-disabled-skill\n---\n")
	writeToolSettings(t, projectRoot, `{"disabledTools":["disabled-skill"]}`)
}

func writeSkillSettings(t *testing.T, projectRoot string, content string) {
	t.Helper()
	settingsDir := filepath.Join(projectRoot, ".uloop")
	mustMkdir(t, settingsDir)
	if err := os.WriteFile(filepath.Join(settingsDir, "settings.skills.json"), []byte(content), 0o644); err != nil {
		t.Fatalf("failed to write skill settings: %v", err)
	}
}

func assertSkillInstalledAt(t *testing.T, installRoot string, skillName string) {
	t.Helper()
	skillFile := filepath.Join(installRoot, ".claude", "skills", skillName, "SKILL.md")
	if _, err := os.Stat(skillFile); err != nil {
		t.Fatalf("%s should be installed at %s: %v", skillName, installRoot, err)
	}
}

func assertSkillAbsentAt(t *testing.T, installRoot string, skillName string) {
	t.Helper()
	skillDir := filepath.Join(installRoot, ".claude", "skills", skillName)
	if _, err := os.Stat(skillDir); !os.IsNotExist(err) {
		t.Fatalf("%s should not exist at %s: %v", skillName, installRoot, err)
	}
}

func mustMkdir(t *testing.T, directory string) {
	t.Helper()
	if err := os.MkdirAll(directory, 0o755); err != nil {
		t.Fatalf("failed to create %s: %v", directory, err)
	}
}

// Tests that --output-dir ignores a saved Git install root, since it names its own destination.
func TestTryHandleSkillsRequestOutputDirIgnoresSavedInstallRoot(t *testing.T) {
	projectRoot := filepath.Join(t.TempDir(), "unity")
	writeUnityProjectWithSkills(t, projectRoot)
	writeSkillSettings(t, projectRoot, `{"installRoot":"git"}`)
	storeDir := filepath.Join(t.TempDir(), "store")

	stderr := &bytes.Buffer{}
	_, code := tryHandleSkillsRequest(
		[]string{"skills", "install", "--output-dir", storeDir},
		"",
		projectRoot,
		&bytes.Buffer{},
		stderr)

	if code != 0 {
		t.Fatalf("install should succeed: code=%d stderr=%s", code, stderr.String())
	}
	if _, err := os.Stat(filepath.Join(storeDir, "uloop-enabled-skill", "SKILL.md")); err != nil {
		t.Fatalf("skill should be synced into the store: %v", err)
	}
}

// Tests that subcommand help documents the --root option.
func TestPrintSkillsSubcommandHelpListsRootOption(t *testing.T) {
	stdout := &bytes.Buffer{}
	printSkillsSubcommandHelp("install", stdout)
	if !strings.Contains(stdout.String(), skillsRootFlagName+" <unity|git>") {
		t.Fatalf("help should list --root:\n%s", stdout.String())
	}
}
