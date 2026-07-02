package architecture

import (
	"encoding/json"
	"fmt"
	"io"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
	"testing"
)

const (
	cliModulePath          = "github.com/hatayama/unity-cli-loop/cli"
	maxProductionFileLines = 500
)

type goPackage struct {
	ImportPath string
	Imports    []string
}

type layoutContract struct {
	SchemaVersion int            `json:"schemaVersion"`
	Layout        layoutSection  `json:"layout"`
	Binaries      binariesLayout `json:"binaries"`
}

type layoutSection struct {
	CliDir  string `json:"cliDir"`
	DistDir string `json:"distDir"`
}

type binariesLayout struct {
	Cli cliBinaryNames `json:"cli"`
}

type cliBinaryNames struct {
	Unix    string `json:"unix"`
	Windows string `json:"windows"`
}

// Tests that every package outside the CLI orchestration layer (dispatcher,
// project runner, shared CLI core) and cmd/ stays free of orchestration
// imports. Skipping only known orchestration packages keeps future packages
// covered by default instead of requiring a hand-maintained feature list.
func TestCliFeaturePackagesDoNotImportOrchestrationLayer(t *testing.T) {
	moduleRoot := findModuleRoot(t)
	packages := listPackages(t, moduleRoot)
	orchestrationPackagePrefixes := []string{
		cliModulePath + "/internal/dispatcher",
		cliModulePath + "/internal/projectrunner",
		cliModulePath + "/internal/clicore",
	}
	for _, goPackage := range packages {
		if hasAnyPackagePrefix(goPackage.ImportPath, orchestrationPackagePrefixes) ||
			strings.HasPrefix(goPackage.ImportPath, cliModulePath+"/cmd/") {
			continue
		}
		for _, importedPath := range goPackage.Imports {
			if hasAnyPackagePrefix(importedPath, orchestrationPackagePrefixes) {
				t.Fatalf("feature package %s must not import CLI orchestration package %s", goPackage.ImportPath, importedPath)
			}
		}
	}
}

// hasAnyPackagePrefix reports whether importPath is packagePrefix itself or a subpackage of it,
// avoiding false positives such as "/internal/project" matching "/internal/projectrunner".
func hasAnyPackagePrefix(importPath string, packagePrefixes []string) bool {
	for _, packagePrefix := range packagePrefixes {
		if importPath == packagePrefix || strings.HasPrefix(importPath, packagePrefix+"/") {
			return true
		}
	}
	return false
}

// Tests that CLI internal packages stay inside explicit runtime boundaries.
func TestCliInternalPackagesStayInsideExplicitBoundaries(t *testing.T) {
	moduleRoot := findModuleRoot(t)
	packages := listPackages(t, moduleRoot)
	for _, goPackage := range packages {
		if !strings.HasPrefix(goPackage.ImportPath, cliModulePath+"/internal/") {
			continue
		}
		if goPackage.ImportPath == cliModulePath+"/internal/architecture" {
			continue
		}
		for _, boundary := range []string{"/internal/automation", "/internal/clicore", "/internal/dispatcher", "/internal/install", "/internal/project", "/internal/projectrunner", "/internal/skills", "/internal/tools", "/internal/uninstall", "/internal/unityipc", "/internal/update", "/internal/version"} {
			if strings.Contains(goPackage.ImportPath, boundary) {
				goto nextPackage
			}
		}
		t.Fatalf("CLI internal package must live under an explicit runtime boundary: %s", goPackage.ImportPath)
	nextPackage:
	}
}

// Tests that the dispatcher command only enters the dispatcher package.
func TestDispatcherCommandOnlyDependsOnDispatcherEntrypoint(t *testing.T) {
	assertCommandOnlyDependsOnInternalEntrypoint(t, "./cmd/dispatcher", cliModulePath+"/internal/dispatcher")
}

// Tests that the project runner command only enters the project runner package.
func TestProjectRunnerCommandOnlyDependsOnProjectRunnerEntrypoint(t *testing.T) {
	assertCommandOnlyDependsOnInternalEntrypoint(t, "./cmd/project-runner", cliModulePath+"/internal/projectrunner")
}

// Tests that the dispatcher binary does not transitively pull in the project runner package.
func TestDispatcherBinaryDoesNotTransitivelyDependOnProjectRunner(t *testing.T) {
	assertBinaryDoesNotTransitivelyDependOn(t, "./cmd/dispatcher", cliModulePath+"/internal/projectrunner")
}

// Tests that the project runner binary does not transitively pull in the dispatcher package.
func TestProjectRunnerBinaryDoesNotTransitivelyDependOnDispatcher(t *testing.T) {
	assertBinaryDoesNotTransitivelyDependOn(t, "./cmd/project-runner", cliModulePath+"/internal/dispatcher")
}

