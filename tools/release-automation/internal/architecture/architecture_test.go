package architecture

import (
	"encoding/json"
	"fmt"
	"io"
	"os"
	"os/exec"
	"path/filepath"
	"sort"
	"strings"
	"testing"
)

const (
	repositoryModulePrefix      = "github.com/hatayama/unity-cli-loop/"
	commonModulePath            = repositoryModulePrefix + "common"
	dispatcherModulePath        = repositoryModulePrefix + "dispatcher"
	projectRunnerModulePath     = repositoryModulePrefix + "project-runner"
	releaseAutomationModulePath = repositoryModulePrefix + "tools/release-automation"
	maxProductionFileLines      = 500
)

// goPackage mirrors the subset of `go list -json` output that these tests read.
type goPackage struct {
	ImportPath string
	Imports    []string
}

// goModEditJSON mirrors the subset of `go mod edit -json` output that these tests read.
type goModEditJSON struct {
	Module  goModEditModule    `json:"Module"`
	Go      string             `json:"Go"`
	Require []goModEditRequire `json:"Require"`
}

type goModEditModule struct {
	Path string `json:"Path"`
}

type goModEditRequire struct {
	Path string `json:"Path"`
}

// goWorkEditJSON mirrors the subset of `go work edit -json` output that these tests read.
type goWorkEditJSON struct {
	Go  string          `json:"Go"`
	Use []goWorkEditUse `json:"Use"`
}

type goWorkEditUse struct {
	DiskPath string `json:"DiskPath"`
}

type layoutContract struct {
	SchemaVersion int            `json:"schemaVersion"`
	Layout        layoutSection  `json:"layout"`
	Binaries      binariesLayout `json:"binaries"`
}

type layoutSection struct {
	Modules layoutModules `json:"modules"`
	DistDir string        `json:"distDir"`
}

type layoutModules struct {
	Common            string `json:"common"`
	Dispatcher        string `json:"dispatcher"`
	ProjectRunner     string `json:"projectRunner"`
	ReleaseAutomation string `json:"releaseAutomation"`
}

type binariesLayout struct {
	Dispatcher    binaryNames `json:"dispatcher"`
	ProjectRunner binaryNames `json:"projectRunner"`
}

type binaryNames struct {
	Unix    string `json:"unix"`
	Windows string `json:"windows"`
}

// Tests that the pre-split top-level `cli/` directory no longer exists at the repo root.
func TestNoTopLevelCliDirectory(t *testing.T) {
	repositoryRoot := findRepositoryRoot(t)
	cliPath := filepath.Join(repositoryRoot, "cli")
	if _, err := os.Stat(cliPath); err == nil {
		t.Fatalf("pre-split top-level directory must not exist: %s", cliPath)
	}
}

// Tests that every module's require directives respect the repo's dependency direction:
// common depends on nothing else in the repo; the other three modules only depend on common.
func TestModuleDependencyDirections(t *testing.T) {
	repositoryRoot := findRepositoryRoot(t)
	contract := readLayoutContract(t, filepath.Join(repositoryRoot, "layout-contract.json"))

	commonDir := filepath.Join(repositoryRoot, contract.Layout.Modules.Common)
	commonRequires := readGoModEdit(t, commonDir).Require
	for _, require := range commonRequires {
		if strings.HasPrefix(require.Path, repositoryModulePrefix) {
			t.Fatalf("common module must not require any other repo module, got %s", require.Path)
		}
	}

	dependentModuleDirs := []string{
		filepath.Join(repositoryRoot, contract.Layout.Modules.Dispatcher),
		filepath.Join(repositoryRoot, contract.Layout.Modules.ProjectRunner),
		filepath.Join(repositoryRoot, contract.Layout.Modules.ReleaseAutomation),
	}
	for _, moduleDir := range dependentModuleDirs {
		requires := readGoModEdit(t, moduleDir).Require
		for _, require := range requires {
			if !strings.HasPrefix(require.Path, repositoryModulePrefix) {
				continue
			}
			if require.Path != commonModulePath {
				t.Fatalf("module %s may only depend on %s among repo modules, got %s", moduleDir, commonModulePath, require.Path)
			}
		}
	}
}

