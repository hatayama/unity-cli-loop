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
	context AssemblyContext,
) error {
	baseline, err := lastBuildTime(projectRoot, rsp, dagDir)
	if err != nil {
		return err
	}

	if asmdef != nil {
		if changeErr := detectAssemblyDefinitionChange(
			*asmdef, rsp, dagDir, baseline, context); changeErr != nil {
			return changeErr
		}
	}

	if rsp.AdditionalFile != "" && !fileExists(filepath.Join(projectRoot, rsp.AdditionalFile)) {
		return unityBuildRequired("the Bee artifacts are incomplete for %s", rsp.AssemblyName)
	}

	return nil
}

// detectAssemblyDefinitionChange compares an assembly definition against the response file once its
// timestamp says it may have moved.
// Why the timestamp is only a trigger: switching branches rewrites every .asmdef file without
// changing its content, and Unity's incremental build hashes content, so it rebuilds nothing and
// the timestamp alone would refuse the project forever.
func detectAssemblyDefinitionChange(
	asmdef AssemblyDefinition, rsp ResponseFile, dagDir string,
	baseline time.Time, context AssemblyContext,
) error {
	asmdefTime, err := modificationTime(asmdef.Path)
	if err != nil {
		return err
	}
	if !asmdefTime.After(baseline) {
		return nil
	}

	contract, readErr := readAssemblyDefinitionContract(asmdef.Path)
	if readErr != nil {
		return readErr
	}
	if reason := detectContractDisagreement(contract, rsp, dagDir, context); reason != "" {
		return unityBuildRequired("assembly definition %s %s", asmdef.Name, reason)
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
		sourceTime, statErr := modificationTime(sourcePath(projectRoot, source))
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
		return time.Time{}, unityBuildRequired(
			"the Unity Editor has never built %s", rsp.AssemblyName)
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
