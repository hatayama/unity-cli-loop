package compilecheck

import (
	"path/filepath"
	"testing"
	"time"
)

const localPackageAssemblyName = "Local.Package"

// newLocalPackageProject builds a project that develops one package from a directory outside the
// project root, the way a package author's test project consumes the package under development.
// The package's response file records its source as an absolute path, exactly as Bee does.
func newLocalPackageProject(t *testing.T) (string, string) {
	t.Helper()
	parent := t.TempDir()
	projectRoot := filepath.Join(parent, "Project")
	packageRoot := filepath.Join(parent, "Package")

	writePlanAssembly(t, projectRoot, "A", nil)
	writeFileAt(t, filepath.Join(projectRoot, "Packages", "manifest.json"),
		`{"dependencies":{"com.example.local":"file:../../Package"}}`)

	sourcePath := filepath.Join(packageRoot, "Runtime", localPackageAssemblyName+".cs")
	writeFileAt(t, sourcePath, "")
	writeFileAt(t, filepath.Join(packageRoot, "Runtime", localPackageAssemblyName+".asmdef"),
		`{"name":"`+localPackageAssemblyName+`"}`)
	writeFileAt(t,
		filepath.Join(projectRoot, planDagDirectory, localPackageAssemblyName+".rsp"),
		joinLines([]string{
			`-out:"` + planDagDirectory + `/` + localPackageAssemblyName + `.dll"`,
			`-refout:"` + planDagDirectory + `/` + localPackageAssemblyName + `.ref.dll"`,
			`"` + sourcePath + `"`,
		}))
	writeFileAt(t, filepath.Join(projectRoot, planDagDirectory, localPackageAssemblyName+".dll"), "")

	buildTime := time.Now()
	setModificationTime(t, sourcePath, buildTime.Add(-time.Hour))
	setModificationTime(t,
		filepath.Join(packageRoot, "Runtime", localPackageAssemblyName+".asmdef"),
		buildTime.Add(-time.Hour))
	setModificationTime(t,
		filepath.Join(projectRoot, planDagDirectory, localPackageAssemblyName+".dll"), buildTime)
	settlePlanProject(t, projectRoot, "A")

	return projectRoot, packageRoot
}

// Verifies an assembly definition of a package referenced by a local path is indexed, so its
// assembly is not mistaken for one whose assembly definition the project deleted.
func TestIndexAssemblyDefinitionsFindsLocallyReferencedPackages(t *testing.T) {
	projectRoot, packageRoot := newLocalPackageProject(t)

	assemblyDefinitions, err := IndexAssemblyDefinitions(projectRoot)
	if err != nil {
		t.Fatalf("failed to index the assembly definitions: %v", err)
	}

	definition, found := assemblyDefinitions[localPackageAssemblyName]
	if !found {
		t.Fatalf("the local package's assembly definition is missing from the index: %v",
			assemblyDefinitions)
	}
	if !isUnderDirectory(definition.Path, packageRoot) {
		t.Errorf("indexed path = %q, want one inside the local package", definition.Path)
	}
}

// Verifies a project that develops a package from a local path compiles instead of being refused as
// one whose assembly definitions were removed after the last Unity build.
func TestBuildCompilePlanAcceptsALocallyReferencedPackage(t *testing.T) {
	projectRoot, _ := newLocalPackageProject(t)

	if _, err := BuildCompilePlan(projectRoot, planDagDirectory, true); err != nil {
		t.Fatalf("expected a locally referenced package to be accepted, got error: %v", err)
	}
}

