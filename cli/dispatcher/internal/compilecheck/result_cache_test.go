package compilecheck

import (
	"encoding/json"
	"os"
	"path/filepath"
	"testing"
)

// newManifestUnit prepares one compiled unit together with the outputs its compile left behind.
func newManifestUnit(t *testing.T) (string, CompileUnit) {
	t.Helper()
	outputDirectoryPath := t.TempDir()
	unit := CompileUnit{Assembly: ResponseFile{
		AssemblyName:  "Alpha",
		OutputPath:    filepath.Join(planDagDirectory, "Alpha"+assemblyExtension),
		RefOutputPath: filepath.Join(planDagDirectory, "Alpha"+referenceAssemblyExtension),
	}}
	writeOutputFile(t, outputDirectoryPath, "Alpha"+assemblyExtension, "assembly")
	writeOutputFile(t, outputDirectoryPath, "Alpha"+referenceAssemblyExtension, "surface")

	return outputDirectoryPath, unit
}

// writeOutputFile writes one file into the check's own output directory.
func writeOutputFile(t *testing.T, outputDirectoryPath string, name string, content string) {
	t.Helper()
	if err := os.WriteFile(
		filepath.Join(outputDirectoryPath, name), []byte(content), 0o600); err != nil {
		t.Fatalf("failed to write %s: %v", name, err)
	}
}

// storedResult is the result the tests record and expect to read back unchanged.
func storedResult() UnitResult {
	return UnitResult{
		Assembly:  "Alpha",
		Succeeded: false,
		Diagnostics: []Diagnostic{{
			Severity: severityError,
			Code:     "CS0103",
			Message:  "The name 'Missing' does not exist in the current context",
			File:     filepath.Join("Assets", "Alpha", "Alpha.cs"),
			Line:     3,
			Column:   9,
		}},
	}
}

// recordResult stores one compile's outcome under a key.
func recordResult(t *testing.T, outputDirectoryPath string, unit CompileUnit, key string) {
	t.Helper()
	if err := writeUnitResultManifest(outputDirectoryPath, unit, key, storedResult()); err != nil {
		t.Fatalf("failed to write the manifest: %v", err)
	}
}

// Verifies a recorded compile is handed back with the diagnostics and the outcome it had, rather
// than as a success: an assembly that failed must keep failing until something about it changes.
func TestLoadReusableResultReplaysTheRecordedDiagnostics(t *testing.T) {
	outputDirectoryPath, unit := newManifestUnit(t)
	recordResult(t, outputDirectoryPath, unit, "key")

	result, reusable := loadReusableResult(outputDirectoryPath, unit, "key")

	if !reusable {
		t.Fatal("expected the recorded result to be reusable")
	}
	if result.Succeeded {
		t.Error("a failed compile must not come back as a successful one")
	}
	if !result.Reused {
		t.Error("a replayed result should say it was reused")
	}
	if result.Assembly != "Alpha" {
		t.Errorf("assembly = %q, want Alpha", result.Assembly)
	}
	if len(result.Diagnostics) != 1 || result.Diagnostics[0].Code != "CS0103" {
		t.Errorf("diagnostics = %+v, want the recorded CS0103", result.Diagnostics)
	}
}

// Verifies a result recorded for other inputs is not reused.
func TestLoadReusableResultRejectsAnotherKey(t *testing.T) {
	outputDirectoryPath, unit := newManifestUnit(t)
	recordResult(t, outputDirectoryPath, unit, "key")

	if _, reusable := loadReusableResult(outputDirectoryPath, unit, "other key"); reusable {
		t.Error("a result recorded for other inputs should not be reused")
	}
}

// Verifies an output that no longer holds what the compile wrote stops the reuse: the assemblies
// below this one read those bytes, so replaying diagnostics over them would check nothing.
func TestLoadReusableResultRejectsAChangedOutput(t *testing.T) {
	outputDirectoryPath, unit := newManifestUnit(t)
	recordResult(t, outputDirectoryPath, unit, "key")

	writeOutputFile(t, outputDirectoryPath, "Alpha"+referenceAssemblyExtension, "another surface")

	if _, reusable := loadReusableResult(outputDirectoryPath, unit, "key"); reusable {
		t.Error("a changed output should stop the reuse")
	}
}

