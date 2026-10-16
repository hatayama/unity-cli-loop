// Package resultstore keeps what earlier runs of compile-check recorded for each assembly in one
// output directory, and is the only code that reads or writes those records.
//
// A store keeps four records per assembly:
//   - the result of its last compile: the inputs it ran on (as a key), what it reported, and a hash
//     of every output it wrote;
//   - the baseline of the changed-files check: what the last successful full compile saw of each
//     source, its non-source inputs and the worker build that fingerprinted it;
//   - the generator output that full compile saved;
//   - the result of its last changed-files check: the inputs, the baseline and the worker build it
//     checked with, and what it reported.
//
// Contract:
//   - Records are written through a temporary file and renamed into place, so a run stopped while
//     writing never leaves half a record behind.
//   - A compile result is reusable only under the key it was recorded with, and only while every
//     output it hashed still holds those bytes. It may still apply while some project references are
//     unknown only when every other part of that key, and those outputs, are as recorded.
//   - A baseline is handed out only when the compile result recorded with it is still on disk: the
//     same key, a successful compile, and outputs that still match. Otherwise LoadBaseline says why
//     there is none, in one of these reasons:
//     "no baseline: no full compile of this assembly is on record",
//     "no baseline: the last full compile had errors; the first full compile without errors records one",
//     "no baseline: the last full compile did not record one",
//     "no baseline: it was recorded by another version of compile-check",
//     "baseline compile is no longer on disk".
//   - A changed-files check is reusable only under the input key, the baseline and the worker build
//     it was recorded with, and is never recorded without all three.
//   - Forget removes every kind of record of an assembly. It does not touch the assembly's outputs,
//     and the store never creates the generator output directory; it only names where it is.
package resultstore

import (
	"crypto/sha256"
	"encoding/hex"
	"fmt"
	"io"
	"os"
	"path/filepath"
)

const resultManifestTempSuffix = ".tmp"

// Store reads and writes the records kept in one output directory. D is the type of a diagnostic a
// compile reported; the store keeps diagnostics as they were given and never reads them.
type Store[D any] struct {
	projectRoot string
	// outputDir is relative to the project root, which is how the records name the generator output.
	outputDir string
}

// Open opens the records kept in outputDir, a directory relative to projectRoot.
func Open[D any](projectRoot string, outputDir string) Store[D] {
	return Store[D]{projectRoot: projectRoot, outputDir: outputDir}
}

// directoryPath names the output directory the records live in.
func (s Store[D]) directoryPath() string {
	return filepath.Join(s.projectRoot, s.outputDir)
}

// Forget removes every record of the assembly: its compile result, its baseline, its saved generator
// output, and its last changed-files check.
func (s Store[D]) Forget(assembly string) error {
	names := []string{
		// Why the compile result goes with the outputs: it describes those very files, so leaving it
		// behind would let a later run replay diagnostics for outputs this one removed.
		assembly + resultManifestExtension,
		// The baseline and the generator output describe the same compile as the compile result.
		assembly + baselineRecordExtension,
	}
	for _, name := range names {
		path := filepath.Join(s.directoryPath(), name)
		if err := os.Remove(path); err != nil && !os.IsNotExist(err) {
			return fmt.Errorf("failed to remove %s: %w", path, err)
		}
	}

	generatedPath := filepath.Join(s.directoryPath(), assembly+generatedDirectoryExtension)
	if err := os.RemoveAll(generatedPath); err != nil {
		return fmt.Errorf("failed to remove %s: %w", generatedPath, err)
	}

	// The last changed-files check was checked against the compile whose records are going.
	fastResult := fastResultPath(s.directoryPath(), assembly)
	if err := os.Remove(fastResult); err != nil && !os.IsNotExist(err) {
		return fmt.Errorf("failed to remove %s: %w", fastResult, err)
	}

	return nil
}

// WriteFileAtomically writes content to path through a temporary file in directoryPath.
func WriteFileAtomically(directoryPath string, path string, content []byte) error {
	// Why the temporary name is unique per writer: two runs of the same project writing the same
	// file at once must not rename each other's half-written file into place.
	temporary, err := os.CreateTemp(directoryPath, filepath.Base(path)+".*"+resultManifestTempSuffix)
	if err != nil {
		return fmt.Errorf("failed to write %s: %w", path, err)
	}
	temporaryPath := temporary.Name()
	if _, err := temporary.Write(content); err != nil {
		_ = temporary.Close()
		_ = os.Remove(temporaryPath)

		return fmt.Errorf("failed to write %s: %w", temporaryPath, err)
	}
	if err := temporary.Close(); err != nil {
		_ = os.Remove(temporaryPath)

		return fmt.Errorf("failed to write %s: %w", temporaryPath, err)
	}
	if err := os.Rename(temporaryPath, path); err != nil {
		_ = os.Remove(temporaryPath)

		return fmt.Errorf("failed to write %s: %w", path, err)
	}

	return nil
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

// projectPath resolves a path relative to the project root, leaving an absolute one as it is.
func projectPath(projectRoot string, path string) string {
	if filepath.IsAbs(path) {
		return path
	}

	return filepath.Join(projectRoot, path)
}
