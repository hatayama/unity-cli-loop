package compilecheck

import (
	"os"
	"path/filepath"
	"testing"
)

// newReferenceSurfaceProject builds a project whose plan compiles one assembly with a reference
// assembly, and returns the project root together with that plan and unit.
func newReferenceSurfaceProject(t *testing.T, refOutputPath string) (string, BuildPlan, CompileUnit) {
	t.Helper()
	projectRoot := t.TempDir()
	plan := BuildPlan{
		DagDir:    planDagDirectory,
		OutputDir: filepath.Join("Library", "uloop", "compile-check", "aaaa.dag"),
	}
	unit := CompileUnit{Assembly: ResponseFile{AssemblyName: "A", RefOutputPath: refOutputPath}}
	plan.Units = []CompileUnit{unit}
	for _, directory := range []string{plan.OutputDir, planDagDirectory} {
		if err := os.MkdirAll(filepath.Join(projectRoot, directory), outputDirPermissions); err != nil {
			t.Fatalf("failed to create %s: %v", directory, err)
		}
	}

	return projectRoot, plan, unit
}

// writeReferenceAssembly writes one reference assembly with the given bytes.
func writeReferenceAssembly(t *testing.T, path string, content string) {
	t.Helper()
	if err := os.WriteFile(path, []byte(content), 0o600); err != nil {
		t.Fatalf("failed to write %s: %v", path, err)
	}
}

// Verifies a reference assembly identical to the one the last Unity build produced counts as an
// unchanged public surface, which is what lets a dependent be left out of the run.
func TestReferenceSurfaceUnchangedAcceptsIdenticalReferenceAssemblies(t *testing.T) {
	unityPath := filepath.Join(planDagDirectory, "A.ref.dll")
	projectRoot, plan, unit := newReferenceSurfaceProject(t, unityPath)
	writeReferenceAssembly(t, filepath.Join(projectRoot, unityPath), "surface")
	writeReferenceAssembly(t, filepath.Join(projectRoot, plan.OutputDir, "A.ref.dll"), "surface")

	if !referenceSurfaceUnchanged(projectRoot, plan, unit) {
		t.Error("identical reference assemblies should count as an unchanged surface")
	}
}

// Verifies a single differing byte counts as a changed surface, so the dependents are compiled.
func TestReferenceSurfaceUnchangedRejectsDifferingReferenceAssemblies(t *testing.T) {
	unityPath := filepath.Join(planDagDirectory, "A.ref.dll")
	projectRoot, plan, unit := newReferenceSurfaceProject(t, unityPath)
	writeReferenceAssembly(t, filepath.Join(projectRoot, unityPath), "surface")
	writeReferenceAssembly(t, filepath.Join(projectRoot, plan.OutputDir, "A.ref.dll"), "surfacf")

	if referenceSurfaceUnchanged(projectRoot, plan, unit) {
		t.Error("reference assemblies differing by one byte should count as a changed surface")
	}
}

// Verifies a missing output - which is what a failed compile leaves behind - counts as changed.
func TestReferenceSurfaceUnchangedRejectsAMissingOutput(t *testing.T) {
	unityPath := filepath.Join(planDagDirectory, "A.ref.dll")
	projectRoot, plan, unit := newReferenceSurfaceProject(t, unityPath)
	writeReferenceAssembly(t, filepath.Join(projectRoot, unityPath), "surface")

	if referenceSurfaceUnchanged(projectRoot, plan, unit) {
		t.Error("a reference assembly this run did not produce should count as a changed surface")
	}
}

// Verifies a missing Unity-side reference assembly counts as changed rather than as a match.
func TestReferenceSurfaceUnchangedRejectsAMissingUnityOutput(t *testing.T) {
	unityPath := filepath.Join(planDagDirectory, "A.ref.dll")
	projectRoot, plan, unit := newReferenceSurfaceProject(t, unityPath)
	writeReferenceAssembly(t, filepath.Join(projectRoot, plan.OutputDir, "A.ref.dll"), "surface")

	if referenceSurfaceUnchanged(projectRoot, plan, unit) {
		t.Error("a missing Unity reference assembly should count as a changed surface")
	}
}

// Verifies an assembly built without a reference assembly has nothing to compare and counts as changed.
func TestReferenceSurfaceUnchangedRejectsAnAssemblyWithoutAReferenceAssembly(t *testing.T) {
	projectRoot, plan, unit := newReferenceSurfaceProject(t, "")

	if referenceSurfaceUnchanged(projectRoot, plan, unit) {
		t.Error("an assembly with no reference assembly should count as a changed surface")
	}
}
