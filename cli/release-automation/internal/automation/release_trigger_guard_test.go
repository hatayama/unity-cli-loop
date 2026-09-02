package automation

import (
	"context"
	"os"
	"os/exec"
	"path/filepath"
	"sort"
	"strings"
	"testing"
)

// Verifies changes outside the shared release inputs do not require triggers.
func TestReleaseTriggerGuardPassesWithoutSharedInputChanges(t *testing.T) {
	result := AnalyzeReleaseTriggerGuard([]string{
		"cli/dispatcher/internal/dispatcher/launch.go",
		"cli/project-runner/internal/projectrunner/run.go",
		"scripts/check-go-cli.sh",
	})

	if len(result.Violations) != 0 {
		t.Fatalf("expected no violations, got %v", result.Violations)
	}
}

// Verifies shared common source changes require triggers in both release package roots.
func TestReleaseTriggerGuardRequiresBothTriggersForSharedCommonChanges(t *testing.T) {
	result := AnalyzeReleaseTriggerGuard([]string{"cli/common/clicore/output.go"})

	if len(result.Violations) != 1 {
		t.Fatalf("expected one violation, got %v", result.Violations)
	}
	violation := result.Violations[0]
	if len(violation.ChangedInputs) != 1 || violation.ChangedInputs[0] != "cli/common/clicore/output.go" {
		t.Fatalf("expected the changed common source to be listed, got %v", violation.ChangedInputs)
	}
	expectedRoots := []string{"cli/dispatcher/", "cli/project-runner/"}
	if len(violation.MissingTriggerRoots) != len(expectedRoots) {
		t.Fatalf("expected missing roots %v, got %v", expectedRoots, violation.MissingTriggerRoots)
	}
	for index, expectedRoot := range expectedRoots {
		if violation.MissingTriggerRoots[index] != expectedRoot {
			t.Fatalf("expected missing roots %v, got %v", expectedRoots, violation.MissingTriggerRoots)
		}
	}
}

// Verifies embedded shared common scripts require triggers in both release package roots.
func TestReleaseTriggerGuardRequiresBothTriggersForSharedEmbeddedScripts(t *testing.T) {
	result := AnalyzeReleaseTriggerGuard([]string{"cli/common/unityprocess/focus_unity_process.ps1"})

	if len(result.Violations) != 1 {
		t.Fatalf("expected one violation, got %v", result.Violations)
	}
	violation := result.Violations[0]
	if len(violation.ChangedInputs) != 1 || violation.ChangedInputs[0] != "cli/common/unityprocess/focus_unity_process.ps1" {
		t.Fatalf("expected the changed embedded script to be listed, got %v", violation.ChangedInputs)
	}
	expectedRoots := []string{"cli/dispatcher/", "cli/project-runner/"}
	if len(violation.MissingTriggerRoots) != len(expectedRoots) {
		t.Fatalf("expected missing roots %v, got %v", expectedRoots, violation.MissingTriggerRoots)
	}
	for index, expectedRoot := range expectedRoots {
		if violation.MissingTriggerRoots[index] != expectedRoot {
			t.Fatalf("expected missing roots %v, got %v", expectedRoots, violation.MissingTriggerRoots)
		}
	}
}

// Verifies a partial trigger for shared common changes still fails for the missing package root.
func TestReleaseTriggerGuardDetectsMissingDispatcherTrigger(t *testing.T) {
	result := AnalyzeReleaseTriggerGuard([]string{
		"cli/common/clicore/output.go",
		"cli/project-runner/shared-inputs-stamp.json",
	})

	if len(result.Violations) != 1 {
		t.Fatalf("expected one violation, got %v", result.Violations)
	}
	violation := result.Violations[0]
	if len(violation.MissingTriggerRoots) != 1 || violation.MissingTriggerRoots[0] != "cli/dispatcher/" {
		t.Fatalf("expected only cli/dispatcher/ to be missing, got %v", violation.MissingTriggerRoots)
	}
}