// Tests that the Go toolchain directive stays identical across every go.mod, go.work, and .go-version.
func TestGoToolchainSingleSourceOfTruth(t *testing.T) {
	repositoryRoot := findRepositoryRoot(t)
	contract := readLayoutContract(t, filepath.Join(repositoryRoot, "layout-contract.json"))

	workDirective := readGoWorkEdit(t, repositoryRoot).Go
	if workDirective == "" {
		t.Fatalf("go.work must declare a go directive")
	}

	for _, moduleDir := range allModuleDirs(repositoryRoot, contract) {
		moduleDirective := readGoModEdit(t, moduleDir).Go
		if moduleDirective != workDirective {
			t.Fatalf("go directive mismatch: %s has %q, go.work has %q", moduleDir, moduleDirective, workDirective)
		}
	}

	goVersionRaw, err := os.ReadFile(filepath.Join(repositoryRoot, ".go-version"))
	if err != nil {
		t.Fatalf("failed to read .go-version: %v", err)
	}
	goVersion := strings.TrimSpace(string(goVersionRaw))
	if goVersion != workDirective && !strings.HasPrefix(goVersion, workDirective+".") {
		t.Fatalf(".go-version %q must equal or start with %q followed by '.'", goVersion, workDirective)
	}
}

// Tests that the dispatcher command only enters dispatcher-internal code through the dispatcher entrypoint.
func TestDispatcherCommandOnlyDependsOnDispatcherEntrypoint(t *testing.T) {
	repositoryRoot := findRepositoryRoot(t)
	contract := readLayoutContract(t, filepath.Join(repositoryRoot, "layout-contract.json"))
	dispatcherDir := filepath.Join(repositoryRoot, contract.Layout.Modules.Dispatcher)
	assertCommandOnlyDependsOnInternalEntrypoint(t, dispatcherDir, dispatcherModulePath, "./cmd/dispatcher", dispatcherModulePath+"/internal/dispatcher")
}

// Tests that the project runner command only enters project-runner-internal code through the projectrunner entrypoint.
func TestProjectRunnerCommandOnlyDependsOnProjectRunnerEntrypoint(t *testing.T) {
	repositoryRoot := findRepositoryRoot(t)
	contract := readLayoutContract(t, filepath.Join(repositoryRoot, "layout-contract.json"))
	projectRunnerDir := filepath.Join(repositoryRoot, contract.Layout.Modules.ProjectRunner)
	assertCommandOnlyDependsOnInternalEntrypoint(t, projectRunnerDir, projectRunnerModulePath, "./cmd/project-runner", projectRunnerModulePath+"/internal/projectrunner")
}

// Tests that the dispatcher binary does not transitively pull in the project runner's projectrunner package.
func TestDispatcherBinaryDoesNotTransitivelyDependOnProjectRunner(t *testing.T) {
	repositoryRoot := findRepositoryRoot(t)
	contract := readLayoutContract(t, filepath.Join(repositoryRoot, "layout-contract.json"))
	dispatcherDir := filepath.Join(repositoryRoot, contract.Layout.Modules.Dispatcher)
	assertBinaryDoesNotTransitivelyDependOn(t, dispatcherDir, "./cmd/dispatcher", projectRunnerModulePath+"/internal/projectrunner")
}

// Tests that the project runner binary does not transitively pull in the dispatcher module's dispatcher package.
func TestProjectRunnerBinaryDoesNotTransitivelyDependOnDispatcher(t *testing.T) {
	repositoryRoot := findRepositoryRoot(t)
	contract := readLayoutContract(t, filepath.Join(repositoryRoot, "layout-contract.json"))
	projectRunnerDir := filepath.Join(repositoryRoot, contract.Layout.Modules.ProjectRunner)
	assertBinaryDoesNotTransitivelyDependOn(t, projectRunnerDir, "./cmd/project-runner", dispatcherModulePath+"/internal/dispatcher")
}

