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
	// changeReasonUnityBuildFailed is why an assembly is compiled although its artifact is newer
	// than everything it compiles from: that artifact is not what its current sources produce.
	changeReasonUnityBuildFailed = "last Unity build failed"
	// changeReasonUnityArtifactMissing is why an assembly with no artifact at all is compiled rather
	// than refused: Unity 6's Bee deletes the assembly it failed to compile, and an assembly its
	// upstream failure kept from ever being compiled never had one.
	changeReasonUnityArtifactMissing = "no artifact from the last Unity build"

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
	baseline, artifactExists, err := unityArtifactTime(projectRoot, rsp, dagDir)
	if err != nil {
		return err
	}

	if asmdef != nil {
		if changeErr := detectAssemblyDefinitionChange(
			*asmdef, rsp, dagDir, baseline, artifactExists, context); changeErr != nil {
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
func detectAssemblyDefinitionChange(
	asmdef AssemblyDefinition, rsp ResponseFile, dagDir string,
	baseline time.Time, artifactExists bool, context AssemblyContext,
) error {
	compare, err := contractComparisonNeeded(asmdef, baseline, artifactExists)
	if err != nil {
		return err
	}
	if !compare {
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

// contractComparisonNeeded reports whether the assembly definition has to be read and compared
// against the response file.
// Why the timestamp is only a trigger: switching branches rewrites every .asmdef file without
// changing its content, and Unity's incremental build hashes content, so it rebuilds nothing and
// the timestamp alone would refuse the project forever.
// Why a missing artifact always compares: the trigger needs a baseline to be newer than, and with
// none left the comparison is the only thing that can still refuse a configuration the response
// file no longer describes.
func contractComparisonNeeded(
	asmdef AssemblyDefinition, baseline time.Time, artifactExists bool,
) (bool, error) {
	if !artifactExists {
		return true, nil
	}

	asmdefTime, err := modificationTime(asmdef.Path)
	if err != nil {
		return false, err
	}

	return asmdefTime.After(baseline), nil
}

// DetectSourceChange reports whether the assembly's sources changed since Unity last built it.
func DetectSourceChange(
	projectRoot string, rsp ResponseFile, sources []string, dagDir string,
) (ChangeReport, error) {
	baseline, artifactExists, err := unityArtifactTime(projectRoot, rsp, dagDir)
	if err != nil {
		return ChangeReport{}, err
	}
	// Why a missing artifact ends the check here: the source set and the timestamps are compared
	// against what the last build produced, and there is nothing left to compare them against. The
	// assembly has to be compiled whatever they would have said.
	if !artifactExists {
		return ChangeReport{Changed: true, Reason: changeReasonUnityArtifactMissing}, nil
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

// unityArtifactTime reads when Unity last wrote this assembly, the baseline every check compares
// against, and reports whether that artifact is there at all.
// Why a missing artifact is not refused here: Unity 6's Bee deletes the assembly whose compiler
// step failed, and an assembly an upstream failure kept from ever being compiled never had one.
// Bee writes the response file before the compiler runs, so what this check compiles from survives
// either case. A dag holding no assembly at all is still refused, in DetectAssemblySetChange.
func unityArtifactTime(
	projectRoot string, rsp ResponseFile, dagDir string,
) (time.Time, bool, error) {
	assemblyPath := filepath.Join(projectRoot, dagDir, rsp.AssemblyName+assemblyExtension)
	info, err := os.Stat(assemblyPath)
	if os.IsNotExist(err) {
		return time.Time{}, false, nil
	}
	if err != nil {
		return time.Time{}, false, fmt.Errorf("failed to inspect %s: %w", assemblyPath, err)
	}

	return info.ModTime(), true, nil
}

// modificationTime reads one file's modification time.
func modificationTime(path string) (time.Time, error) {
	info, err := os.Stat(path)
	if err != nil {
		return time.Time{}, fmt.Errorf("failed to inspect %s: %w", path, err)
	}

	return info.ModTime(), nil
}