// Verifies shared common changes pass once both release package roots are touched.
func TestReleaseTriggerGuardAcceptsSharedCommonChangesWithBothTriggers(t *testing.T) {
	result := AnalyzeReleaseTriggerGuard([]string{
		"cli/common/clicore/output.go",
		"cli/dispatcher/shared-inputs-stamp.json",
		"cli/project-runner/shared-inputs-stamp.json",
	})

	if len(result.Violations) != 0 {
		t.Fatalf("expected no violations, got %v", result.Violations)
	}
}

// Verifies release-please stamp targets, test-only files, and test helper packages under common are not release inputs.
func TestReleaseTriggerGuardIgnoresNonBinaryCommonChanges(t *testing.T) {
	result := AnalyzeReleaseTriggerGuard([]string{
		"cli/common/clicore/output_test.go",
		"cli/common/clitest/clitest.go",
		"cli/common/clicontract/contract.json",
	})

	if len(result.Violations) != 0 {
		t.Fatalf("expected no violations, got %v", result.Violations)
	}
}

// Verifies structural catalog changes count as a shared release input. Description-only
// regenerations are filtered out before this function by the shape comparison in
// AnalyzeReleaseTriggerGuardForRefs.
func TestReleaseTriggerGuardCoversTheEmbeddedToolCatalog(t *testing.T) {
	result := AnalyzeReleaseTriggerGuard([]string{CatalogRelativePath})

	if len(result.Violations) != 1 {
		t.Fatalf("expected one violation, got %v", result.Violations)
	}
	if len(result.Violations[0].MissingTriggerRoots) != 2 {
		t.Fatalf("expected both release triggers to be required, got %v", result.Violations[0].MissingTriggerRoots)
	}
}

// Verifies a structural catalog change passes once both release triggers are stamped.
func TestReleaseTriggerGuardAcceptsTheEmbeddedToolCatalogWithBothTriggers(t *testing.T) {
	result := AnalyzeReleaseTriggerGuard([]string{
		CatalogRelativePath,
		"cli/dispatcher/shared-inputs-stamp.json",
		"cli/project-runner/shared-inputs-stamp.json",
	})

	if len(result.Violations) != 0 {
		t.Fatalf("expected no violations, got %v", result.Violations)
	}
}

// Verifies common go.mod and go.sum changes count as shared release inputs for both binaries.
func TestReleaseTriggerGuardCoversCommonModuleFiles(t *testing.T) {
	result := AnalyzeReleaseTriggerGuard([]string{"cli/common/go.mod", "cli/common/go.sum"})

	if len(result.Violations) != 1 {
		t.Fatalf("expected one violation, got %v", result.Violations)
	}
	if len(result.Violations[0].ChangedInputs) != 2 {
		t.Fatalf("expected both module files to be listed, got %v", result.Violations[0].ChangedInputs)
	}
}

// Verifies dispatcher-only common package changes require only a dispatcher trigger.
func TestReleaseTriggerGuardRequiresDispatcherTriggerForDispatcherOnlyCommonChanges(t *testing.T) {
	result := AnalyzeReleaseTriggerGuard([]string{"cli/common/version/compare.go"})

	if len(result.Violations) != 1 {
		t.Fatalf("expected one violation, got %v", result.Violations)
	}
	violation := result.Violations[0]
	if len(violation.MissingTriggerRoots) != 1 || violation.MissingTriggerRoots[0] != "cli/dispatcher/" {
		t.Fatalf("expected only cli/dispatcher/ to be missing, got %v", violation.MissingTriggerRoots)
	}
}

// Verifies dispatcher-only common changes pass once the dispatcher package root is touched.
func TestReleaseTriggerGuardAcceptsDispatcherOnlyCommonChangesWithDispatcherTrigger(t *testing.T) {
	result := AnalyzeReleaseTriggerGuard([]string{
		"cli/common/version/compare.go",
		"cli/dispatcher/shared-inputs-stamp.json",
	})

	if len(result.Violations) != 0 {
		t.Fatalf("expected no violations, got %v", result.Violations)
	}
}

