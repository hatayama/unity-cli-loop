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

// Tests that core onion layers only import packages from allowed inner or outer boundaries.
func TestCoreOnionLayerDependencies(t *testing.T) {
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

// Tests that core internal packages stay inside explicit core-only boundaries.
func TestCoreInternalPackagesStayInsideExplicitBoundaries(t *testing.T) {
	moduleRoot := findModuleRoot(t)
	packages := listPackages(t, moduleRoot)
	for _, goPackage := range packages {
		if !strings.HasPrefix(goPackage.ImportPath, coreModulePath+"/internal/") {
			continue
		}
		if goPackage.ImportPath == coreModulePath+"/internal/architecture" {
			continue
		}
		for _, boundary := range []string{"/internal/adapters/", "/internal/app", "/internal/application", "/internal/ports", "/internal/presentation/"} {
			if strings.Contains(goPackage.ImportPath, boundary) {
				goto nextPackage
			}
		}
		t.Fatalf("core internal package must live under adapters, app, application, ports, or presentation: %s", goPackage.ImportPath)
	nextPackage:
	}
}

// Tests that the project-local core binary stays independent from the global dispatcher module.
func TestCoreCommandDoesNotDependOnDispatcherModule(t *testing.T) {
	moduleRoot := findModuleRoot(t)
	command := exec.Command("go", "list", "-deps", "./cmd/uloop-core")
	command.Dir = moduleRoot
	output, err := command.Output()
	if err != nil {
		t.Fatalf("go list failed: %v", err)
	}

	for _, dependency := range strings.Split(strings.TrimSpace(string(output)), "\n") {
		if strings.HasPrefix(dependency, dispatcherModulePath) {
			t.Fatalf("core command must not depend on dispatcher module package %s", dependency)
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

func layerOf(importPath string) string {
	switch {
	case strings.HasPrefix(importPath, sharedModulePath+"/domain"):
		return "domain"
	case strings.HasPrefix(importPath, sharedModulePath+"/version"):
		return "version"
	case strings.HasPrefix(importPath, sharedModulePath+"/adapters"):
		return "shared-adapters"
	case importPath == coreModulePath:
		return "contract"
	case strings.HasPrefix(importPath, coreModulePath+"/internal/application"):
		return "application"
	case strings.HasPrefix(importPath, coreModulePath+"/internal/ports"):
		return "ports"
	case strings.HasPrefix(importPath, coreModulePath+"/internal/adapters"):
		return "core-adapters"
	case strings.HasPrefix(importPath, coreModulePath+"/internal/presentation"):
		return "presentation"
	case strings.HasPrefix(importPath, coreModulePath+"/internal/app"):
		return "app"
	case strings.HasPrefix(importPath, coreModulePath+"/cmd/"):
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
	case "shared-adapters":
		return targetLayer == "domain" || targetLayer == "shared-adapters"
	case "contract":
		return targetLayer == "contract"
	case "application":
		return targetLayer == "domain" || targetLayer == "ports" || targetLayer == "application"
	case "ports":
		return targetLayer == "domain" || targetLayer == "ports"
	case "core-adapters":
		return targetLayer == "domain" || targetLayer == "ports" || targetLayer == "application" || targetLayer == "shared-adapters" || targetLayer == "core-adapters"
	case "presentation":
		return targetLayer == "domain" || targetLayer == "version" || targetLayer == "contract" || targetLayer == "ports" || targetLayer == "application" || targetLayer == "shared-adapters" || targetLayer == "core-adapters" || targetLayer == "presentation"
	case "app":
		return targetLayer == "domain" || targetLayer == "version" || targetLayer == "contract" || targetLayer == "ports" || targetLayer == "application" || targetLayer == "shared-adapters" || targetLayer == "core-adapters" || targetLayer == "presentation"
	case "cmd":
		return targetLayer == "app" || importedPath == coreModulePath+"/internal/app"
	default:
		return true
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