// Tests that production Go files across every module stay small enough to keep each file focused on one responsibility.
func TestProductionGoFilesStayFocused(t *testing.T) {
	repositoryRoot := findRepositoryRoot(t)
	contract := readLayoutContract(t, filepath.Join(repositoryRoot, "layout-contract.json"))

	for _, moduleDir := range allModuleDirs(repositoryRoot, contract) {
		walkErr := filepath.WalkDir(moduleDir, func(path string, entry os.DirEntry, walkErr error) error {
			if walkErr != nil {
				return walkErr
			}
			if entry.IsDir() {
				if entry.Name() == "dist" {
					return filepath.SkipDir
				}
				return nil
			}
			if !strings.HasSuffix(entry.Name(), ".go") || strings.HasSuffix(entry.Name(), "_test.go") {
				return nil
			}
			lineCount, err := countLines(path)
			if err != nil {
				return err
			}
			if lineCount > maxProductionFileLines {
				relativePath, err := filepath.Rel(repositoryRoot, path)
				if err != nil {
					return err
				}
				return fmt.Errorf("%s has %d lines; split files above %d lines", relativePath, lineCount, maxProductionFileLines)
			}
			return nil
		})
		if walkErr != nil {
			t.Fatal(walkErr)
		}
	}
}

// Tests that every package in the common module (except clicore itself) stays free of clicore imports.
func TestCommonPackagesDoNotImportClicore(t *testing.T) {
	repositoryRoot := findRepositoryRoot(t)
	contract := readLayoutContract(t, filepath.Join(repositoryRoot, "layout-contract.json"))
	commonDir := filepath.Join(repositoryRoot, contract.Layout.Modules.Common)

	clicorePackage := commonModulePath + "/clicore"
	for _, goPackage := range listPackages(t, commonDir) {
		if goPackage.ImportPath == clicorePackage || strings.HasPrefix(goPackage.ImportPath, clicorePackage+"/") {
			continue
		}
		for _, importedPath := range goPackage.Imports {
			if importedPath == clicorePackage || strings.HasPrefix(importedPath, clicorePackage+"/") {
				t.Fatalf("common package %s must not import %s", goPackage.ImportPath, importedPath)
			}
		}
	}
}

// Tests that every module's internal packages sit under the explicit boundary list allowed for that module.
func TestInternalBoundariesPerModule(t *testing.T) {
	repositoryRoot := findRepositoryRoot(t)
	contract := readLayoutContract(t, filepath.Join(repositoryRoot, "layout-contract.json"))

	type moduleBoundary struct {
		moduleDir  string
		modulePath string
		allowed    []string
	}
	moduleBoundaries := []moduleBoundary{
		{
			// The common module publishes shared packages only; it must never grow internal packages.
			moduleDir:  filepath.Join(repositoryRoot, contract.Layout.Modules.Common),
			modulePath: commonModulePath,
			allowed:    []string{},
		},
		{
			moduleDir:  filepath.Join(repositoryRoot, contract.Layout.Modules.ProjectRunner),
			modulePath: projectRunnerModulePath,
			allowed:    []string{"projectrunner"},
		},
		{
			moduleDir:  filepath.Join(repositoryRoot, contract.Layout.Modules.Dispatcher),
			modulePath: dispatcherModulePath,
			allowed:    []string{"dispatcher", "install", "uninstall", "update"},
		},
		{
			moduleDir:  filepath.Join(repositoryRoot, contract.Layout.Modules.ReleaseAutomation),
			modulePath: releaseAutomationModulePath,
			allowed:    []string{"automation", "architecture"},
		},
	}

	for _, boundary := range moduleBoundaries {
		allowedPrefixes := []string{}
		for _, name := range boundary.allowed {
			allowedPrefixes = append(allowedPrefixes, boundary.modulePath+"/internal/"+name)
		}
		internalPrefix := boundary.modulePath + "/internal/"
		for _, goPackage := range listPackages(t, boundary.moduleDir) {
			if !strings.HasPrefix(goPackage.ImportPath, internalPrefix) {
				continue
			}
			if !hasAnyPackagePrefix(goPackage.ImportPath, allowedPrefixes) {
				t.Fatalf("%s internal package must live under one of %v, got %s", boundary.modulePath, boundary.allowed, goPackage.ImportPath)
			}
		}
	}
}

