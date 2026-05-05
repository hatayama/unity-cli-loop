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
	coreModulePath         = "github.com/hatayama/unity-cli-loop/Packages/src/Cli/Core"
	dispatcherModulePath   = "github.com/hatayama/unity-cli-loop/Packages/src/Cli/Dispatcher"
	sharedModulePath       = "github.com/hatayama/unity-cli-loop/Packages/src/Cli/Shared"
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
	CliDir             string `json:"cliDir"`
	CoreDir            string `json:"coreDir"`
	DispatcherDir      string `json:"dispatcherDir"`
	SharedDir          string `json:"sharedDir"`
	DistDir            string `json:"distDir"`
	ProjectLocalBinDir string `json:"projectLocalBinDir"`
}

type binariesLayout struct {
	Core       binaryNames `json:"core"`
	Dispatcher binaryNames `json:"dispatcher"`
}

type binaryNames struct {
	Unix    string `json:"unix"`
	Windows string `json:"windows"`
}

type coreContract struct {
	DispatcherVersionEnv string `json:"dispatcherVersionEnv"`
}

type dispatcherContract struct {
	DispatcherVersionEnv string `json:"dispatcherVersionEnv"`
}

// Tests that shared packages do not depend on runtime-specific modules.
func TestSharedPackagesDoNotImportRuntimeModules(t *testing.T) {
	moduleRoot := findModuleRoot(t)
	packages := listPackages(t, moduleRoot)
	for _, goPackage := range packages {
		for _, importedPath := range goPackage.Imports {
			if strings.HasPrefix(importedPath, coreModulePath) || strings.HasPrefix(importedPath, dispatcherModulePath) {
				t.Fatalf("shared package %s must not import runtime module package %s", goPackage.ImportPath, importedPath)
			}
		}
	}
}

// Tests that shared packages remain importable by sibling modules instead of using Go internal visibility.
func TestSharedPackagesDoNotUseInternalDirectories(t *testing.T) {
	moduleRoot := findModuleRoot(t)
	packages := listPackages(t, moduleRoot)
	for _, goPackage := range packages {
		if !strings.HasPrefix(goPackage.ImportPath, sharedModulePath) {
			continue
		}
		if strings.Contains(goPackage.ImportPath, "/internal/") {
			t.Fatalf("shared package must not use internal visibility: %s", goPackage.ImportPath)
		}
	}
}

// Tests that the parent CLI layout manifest matches repository paths used by tooling.
func TestLayoutContractMatchesRepositoryPaths(t *testing.T) {
	moduleRoot := findModuleRoot(t)
	cliRoot := filepath.Dir(moduleRoot)
	repositoryRoot := findRepositoryRoot(t, cliRoot)
	contract := readLayoutContract(t, filepath.Join(cliRoot, "layout-contract.json"))

	if contract.SchemaVersion != 1 {
		t.Fatalf("layout contract schema version mismatch: %d", contract.SchemaVersion)
	}
	assertDirectoryName(t, cliRoot, contract.Layout.CliDir)
	assertPathExists(t, filepath.Join(cliRoot, contract.Layout.CoreDir))
	assertPathExists(t, filepath.Join(cliRoot, contract.Layout.DispatcherDir))
	assertPathExists(t, filepath.Join(cliRoot, contract.Layout.SharedDir))
	assertPathExists(t, filepath.Join(cliRoot, contract.Layout.CoreDir, contract.Layout.DistDir))
	assertPathExists(t, filepath.Join(cliRoot, contract.Layout.DispatcherDir, contract.Layout.DistDir))
	assertTextContains(t, filepath.Join(repositoryRoot, "scripts", "build-go-cli.sh"), packagePath(contract, contract.Layout.CoreDir))
	assertTextContains(t, filepath.Join(repositoryRoot, "scripts", "build-go-cli.sh"), packagePath(contract, contract.Layout.DispatcherDir))
	assertTextContains(t, filepath.Join(repositoryRoot, "scripts", "verify-go-cli-dist.sh"), filepath.ToSlash(filepath.Join(packagePath(contract, contract.Layout.CoreDir), contract.Layout.DistDir, "darwin-arm64", contract.Binaries.Core.Unix)))
	assertTextContains(t, filepath.Join(repositoryRoot, ".github", "workflows", "security-scan.yml"), packagePath(contract, contract.Layout.SharedDir))
	assertTextContains(t, filepath.Join(repositoryRoot, ".github", "workflows", "security-scan.yml"), packagePath(contract, contract.Layout.CoreDir))
	assertTextContains(t, filepath.Join(repositoryRoot, ".github", "workflows", "security-scan.yml"), packagePath(contract, contract.Layout.DispatcherDir))
}

// Tests that core and dispatcher agree on the environment key used for compatibility handoff.
func TestRuntimeContractsShareDispatcherVersionProtocol(t *testing.T) {
	moduleRoot := findModuleRoot(t)
	cliRoot := filepath.Dir(moduleRoot)
	core := readCoreContract(t, filepath.Join(cliRoot, "Core~", "contract.json"))
	dispatcher := readDispatcherContract(t, filepath.Join(cliRoot, "Dispatcher~", "contract.json"))

	if core.DispatcherVersionEnv != dispatcher.DispatcherVersionEnv {
		t.Fatalf("dispatcher version env mismatch: core=%s dispatcher=%s", core.DispatcherVersionEnv, dispatcher.DispatcherVersionEnv)
	}
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

func readCoreContract(t *testing.T, path string) coreContract {
	t.Helper()
	content, err := os.ReadFile(path)
	if err != nil {
		t.Fatalf("failed to read core contract: %v", err)
	}
	var contract coreContract
	if err := json.Unmarshal(content, &contract); err != nil {
		t.Fatalf("failed to parse core contract: %v", err)
	}
	return contract
}

func readDispatcherContract(t *testing.T, path string) dispatcherContract {
	t.Helper()
	content, err := os.ReadFile(path)
	if err != nil {
		t.Fatalf("failed to read dispatcher contract: %v", err)
	}
	var contract dispatcherContract
	if err := json.Unmarshal(content, &contract); err != nil {
		t.Fatalf("failed to parse dispatcher contract: %v", err)
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
