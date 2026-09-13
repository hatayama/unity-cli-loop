package compilecheck

import (
	"context"
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"fmt"
	"io"
	"os"
	"path/filepath"
)

const (
	// resultManifestFormatVersion guards against reading a manifest an older or newer build wrote:
	// what belongs in the key is part of this check's code, so a manifest from another version says
	// nothing about whether this one would compile the same thing.
	resultManifestFormatVersion = 1

	resultManifestExtension   = ".result.json"
	resultManifestTempSuffix  = ".tmp"
	resultManifestPermissions = 0o600
)

// unitResultManifest is what one finished compile left behind for the next run: the inputs it ran
// on, what it reported, and the outputs it wrote.
type unitResultManifest struct {
	FormatVersion int
	InputKey      string
	Succeeded     bool
	Diagnostics   []Diagnostic
	// Outputs maps the name of each file the compile wrote in the check's output directory to its
	// content hash, so a run that finds them changed underneath compiles again.
	Outputs map[string]string
}

// reuseOrCompile reports what csc would say about one unit, replaying the previous run's answer when
// every input that decides it is still exactly as that run read it, and compiling otherwise.
func (c Compiler) reuseOrCompile(
	ctx context.Context, plan BuildPlan, unit CompileUnit,
) (UnitResult, error) {
	outputDirectoryPath := filepath.Join(c.ProjectRoot, plan.OutputDir)
	// Why an unreadable input is not an error here: the compile that follows reads the same files and
	// reports what is wrong with them in its own way. All this loses is the chance to reuse.
	key, keyErr := computeInputKey(c.ProjectRoot, c.Paths, plan, unit)
	if keyErr != nil {
		key = ""
	}
	// Why --all only skips the lookup and still records: it is the way out of a reuse that got
	// something wrong, and the ordinary run after it should still be able to reuse what it compiled.
	if key != "" && !plan.All {
		if reused, found := loadReusableResult(outputDirectoryPath, unit, key); found {
			return reused, nil
		}
	}

	// Why nothing is recorded unless csc ran to completion: a compile that was cancelled or never
	// started says nothing about the inputs, and CompileUnit has already removed the manifest of the
	// compile this one replaces, so an interrupted run leaves no record at all rather than a stale one.
	result, err := c.CompileUnit(ctx, plan, unit)
	if err != nil || key == "" || result.RawOutput != "" {
		return result, err
	}
	if writeErr := writeUnitResultManifest(outputDirectoryPath, unit, key, result); writeErr != nil {
		return UnitResult{}, writeErr
	}

	return result, nil
}

// manifestPath names where one unit's manifest lives.
func manifestPath(outputDirectoryPath string, unit CompileUnit) string {
	return filepath.Join(outputDirectoryPath, unit.Assembly.AssemblyName+resultManifestExtension)
}

// writeUnitResultManifest records what a compile ran on and what it reported, and records nothing
// when that compile claimed success without producing the outputs it was told to produce.
// Why it is written through a temporary file: a run interrupted while writing would otherwise leave
// half a manifest behind, and the next run would read it as a description of outputs it never saw.
func writeUnitResultManifest(
	outputDirectoryPath string, unit CompileUnit, key string, result UnitResult,
) error {
	outputs, complete, err := hashUnitOutputs(outputDirectoryPath, unit, result.Succeeded)
	if err != nil {
		return err
	}
	if !complete {
		return nil
	}

	content, err := json.Marshal(unitResultManifest{
		FormatVersion: resultManifestFormatVersion,
		InputKey:      key,
		Succeeded:     result.Succeeded,
		Diagnostics:   result.Diagnostics,
		Outputs:       outputs,
	})
	if err != nil {
		return fmt.Errorf("failed to record the result of %s: %w", unit.Assembly.AssemblyName, err)
	}

	path := manifestPath(outputDirectoryPath, unit)
	temporaryPath := path + resultManifestTempSuffix
	if err := os.WriteFile(temporaryPath, content, resultManifestPermissions); err != nil {
		return fmt.Errorf("failed to write %s: %w", temporaryPath, err)
	}
	if err := os.Rename(temporaryPath, path); err != nil {
		return fmt.Errorf("failed to write %s: %w", path, err)
	}

	return nil
}

// loadReusableResult reports what a previous compile of this unit found, when that compile ran on
// the same inputs and its outputs are still on disk exactly as it wrote them.
// Why anything unexpected simply means no: the caller compiles the unit, which is what it would
// have done anyway. Nothing here is worth failing a run over.
func loadReusableResult(
	outputDirectoryPath string, unit CompileUnit, key string,
) (UnitResult, bool) {
	content, err := os.ReadFile(manifestPath(outputDirectoryPath, unit))
	if err != nil {
		return UnitResult{}, false
	}

	var manifest unitResultManifest
	if err := json.Unmarshal(content, &manifest); err != nil {
		return UnitResult{}, false
	}
	if manifest.FormatVersion != resultManifestFormatVersion || manifest.InputKey != key || key == "" {
		return UnitResult{}, false
	}
	if !outputsStillMatch(outputDirectoryPath, manifest.Outputs) {
		return UnitResult{}, false
	}

	// Why the recorded outcome is replayed rather than assumed successful: a compile that reported
	// errors reports them again until one of its inputs moves, and swallowing them here would turn a
	// broken project into a clean run.
	return UnitResult{
		Assembly:    unit.Assembly.AssemblyName,
		Diagnostics: manifest.Diagnostics,
		Succeeded:   manifest.Succeeded,
		Reused:      true,
	}, true
}

// outputsStillMatch reports whether every file a compile wrote is still there and still holds what
// it wrote.
func outputsStillMatch(outputDirectoryPath string, outputs map[string]string) bool {
	for name, recordedHash := range outputs {
		currentHash, err := hashFile(filepath.Join(outputDirectoryPath, name))
		if err != nil || currentHash != recordedHash {
			return false
		}
	}

	return true
}

// hashUnitOutputs hashes the files a compile of this unit wrote.
// Why a missing output is tolerated only for a failed compile: that is the compile which writes
// neither the assembly nor its reference assembly. A successful compile that left one of them out
// is not a compile whose result describes anything - recording it would replay a success backed by
// files no run ever produced, and the outputs check would pass because it has nothing to check.
func hashUnitOutputs(
	outputDirectoryPath string, unit CompileUnit, succeeded bool,
) (map[string]string, bool, error) {
	outputs := map[string]string{}
	for _, outputPath := range []string{unit.Assembly.OutputPath, unit.Assembly.RefOutputPath} {
		if outputPath == "" {
			continue
		}
		name := filepath.Base(outputPath)
		hashed, err := hashFile(filepath.Join(outputDirectoryPath, name))
		if os.IsNotExist(err) {
			if succeeded {
				return nil, false, nil
			}

			continue
		}
		if err != nil {
			return nil, false, err
		}
		outputs[name] = hashed
	}

	return outputs, true, nil
}

// hashFile reports what a file holds, as a hash.
func hashFile(path string) (string, error) {
	file, err := os.Open(path)
	if err != nil {
		return "", err
	}
	defer func() { _ = file.Close() }()

	digest := sha256.New()
	if _, err := io.Copy(digest, file); err != nil {
		return "", fmt.Errorf("failed to read %s: %w", path, err)
	}

	return hex.EncodeToString(digest.Sum(nil)), nil
}