// Tests that the layout contract matches repository paths used by tooling and downstream scripts.
func TestLayoutContractMatchesRepositoryPaths(t *testing.T) {
	repositoryRoot := findRepositoryRoot(t)
	contract := readLayoutContract(t, filepath.Join(repositoryRoot, "layout-contract.json"))

	if contract.SchemaVersion != 2 {
		t.Fatalf("layout contract schema version mismatch: %d", contract.SchemaVersion)
	}
	moduleDirs := map[string]string{
		"common":            contract.Layout.Modules.Common,
		"dispatcher":        contract.Layout.Modules.Dispatcher,
		"projectRunner":     contract.Layout.Modules.ProjectRunner,
		"releaseAutomation": contract.Layout.Modules.ReleaseAutomation,
	}
	for name, moduleDir := range moduleDirs {
		if moduleDir == "" {
			t.Fatalf("layout contract module %s is empty", name)
		}
		assertPathExists(t, filepath.Join(repositoryRoot, moduleDir))
	}
	projectRunnerDir := filepath.Join(repositoryRoot, contract.Layout.Modules.ProjectRunner)
	assertPathExists(t, filepath.Join(projectRunnerDir, "cmd"))
	assertPathExists(t, filepath.Join(projectRunnerDir, "internal"))
	assertPathDoesNotExist(t, filepath.Join(projectRunnerDir, "Core~"))
	assertPathDoesNotExist(t, filepath.Join(projectRunnerDir, "Dispatcher~"))
	assertPathDoesNotExist(t, filepath.Join(projectRunnerDir, "Shared~"))

	assertTextContains(t, filepath.Join(repositoryRoot, "scripts", "build-go-cli.sh"), contract.Layout.Modules.ProjectRunner)

	distDir := contract.Layout.DistDir
	verifyScript := filepath.Join(repositoryRoot, "scripts", "verify-go-cli-dist.sh")
	assertTextContains(t, verifyScript, filepath.ToSlash(filepath.Join(distDir, "darwin-arm64", contract.Binaries.Dispatcher.Unix)))
	assertTextContains(t, verifyScript, filepath.ToSlash(filepath.Join(distDir, "darwin-arm64", contract.Binaries.ProjectRunner.Unix)))
	assertTextContains(t, verifyScript, filepath.ToSlash(filepath.Join(distDir, "windows-amd64", contract.Binaries.Dispatcher.Windows)))
	assertTextContains(t, verifyScript, filepath.ToSlash(filepath.Join(distDir, "windows-amd64", contract.Binaries.ProjectRunner.Windows)))
}

// Tests that the set of module dirs in the layout contract equals the go.work `use` set
// and that each module dir string is referenced from the scripts and workflows that drive multi-module CI.
func TestModuleEnumerationConsistency(t *testing.T) {
	repositoryRoot := findRepositoryRoot(t)
	contract := readLayoutContract(t, filepath.Join(repositoryRoot, "layout-contract.json"))

	contractModuleDirs := []string{
		contract.Layout.Modules.Common,
		contract.Layout.Modules.Dispatcher,
		contract.Layout.Modules.ProjectRunner,
		contract.Layout.Modules.ReleaseAutomation,
	}
	sort.Strings(contractModuleDirs)

	workEdit := readGoWorkEdit(t, repositoryRoot)
	workModuleDirs := []string{}
	for _, use := range workEdit.Use {
		workModuleDirs = append(workModuleDirs, normalizeWorkDiskPath(use.DiskPath))
	}
	sort.Strings(workModuleDirs)

	if !stringSlicesEqual(contractModuleDirs, workModuleDirs) {
		t.Fatalf("layout contract modules %v must equal go.work use entries %v", contractModuleDirs, workModuleDirs)
	}

	scriptsToCheck := []string{
		filepath.Join(repositoryRoot, "scripts", "check-go-cli-source.sh"),
		filepath.Join(repositoryRoot, "scripts", "check-code-complexity.sh"),
		filepath.Join(repositoryRoot, ".github", "workflows", "code-complexity.yml"),
	}
	for _, moduleDir := range contractModuleDirs {
		for _, scriptPath := range scriptsToCheck {
			assertTextContains(t, scriptPath, moduleDir)
		}
	}
}

