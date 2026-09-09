package compilecheck

import (
	"path/filepath"
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

	sources, err := RebuildSources(projectRoot, rsp, &asmdef)
	if err != nil {
		t.Fatalf("expected the rebuild to succeed, got error: %v", err)
	}

	assertStrings(t, "sources", sources, []string{
		filepath.Join("Assets", "Foo", "A.cs"),
		filepath.Join("Assets", "Foo", "Sub", "B.cs"),
	})
}

// Verifies a source pulled in from outside the assembly directory survives the re-glob.
func TestRebuildSourcesKeepsSourcesOutsideTheAssemblyDirectory(t *testing.T) {
	projectRoot := newAssemblyProject(t)
	writeFileAt(t, filepath.Join(projectRoot, "Assets", "Shared", "E.cs"), "")
	rsp := ResponseFile{Sources: []string{
		filepath.Join("Assets", "Foo", "A.cs"),
		filepath.Join("Assets", "Shared", "E.cs"),
	}}
	asmdef := AssemblyDefinition{
		Name:      "Foo",
		Path:      filepath.Join(projectRoot, "Assets", "Foo", "Foo.asmdef"),
		Directory: filepath.Join(projectRoot, "Assets", "Foo"),
	}

	sources, err := RebuildSources(projectRoot, rsp, &asmdef)
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

	sources, err := RebuildSources(projectRoot, rsp, nil)
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

	sources, err := RebuildSources(projectRoot, rsp, &asmdef)
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
