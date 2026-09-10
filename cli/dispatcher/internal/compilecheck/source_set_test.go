package compilecheck

import (
	"os"
	"path/filepath"
	"strings"
	"testing"
)

// newAssemblyProject lays out one project with a nested assembly, an ignored folder and a subfolder.
func newAssemblyProject(t *testing.T) string {
	t.Helper()
	projectRoot := t.TempDir()
	writeFileAt(t, filepath.Join(projectRoot, "Assets", "Foo", "Foo.asmdef"), `{"name":"Foo"}`)
	writeFileAt(t, filepath.Join(projectRoot, "Assets", "Foo", "A.cs"), "")
	writeFileAt(t, filepath.Join(projectRoot, "Assets", "Foo", "Sub", "B.cs"), "")
	writeFileAt(t, filepath.Join(projectRoot, "Assets", "Foo", "Nested", "Nested.asmdef"), `{"name":"Nested"}`)
	writeFileAt(t, filepath.Join(projectRoot, "Assets", "Foo", "Nested", "C.cs"), "")
	writeFileAt(t, filepath.Join(projectRoot, "Assets", "Foo", "Tmp~", "D.cs"), "")

	return projectRoot
}

// newOwnerIndexForProject indexes the .asmdef and .asmref files of a project the way one run does.
func newOwnerIndexForProject(t *testing.T, projectRoot string) assemblyOwnerIndex {
	t.Helper()
	definitions, err := IndexAssemblyDefinitions(projectRoot)
	if err != nil {
		t.Fatalf("failed to index the assembly definitions: %v", err)
	}
	references, referenceErr := IndexAssemblyReferences(projectRoot)
	if referenceErr != nil {
		t.Fatalf("failed to index the assembly references: %v", referenceErr)
	}

	return newAssemblyOwnerIndex(
		definitions, references, NewAssemblyContext(assemblyGraph{}, definitions))
}

// Verifies deleted sources drop out, new sources appear, and nested or ignored folders stay excluded.
func TestRebuildSourcesReflectsFilesAddedAndDeletedSinceTheLastBuild(t *testing.T) {
	projectRoot := newAssemblyProject(t)
	rsp := ResponseFile{Sources: []string{
		filepath.Join("Assets", "Foo", "A.cs"),
		filepath.Join("Assets", "Foo", "Deleted.cs"),
	}}
	asmdef := AssemblyDefinition{
		Name:      "Foo",
		Path:      filepath.Join(projectRoot, "Assets", "Foo", "Foo.asmdef"),
		Directory: filepath.Join(projectRoot, "Assets", "Foo"),
	}

	sources, err := RebuildSources(projectRoot, rsp, &asmdef, newOwnerIndexForProject(t, projectRoot))
	if err != nil {
		t.Fatalf("expected the rebuild to succeed, got error: %v", err)
	}

	assertStrings(t, "sources", sources, []string{
		filepath.Join("Assets", "Foo", "A.cs"),
		filepath.Join("Assets", "Foo", "Sub", "B.cs"),
	})
}

// Verifies a source an .asmref attaches from outside the assembly directory survives the re-glob.
func TestRebuildSourcesKeepsSourcesOutsideTheAssemblyDirectory(t *testing.T) {
	projectRoot := newAssemblyProject(t)
	writeFileAt(t, filepath.Join(projectRoot, "Assets", "Shared", "E.cs"), "")
	writeFileAt(t,
		filepath.Join(projectRoot, "Assets", "Shared", "Foo.Ref.asmref"), `{"reference":"Foo"}`)
	rsp := ResponseFile{Sources: []string{
		filepath.Join("Assets", "Foo", "A.cs"),
		filepath.Join("Assets", "Shared", "E.cs"),
	}}
	asmdef := AssemblyDefinition{
		Name:      "Foo",
		Path:      filepath.Join(projectRoot, "Assets", "Foo", "Foo.asmdef"),
		Directory: filepath.Join(projectRoot, "Assets", "Foo"),
	}

	sources, err := RebuildSources(projectRoot, rsp, &asmdef, newOwnerIndexForProject(t, projectRoot))
	if err != nil {
		t.Fatalf("expected the rebuild to succeed, got error: %v", err)
	}

	assertStrings(t, "sources", sources, []string{
		filepath.Join("Assets", "Foo", "A.cs"),
		filepath.Join("Assets", "Foo", "Sub", "B.cs"),
		filepath.Join("Assets", "Shared", "E.cs"),
	})
}

