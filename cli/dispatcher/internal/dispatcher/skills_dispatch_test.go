package dispatcher

import (
	"bytes"
	"errors"
	"os"
	"path/filepath"
	"runtime"
	"strings"
	"testing"
)

const sampleSkillContent = "---\nname: uloop-sample\n---\n\n# sample\n"

// createSkillsTestProject creates a Unity project with one discoverable project skill.
func createSkillsTestProject(t *testing.T) string {
	t.Helper()
	projectRoot := createDispatcherUnityProject(t)
	writeTestSkill(t, projectRoot, "Assets/Editor/SampleTool/Skill", sampleSkillContent)
	return projectRoot
}

func runSkillsRequestForTest(t *testing.T, startPath string, args ...string) (int, string, string) {
	t.Helper()
	var stdout bytes.Buffer
	var stderr bytes.Buffer
	handled, code := tryHandleSkillsRequest(append([]string{"skills"}, args...), startPath, "", &stdout, &stderr)
	if !handled {
		t.Fatalf("skills request %v was not handled", args)
	}
	return code, stdout.String(), stderr.String()
}

func stubSkillsUserHomeDir(t *testing.T, homeDir string, err error) {
	t.Helper()
	previous := userHomeDir
	t.Cleanup(func() {
		userHomeDir = previous
	})
	userHomeDir = func() (string, error) {
		return homeDir, err
	}
	// A regression that bypasses userHomeDir must land in a throwaway home, never the real one.
	isolatedHome := t.TempDir()
	t.Setenv("HOME", isolatedHome)
	t.Setenv("USERPROFILE", isolatedHome)
}

func TestTryHandleSkillsRequestInstallListUninstallRoundTrip(t *testing.T) {
	// Verifies install, list, and uninstall through the public request entry point act on the project's skill.
	projectRoot := createSkillsTestProject(t)
	installedSkillFile := filepath.Join(projectRoot, ".claude", "skills", "uloop-sample", "SKILL.md")

	code, stdout, stderr := runSkillsRequestForTest(t, projectRoot, "install", "--claude")
	if code != 0 || !strings.Contains(stdout, "Installed: 1") {
		t.Fatalf("install failed: code=%d stdout=%s stderr=%s", code, stdout, stderr)
	}
	assertFileContent(t, installedSkillFile, sampleSkillContent)

	code, stdout, stderr = runSkillsRequestForTest(t, projectRoot, "list", "--claude")
	if code != 0 || !strings.Contains(stdout, "uloop-sample (installed)") || !strings.Contains(stdout, "Claude Code (Project)") {
		t.Fatalf("list failed: code=%d stdout=%s stderr=%s", code, stdout, stderr)
	}

	code, stdout, stderr = runSkillsRequestForTest(t, projectRoot, "uninstall", "--claude")
	if code != 0 || !strings.Contains(stdout, "Removed: 1") {
		t.Fatalf("uninstall failed: code=%d stdout=%s stderr=%s", code, stdout, stderr)
	}
	if fileExists(installedSkillFile) {
		t.Fatal("uninstall must remove the installed skill")
	}
}

func TestTryHandleSkillsRequestPrintsTargetGuidanceWithoutTargets(t *testing.T) {
	// Verifies install and uninstall without target flags only print guidance and change nothing when no target holds a uloop skill yet.
	projectRoot := createSkillsTestProject(t)
	for _, subcommand := range []string{"install", "uninstall"} {
		code, stdout, stderr := runSkillsRequestForTest(t, projectRoot, subcommand)
		if code != 0 || !strings.Contains(stdout, "--claude") {
			t.Fatalf("%s guidance mismatch: code=%d stdout=%s stderr=%s", subcommand, code, stdout, stderr)
		}
	}
	if fileExists(filepath.Join(projectRoot, ".claude")) {
		t.Fatal("guidance must not create any skill directory")
	}
}