// Verifies common package trigger whitelists match the packages imported by release binaries.
func TestReleaseTriggerGuardCommonPackageWhitelistsMatchGoDependencies(t *testing.T) {
	repoRoot, err := gitRepoRoot(context.Background())
	if err != nil {
		t.Fatalf("failed to resolve repository root: %v", err)
	}

	dispatcherCommonRoots := commonPackageRootsImportedByModule(t, filepath.Join(repoRoot, "cli", "dispatcher"))
	projectRunnerCommonRoots := commonPackageRootsImportedByModule(t, filepath.Join(repoRoot, "cli", "project-runner"))

	expectedSharedRoots := intersectSortedStrings(dispatcherCommonRoots, projectRunnerCommonRoots)
	expectedDispatcherOnlyRoots := subtractSortedStrings(dispatcherCommonRoots, projectRunnerCommonRoots)
	projectRunnerOnlyRoots := subtractSortedStrings(projectRunnerCommonRoots, dispatcherCommonRoots)

	assertStringSlicesEqual(t, sharedCommonPackageRoots, expectedSharedRoots, "shared common package roots")
	assertStringSlicesEqual(t, dispatcherOnlyCommonPackageRoots, expectedDispatcherOnlyRoots, "dispatcher-only common package roots")
	if len(projectRunnerOnlyRoots) != 0 {
		t.Fatalf("project-runner-only common package roots need a release trigger rule: %v", projectRunnerOnlyRoots)
	}
}

// Verifies installer script changes require a dispatcher release trigger only.
func TestReleaseTriggerGuardRequiresDispatcherTriggerForInstallerChanges(t *testing.T) {
	result := AnalyzeReleaseTriggerGuard([]string{"scripts/install.sh"})

	if len(result.Violations) != 1 {
		t.Fatalf("expected one violation, got %v", result.Violations)
	}
	violation := result.Violations[0]
	if len(violation.MissingTriggerRoots) != 1 || violation.MissingTriggerRoots[0] != "cli/dispatcher/" {
		t.Fatalf("expected only cli/dispatcher/ to be missing, got %v", violation.MissingTriggerRoots)
	}
}

// Verifies embedded dispatcher script templates are classified as dispatcher inputs.
func TestReleaseTriggerGuardMatchesEmbeddedDispatcherScripts(t *testing.T) {
	for _, file := range []string{
		"cli/dispatcher/internal/install/scripts/install_darwin.sh",
		"cli/dispatcher/internal/install/scripts/install_windows.ps1",
		"cli/dispatcher/internal/uninstall/scripts/uninstall_darwin.sh",
		"cli/dispatcher/internal/uninstall/scripts/uninstall_windows_delete.ps1",
		"cli/dispatcher/internal/uninstall/scripts/uninstall_windows_launch.ps1",
	} {
		if !isDispatcherScriptInput(file) {
			t.Fatalf("expected embedded dispatcher script to match: %s", file)
		}
	}
}

func commonPackageRootsImportedByModule(t *testing.T, moduleDir string) []string {
	t.Helper()
	command := exec.Command("go", "list", "-deps", "./...")
	command.Dir = moduleDir
	output, err := command.Output()
	if err != nil {
		t.Fatalf("go list -deps failed in %s: %v", moduleDir, err)
	}

	rootsByPath := map[string]struct{}{}
	for _, dependency := range strings.Split(strings.TrimSpace(string(output)), "\n") {
		if !strings.HasPrefix(dependency, "github.com/hatayama/unity-cli-loop/common/") {
			continue
		}
		commonPackage := strings.TrimPrefix(dependency, "github.com/hatayama/unity-cli-loop/common/")
		commonRoot, _, _ := strings.Cut(commonPackage, "/")
		rootsByPath["cli/common/"+commonRoot+"/"] = struct{}{}
	}

	roots := []string{}
	for root := range rootsByPath {
		roots = append(roots, root)
	}
	sort.Strings(roots)
	return roots
}