// Verifies an assembly with no .asmdef keeps only the response file sources that still exist.
func TestRebuildSourcesWithoutAssemblyDefinitionKeepsExistingSourcesOnly(t *testing.T) {
	projectRoot := newAssemblyProject(t)
	rsp := ResponseFile{Sources: []string{
		filepath.Join("Assets", "Foo", "A.cs"),
		filepath.Join("Assets", "Foo", "Deleted.cs"),
	}}

	sources, err := RebuildSources(projectRoot, rsp, nil, assemblyOwnerIndex{})
	if err != nil {
		t.Fatalf("expected the rebuild to succeed, got error: %v", err)
	}

	assertStrings(t, "sources", sources, []string{filepath.Join("Assets", "Foo", "A.cs")})
}

// Verifies a package-cache assembly is taken from the response file instead of being walked.
func TestRebuildSourcesSkipsGlobbingForPackageCacheAssemblies(t *testing.T) {
	projectRoot := t.TempDir()
	packageDirectory := filepath.Join(projectRoot, "Library", "PackageCache", "com.example.pkg")
	writeFileAt(t, filepath.Join(packageDirectory, "Pkg.asmdef"), `{"name":"Pkg"}`)
	writeFileAt(t, filepath.Join(packageDirectory, "A.cs"), "")
	writeFileAt(t, filepath.Join(packageDirectory, "Untracked.cs"), "")
	rsp := ResponseFile{Sources: []string{
		filepath.Join("Library", "PackageCache", "com.example.pkg", "A.cs"),
	}}
	asmdef := AssemblyDefinition{
		Name:      "Pkg",
		Path:      filepath.Join(packageDirectory, "Pkg.asmdef"),
		Directory: packageDirectory,
	}

	sources, err := RebuildSources(projectRoot, rsp, &asmdef, newOwnerIndexForProject(t, projectRoot))
	if err != nil {
		t.Fatalf("expected the rebuild to succeed, got error: %v", err)
	}

	assertStrings(t, "sources", sources, []string{
		filepath.Join("Library", "PackageCache", "com.example.pkg", "A.cs"),
	})
}

// Verifies assembly definitions under Assets, Packages and the package cache all reach the index.
func TestIndexAssemblyDefinitionsCoversEveryProjectRoot(t *testing.T) {
	projectRoot := newAssemblyProject(t)
	writeFileAt(t, filepath.Join(projectRoot, "Packages", "local", "Local.asmdef"), `{"name":"Local"}`)
	writeFileAt(t,
		filepath.Join(projectRoot, "Library", "PackageCache", "com.example.pkg", "Pkg.asmdef"),
		`{"name":"Pkg"}`)
	writeFileAt(t, filepath.Join(projectRoot, "Assets", "Foo", "Tmp~", "Hidden.asmdef"), `{"name":"Hidden"}`)

	index, err := IndexAssemblyDefinitions(projectRoot)
	if err != nil {
		t.Fatalf("expected the index to build, got error: %v", err)
	}

	for _, name := range []string{"Foo", "Nested", "Local", "Pkg"} {
		if _, found := index[name]; !found {
			t.Errorf("assembly %s is missing from the index", name)
		}
	}
	if _, found := index["Hidden"]; found {
		t.Error("an assembly definition under an ignored folder must not be indexed")
	}
	foo := index["Foo"]
	if foo.Directory != filepath.Join(projectRoot, "Assets", "Foo") {
		t.Errorf("Foo directory = %s", foo.Directory)
	}
}