func TestRunSkillsSubcommandInstallWithoutTargetRefreshesDetectedInstall(t *testing.T) {
	// Verifies install without a target flag refreshes a target that already holds a uloop skill instead of printing guidance.
	root := t.TempDir()
	skill := writeDirModeSkillSource(t, root, "uloop-sample")
	skills := []skillDefinition{skill}
	claudeOptions := skillCommandOptions{targets: []skillTarget{targetConfigs["claude"]}}
	var setupStderr bytes.Buffer
	if code := runSkillsSubcommand("install", root, skills, claudeOptions, &bytes.Buffer{}, &setupStderr); code != 0 {
		t.Fatalf("initial install failed: code=%d stderr=%s", code, setupStderr.String())
	}
	baseDir, err := getSkillsBaseDir(root, targetConfigs["claude"], claudeOptions.global)
	if err != nil {
		t.Fatalf("failed to resolve the skills base dir: %v", err)
	}
	installedSkillFile := filepath.Join(getPreferredSkillDir(baseDir, skill.name, groupManagedSkillsForOptions(claudeOptions)), "SKILL.md")
	writeDispatcherTestFile(t, installedSkillFile, "stale")
	sourceContent, err := os.ReadFile(filepath.Join(skill.sourceDirectory, "SKILL.md"))
	if err != nil {
		t.Fatalf("failed to read the skill source: %v", err)
	}
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := runSkillsSubcommand("install", root, skills, skillCommandOptions{}, &stdout, &stderr)

	if code != 0 {
		t.Fatalf("install without a target failed: code=%d stdout=%s stderr=%s", code, stdout.String(), stderr.String())
	}
	if !strings.Contains(stdout.String(), "Auto-refreshing") || strings.Contains(stdout.String(), "Please specify at least one target") {
		t.Fatalf("install without a target must refresh the detected install instead of printing guidance:\n%s", stdout.String())
	}
	assertFileContent(t, installedSkillFile, string(sourceContent))
}

func TestRunSkillsSubcommandInstallWithoutTargetAndNoInstallPrintsGuidance(t *testing.T) {
	// Verifies install without a target flag still prints guidance and writes no skill when no target holds a uloop skill.
	root := t.TempDir()
	skill := writeDirModeSkillSource(t, root, "uloop-sample")
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := runSkillsSubcommand("install", root, []skillDefinition{skill}, skillCommandOptions{}, &stdout, &stderr)

	if code != 0 {
		t.Fatalf("install without a target failed: code=%d stdout=%s stderr=%s", code, stdout.String(), stderr.String())
	}
	if !strings.Contains(stdout.String(), "Please specify at least one target for 'install'") || strings.Contains(stdout.String(), "Auto-refreshing") {
		t.Fatalf("install without a target must only print guidance when nothing is installed:\n%s", stdout.String())
	}
	walkErr := filepath.WalkDir(root, func(path string, entry os.DirEntry, err error) error {
		if err != nil {
			return err
		}
		if entry.IsDir() && path == skill.sourceDirectory {
			return filepath.SkipDir
		}
		if !entry.IsDir() && entry.Name() == "SKILL.md" {
			t.Errorf("install without a target must not write a skill file: %s", path)
		}
		return nil
	})
	if walkErr != nil {
		t.Fatalf("failed to walk the project root: %v", walkErr)
	}
}

func TestRunSkillsSubcommandInstallWithoutTargetReportsDetectionErrors(t *testing.T) {
	// Verifies install without a target flag fails with code 1 instead of printing guidance when installed targets cannot be detected.
	stubSkillsUserHomeDir(t, "", errors.New("home unavailable"))
	skill := skillDefinition{name: "uloop-sample", content: []byte(sampleSkillContent)}
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := runSkillsSubcommand("install", t.TempDir(), []skillDefinition{skill}, skillCommandOptions{global: true}, &stdout, &stderr)

	if code != 1 || !strings.Contains(stderr.String(), "home unavailable") || strings.Contains(stdout.String(), "Please specify at least one target") {
		t.Fatalf("expected the detection error: code=%d stdout=%s stderr=%s", code, stdout.String(), stderr.String())
	}
}

