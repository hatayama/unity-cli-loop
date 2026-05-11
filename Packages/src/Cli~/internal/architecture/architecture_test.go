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
	cliModulePath          = "github.com/hatayama/unity-cli-loop/Packages/src/Cli"
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

// Tests that CLI onion layers only import packages from allowed inner or outer boundaries.
func TestCliOnionLayerDependencies(t *testing.T) {
	moduleRoot := findModuleRoot(t)
	packages := listPackages(t, moduleRoot)
	for _, goPackage := range packages {
		sourceLayer := layerOf(goPackage.ImportPath)
		if sourceLayer == "" {
			continue
		}
		for _, importedPath := range goPackage.Imports {
			targetLayer := layerOf(importedPath)
			if targetLayer == "" {
				continue
			}
			if !isAllowedDependency(sourceLayer, targetLayer, importedPath) {
				t.Fatalf("%s package %s must not import %s package %s", sourceLayer, goPackage.ImportPath, targetLayer, importedPath)
			}
		}
	}
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
		for _, boundary := range []string{"/internal/adapters/", "/internal/app", "/internal/application", "/internal/domain", "/internal/ports", "/internal/presentation", "/internal/version"} {
			if strings.Contains(goPackage.ImportPath, boundary) {
				goto nextPackage
			}
		}
		t.Fatalf("CLI internal package must live under an explicit runtime boundary: %s", goPackage.ImportPath)
	nextPackage:
	}
}

// Tests that the native CLI command does not depend on removed split runtime modules.
func TestCliCommandDoesNotDependOnRemovedSplitModules(t *testing.T) {
	moduleRoot := findModuleRoot(t)
	command := exec.Command("go", "list", "-deps", "./cmd/uloop")
	command.Dir = moduleRoot
	output, err := command.Output()
	if err != nil {
		t.Fatalf("go list failed: %v", err)
	}

	for _, dependency := range strings.Split(strings.TrimSpace(string(output)), "\n") {
		for _, removedModule := range []string{"/Packages/src/Cli/Dispatcher", "/Packages/src/Cli/Core", "/Packages/src/Cli/Shared"} {
			if strings.Contains(dependency, removedModule) {
				t.Fatalf("CLI command must not depend on removed split module package %s", dependency)
			}
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
	assertPathExists(t, filepath.Join(moduleRoot, contract.Layout.DistDir))
	assertPathDoesNotExist(t, filepath.Join(moduleRoot, "Core~"))
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

func layerOf(importPath string) string {
	switch {
	case strings.HasPrefix(importPath, cliModulePath+"/internal/domain"):
		return "domain"
	case strings.HasPrefix(importPath, cliModulePath+"/internal/version"):
		return "version"
	case strings.HasPrefix(importPath, cliModulePath+"/internal/adapters"):
		return "adapters"
	case importPath == cliModulePath:
		return "contract"
	case strings.HasPrefix(importPath, cliModulePath+"/internal/application"):
		return "application"
	case strings.HasPrefix(importPath, cliModulePath+"/internal/ports"):
		return "ports"
	case strings.HasPrefix(importPath, cliModulePath+"/internal/presentation"):
		return "presentation"
	case strings.HasPrefix(importPath, cliModulePath+"/internal/app"):
		return "app"
	case strings.HasPrefix(importPath, cliModulePath+"/cmd/"):
		return "cmd"
	default:
		return ""
	}
}

func isAllowedDependency(sourceLayer string, targetLayer string, importedPath string) bool {
	switch sourceLayer {
	case "domain":
		return targetLayer == "domain"
	case "version":
		return targetLayer == "version"
	case "contract":
		return targetLayer == "contract"
	case "application":
		return targetLayer == "domain" || targetLayer == "ports" || targetLayer == "application"
	case "ports":
		return targetLayer == "domain" || targetLayer == "ports"
	case "adapters":
		return targetLayer == "domain" || targetLayer == "ports" || targetLayer == "application" || targetLayer == "adapters"
	case "presentation":
		return targetLayer == "domain" || targetLayer == "version" || targetLayer == "contract" || targetLayer == "ports" || targetLayer == "application" || targetLayer == "adapters" || targetLayer == "presentation"
	case "app":
		return targetLayer == "domain" || targetLayer == "version" || targetLayer == "contract" || targetLayer == "ports" || targetLayer == "application" || targetLayer == "adapters" || targetLayer == "presentation"
	case "cmd":
		return targetLayer == "app" || importedPath == cliModulePath+"/internal/app"
	default:
		return true
	}
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
	return filepath.ToSlash(filepath.Join("Packages", "src", contract.Layout.CliDir, childDir))
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