// assertBinaryDoesNotTransitivelyDependOn runs `go list -deps` for the given command
// and fails when forbiddenPackage appears in its transitive dependency list.
func assertBinaryDoesNotTransitivelyDependOn(t *testing.T, moduleDir string, commandPath string, forbiddenPackage string) {
	t.Helper()
	command := exec.Command("go", "list", "-deps", commandPath)
	command.Dir = moduleDir
	output, err := command.Output()
	if err != nil {
		t.Fatalf("go list -deps failed: %v", err)
	}
	for _, dependency := range strings.Split(strings.TrimSpace(string(output)), "\n") {
		if dependency == forbiddenPackage {
			t.Fatalf("%s must not transitively depend on %s", commandPath, forbiddenPackage)
		}
	}
}

// assertCommandOnlyDependsOnInternalEntrypoint fails when the command's direct imports include
// any internal package other than expectedEntrypoint from its own module.
func assertCommandOnlyDependsOnInternalEntrypoint(t *testing.T, moduleDir string, modulePath string, commandPath string, expectedEntrypoint string) {
	t.Helper()
	command := exec.Command("go", "list", "-json", commandPath)
	command.Dir = moduleDir
	output, err := command.Output()
	if err != nil {
		t.Fatalf("go list failed: %v", err)
	}

	var commandPackage goPackage
	if err := json.Unmarshal(output, &commandPackage); err != nil {
		t.Fatalf("failed to decode command package: %v", err)
	}
	for _, dependency := range commandPackage.Imports {
		if !strings.HasPrefix(dependency, modulePath+"/internal/") {
			continue
		}
		if dependency != expectedEntrypoint {
			t.Fatalf("%s must enter internal code through %s, got %s", commandPath, expectedEntrypoint, dependency)
		}
	}
}

// hasAnyPackagePrefix reports whether importPath is packagePrefix itself or a subpackage of it.
func hasAnyPackagePrefix(importPath string, packagePrefixes []string) bool {
	for _, packagePrefix := range packagePrefixes {
		if importPath == packagePrefix || strings.HasPrefix(importPath, packagePrefix+"/") {
			return true
		}
	}
	return false
}

// allModuleDirs returns the absolute directory paths of every repo module in a stable order.
func allModuleDirs(repositoryRoot string, contract layoutContract) []string {
	return []string{
		filepath.Join(repositoryRoot, contract.Layout.Modules.Common),
		filepath.Join(repositoryRoot, contract.Layout.Modules.Dispatcher),
		filepath.Join(repositoryRoot, contract.Layout.Modules.ProjectRunner),
		filepath.Join(repositoryRoot, contract.Layout.Modules.ReleaseAutomation),
	}
}

// listPackages runs `go list -json ./...` inside moduleDir and decodes each package object.
func listPackages(t *testing.T, moduleDir string) []goPackage {
	t.Helper()
	command := exec.Command("go", "list", "-json", "./...")
	command.Dir = moduleDir
	output, err := command.Output()
	if err != nil {
		t.Fatalf("go list failed in %s: %v", moduleDir, err)
	}

	decoder := json.NewDecoder(strings.NewReader(string(output)))
	packages := []goPackage{}
	for {
		var goPackage goPackage
		err := decoder.Decode(&goPackage)
		if err == io.EOF {
			break
		}
		if err != nil {
			t.Fatalf("failed to decode go list output: %v", err)
		}
		packages = append(packages, goPackage)
	}
	return packages
}

