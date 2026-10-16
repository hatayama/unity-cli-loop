package resultstore

import (
	"encoding/json"
	"fmt"
	"io/fs"
	"os"
	"path/filepath"
)

const (
	baselineRecordFormatVersion = 3
	baselineRecordExtension     = ".fastcheck.json"
	generatedDirectoryExtension = ".generated"
)

// KeyDigest is a key over several kinds of input together with a digest of each kind, so a change
// of the key can be told by kind.
type KeyDigest struct {
	Key string
	// Parts maps each kind of input to its digest.
	Parts map[string]string
}

// BaselineFile describes one source as the full compile saw it.
type BaselineFile struct {
	ContentHash string
	Fingerprint string
}

// BaselineInputs is what the caller knows about the full compile a baseline describes: its input
// key, its key over every input but the source contents, the worker build that fingerprinted its
// sources, and each source.
type BaselineInputs struct {
	InputKey     string
	NonSourceKey KeyDigest
	WorkerKey    string
	Files        map[string]BaselineFile
}

// Baseline is a recorded baseline together with what its full compile reported.
type Baseline[D any] struct {
	InputKey     string
	NonSourceKey KeyDigest
	WorkerKey    string
	Files        map[string]BaselineFile
	// GeneratedDirectory is relative to the project root and slash-separated, as recorded.
	GeneratedDirectory string
	// Diagnostics is what the full compile this baseline describes reported.
	Diagnostics    []D
	generatedFiles map[string]string
}

// baselineRecord is what a successful full compile leaves for the next changed-files check.
type baselineRecord struct {
	FormatVersion int
	// InputKey is the key of the full compile this baseline describes; its manifest carries the same.
	InputKey string
	// NonSourceKey covers every input but the source contents, so any other change compiles in full.
	NonSourceKey KeyDigest
	// WorkerKey names the worker build that computed the fingerprints.
	WorkerKey          string
	GeneratedDirectory string // relative to the project root
	// GeneratedFiles maps each generated file, relative to GeneratedDirectory, to its content hash,
	// so a check never runs without the generator output its baseline compile had.
	GeneratedFiles map[string]string
	Files          map[string]BaselineFile
}

// GeneratedDirectory names, relative to the project root, where a full compile saves the generator
// output of an assembly.
func (s Store[D]) GeneratedDirectory(assembly string) string {
	return filepath.Join(s.outputDir, assembly+generatedDirectoryExtension)
}

// anotherVersionReason says a baseline, or the compile it describes, was recorded by another version.
const anotherVersionReason = "no baseline: it was recorded by another version of compile-check"

// LoadBaseline reads the baseline of an assembly together with what the full compile it describes
// reported, or says why there is none to use.
func (s Store[D]) LoadBaseline(assembly string) (Baseline[D], string) {
	content, err := os.ReadFile(baselineRecordPath(s.directoryPath(), assembly))
	if err != nil {
		return Baseline[D]{}, s.missingBaselineReason(assembly)
	}

	var record baselineRecord
	if err := json.Unmarshal(content, &record); err != nil || record.FormatVersion != baselineRecordFormatVersion {
		return Baseline[D]{}, anotherVersionReason
	}

	// In the normal flow the record and the manifest are written together under one key, and a full
	// compile removes both, so the checks below other than the format version only catch a state
	// someone changed by hand.
	const goneFromDisk = "baseline compile is no longer on disk"
	manifestContent, err := os.ReadFile(manifestPath(s.directoryPath(), assembly))
	if err != nil {
		return Baseline[D]{}, goneFromDisk
	}

	var manifest unitResultManifest[D]
	if err := json.Unmarshal(manifestContent, &manifest); err != nil {
		return Baseline[D]{}, goneFromDisk
	}
	// Why a manifest of another format version ends the baseline too: a changed-files check leaves the
	// outputs of the full compile in place, so a baseline would hand dependents what an older version
	// compiled long after the manifest itself stopped being reused.
	if manifest.FormatVersion != resultManifestFormatVersion {
		return Baseline[D]{}, anotherVersionReason
	}
	if manifest.InputKey != record.InputKey || !manifest.Succeeded ||
		!outputsStillMatch(s.directoryPath(), manifest.Outputs) {
		return Baseline[D]{}, goneFromDisk
	}

	return Baseline[D]{
		InputKey:           record.InputKey,
		NonSourceKey:       record.NonSourceKey,
		WorkerKey:          record.WorkerKey,
		Files:              record.Files,
		GeneratedDirectory: record.GeneratedDirectory,
		Diagnostics:        manifest.Diagnostics,
		generatedFiles:     record.GeneratedFiles,
	}, ""
}

