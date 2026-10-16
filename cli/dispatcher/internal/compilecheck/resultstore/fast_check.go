package resultstore

import (
	"encoding/json"
	"fmt"
	"os"
	"path/filepath"
)

const (
	// fastResultFormatVersion guards against replaying a check an older or newer build recorded: what
	// the record holds is part of this check's code.
	fastResultFormatVersion = 1

	fastResultExtension = ".fastresult.json"
)

// FastCheckResult is what one check of an assembly's changed files reported.
type FastCheckResult[D any] struct {
	Succeeded          bool
	Diagnostics        []D
	ChangedFiles       []string
	UncheckedCodes     []string
	GeneratorsNotRerun bool
}

// fastCheckRecord is what one check of an assembly's changed files left behind for the next run: the
// inputs, the baseline and the worker build it checked with, and what it reported.
type fastCheckRecord[D any] struct {
	FormatVersion      int
	InputKey           string
	BaselineInputKey   string
	WorkerKey          string
	Succeeded          bool
	Diagnostics        []D
	ChangedFiles       []string
	UncheckedCodes     []string
	GeneratorsNotRerun bool
}

// fastResultPath names where one assembly's recorded check lives.
func fastResultPath(directoryPath string, assembly string) string {
	return filepath.Join(directoryPath, assembly+fastResultExtension)
}

// ReusableFastCheck reports the recorded check of the assembly when it answered for these inputs
// (key), against this baseline, with this worker build. Anything unexpected means no.
// Why anything unexpected simply means no: the caller checks the changed files again, which is what
// it would have done anyway.
func (s Store[D]) ReusableFastCheck(
	assembly string, key string, baseline Baseline[D], workerKey string,
) (FastCheckResult[D], bool) {
	if key == "" {
		return FastCheckResult[D]{}, false
	}
	content, err := os.ReadFile(fastResultPath(s.directoryPath(), assembly))
	if err != nil {
		return FastCheckResult[D]{}, false
	}

	var record fastCheckRecord[D]
	if err := json.Unmarshal(content, &record); err != nil {
		return FastCheckResult[D]{}, false
	}
	if record.FormatVersion != fastResultFormatVersion || record.InputKey != key ||
		record.BaselineInputKey != baseline.InputKey || record.WorkerKey != workerKey {
		return FastCheckResult[D]{}, false
	}

	return FastCheckResult[D]{
		Succeeded:          record.Succeeded,
		Diagnostics:        record.Diagnostics,
		ChangedFiles:       record.ChangedFiles,
		UncheckedCodes:     record.UncheckedCodes,
		GeneratorsNotRerun: record.GeneratorsNotRerun,
	}, true
}

// RecordFastCheck records what a check of the assembly's changed files reported for these inputs,
// against this baseline, with this worker build, through a temporary file.
// Why a missing key is refused rather than recorded: a record under an empty key would later match
// another empty key, and replay a check of inputs nobody can name.
func (s Store[D]) RecordFastCheck(
	assembly string, key string, baseline Baseline[D], workerKey string, result FastCheckResult[D],
) error {
	if missing := missingFastCheckKey(key, baseline.InputKey, workerKey); missing != "" {
		return fmt.Errorf("refusing to record a changed-files check of %s without %s", assembly, missing)
	}

	content, err := json.Marshal(fastCheckRecord[D]{
		FormatVersion:      fastResultFormatVersion,
		InputKey:           key,
		BaselineInputKey:   baseline.InputKey,
		WorkerKey:          workerKey,
		Succeeded:          result.Succeeded,
		Diagnostics:        result.Diagnostics,
		ChangedFiles:       result.ChangedFiles,
		UncheckedCodes:     result.UncheckedCodes,
		GeneratorsNotRerun: result.GeneratorsNotRerun,
	})
	if err != nil {
		return fmt.Errorf("failed to record the changed-files check of %s: %w", assembly, err)
	}

	return WriteFileAtomically(s.directoryPath(), fastResultPath(s.directoryPath(), assembly), content)
}

// missingFastCheckKey names the first of the keys a check is looked up by that is empty, or returns
// "" when all three are there.
func missingFastCheckKey(key string, baselineInputKey string, workerKey string) string {
	switch {
	case key == "":
		return "an input key"
	case baselineInputKey == "":
		return "a baseline"
	case workerKey == "":
		return "a worker build"
	default:
		return ""
	}
}