// Verifies an AppleDouble ".asmdef" holding binary bytes is skipped instead of failing the index.
func TestIndexAssemblyDefinitionsSkipsFilesUnityIgnores(t *testing.T) {
	projectRoot := newAssemblyProject(t)
	writeFileAt(t,
		filepath.Join(projectRoot, "Assets", "Foo", "._Foo.asmdef"),
		"\x00\x05\x16\x07\x00\x02\x00\x00Mac OS X")

	index, err := IndexAssemblyDefinitions(projectRoot)
	if err != nil {
		t.Fatalf("expected the index to build, got error: %v", err)
	}

	if _, found := index["Foo"]; !found {
		t.Error("the real assembly definition must still reach the index")
	}
}

// Verifies an AppleDouble ".cs" is not compiled while the file it shadows still is.
func TestRebuildSourcesSkipsSourceFilesUnityIgnores(t *testing.T) {
	projectRoot := newAssemblyProject(t)
	writeFileAt(t, filepath.Join(projectRoot, "Assets", "Foo", "._A.cs"), "\x00\x05\x16\x07")
	asmdef := AssemblyDefinition{
		Name:      "Foo",
		Path:      filepath.Join(projectRoot, "Assets", "Foo", "Foo.asmdef"),
		Directory: filepath.Join(projectRoot, "Assets", "Foo"),
	}

	sources, err := RebuildSources(projectRoot, ResponseFile{}, &asmdef, newOwnerIndexForProject(t, projectRoot))
	if err != nil {
		t.Fatalf("expected the rebuild to succeed, got error: %v", err)
	}

	assertStrings(t, "sources", sources, []string{
		filepath.Join("Assets", "Foo", "A.cs"),
		filepath.Join("Assets", "Foo", "Sub", "B.cs"),
	})
}

// Verifies a directory whose only assembly definition is an AppleDouble is not an assembly boundary.
func TestRebuildSourcesDoesNotTreatIgnoredAssemblyDefinitionsAsABoundary(t *testing.T) {
	projectRoot := newAssemblyProject(t)
	writeFileAt(t,
		filepath.Join(projectRoot, "Assets", "Foo", "Sub", "._Other.asmdef"),
		"\x00\x05\x16\x07")
	asmdef := AssemblyDefinition{
		Name:      "Foo",
		Path:      filepath.Join(projectRoot, "Assets", "Foo", "Foo.asmdef"),
		Directory: filepath.Join(projectRoot, "Assets", "Foo"),
	}

	sources, err := RebuildSources(projectRoot, ResponseFile{}, &asmdef, newOwnerIndexForProject(t, projectRoot))
	if err != nil {
		t.Fatalf("expected the rebuild to succeed, got error: %v", err)
	}

	assertStrings(t, "sources", sources, []string{
		filepath.Join("Assets", "Foo", "A.cs"),
		filepath.Join("Assets", "Foo", "Sub", "B.cs"),
	})
}

// newNestedAsmrefProject lays out a parent assembly, a child assembly inside it, and an .asmref
// folder nested inside the child that attaches its sources back to the parent.
func newNestedAsmrefProject(t *testing.T) (string, ResponseFile, AssemblyDefinition) {
	t.Helper()
	projectRoot := t.TempDir()
	writeFileAt(t, filepath.Join(projectRoot, "Assets", "P", "P.asmdef"), `{"name":"P"}`)
	writeFileAt(t, filepath.Join(projectRoot, "Assets", "P", "A.cs"), "")
	writeFileAt(t, filepath.Join(projectRoot, "Assets", "P", "Core", "Core.asmdef"), `{"name":"Core"}`)
	writeFileAt(t, filepath.Join(projectRoot, "Assets", "P", "Core", "Child.cs"), "")
	writeFileAt(t,
		filepath.Join(projectRoot, "Assets", "P", "Core", "Dialog", "P.Ref.asmref"),
		`{"reference":"P"}`)
	writeFileAt(t, filepath.Join(projectRoot, "Assets", "P", "Core", "Dialog", "Dialog.cs"), "")
	rsp := ResponseFile{
		AssemblyName: "P",
		Sources: []string{
			filepath.Join("Assets", "P", "A.cs"),
			filepath.Join("Assets", "P", "Core", "Dialog", "Dialog.cs"),
		},
	}
	asmdef := AssemblyDefinition{
		Name:      "P",
		Path:      filepath.Join(projectRoot, "Assets", "P", "P.asmdef"),
		Directory: filepath.Join(projectRoot, "Assets", "P"),
	}

	return projectRoot, rsp, asmdef
}