func TestTryHandleSkillsRequestInstallsIntoOutputDir(t *testing.T) {
	// Verifies --output-dir routes the request to dir mode and installs and removes the skill there.
	projectRoot := createSkillsTestProject(t)
	outputDir := filepath.Join(t.TempDir(), "skills")

	code, stdout, stderr := runSkillsRequestForTest(t, projectRoot, "install", "--output-dir", outputDir)
	if code != 0 {
		t.Fatalf("dir install failed: code=%d stdout=%s stderr=%s", code, stdout, stderr)
	}
	assertFileContent(t, filepath.Join(outputDir, "uloop-sample", "SKILL.md"), sampleSkillContent)

	code, stdout, stderr = runSkillsRequestForTest(t, projectRoot, "uninstall", "--output-dir", outputDir)
	if code != 0 {
		t.Fatalf("dir uninstall failed: code=%d stdout=%s stderr=%s", code, stdout, stderr)
	}
	if fileExists(filepath.Join(outputDir, "uloop-sample")) {
		t.Fatal("dir uninstall must remove the installed skill")
	}
}

func TestTryHandleSkillsRequestListsGlobalTargets(t *testing.T) {
	// Verifies --global lists skills under the home directory and labels the location Global, even inside a project.
	projectRoot := createSkillsTestProject(t)
	homeDir := t.TempDir()
	stubSkillsUserHomeDir(t, homeDir, nil)

	code, stdout, stderr := runSkillsRequestForTest(t, projectRoot, "list", "--global", "--claude")

	if code != 0 || !strings.Contains(stdout, "Claude Code (Global)") || !strings.Contains(stdout, filepath.Join(homeDir, ".claude", "skills")) {
		t.Fatalf("global list mismatch: code=%d stdout=%s stderr=%s", code, stdout, stderr)
	}
}

func TestTryHandleSkillsRequestReportsArgumentAndProjectErrors(t *testing.T) {
	// Verifies option errors and a missing Unity project exit with code 1 before any skill is touched.
	projectRoot := createSkillsTestProject(t)
	cases := []struct {
		name        string
		startPath   string
		args        []string
		wantMessage string
	}{
		{name: "unknown option", startPath: projectRoot, args: []string{"install", "--bogus"}, wantMessage: "--bogus"},
		{name: "no project", startPath: t.TempDir(), args: []string{"list"}, wantMessage: "Unity project"},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			code, stdout, stderr := runSkillsRequestForTest(t, testCase.startPath, testCase.args...)
			if code != 1 || !strings.Contains(stderr, testCase.wantMessage) {
				t.Fatalf("expected code 1 with %q: code=%d stdout=%s stderr=%s", testCase.wantMessage, code, stdout, stderr)
			}
		})
	}
}

func TestTryHandleSkillsRequestUsesExplicitProjectPath(t *testing.T) {
	// Verifies a global --project-path selects the project even when the start path is outside it.
	projectRoot := createSkillsTestProject(t)
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	handled, code := tryHandleSkillsRequest([]string{"skills", "list", "--claude"}, t.TempDir(), projectRoot, &stdout, &stderr)

	if !handled || code != 0 || !strings.Contains(stdout.String(), "uloop-sample (not installed)") {
		t.Fatalf("explicit project list mismatch: code=%d stdout=%s stderr=%s", code, stdout.String(), stderr.String())
	}
}

func TestTryHandleSkillsRequestInstallsV3MigrationSkill(t *testing.T) {
	// Verifies install-v3-migration installs the temporary migration skill from the package root.
	projectRoot := createDispatcherUnityProject(t)
	writeV3MigrationSkillFixture(t, projectRoot, "---\nname: v3-cli-invocation-migration\n---\n")

	code, stdout, stderr := runSkillsRequestForTest(t, projectRoot, "install-v3-migration", "--claude")

	if code != 0 {
		t.Fatalf("migration install failed: code=%d stdout=%s stderr=%s", code, stdout, stderr)
	}
	if !fileExists(filepath.Join(projectRoot, ".claude", "skills", v3MigrationSkillName, "SKILL.md")) {
		t.Fatal("migration skill must be installed")
	}
}

func TestTryHandleSkillsRequestReportsMissingV3MigrationSource(t *testing.T) {
	// Verifies install-v3-migration fails with code 1 when the package root holding the migration skill is missing.
	projectRoot := createDispatcherUnityProject(t)

	code, stdout, stderr := runSkillsRequestForTest(t, projectRoot, "install-v3-migration", "--claude")

	if code != 1 || !strings.Contains(stderr, "package root was not found") {
		t.Fatalf("expected a missing package root error: code=%d stdout=%s stderr=%s", code, stdout, stderr)
	}
}