// Verifies the rebuilt source list of a local package keeps the absolute spelling of the response
// file, so unchanged sources are not reported as removed and added at once.
func TestRebuildSourcesKeepsAbsolutePathsOfALocalPackage(t *testing.T) {
	projectRoot, _ := newLocalPackageProject(t)

	graph, err := loadAssemblyGraph(projectRoot, planDagDirectory)
	if err != nil {
		t.Fatalf("failed to load the assembly graph: %v", err)
	}
	assemblyDefinitions, err := IndexAssemblyDefinitions(projectRoot)
	if err != nil {
		t.Fatalf("failed to index the assembly definitions: %v", err)
	}
	definition := assemblyDefinitions[localPackageAssemblyName]
	rsp := graph.byName[localPackageAssemblyName]

	sources, err := RebuildSources(projectRoot, rsp, &definition)
	if err != nil {
		t.Fatalf("failed to rebuild the sources: %v", err)
	}

	assertStrings(t, "sources", sources, rsp.Sources)

	report, err := DetectSourceChange(projectRoot, rsp, sources, planDagDirectory)
	if err != nil {
		t.Fatalf("failed to detect source changes: %v", err)
	}
	if report.Changed {
		t.Errorf("unchanged local package sources reported a change: %q", report.Reason)
	}
}

// Verifies a manifest that cannot be read leaves the project's own roots in place rather than
// turning a manifest problem into a refusal to compile.
func TestLocalPackageRootsIgnoresAnUnreadableManifest(t *testing.T) {
	projectRoot := t.TempDir()
	writeFileAt(t, filepath.Join(projectRoot, "Packages", "manifest.json"), "not json")

	if roots := localPackageRoots(projectRoot); len(roots) != 0 {
		t.Errorf("roots = %v, want none", roots)
	}
}

// Verifies a dependency that names a registry version rather than a local path adds no root.
func TestLocalPackageRootsSkipsRegistryDependencies(t *testing.T) {
	projectRoot := t.TempDir()
	writeFileAt(t, filepath.Join(projectRoot, "Packages", "manifest.json"),
		`{"dependencies":{"com.unity.example":"1.2.3"}}`)

	if roots := localPackageRoots(projectRoot); len(roots) != 0 {
		t.Errorf("roots = %v, want none", roots)
	}
}

// byteOrderMark is what an editor on Windows writes in front of UTF-8 JSON.
const byteOrderMark = "\uFEFF"

// Verifies an assembly definition saved as UTF-8 with a byte order mark is still indexed, instead
// of failing the whole command with a JSON parse error.
func TestIndexAssemblyDefinitionsReadsAnAssemblyDefinitionWithAByteOrderMark(t *testing.T) {
	projectRoot := t.TempDir()
	writeFileAt(t, filepath.Join(projectRoot, "Assets", "A", "A.asmdef"),
		byteOrderMark+`{"name":"A"}`)

	assemblyDefinitions, err := IndexAssemblyDefinitions(projectRoot)
	if err != nil {
		t.Fatalf("expected the byte order mark to be tolerated, got error: %v", err)
	}
	if _, found := assemblyDefinitions["A"]; !found {
		t.Errorf("index = %v, want it to contain A", assemblyDefinitions)
	}
}

// Verifies the contract reader tolerates a byte order mark too, so a Windows-authored assembly
// definition is compared against its response file rather than reported as unreadable.
func TestReadAssemblyDefinitionContractReadsAByteOrderMark(t *testing.T) {
	projectRoot := t.TempDir()
	path := filepath.Join(projectRoot, "A.asmdef")
	writeFileAt(t, path, byteOrderMark+`{"name":"A","includePlatforms":["iOS"]}`)

	contract, err := readAssemblyDefinitionContract(path)
	if err != nil {
		t.Fatalf("expected the byte order mark to be tolerated, got error: %v", err)
	}
	if contract.buildsForEditor() {
		t.Error("the platform list should have been read, so the assembly must not build for the Editor")
	}
}

// Verifies a package manifest saved with a byte order mark still contributes its local package
// roots, rather than silently leaving them out of the index.
func TestLocalPackageRootsReadsAManifestWithAByteOrderMark(t *testing.T) {
	projectRoot := t.TempDir()
	writeFileAt(t, filepath.Join(projectRoot, "Packages", "manifest.json"),
		byteOrderMark+`{"dependencies":{"com.example.local":"file:../../Package"}}`)

	roots := localPackageRoots(projectRoot)
	if len(roots) != 1 {
		t.Fatalf("roots = %v, want exactly one", roots)
	}
	if filepath.Base(roots[0]) != "Package" {
		t.Errorf("root = %q, want the directory the manifest points at", roots[0])
	}
}
