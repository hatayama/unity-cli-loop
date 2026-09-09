package compilecheck

import (
	"fmt"
	"os"
	"path/filepath"
	"time"
)

const (
	assemblyExtension = ".dll"

	changeReasonSourceNewer   = "source newer than last build"
	changeReasonSourceAdded   = "new source file"
	changeReasonSourceRemoved = "source removed"

	runCompileFirstAdvice = "run `uloop compile` first"
)

// ChangeReport says whether an assembly's inputs moved since Unity last built it, and what moved.
type ChangeReport struct {
	Changed bool
	Reason  string
}

// DetectStructuralChange fails when the response file can no longer describe the assembly.
// Why this is fatal rather than a rebuild: references, defineConstraints and includePlatforms live
// in the assembly definition and never reach the response file, so compiling anyway would report
// diagnostics for a configuration the project no longer has.
func DetectStructuralChange(
	projectRoot string, rsp ResponseFile, asmdef *AssemblyDefinition, dagDir string,
) error {
	baseline, err := lastBuildTime(projectRoot, rsp, dagDir)
	if err != nil {
		return err
	}

	if asmdef != nil {
		asmdefTime, statErr := modificationTime(asmdef.Path)
		if statErr != nil {
			return statErr
		}
		if asmdefTime.After(baseline) {
			return fmt.Errorf(
				"assembly definition %s changed after the last Unity build; %s",
				asmdef.Name, runCompileFirstAdvice)
		}
	}

	if rsp.AdditionalFile != "" && !fileExists(filepath.Join(projectRoot, rsp.AdditionalFile)) {
		return fmt.Errorf("the Bee artifacts are incomplete for %s; %s", rsp.AssemblyName, runCompileFirstAdvice)
	}

	return nil
}

// DetectSourceChange reports whether the assembly's sources changed since Unity last built it.
func DetectSourceChange(
	projectRoot string, rsp ResponseFile, sources []string, dagDir string,
) (ChangeReport, error) {
	baseline, err := lastBuildTime(projectRoot, rsp, dagDir)
	if err != nil {
		return ChangeReport{}, err
	}

	if report, changed := compareSourceSets(rsp.Sources, sources); changed {
		return report, nil
	}

	for _, source := range sources {
		sourceTime, statErr := modificationTime(filepath.Join(projectRoot, source))
		if statErr != nil {
			return ChangeReport{}, statErr
		}
		if sourceTime.After(baseline) {
			return ChangeReport{Changed: true, Reason: changeReasonSourceNewer}, nil
		}
	}

	return ChangeReport{}, nil
}

// compareSourceSets reports whether the rebuilt source list gained or lost files.
func compareSourceSets(recorded []string, current []string) (ChangeReport, bool) {
	recordedSet := map[string]bool{}
	for _, source := range recorded {
		recordedSet[source] = true
	}
	for _, source := range current {
		if !recordedSet[source] {
			return ChangeReport{Changed: true, Reason: changeReasonSourceAdded}, true
		}
	}

	currentSet := map[string]bool{}
	for _, source := range current {
		currentSet[source] = true
	}
	for _, source := range recorded {
		if !currentSet[source] {
			return ChangeReport{Changed: true, Reason: changeReasonSourceRemoved}, true
		}
	}

	return ChangeReport{}, false
}

// lastBuildTime reads when Unity last wrote this assembly, the baseline every check compares against.
func lastBuildTime(projectRoot string, rsp ResponseFile, dagDir string) (time.Time, error) {
	assemblyPath := filepath.Join(projectRoot, dagDir, rsp.AssemblyName+assemblyExtension)
	info, err := os.Stat(assemblyPath)
	if err != nil {
		return time.Time{}, fmt.Errorf(
			"the Unity Editor has never built %s; %s", rsp.AssemblyName, runCompileFirstAdvice)
	}

	return info.ModTime(), nil
}

// modificationTime reads one file's modification time.
func modificationTime(path string) (time.Time, error) {
	info, err := os.Stat(path)
	if err != nil {
		return time.Time{}, fmt.Errorf("failed to inspect %s: %w", path, err)
	}

	return info.ModTime(), nil
}