func TestRunSkillsSubcommandRejectsUnroutedSubcommands(t *testing.T) {
	// Verifies subcommands that bypassed routing fail with code 1 without running any handler.
	options := skillCommandOptions{targets: []skillTarget{targetConfigs["claude"]}}
	projectRoot := t.TempDir()
	cases := []struct {
		name       string
		run        func(stdout *bytes.Buffer, stderr *bytes.Buffer) int
		wantStderr string
	}{
		{name: "project mode", run: func(stdout *bytes.Buffer, stderr *bytes.Buffer) int {
			return runSkillsSubcommand("bogus", projectRoot, nil, options, stdout, stderr)
		}},
		{name: "v3 migration", run: func(stdout *bytes.Buffer, stderr *bytes.Buffer) int {
			return runV3MigrationSkillsSubcommand("bogus", projectRoot, options, stdout, stderr)
		}},
		{name: "dir mode", wantStderr: "The bogus subcommand does not support", run: func(stdout *bytes.Buffer, stderr *bytes.Buffer) int {
			return runSkillsDirSubcommand("bogus", projectRoot, nil, t.TempDir(), stdout, stderr)
		}},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			var stdout bytes.Buffer
			var stderr bytes.Buffer
			code := testCase.run(&stdout, &stderr)
			if code != 1 || stdout.Len() != 0 {
				t.Fatalf("expected code 1 with no output: code=%d stdout=%s", code, stdout.String())
			}
			if !strings.Contains(stderr.String(), testCase.wantStderr) {
				t.Fatalf("stderr mismatch: want %q got %s", testCase.wantStderr, stderr.String())
			}
		})
	}
}

func TestRunSkillsDirSubcommandReportsUnreadableOutputDir(t *testing.T) {
	// Verifies an output directory below a regular file is reported instead of being treated as absent.
	if runtime.GOOS == "windows" {
		t.Skip("Windows reports a path below a file as not found rather than ENOTDIR.")
	}
	parentFile := filepath.Join(t.TempDir(), "file")
	writeDispatcherTestFile(t, parentFile, "x")
	var stderr bytes.Buffer

	code := runSkillsDirSubcommand("list", t.TempDir(), nil, filepath.Join(parentFile, "skills"), &bytes.Buffer{}, &stderr)

	if code != 1 || !strings.Contains(stderr.String(), "stat "+filepath.Join(parentFile, "skills")+": not a directory") {
		t.Fatalf("expected an error: code=%d stderr=%s", code, stderr.String())
	}
}

func TestPathContainsRejectsUnrelatablePaths(t *testing.T) {
	// Verifies paths that cannot be made relative to each other are not reported as contained.
	if pathContains("relative", string(filepath.Separator)+"absolute") {
		t.Fatal("a relative parent cannot contain an absolute child")
	}
}

func TestRunSkillsListReportsBaseDirAndStatusErrors(t *testing.T) {
	// Verifies list fails with code 1 when the global home cannot be resolved or a skill status cannot be read.
	skill := skillDefinition{name: "uloop-sample", content: []byte(sampleSkillContent)}
	t.Run("home lookup", func(t *testing.T) {
		stubSkillsUserHomeDir(t, "", errors.New("home unavailable"))
		var stderr bytes.Buffer
		code := runSkillsList(t.TempDir(), []skillDefinition{skill}, skillCommandOptions{global: true, targets: []skillTarget{targetConfigs["claude"]}}, &bytes.Buffer{}, &stderr)
		if code != 1 || !strings.Contains(stderr.String(), "home unavailable") {
			t.Fatalf("expected the home error: code=%d stderr=%s", code, stderr.String())
		}
	})
	t.Run("status stat", func(t *testing.T) {
		projectRoot := t.TempDir()
		blockSkillsDirWithFile(t, projectRoot, ".claude")
		var stderr bytes.Buffer
		code := runSkillsList(projectRoot, []skillDefinition{skill}, skillCommandOptions{targets: []skillTarget{targetConfigs["claude"]}}, &bytes.Buffer{}, &stderr)
		if code != 1 || !strings.Contains(stderr.String(), filepath.Join(".claude", "skills", "uloop-sample", "SKILL.md")+": not a directory") {
			t.Fatalf("expected a status error: code=%d stderr=%s", code, stderr.String())
		}
	})
}

