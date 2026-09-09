package compilecheck

import (
	"fmt"
	"os"
	"path/filepath"
	"sort"
	"time"
)

const (
	changeReasonAssemblyAdded   = "was added after the last Unity build"
	changeReasonAssemblyRemoved = "was removed after the last Unity build"
)

// predefinedAssemblyNames are the assemblies Unity builds without an .asmdef of their own.
var predefinedAssemblyNames = map[string]bool{
	"Assembly-CSharp":                  true,
	"Assembly-CSharp-firstpass":        true,
	"Assembly-CSharp-Editor":           true,
	"Assembly-CSharp-Editor-firstpass": true,
}

// DetectAssemblySetChange refuses a run whose set of assemblies no longer matches the last build.
// Why it cannot be replayed: an added or removed assembly definition changes which assemblies exist
// and which sources belong to each of them, and the response files describe the old set only.
func DetectAssemblySetChange(
	projectRoot string, dagDir string, graph assemblyGraph,
	assemblyDefinitions map[string]AssemblyDefinition,
) error {
	buildTime, err := newestBuildTime(projectRoot, dagDir)
	if err != nil {
		return err
	}

	if name, found := addedAssemblyName(graph, assemblyDefinitions, buildTime); found {
		return fmt.Errorf(
			"assembly definition %s %s; %s", name, changeReasonAssemblyAdded, runCompileFirstAdvice)
	}
	if name, found := removedAssemblyName(graph, assemblyDefinitions); found {
		return fmt.Errorf(
			"assembly definition %s %s; %s", name, changeReasonAssemblyRemoved, runCompileFirstAdvice)
	}

	return nil
}

// addedAssemblyName names an assembly definition written after the last build that Bee never saw.
// Why the modification time matters: an assembly definition excluded by platform or test settings
// legitimately has no response file, so the absence alone would reject healthy projects.
func addedAssemblyName(
	graph assemblyGraph, assemblyDefinitions map[string]AssemblyDefinition, buildTime time.Time,
) (string, bool) {
	names := make([]string, 0, len(assemblyDefinitions))
	for name := range assemblyDefinitions {
		names = append(names, name)
	}
	sort.Strings(names)

	for _, name := range names {
		if _, built := graph.byName[name]; built {
			continue
		}
		written, err := modificationTime(assemblyDefinitions[name].Path)
		if err != nil || !written.After(buildTime) {
			continue
		}

		return name, true
	}

	return "", false
}

// removedAssemblyName names an assembly Bee built whose assembly definition is gone from the project.
func removedAssemblyName(
	graph assemblyGraph, assemblyDefinitions map[string]AssemblyDefinition,
) (string, bool) {
	for _, name := range graph.names {
		if predefinedAssemblyNames[name] {
			continue
		}
		if _, defined := assemblyDefinitions[name]; !defined {
			return name, true
		}
	}

	return "", false
}

// newestBuildTime is when the Editor last wrote an assembly into the dag, the baseline every
// project change is compared against.
func newestBuildTime(projectRoot string, dagDir string) (time.Time, error) {
	dagDirectoryPath := filepath.Join(projectRoot, dagDir)
	matches, err := filepath.Glob(filepath.Join(dagDirectoryPath, "*"+assemblyExtension))
	if err != nil {
		return time.Time{}, fmt.Errorf("failed to list assemblies in %s: %w", dagDirectoryPath, err)
	}

	newest := time.Time{}
	for _, path := range matches {
		info, statErr := os.Stat(path)
		if statErr != nil {
			continue
		}
		if info.ModTime().After(newest) {
			newest = info.ModTime()
		}
	}
	if newest.IsZero() {
		return time.Time{}, fmt.Errorf(
			"the Unity Editor has never built %s; %s", dagDirectoryPath, runCompileFirstAdvice)
	}

	return newest, nil
}