// Verifies an .asmref folder nested inside a child assembly's directory keeps its recorded sources.
func TestRebuildSourcesKeepsSourcesOfAnAsmrefNestedInsideAChildAssembly(t *testing.T) {
	projectRoot, rsp, asmdef := newNestedAsmrefProject(t)

	sources, err := RebuildSources(projectRoot, rsp, &asmdef, newOwnerIndexForProject(t, projectRoot))
	if err != nil {
		t.Fatalf("expected the rebuild to succeed, got error: %v", err)
	}

	assertStrings(t, "sources", sources, []string{
		filepath.Join("Assets", "P", "A.cs"),
		filepath.Join("Assets", "P", "Core", "Dialog", "Dialog.cs"),
	})
}

// Verifies removing an .asmref after the last build stops the run instead of keeping its sources in
// the assembly that no longer owns them, which would compile them into two assemblies at once.
func TestRebuildSourcesRejectsARecordedSourceNoAsmrefAttachesAnyMore(t *testing.T) {
	projectRoot, rsp, asmdef := newNestedAsmrefProject(t)
	if err := os.Remove(
		filepath.Join(projectRoot, "Assets", "P", "Core", "Dialog", "P.Ref.asmref")); err != nil {
		t.Fatalf("failed to remove the assembly reference: %v", err)
	}

	_, err := RebuildSources(projectRoot, rsp, &asmdef, newOwnerIndexForProject(t, projectRoot))
	if err == nil {
		t.Fatal("expected a source no assembly reference attaches any more to be rejected")
	}
	if !strings.Contains(err.Error(), "uloop compile") {
		t.Errorf("the error should tell the user how to recover, got: %v", err)
	}
}

// Verifies an .asmref moved with os.Rename, which carries its modification time along, still stops
// the run, so the detection does not depend on the timestamp of the file.
func TestRebuildSourcesRejectsAnAsmrefMovedWithoutChangingItsTimestamp(t *testing.T) {
	projectRoot, rsp, asmdef := newNestedAsmrefProject(t)
	referencePath := filepath.Join(projectRoot, "Assets", "P", "Core", "Dialog", "P.Ref.asmref")
	movedPath := filepath.Join(projectRoot, "Assets", "P", "Core", "Other", "P.Ref.asmref")
	if err := os.MkdirAll(filepath.Dir(movedPath), 0o755); err != nil {
		t.Fatalf("failed to create the destination folder: %v", err)
	}
	if err := os.Rename(referencePath, movedPath); err != nil {
		t.Fatalf("failed to move the assembly reference: %v", err)
	}

	_, err := RebuildSources(projectRoot, rsp, &asmdef, newOwnerIndexForProject(t, projectRoot))
	if err == nil {
		t.Fatal("expected a moved assembly reference to be rejected")
	}
}