// Verifies a missing output stops the reuse.
func TestLoadReusableResultRejectsAMissingOutput(t *testing.T) {
	outputDirectoryPath, unit := newManifestUnit(t)
	recordResult(t, outputDirectoryPath, unit, "key")

	if err := os.Remove(filepath.Join(outputDirectoryPath, "Alpha"+assemblyExtension)); err != nil {
		t.Fatalf("failed to remove the output: %v", err)
	}

	if _, reusable := loadReusableResult(outputDirectoryPath, unit, "key"); reusable {
		t.Error("a missing output should stop the reuse")
	}
}

// Verifies a manifest that cannot be read is treated as no manifest at all rather than as a
// failure: the assembly simply compiles again.
func TestLoadReusableResultRejectsAnUnreadableManifest(t *testing.T) {
	outputDirectoryPath, unit := newManifestUnit(t)
	writeOutputFile(t, outputDirectoryPath, "Alpha.result.json", "{not json")

	if _, reusable := loadReusableResult(outputDirectoryPath, unit, "key"); reusable {
		t.Error("an unreadable manifest should not be reused")
	}
}

// Verifies a manifest written by another version of this check is not reused.
func TestLoadReusableResultRejectsAnotherFormatVersion(t *testing.T) {
	outputDirectoryPath, unit := newManifestUnit(t)
	recordResult(t, outputDirectoryPath, unit, "key")
	path := manifestPath(outputDirectoryPath, unit)
	content, err := os.ReadFile(path)
	if err != nil {
		t.Fatalf("failed to read the manifest: %v", err)
	}
	var manifest unitResultManifest
	if err := json.Unmarshal(content, &manifest); err != nil {
		t.Fatalf("failed to parse the manifest: %v", err)
	}
	manifest.FormatVersion = resultManifestFormatVersion + 1
	rewritten, err := json.Marshal(manifest)
	if err != nil {
		t.Fatalf("failed to write the manifest back: %v", err)
	}
	if err := os.WriteFile(path, rewritten, 0o600); err != nil {
		t.Fatalf("failed to write the manifest back: %v", err)
	}

	if _, reusable := loadReusableResult(outputDirectoryPath, unit, "key"); reusable {
		t.Error("a manifest of another format version should not be reused")
	}
}

// Verifies removing a unit's outputs removes the manifest with them, so nothing describes a compile
// whose outputs are gone.
func TestRemoveUnitOutputsRemovesTheManifest(t *testing.T) {
	outputDirectoryPath, unit := newManifestUnit(t)
	recordResult(t, outputDirectoryPath, unit, "key")

	if err := removeUnitOutputs(outputDirectoryPath, unit); err != nil {
		t.Fatalf("failed to remove the outputs: %v", err)
	}

	if _, err := os.Stat(manifestPath(outputDirectoryPath, unit)); !os.IsNotExist(err) {
		t.Error("the manifest should have been removed with the outputs")
	}
}

// Verifies a compile that wrote no outputs still records what it reported, so the next run can tell
// a failure it already knows from one it has not seen.
func TestWriteUnitResultManifestRecordsACompileThatWroteNothing(t *testing.T) {
	outputDirectoryPath, unit := newManifestUnit(t)
	for _, name := range []string{"Alpha" + assemblyExtension, "Alpha" + referenceAssemblyExtension} {
		if err := os.Remove(filepath.Join(outputDirectoryPath, name)); err != nil {
			t.Fatalf("failed to remove %s: %v", name, err)
		}
	}
	recordResult(t, outputDirectoryPath, unit, "key")

	result, reusable := loadReusableResult(outputDirectoryPath, unit, "key")

	if !reusable {
		t.Fatal("expected a compile that wrote nothing to be reusable")
	}
	if len(result.Diagnostics) != 1 {
		t.Errorf("diagnostics = %+v, want the recorded one", result.Diagnostics)
	}
}
