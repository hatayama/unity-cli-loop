package compilecheck

import (
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"
)

const stalenessDagDirectory = "Library/Bee/artifacts/aaaa.dag"

// setModificationTime moves a file's timestamp so the baseline comparison can be driven exactly.
func setModificationTime(t *testing.T, path string, when time.Time) {
	t.Helper()
	if err := os.Chtimes(path, when, when); err != nil {
		t.Fatalf("failed to set the modification time of %s: %v", path, err)
	}
}

// newStalenessProject builds a project whose assembly was built after its single source was written.
func newStalenessProject(t *testing.T) (string, ResponseFile, AssemblyDefinition) {
	t.Helper()
	projectRoot := t.TempDir()
	sourcePath := filepath.Join(projectRoot, "Assets", "Foo", "A.cs")
	asmdefPath := filepath.Join(projectRoot, "Assets", "Foo", "Foo.asmdef")
	additionalPath := filepath.Join(projectRoot, stalenessDagDirectory, "Foo.UnityAdditionalFile.txt")
	assemblyPath := filepath.Join(projectRoot, stalenessDagDirectory, "Foo.dll")
	writeFileAt(t, sourcePath, "")
	writeFileAt(t, asmdefPath, `{"name":"Foo"}`)
	writeFileAt(t, additionalPath, "")
	writeFileAt(t, assemblyPath, "")

	buildTime := time.Now()
	setModificationTime(t, sourcePath, buildTime.Add(-time.Hour))
	setModificationTime(t, asmdefPath, buildTime.Add(-time.Hour))
	setModificationTime(t, assemblyPath, buildTime)

	rsp := ResponseFile{
		AssemblyName:   "Foo",
		Sources:        []string{filepath.Join("Assets", "Foo", "A.cs")},
		AdditionalFile: filepath.Join(stalenessDagDirectory, "Foo.UnityAdditionalFile.txt"),
	}
	asmdef := AssemblyDefinition{Name: "Foo", Path: asmdefPath, Directory: filepath.Dir(asmdefPath)}

	return projectRoot, rsp, asmdef
}

// Verifies an assembly whose sources predate the last build is reported as unchanged.
func TestDetectSourceChangeReportsNoChangeWhenNothingMoved(t *testing.T) {
	projectRoot, rsp, _ := newStalenessProject(t)

	report, err := DetectSourceChange(projectRoot, rsp, rsp.Sources, stalenessDagDirectory)
	if err != nil {
		t.Fatalf("expected the check to succeed, got error: %v", err)
	}
	if report.Changed {
		t.Errorf("expected no change, got reason %q", report.Reason)
	}
}

// Verifies a source edited after the last build is reported as changed with the timestamp reason.
func TestDetectSourceChangeReportsSourceNewerThanLastBuild(t *testing.T) {
	projectRoot, rsp, _ := newStalenessProject(t)
	setModificationTime(t, filepath.Join(projectRoot, rsp.Sources[0]), time.Now().Add(time.Hour))

	report, err := DetectSourceChange(projectRoot, rsp, rsp.Sources, stalenessDagDirectory)
	if err != nil {
		t.Fatalf("expected the check to succeed, got error: %v", err)
	}
	if !report.Changed || report.Reason != changeReasonSourceNewer {
		t.Errorf("report = %+v, want a change with reason %q", report, changeReasonSourceNewer)
	}
}

// Verifies a source added since the last build is reported as changed even when no file is newer.
func TestDetectSourceChangeReportsAddedSource(t *testing.T) {
	projectRoot, rsp, _ := newStalenessProject(t)
	addedPath := filepath.Join(projectRoot, "Assets", "Foo", "B.cs")
	writeFileAt(t, addedPath, "")
	setModificationTime(t, addedPath, time.Now().Add(-time.Hour))
	sources := append([]string{}, rsp.Sources...)
	sources = append(sources, filepath.Join("Assets", "Foo", "B.cs"))

	report, err := DetectSourceChange(projectRoot, rsp, sources, stalenessDagDirectory)
	if err != nil {
		t.Fatalf("expected the check to succeed, got error: %v", err)
	}
	if !report.Changed || report.Reason != changeReasonSourceAdded {
		t.Errorf("report = %+v, want a change with reason %q", report, changeReasonSourceAdded)
	}
}

// Verifies a source deleted since the last build is reported as changed.
func TestDetectSourceChangeReportsRemovedSource(t *testing.T) {
	projectRoot, rsp, _ := newStalenessProject(t)

	report, err := DetectSourceChange(projectRoot, rsp, []string{}, stalenessDagDirectory)
	if err != nil {
		t.Fatalf("expected the check to succeed, got error: %v", err)
	}
	if !report.Changed || report.Reason != changeReasonSourceRemoved {
		t.Errorf("report = %+v, want a change with reason %q", report, changeReasonSourceRemoved)
	}
}

// Verifies an assembly definition that gained a reference after the last build stops the run
// instead of compiling, and says how to recover.
func TestDetectStructuralChangeRejectsEditedAssemblyDefinition(t *testing.T) {
	projectRoot, rsp, asmdef := newStalenessProject(t)
	writeFileAt(t, asmdef.Path, `{"name":"Foo","includePlatforms":["iOS"]}`)
	setModificationTime(t, asmdef.Path, time.Now().Add(time.Hour))

	err := DetectStructuralChange(projectRoot, rsp, &asmdef, stalenessDagDirectory, AssemblyContext{})
	if err == nil {
		t.Fatal("expected an edited assembly definition to be rejected")
	}
	if !strings.Contains(err.Error(), "uloop compile") {
		t.Errorf("the error should tell the user how to recover, got: %v", err)
	}
}

// Verifies a missing Bee additional file stops the run instead of compiling without it.
func TestDetectStructuralChangeRejectsIncompleteBeeArtifacts(t *testing.T) {
	projectRoot, rsp, asmdef := newStalenessProject(t)
	if err := os.Remove(filepath.Join(projectRoot, rsp.AdditionalFile)); err != nil {
		t.Fatalf("failed to remove the additional file: %v", err)
	}

	if err := DetectStructuralChange(
		projectRoot, rsp, &asmdef, stalenessDagDirectory, AssemblyContext{}); err == nil {
		t.Fatal("expected incomplete Bee artifacts to be rejected")
	}
}

// Verifies an assembly Unity has never built is rejected, since there is no baseline to compare to.
func TestDetectStructuralChangeRejectsNeverBuiltAssembly(t *testing.T) {
	projectRoot, rsp, asmdef := newStalenessProject(t)
	if err := os.Remove(filepath.Join(projectRoot, stalenessDagDirectory, "Foo.dll")); err != nil {
		t.Fatalf("failed to remove the assembly: %v", err)
	}

	if err := DetectStructuralChange(
		projectRoot, rsp, &asmdef, stalenessDagDirectory, AssemblyContext{}); err == nil {
		t.Fatal("expected a never-built assembly to be rejected")
	}
}

// Verifies a structurally unchanged assembly passes the check.
func TestDetectStructuralChangeAcceptsUnchangedAssembly(t *testing.T) {
	projectRoot, rsp, asmdef := newStalenessProject(t)

	if err := DetectStructuralChange(
		projectRoot, rsp, &asmdef, stalenessDagDirectory, AssemblyContext{}); err != nil {
		t.Fatalf("expected the unchanged assembly to pass, got error: %v", err)
	}
}