// Verifies an .asmdef moved with os.Rename into another assembly's folder stops the run even though
// the move left its modification time untouched.
func TestRebuildSourcesRejectsAnAssemblyDefinitionMovedWithoutChangingItsTimestamp(t *testing.T) {
	projectRoot := newAssemblyProject(t)
	movedPath := filepath.Join(projectRoot, "Assets", "Elsewhere", "Foo.asmdef")
	writeFileAt(t, filepath.Join(projectRoot, "Assets", "Elsewhere", "E.cs"), "")
	if err := os.Rename(
		filepath.Join(projectRoot, "Assets", "Foo", "Foo.asmdef"), movedPath); err != nil {
		t.Fatalf("failed to move the assembly definition: %v", err)
	}
	rsp := ResponseFile{
		AssemblyName: "Foo",
		Sources:      []string{filepath.Join("Assets", "Foo", "A.cs")},
	}
	asmdef := AssemblyDefinition{Name: "Foo", Path: movedPath, Directory: filepath.Dir(movedPath)}

	_, err := RebuildSources(projectRoot, rsp, &asmdef, newOwnerIndexForProject(t, projectRoot))
	if err == nil {
		t.Fatal("expected a moved assembly definition to be rejected")
	}
	if !strings.Contains(err.Error(), "uloop compile") {
		t.Errorf("the error should tell the user how to recover, got: %v", err)
	}
}

// Verifies an assembly definition whose own folder holds no C# source passes the moved check, since
// an assembly can own all of its sources through .asmref folders elsewhere.
func TestRebuildSourcesAcceptsAnAssemblyDefinitionWithoutSourcesOfItsOwn(t *testing.T) {
	projectRoot := t.TempDir()
	writeFileAt(t, filepath.Join(projectRoot, "Assets", "P", "P.asmdef"), `{"name":"P"}`)
	writeFileAt(t, filepath.Join(projectRoot, "Assets", "Shared", "P.Ref.asmref"), `{"reference":"P"}`)
	writeFileAt(t, filepath.Join(projectRoot, "Assets", "Shared", "D.cs"), "")
	rsp := ResponseFile{
		AssemblyName: "P",
		Sources:      []string{filepath.Join("Assets", "Shared", "D.cs")},
	}
	asmdef := AssemblyDefinition{
		Name:      "P",
		Path:      filepath.Join(projectRoot, "Assets", "P", "P.asmdef"),
		Directory: filepath.Join(projectRoot, "Assets", "P"),
	}

	sources, err := RebuildSources(projectRoot, rsp, &asmdef, newOwnerIndexForProject(t, projectRoot))
	if err != nil {
		t.Fatalf("expected an assembly without sources of its own to pass, got error: %v", err)
	}

	assertStrings(t, "sources", sources, []string{filepath.Join("Assets", "Shared", "D.cs")})
}

// Verifies an assembly definition sitting over sources its own response file never recorded stops
// the run even when nothing is left to rescue, which is the shape a moved .asmdef leaves behind once
// the sources of its old location are gone.
func TestRebuildSourcesRejectsAnAssemblyDefinitionThatLeftItsRecordedSources(t *testing.T) {
	projectRoot := t.TempDir()
	writeFileAt(t, filepath.Join(projectRoot, "Assets", "Other", "Foo.asmdef"), `{"name":"Foo"}`)
	writeFileAt(t, filepath.Join(projectRoot, "Assets", "Other", "C.cs"), "")
	rsp := ResponseFile{
		AssemblyName: "Foo",
		Sources:      []string{filepath.Join("Assets", "Foo", "A.cs")},
	}
	asmdef := AssemblyDefinition{
		Name:      "Foo",
		Path:      filepath.Join(projectRoot, "Assets", "Other", "Foo.asmdef"),
		Directory: filepath.Join(projectRoot, "Assets", "Other"),
	}

	_, err := RebuildSources(projectRoot, rsp, &asmdef, newOwnerIndexForProject(t, projectRoot))
	if err == nil {
		t.Fatal("expected an assembly definition that left its recorded sources to be rejected")
	}
	if !strings.Contains(err.Error(), "uloop compile") {
		t.Errorf("the error should tell the user how to recover, got: %v", err)
	}
}