func assertBinaryDoesNotTransitivelyDependOn(t *testing.T, commandPath string, forbiddenPackage string) {
	t.Helper()
	moduleRoot := findModuleRoot(t)
	command := exec.Command("go", "list", "-deps", commandPath)
	command.Dir = moduleRoot
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

func assertCommandOnlyDependsOnInternalEntrypoint(t *testing.T, commandPath string, expectedEntrypoint string) {
	t.Helper()
	moduleRoot := findModuleRoot(t)
	command := exec.Command("go", "list", "-json", commandPath)
	command.Dir = moduleRoot
	output, err := command.Output()
	if err != nil {
		t.Fatalf("go list failed: %v", err)
	}

	var commandPackage goPackage
	if err := json.Unmarshal(output, &commandPackage); err != nil {
		t.Fatalf("failed to decode command package: %v", err)
	}
	for _, dependency := range commandPackage.Imports {
		for _, removedModule := range []string{"/cli/Dispatcher", "/cli/Core", "/cli/Shared"} {
			if strings.Contains(dependency, removedModule) {
				t.Fatalf("CLI command must not depend on removed split module package %s", dependency)
			}
		}
		if !strings.HasPrefix(dependency, cliModulePath+"/internal/") {
			continue
		}
		if dependency != expectedEntrypoint {
			t.Fatalf("%s must enter internal code through %s, got %s", commandPath, expectedEntrypoint, dependency)
		}
	}
}

// Tests that the parent CLI layout manifest matches repository paths used by tooling.
func TestLayoutContractMatchesRepositoryPaths(t *testing.T) {
	moduleRoot := findModuleRoot(t)
	repositoryRoot := findRepositoryRoot(t, moduleRoot)
	contract := readLayoutContract(t, filepath.Join(moduleRoot, "layout-contract.json"))

	if contract.SchemaVersion != 1 {
		t.Fatalf("layout contract schema version mismatch: %d", contract.SchemaVersion)
	}
	assertDirectoryName(t, moduleRoot, contract.Layout.CliDir)
	assertPathExists(t, filepath.Join(moduleRoot, "cmd"))
	assertPathExists(t, filepath.Join(moduleRoot, "internal"))
	assertPathDoesNotExist(t, filepath.Join(moduleRoot, "Core~"))
	assertPathDoesNotExist(t, filepath.Join(moduleRoot, "Dispatcher~"))
	assertPathDoesNotExist(t, filepath.Join(moduleRoot, "Shared~"))
	assertTextContains(t, filepath.Join(repositoryRoot, "scripts", "build-go-cli.sh"), packagePath(contract, ""))
	assertTextContains(t, filepath.Join(repositoryRoot, "scripts", "verify-go-cli-dist.sh"), filepath.ToSlash(filepath.Join(packagePath(contract, contract.Layout.DistDir), "darwin-arm64", contract.Binaries.Cli.Unix)))
}

// Tests that production files stay small enough to keep each file focused on one responsibility.
func TestProductionGoFilesStayFocused(t *testing.T) {
	moduleRoot := findModuleRoot(t)
	err := filepath.WalkDir(moduleRoot, func(path string, entry os.DirEntry, walkErr error) error {
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
			relativePath, err := filepath.Rel(moduleRoot, path)
			if err != nil {
				return err
			}
			return fmt.Errorf("%s has %d lines; split files above %d lines", relativePath, lineCount, maxProductionFileLines)
		}
		return nil
	})
	if err != nil {
		t.Fatal(err)
	}
}

func listPackages(t *testing.T, moduleRoot string) []goPackage {
	t.Helper()
	command := exec.Command("go", "list", "-json", "./...")
	command.Dir = moduleRoot
	output, err := command.Output()
	if err != nil {
		t.Fatalf("go list failed: %v", err)
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

func packagePath(contract layoutContract, childDir string) string {
	return filepath.ToSlash(filepath.Join(contract.Layout.CliDir, childDir))
}

func assertDirectoryName(t *testing.T, path string, expectedName string) {
	t.Helper()
	if filepath.Base(path) != expectedName {
		t.Fatalf("directory name mismatch: %s", path)
	}
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

func findModuleRoot(t *testing.T) string {
	t.Helper()
	currentPath, err := os.Getwd()
	if err != nil {
		t.Fatalf("failed to get working directory: %v", err)
	}
	for {
		if _, err := os.Stat(filepath.Join(currentPath, "go.mod")); err == nil {
			return currentPath
		}
		parentPath := filepath.Dir(currentPath)
		if parentPath == currentPath {
			t.Fatal("go.mod not found")
		}
		currentPath = parentPath
	}
}

func findRepositoryRoot(t *testing.T, startPath string) string {
	t.Helper()
	currentPath := startPath
	for {
		if _, err := os.Stat(filepath.Join(currentPath, ".git")); err == nil {
			return currentPath
		}
		parentPath := filepath.Dir(currentPath)
		if parentPath == currentPath {
			t.Fatal(".git not found")
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
