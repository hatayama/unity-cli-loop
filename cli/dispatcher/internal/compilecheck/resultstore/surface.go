package resultstore

import (
	"encoding/json"
	"fmt"
	"os"
	"path/filepath"
)

const (
	// surfaceRecordFormatVersion guards against reading a verdict an older or newer build recorded.
	// Version 1 had no changed names.
	surfaceRecordFormatVersion = 2

	surfaceRecordExtension = ".surface.json"
)

// SurfaceInputs names the comparison a surface verdict answered: the two reference assemblies, by
// content, and the worker build that compared them.
type SurfaceInputs struct {
	ProducedSha256   string
	UnityBuildSha256 string
	WorkerKey        string
}

// SurfaceVerdict is what the worker answered about whether two reference assemblies show their
// dependents the same API.
type SurfaceVerdict struct {
	Same   bool
	Detail string
	// ChangedNames and UnnamedReason are what a different answer said its change touches.
	ChangedNames  []string
	UnnamedReason string
}

// surfaceRecord is one recorded surface verdict with the comparison it answered.
type surfaceRecord struct {
	FormatVersion    int
	ProducedSha256   string
	UnityBuildSha256 string
	WorkerKey        string
	Same             bool
	Detail           string
	ChangedNames     []string
	UnnamedReason    string
}

func surfaceRecordPath(directoryPath string, assembly string) string {
	return filepath.Join(directoryPath, assembly+surfaceRecordExtension)
}

// ReusableSurfaceVerdict reports the recorded verdict of the assembly when it answered this very
// comparison. Anything unexpected means no.
// Why the record is not removed with the assembly's outputs: it is keyed by the content of both
// reference assemblies, so a record left behind can only ever answer the comparison it was made for.
func (s Store[D]) ReusableSurfaceVerdict(assembly string, inputs SurfaceInputs) (SurfaceVerdict, bool) {
	if inputs.ProducedSha256 == "" || inputs.UnityBuildSha256 == "" || inputs.WorkerKey == "" {
		return SurfaceVerdict{}, false
	}
	content, err := os.ReadFile(surfaceRecordPath(s.directoryPath(), assembly))
	if err != nil {
		return SurfaceVerdict{}, false
	}

	record := surfaceRecord{}
	if err := json.Unmarshal(content, &record); err != nil {
		return SurfaceVerdict{}, false
	}
	if record.FormatVersion != surfaceRecordFormatVersion || record.ProducedSha256 != inputs.ProducedSha256 ||
		record.UnityBuildSha256 != inputs.UnityBuildSha256 || record.WorkerKey != inputs.WorkerKey {
		return SurfaceVerdict{}, false
	}

	return SurfaceVerdict{
		Same: record.Same, Detail: record.Detail, ChangedNames: record.ChangedNames, UnnamedReason: record.UnnamedReason,
	}, true
}

// RecordSurfaceVerdict records what the worker answered for one comparison of the assembly's
// reference assemblies, replacing the verdict of any earlier comparison.
func (s Store[D]) RecordSurfaceVerdict(assembly string, inputs SurfaceInputs, verdict SurfaceVerdict) error {
	content, err := json.Marshal(surfaceRecord{
		FormatVersion:    surfaceRecordFormatVersion,
		ProducedSha256:   inputs.ProducedSha256,
		UnityBuildSha256: inputs.UnityBuildSha256,
		WorkerKey:        inputs.WorkerKey,
		Same:             verdict.Same,
		Detail:           verdict.Detail,
		ChangedNames:     verdict.ChangedNames,
		UnnamedReason:    verdict.UnnamedReason,
	})
	if err != nil {
		return fmt.Errorf("failed to encode the surface verdict of %s: %w", assembly, err)
	}

	return WriteFileAtomically(s.directoryPath(), surfaceRecordPath(s.directoryPath(), assembly), content)
}
