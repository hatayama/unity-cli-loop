package compilecheck

import (
	"context"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
	"testing"
	"time"
)

// blockManifestPath puts a non-empty directory where a unit's manifest belongs, so renaming the
// finished manifest into place fails on every platform.
func blockManifestPath(t *testing.T, manifestFilePath string) {
	t.Helper()
	writeFileAt(t, filepath.Join(manifestFilePath, "occupied"), "")
}

// Verifies a manifest that cannot be renamed into place is reported as a write failure and leaves
// no temporary file behind.
func TestWriteUnitResultManifestReportsAFailedRename(t *testing.T) {
	outputDirectoryPath, unit := newManifestUnit(t)
	blockManifestPath(t, manifestPath(outputDirectoryPath, unit))

	err := writeUnitResultManifest(outputDirectoryPath, unit, "key", storedResult())

	if err == nil || !strings.HasPrefix(err.Error(), "failed to write "+manifestPath(outputDirectoryPath, unit)+":") {
		t.Fatalf("err = %v", err)
	}
	matches, globErr := filepath.Glob(filepath.Join(outputDirectoryPath, "*"+resultManifestTempSuffix))
	if globErr != nil || len(matches) != 0 {
		t.Fatalf("temporary manifests left behind: %v (glob error %v)", matches, globErr)
	}
}

// Verifies a manifest cannot be written into an output directory that does not exist, which is
// reported with the manifest path.
func TestWriteUnitResultManifestReportsAMissingOutputDirectory(t *testing.T) {
	_, unit := newManifestUnit(t)
	outputDirectoryPath := filepath.Join(t.TempDir(), "missing")

	err := writeUnitResultManifest(outputDirectoryPath, unit, "key", storedResult())

	if err == nil || !strings.HasPrefix(err.Error(), "failed to write "+manifestPath(outputDirectoryPath, unit)+":") {
		t.Fatalf("err = %v", err)
	}
}

// Verifies an output that exists but cannot be read is reported instead of being treated as absent.
func TestHashUnitOutputsReportsAnUnreadableOutput(t *testing.T) {
	outputDirectoryPath, unit := newManifestUnit(t)
	unreadablePath := filepath.Join(outputDirectoryPath, "Alpha"+assemblyExtension)
	if err := os.Remove(unreadablePath); err != nil {
		t.Fatalf("failed to remove the assembly: %v", err)
	}
	makeDirAt(t, unreadablePath)

	_, complete, err := hashUnitOutputs(outputDirectoryPath, unit, true)

	if err == nil || complete || !strings.Contains(err.Error(), unreadablePath) {
		t.Fatalf("complete=%v err=%v, want an error naming %s", complete, err, unreadablePath)
	}
}

// Verifies a unit without a reference assembly is hashed by its assembly alone.
func TestHashUnitOutputsSkipsAnAbsentReferenceAssemblyPath(t *testing.T) {
	outputDirectoryPath, unit := newManifestUnit(t)
	unit.Assembly.RefOutputPath = ""

	outputs, complete, err := hashUnitOutputs(outputDirectoryPath, unit, true)

	if err != nil || !complete || len(outputs) != 1 || outputs["Alpha"+assemblyExtension] == "" {
		t.Fatalf("outputs=%v complete=%v err=%v", outputs, complete, err)
	}
}

// Verifies a compile whose result cannot be recorded fails the run rather than reporting a result
// the next run could not reuse.
func TestCompileUnitsFailsWhenAResultCannotBeRecorded(t *testing.T) {
	project := newCachedSchedulerProject(t, nil, "Alpha")
	recorder := newCachedSchedulerRecorder("Alpha")
	compileRunner := recorder.runner()
	compiler := Compiler{
		Paths:       project.paths,
		ProjectRoot: project.root,
		Timeout:     time.Minute,
		// Why the block is placed during the compile: the compile removes any previous manifest before
		// it starts, so only a path occupied after that point reaches the manifest write.
		Run: func(ctx context.Context, cmd *exec.Cmd) (string, string, int, error) {
			stdout, stderr, exitCode, err := compileRunner(ctx, cmd)
			blockManifestPath(t, project.manifestPathOf("Alpha"))
			return stdout, stderr, exitCode, err
		},
	}

	_, err := compileUnits(context.Background(), compiler, project.plan, 1)

	if err == nil || !strings.HasPrefix(err.Error(), "failed to write "+project.manifestPathOf("Alpha")+":") {
		t.Fatalf("err = %v, want the manifest write failure", err)
	}
}