func intersectSortedStrings(left []string, right []string) []string {
	values := []string{}
	for _, leftValue := range left {
		if sortedStringsContain(right, leftValue) {
			values = append(values, leftValue)
		}
	}
	return values
}

func subtractSortedStrings(left []string, right []string) []string {
	values := []string{}
	for _, leftValue := range left {
		if !sortedStringsContain(right, leftValue) {
			values = append(values, leftValue)
		}
	}
	return values
}

func sortedStringsContain(values []string, target string) bool {
	index := sort.SearchStrings(values, target)
	return index < len(values) && values[index] == target
}

func assertStringSlicesEqual(t *testing.T, actual []string, expected []string, label string) {
	t.Helper()
	if len(actual) != len(expected) {
		t.Fatalf("expected %s %v, got %v", label, expected, actual)
	}
	for index, expectedValue := range expected {
		if actual[index] != expectedValue {
			t.Fatalf("expected %s %v, got %v", label, expected, actual)
		}
	}
}

// Verifies installer changes pass once the dispatcher package root is touched.
func TestReleaseTriggerGuardAcceptsInstallerChangesWithDispatcherTrigger(t *testing.T) {
	result := AnalyzeReleaseTriggerGuard([]string{
		"scripts/install.ps1",
		"cli/dispatcher/shared-inputs-stamp.json",
	})

	if len(result.Violations) != 0 {
		t.Fatalf("expected no violations, got %v", result.Violations)
	}
}

// Verifies backslash-separated changed paths are normalized before matching.
func TestReleaseTriggerGuardNormalizesWindowsPaths(t *testing.T) {
	result := AnalyzeReleaseTriggerGuard([]string{
		`cli\common\clicore\output.go`,
		`cli\dispatcher\shared-inputs-stamp.json`,
		`cli\project-runner\shared-inputs-stamp.json`,
	})

	if len(result.Violations) != 0 {
		t.Fatalf("expected no violations after path normalization, got %v", result.Violations)
	}
}

// Verifies the warning lists the changed inputs, missing roots, and the stamp command.
func TestFormatReleaseTriggerWarningNamesStampCommand(t *testing.T) {
	result := AnalyzeReleaseTriggerGuard([]string{"cli/common/clicore/output.go"})

	warning := FormatReleaseTriggerWarning(result)

	for _, expected := range []string{
		"common module sources",
		"`cli/common/clicore/output.go`",
		"`cli/dispatcher/`",
		"`cli/project-runner/`",
		"scripts/stamp-release-inputs.sh",
	} {
		if !strings.Contains(warning, expected) {
			t.Fatalf("expected warning to contain %q, got:\n%s", expected, warning)
		}
	}
}

// Verifies a description-only catalog change is dropped from the shared-input list.
func TestWithoutDescriptionOnlyCatalogChangeRemovesCatalogWhenShapeUnchanged(t *testing.T) {
	filtered := withoutDescriptionOnlyCatalogChange([]string{
		CatalogRelativePath,
		"docs/shared-release-inputs.md",
	}, false)

	if len(filtered) != 1 || filtered[0] != "docs/shared-release-inputs.md" {
		t.Fatalf("expected only the non-catalog file, got %v", filtered)
	}
}

// Verifies a structural catalog change stays on the shared-input list.
func TestWithoutDescriptionOnlyCatalogChangeKeepsCatalogWhenShapeChanged(t *testing.T) {
	changedFiles := []string{CatalogRelativePath, "docs/shared-release-inputs.md"}
	filtered := withoutDescriptionOnlyCatalogChange(changedFiles, true)

	if len(filtered) != 2 || filtered[0] != CatalogRelativePath || filtered[1] != "docs/shared-release-inputs.md" {
		t.Fatalf("expected the original list, got %v", filtered)
	}
}

// Verifies a file list without the catalog is left unchanged.
func TestWithoutDescriptionOnlyCatalogChangeLeavesNonCatalogListsAlone(t *testing.T) {
	changedFiles := []string{"cli/common/clicore/output.go"}
	filtered := withoutDescriptionOnlyCatalogChange(changedFiles, false)

	if len(filtered) != 1 || filtered[0] != "cli/common/clicore/output.go" {
		t.Fatalf("expected the original list, got %v", filtered)
	}
}

