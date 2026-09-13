package compilecheck

import (
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

// manifestPath names where one unit's manifest lives.
func manifestPath(outputDirectoryPath string, unit CompileUnit) string {
	return filepath.Join(outputDirectoryPath, unit.Assembly.AssemblyName+resultManifestExtension)
}

// writeUnitResultManifest records what a compile ran on and what it reported.
// Why it is written through a temporary file: a run interrupted while writing would otherwise leave
// half a manifest behind, and the next run would read it as a description of outputs it never saw.
func writeUnitResultManifest(
	outputDirectoryPath string, unit CompileUnit, key string, result UnitResult,
) error {
	outputs, err := hashUnitOutputs(outputDirectoryPath, unit)
	if err != nil {
		return err
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

// hashUnitOutputs hashes the files a compile of this unit wrote, leaving out the ones it did not
// write: a compile that failed writes neither the assembly nor its reference assembly.
func hashUnitOutputs(outputDirectoryPath string, unit CompileUnit) (map[string]string, error) {
	outputs := map[string]string{}
	for _, outputPath := range []string{unit.Assembly.OutputPath, unit.Assembly.RefOutputPath} {
		if outputPath == "" {
			continue
		}
		name := filepath.Base(outputPath)
		hashed, err := hashFile(filepath.Join(outputDirectoryPath, name))
		if os.IsNotExist(err) {
			continue
		}
		if err != nil {
			return nil, err
		}
		outputs[name] = hashed
	}

	return outputs, nil
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
