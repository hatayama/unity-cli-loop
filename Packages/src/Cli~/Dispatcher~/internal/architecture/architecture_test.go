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
	maxProductionFileLines = 500
)

type goPackage struct {
	ImportPath string
	Imports    []string
}

// Tests that dispatcher packages never import the project-local core module.
func TestDispatcherPackagesDoNotImportCoreModule(t *testing.T) {
	moduleRoot := findModuleRoot(t)
	packages := listPackages(t, moduleRoot)
	for _, goPackage := range packages {
		for _, importedPath := range goPackage.Imports {
			if strings.HasPrefix(importedPath, coreModulePath) {
				t.Fatalf("dispatcher package %s must not import core module package %s", goPackage.ImportPath, importedPath)
			}
		}
	}
}

// Tests that dispatcher internal packages stay inside the dispatcher boundary.
func TestDispatcherInternalPackagesStayInsideDispatcherBoundary(t *testing.T) {
	moduleRoot := findModuleRoot(t)
	packages := listPackages(t, moduleRoot)
	for _, goPackage := range packages {
		if !strings.HasPrefix(goPackage.ImportPath, dispatcherModulePath+"/internal/") {
			continue
		}
		if goPackage.ImportPath == dispatcherModulePath+"/internal/architecture" {
			continue
		}
		if strings.HasPrefix(goPackage.ImportPath, dispatcherModulePath+"/internal/dispatcher") {
			continue
		}
		t.Fatalf("dispatcher internal package must live under internal/dispatcher: %s", goPackage.ImportPath)
	}
}

// Tests that the global dispatcher binary stays independent from all project-local core packages.
func TestDispatcherCommandDoesNotDependOnCoreModule(t *testing.T) {
	moduleRoot := findModuleRoot(t)
	command := exec.Command("go", "list", "-deps", "./cmd/uloop-dispatcher")
	command.Dir = moduleRoot
	output, err := command.Output()
	if err != nil {
		t.Fatalf("go list failed: %v", err)
	}

	for _, dependency := range strings.Split(strings.TrimSpace(string(output)), "\n") {
		if strings.HasPrefix(dependency, coreModulePath) {
			t.Fatalf("dispatcher command must not depend on core module package %s", dependency)
		}
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