const (
	mergeBaseCatalogInitial         = `{"tools":[{"name":"compile","description":"a","inputSchema":{"type":"object","properties":{"X":{"type":"string","description":"d"}}}}]}`
	mergeBaseCatalogStructural      = `{"tools":[{"name":"compile","description":"a","inputSchema":{"type":"object","properties":{"X":{"type":"string","description":"d"},"Y":{"type":"boolean"}}}}]}`
	mergeBaseCatalogDescriptionOnly = `{"tools":[{"name":"compile","description":"b","inputSchema":{"type":"object","properties":{"X":{"type":"string","description":"e"}}}}]}`
)

// Verifies catalog comparison uses merge-base, not the base tip. Comparing against
// main's tip would treat this description-only topic as a structural change because
// main later added a property.
func TestAnalyzeReleaseTriggerGuardForRefsUsesMergeBaseForCatalogShape(t *testing.T) {
	repoRoot := t.TempDir()
	runGitInRepo(t, repoRoot, "init", "-b", "main")

	catalogPath := filepath.Join(repoRoot, filepath.FromSlash(CatalogRelativePath))
	if err := os.MkdirAll(filepath.Dir(catalogPath), 0o755); err != nil {
		t.Fatalf("failed to create catalog directory: %v", err)
	}
	if err := os.WriteFile(catalogPath, []byte(mergeBaseCatalogInitial+"\n"), 0o644); err != nil {
		t.Fatalf("failed to write initial catalog: %v", err)
	}
	runGitInRepo(t, repoRoot, "add", CatalogRelativePath)
	runGitInRepo(t, repoRoot, "commit", "-m", "initial catalog")
	initialCommit := strings.TrimSpace(runGitInRepoOutput(t, repoRoot, "rev-parse", "HEAD"))

	if err := os.WriteFile(catalogPath, []byte(mergeBaseCatalogStructural+"\n"), 0o644); err != nil {
		t.Fatalf("failed to write structural catalog: %v", err)
	}
	runGitInRepo(t, repoRoot, "add", CatalogRelativePath)
	runGitInRepo(t, repoRoot, "commit", "-m", "structural catalog change on main")

	runGitInRepo(t, repoRoot, "switch", "-c", "topic", initialCommit)
	if err := os.WriteFile(catalogPath, []byte(mergeBaseCatalogDescriptionOnly+"\n"), 0o644); err != nil {
		t.Fatalf("failed to write description-only catalog: %v", err)
	}
	runGitInRepo(t, repoRoot, "add", CatalogRelativePath)
	runGitInRepo(t, repoRoot, "commit", "-m", "description-only catalog change")

	t.Chdir(repoRoot)

	result, err := AnalyzeReleaseTriggerGuardForRefs(context.Background(), ReleaseTriggerGuardConfig{
		BaseRef: "main",
		HeadRef: "topic",
	})
	if err != nil {
		t.Fatalf("expected merge-base comparison to succeed, got %v", err)
	}
	if len(result.Violations) != 0 {
		t.Fatalf("expected no violations when comparing against merge-base, got %v", result.Violations)
	}
	if !result.DescriptionOnlyCatalogSkipped {
		t.Fatal("expected the description-only catalog change to be filtered out")
	}
}

func runGitInRepo(t *testing.T, repoRoot string, args ...string) {
	t.Helper()
	_ = runGitInRepoOutput(t, repoRoot, args...)
}

func runGitInRepoOutput(t *testing.T, repoRoot string, args ...string) string {
	t.Helper()
	command := exec.Command("git", append([]string{"-C", repoRoot, "-c", "user.name=test", "-c", "user.email=test@example.com"}, args...)...)
	output, err := command.CombinedOutput()
	if err != nil {
		t.Fatalf("git %v failed: %v\n%s", args, err, output)
	}
	return string(output)
}