// missingBaselineReason says why an assembly has no baseline, from the manifest of its last full
// compile.
// Why the reasons are told apart: a full compile with errors drops the baseline an earlier compile
// left, and a reason that read as "never compiled" sent users looking for a compile that had run.
// Why a missing manifest says nothing about what happened: the records of a unit are removed only
// when its next compile starts, so a compile that never finished leaves nothing on disk, and
// nothing tells that apart from no compile at all.
func (s Store[D]) missingBaselineReason(assembly string) string {
	const notOnRecord = "no baseline: no full compile of this assembly is on record"
	content, err := os.ReadFile(manifestPath(s.directoryPath(), assembly))
	if err != nil {
		return notOnRecord
	}

	var manifest unitResultManifest[D]
	if err := json.Unmarshal(content, &manifest); err != nil {
		return notOnRecord
	}
	if !manifest.Succeeded {
		return "no baseline: the last full compile had errors; the first full compile without errors records one"
	}

	return "no baseline: the last full compile did not record one"
}

// GeneratorOutputUnchanged reports whether the generator output on disk is exactly what the
// baseline recorded: no file missing, added, or changed.
func (s Store[D]) GeneratorOutputUnchanged(baseline Baseline[D]) bool {
	return generatedFilesMatch(projectPath(s.projectRoot, baseline.GeneratedDirectory), baseline.generatedFiles)
}

// RecordBaseline records what the next changed-files check of an assembly compares against, with
// the generator output its full compile saved.
func (s Store[D]) RecordBaseline(assembly string, inputs BaselineInputs) error {
	generatedDirectory := s.GeneratedDirectory(assembly)
	generatedFiles, err := hashGeneratedFiles(filepath.Join(s.projectRoot, generatedDirectory))
	if err != nil {
		return err
	}

	return writeBaselineRecord(s.directoryPath(), assembly, baselineRecord{
		FormatVersion:      baselineRecordFormatVersion,
		InputKey:           inputs.InputKey,
		NonSourceKey:       inputs.NonSourceKey,
		WorkerKey:          inputs.WorkerKey,
		GeneratedDirectory: filepath.ToSlash(generatedDirectory),
		GeneratedFiles:     generatedFiles,
		Files:              inputs.Files,
	})
}

// hashGeneratedFiles hashes every generated C# file under directoryPath, keyed by its slash-separated
// path relative to it. A missing directory holds no files.
func hashGeneratedFiles(directoryPath string) (map[string]string, error) {
	hashes := map[string]string{}
	err := filepath.WalkDir(directoryPath, func(path string, entry fs.DirEntry, walkErr error) error {
		if walkErr != nil {
			if os.IsNotExist(walkErr) && path == directoryPath {
				return filepath.SkipDir
			}

			return walkErr
		}
		if entry.IsDir() || filepath.Ext(path) != ".cs" {
			return nil
		}
		relative, err := filepath.Rel(directoryPath, path)
		if err != nil {
			return err
		}
		hash, err := hashFile(path)
		if err != nil {
			return err
		}
		hashes[filepath.ToSlash(relative)] = hash

		return nil
	})
	if err != nil {
		return nil, fmt.Errorf("failed to read the generator output: %w", err)
	}

	return hashes, nil
}

// generatedFilesMatch reports whether the generator output on disk is exactly what the baseline
// recorded: no file missing, added, or changed.
// Why: the worker reads this output instead of running the generators, and checking without it
// reports errors wherever code uses what the generators produce.
func generatedFilesMatch(directoryPath string, recorded map[string]string) bool {
	current, err := hashGeneratedFiles(directoryPath)
	if err != nil || len(current) != len(recorded) {
		return false
	}
	for name, hash := range recorded {
		if current[name] != hash {
			return false
		}
	}

	return true
}

// writeBaselineRecord writes a baseline through a temporary file, like the manifest.
func writeBaselineRecord(directoryPath string, assembly string, record baselineRecord) error {
	content, err := json.Marshal(record)
	if err != nil {
		return fmt.Errorf("failed to record the baseline of %s: %w", assembly, err)
	}

	return WriteFileAtomically(directoryPath, baselineRecordPath(directoryPath, assembly), content)
}

// baselineRecordPath names where one assembly's baseline lives.
func baselineRecordPath(directoryPath string, assembly string) string {
	return filepath.Join(directoryPath, assembly+baselineRecordExtension)
}