// blockSkillsDirWithFile replaces a target directory with a regular file so every
// lookup below it fails with ENOTDIR instead of "not exist".
func blockSkillsDirWithFile(t *testing.T, projectRoot string, targetDir string) {
	t.Helper()
	if runtime.GOOS == "windows" {
		t.Skip("Windows reports a path below a file as not found rather than ENOTDIR.")
	}
	writeDispatcherTestFile(t, filepath.Join(projectRoot, targetDir), "not a directory")
}

func TestRunSkillsInstallReportsTargetErrors(t *testing.T) {
	// Verifies install fails with code 1 when auto-refresh detection or the requested target cannot be read.
	skill := skillDefinition{name: "uloop-sample", content: []byte(sampleSkillContent)}
	cases := []struct {
		name        string
		blocked     string
		global      bool
		wantMessage string
	}{
		{name: "global home lookup", global: true, wantMessage: "home unavailable"},
		{name: "unrequested target unreadable", blocked: ".codex", wantMessage: filepath.Join(".codex", "skills", "uloop-sample", "SKILL.md") + ": not a directory"},
		{name: "requested target unreadable", blocked: ".claude", wantMessage: filepath.Join(".claude", "skills", "unity-cli-loop") + string(filepath.Separator)},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			projectRoot := t.TempDir()
			if testCase.global {
				stubSkillsUserHomeDir(t, "", errors.New("home unavailable"))
			}
			if testCase.blocked != "" {
				blockSkillsDirWithFile(t, projectRoot, testCase.blocked)
			}
			var stderr bytes.Buffer

			code := runSkillsInstall(projectRoot, []skillDefinition{skill}, skillCommandOptions{global: testCase.global, targets: []skillTarget{targetConfigs["claude"]}}, &bytes.Buffer{}, &stderr)

			if code != 1 || !strings.Contains(stderr.String(), testCase.wantMessage) {
				t.Fatalf("expected %q: code=%d stderr=%s", testCase.wantMessage, code, stderr.String())
			}
		})
	}
}

func TestRunSkillsUninstallReportsTargetErrors(t *testing.T) {
	// Verifies uninstall fails with code 1 when the requested target cannot be read.
	projectRoot := t.TempDir()
	blockSkillsDirWithFile(t, projectRoot, ".claude")
	skill := skillDefinition{name: "uloop-sample"}
	var stderr bytes.Buffer

	code := runSkillsUninstall(projectRoot, []skillDefinition{skill}, skillCommandOptions{targets: []skillTarget{targetConfigs["claude"]}}, &bytes.Buffer{}, &stderr)

	// The deprecated-skill cleanup runs before the per-skill lookup, so its path is the one reported.
	wantMessage := "stat " + filepath.Join(projectRoot, ".claude", "skills", deprecatedSkillNames[0]) + ": not a directory"
	if code != 1 || !strings.Contains(stderr.String(), wantMessage) {
		t.Fatalf("expected an error: code=%d stderr=%s", code, stderr.String())
	}
}

func TestRunSkillsInstallReportsDeprecatedRemovals(t *testing.T) {
	// Verifies install removes retired skill directories and reports how many it removed.
	projectRoot := t.TempDir()
	sourceDir := filepath.Join(projectRoot, "source", "Skill")
	writeSkillFile(t, sourceDir, sampleSkillContent)
	deprecatedDir := filepath.Join(projectRoot, ".claude", "skills", deprecatedSkillNames[0])
	writeSkillFile(t, deprecatedDir, "---\nname: retired\n---\n")
	skill := skillDefinition{name: "uloop-sample", content: []byte(sampleSkillContent), sourceDirectory: sourceDir}
	var stdout bytes.Buffer

	code := runSkillsInstall(projectRoot, []skillDefinition{skill}, skillCommandOptions{targets: []skillTarget{targetConfigs["claude"]}}, &stdout, &bytes.Buffer{})

	if code != 0 || !strings.Contains(stdout.String(), "Deprecated removed: 1") {
		t.Fatalf("expected a deprecated removal: code=%d stdout=%s", code, stdout.String())
	}
	if _, err := os.Stat(deprecatedDir); !os.IsNotExist(err) {
		t.Fatalf("deprecated skill must be removed: %v", err)
	}
}