// readGoModEdit runs `go mod edit -json` inside moduleDir and decodes the result.
func readGoModEdit(t *testing.T, moduleDir string) goModEditJSON {
	t.Helper()
	command := exec.Command("go", "mod", "edit", "-json")
	command.Dir = moduleDir
	output, err := command.Output()
	if err != nil {
		t.Fatalf("go mod edit -json failed in %s: %v", moduleDir, err)
	}
	var result goModEditJSON
	if err := json.Unmarshal(output, &result); err != nil {
		t.Fatalf("failed to decode go mod edit output for %s: %v", moduleDir, err)
	}
	return result
}

// readGoWorkEdit runs `go work edit -json` inside the repository root and decodes the result.
func readGoWorkEdit(t *testing.T, repositoryRoot string) goWorkEditJSON {
	t.Helper()
	command := exec.Command("go", "work", "edit", "-json")
	command.Dir = repositoryRoot
	output, err := command.Output()
	if err != nil {
		t.Fatalf("go work edit -json failed: %v", err)
	}
	var result goWorkEditJSON
	if err := json.Unmarshal(output, &result); err != nil {
		t.Fatalf("failed to decode go work edit output: %v", err)
	}
	return result
}

// normalizeWorkDiskPath strips the leading "./" that `go work` emits so paths compare against layout contract values.
func normalizeWorkDiskPath(diskPath string) string {
	return strings.TrimPrefix(diskPath, "./")
}

// stringSlicesEqual reports whether two sorted string slices are element-wise equal.
func stringSlicesEqual(left []string, right []string) bool {
	if len(left) != len(right) {
		return false
	}
	for index := range left {
		if left[index] != right[index] {
			return false
		}
	}
	return true
}

func readLayoutContract(t *testing.T, path string) layoutContract {
	t.Helper()
	content, err := os.ReadFile(path)
	if err != nil {
		t.Fatalf("failed to read layout contract: %v", err)
	}
	var contract layoutContract
	if err := json.Unmarshal(content, &contract); err != nil {
		t.Fatalf("failed to parse layout contract: %v", err)
	}
	return contract
}

func assertPathExists(t *testing.T, path string) {
	t.Helper()
	if _, err := os.Stat(path); err != nil {
		t.Fatalf("expected path to exist: %s", path)
	}
}

func assertPathDoesNotExist(t *testing.T, path string) {
	t.Helper()
	if _, err := os.Stat(path); err == nil {
		t.Fatalf("expected path not to exist: %s", path)
	}
}

func assertTextContains(t *testing.T, path string, expected string) {
	t.Helper()
	content, err := os.ReadFile(path)
	if err != nil {
		t.Fatalf("failed to read %s: %v", path, err)
	}
	if !strings.Contains(string(content), expected) {
		t.Fatalf("%s must contain %s", path, expected)
	}
}

// findRepositoryRoot walks up from the current working directory until it finds a `.git` entry.
// This works regardless of which module the test is running in.
func findRepositoryRoot(t *testing.T) string {
	t.Helper()
	currentPath, err := os.Getwd()
	if err != nil {
		t.Fatalf("failed to get working directory: %v", err)
	}
	for {
		if _, err := os.Stat(filepath.Join(currentPath, ".git")); err == nil {
			return currentPath
		}
		parentPath := filepath.Dir(currentPath)
		if parentPath == currentPath {
			t.Fatal(".git not found while walking up from CWD")
		}
		currentPath = parentPath
	}
}

func countLines(path string) (int, error) {
	content, err := os.ReadFile(path)
	if err != nil {
		return 0, err
	}
	if len(content) == 0 {
		return 0, nil
	}
	lineCount := strings.Count(string(content), "\n")
	if !strings.HasSuffix(string(content), "\n") {
		lineCount++
	}
	return lineCount, nil
}
