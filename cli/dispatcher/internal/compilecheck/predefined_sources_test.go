package compilecheck

import (
	"path/filepath"
	"testing"
	"time"
)

// predefinedOwnersFor indexes a project the way one run does, with only the named assemblies built,
// and globs the scripts no assembly definition owns.
func predefinedOwnersFor(t *testing.T, projectRoot string, built ...string) assemblyOwnerIndex {
	t.Helper()
	tree := walkProjectTree(projectRoot)
	definitions, err := IndexAssemblyDefinitions(tree)
	if err != nil {
		t.Fatalf("failed to index the assembly definitions: %v", err)
	}
	references, referenceErr := IndexAssemblyReferences(tree)
	if referenceErr != nil {
		t.Fatalf("failed to index the assembly references: %v", referenceErr)
	}
	graph := assemblyGraph{byName: map[string]ResponseFile{}}
	for _, name := range built {
		graph.byName[name] = ResponseFile{AssemblyName: name}
	}
	owners := newAssemblyOwnerIndex(definitions, references, NewAssemblyContext(graph, definitions), tree)
	predefined, globErr := globPredefinedSources(projectRoot, owners)
	if globErr != nil {
		t.Fatalf("globPredefinedSources returned an error: %v", globErr)
	}
	owners.predefinedSources = predefined

	return owners
}

// rebuildPredefinedSources rebuilds the sources of a predefined assembly that recorded the given ones.
func rebuildPredefinedSources(t *testing.T, projectRoot string, assemblyName string, recorded ...string) []string {
	t.Helper()
	rsp := ResponseFile{AssemblyName: assemblyName, Sources: recorded}
	sources, err := RebuildSources(projectRoot, rsp, nil, predefinedOwnersFor(t, projectRoot))
	if err != nil {
		t.Fatalf("RebuildSources returned an error: %v", err)
	}

	return sources
}

// writeProjectFiles writes empty files at the given project-relative paths.
func writeProjectFiles(t *testing.T, projectRoot string, relativePaths ...string) {
	t.Helper()
	for _, relativePath := range relativePaths {
		writeFileAt(t, filepath.Join(projectRoot, filepath.FromSlash(relativePath)), "")
	}
}

// Verifies the editor rule matches an "editor" folder in any case and anywhere in the path, and not
// a folder whose name only contains the word.
func TestPredefinedAssemblyForMatchesTheEditorSegmentOnly(t *testing.T) {
	cases := map[string]string{
		"Assets/Editor/X.cs":              assemblyCSharpEditor,
		"Assets/Tools/editor/X.cs":        assemblyCSharpEditor,
		"Assets/EDITOR/X.cs":              assemblyCSharpEditor,
		"Assets/Scripts/EditorTools/X.cs": assemblyCSharp,
		"Assets/MyEditor/X.cs":            assemblyCSharp,
		"Assets/Editor.cs":                assemblyCSharp,
	}
	for path, want := range cases {
		if got := predefinedAssemblyFor(path); got != want {
			t.Errorf("%s: assembly = %q, want %q", path, got, want)
		}
	}
}

// Verifies the first-pass rule matches each first-pass folder only at the top of Assets, in any case.
func TestPredefinedAssemblyForMatchesTheFirstpassFolders(t *testing.T) {
	cases := map[string]string{
		"Assets/Plugins/X.cs":                    assemblyCSharpFirstpass,
		"Assets/plugins/Sub/X.cs":                assemblyCSharpFirstpass,
		"Assets/Standard Assets/X.cs":            assemblyCSharpFirstpass,
		"Assets/Pro Standard Assets/X.cs":        assemblyCSharpFirstpass,
		"Assets/iPhone Standard Assets/X.cs":     assemblyCSharpFirstpass,
		"Assets/Game/Plugins/X.cs":               assemblyCSharp,
		"Assets/Plugins/Editor/X.cs":             assemblyCSharpEditorFirstpass,
		"Assets/Standard Assets/Sub/editor/X.cs": assemblyCSharpEditorFirstpass,
	}
	for path, want := range cases {
		if got := predefinedAssemblyFor(path); got != want {
			t.Errorf("%s: assembly = %q, want %q", path, got, want)
		}
	}
}

// Verifies a script an assembly definition owns is left to that assembly.
func TestGlobPredefinedSourcesLeavesOutAScriptAnAssemblyDefinitionOwns(t *testing.T) {
	projectRoot := t.TempDir()
	writeFileAt(t, filepath.Join(projectRoot, "Assets", "Foo", "Foo.asmdef"), `{"name":"Foo"}`)
	writeProjectFiles(t, projectRoot, "Assets/Foo/Owned.cs", "Assets/Loose.cs")

	predefined := predefinedOwnersFor(t, projectRoot, "Foo").predefinedSources

	assertStrings(t, assemblyCSharp, predefined[assemblyCSharp], []string{"Assets/Loose.cs"})
	if len(predefined) != 1 {
		t.Errorf("predefined = %v, want only %s", predefined, assemblyCSharp)
	}
}

// Verifies a folder Unity ignores by its name, ending in '~' or starting with '.', is not read.
func TestGlobPredefinedSourcesLeavesOutAHiddenFolder(t *testing.T) {
	projectRoot := t.TempDir()
	writeProjectFiles(t, projectRoot, "Assets/Foo~/X.cs", "Assets/.hidden/X.cs", "Assets/Kept.cs")

	predefined := predefinedOwnersFor(t, projectRoot).predefinedSources

	assertStrings(t, assemblyCSharp, predefined[assemblyCSharp], []string{"Assets/Kept.cs"})
}

// Verifies an assembly definition in Assets itself leaves no script to the predefined assemblies.
func TestGlobPredefinedSourcesReturnsNothingWhenAnAssemblyDefinitionSitsInAssets(t *testing.T) {
	projectRoot := t.TempDir()
	writeFileAt(t, filepath.Join(projectRoot, "Assets", "Root.asmdef"), `{"name":"Root"}`)
	writeProjectFiles(t, projectRoot, "Assets/X.cs", "Assets/Editor/Y.cs")

	if predefined := predefinedOwnersFor(t, projectRoot, "Root").predefinedSources; len(predefined) != 0 {
		t.Errorf("predefined = %v, want none", predefined)
	}
}

// Verifies a script in a package folder is not given to a predefined assembly.
func TestGlobPredefinedSourcesLeavesOutPackageFolders(t *testing.T) {
	projectRoot := t.TempDir()
	writeProjectFiles(t, projectRoot, "Packages/com.example.loose/Loose.cs", "Assets/Kept.cs")

	predefined := predefinedOwnersFor(t, projectRoot).predefinedSources

	assertStrings(t, assemblyCSharp, predefined[assemblyCSharp], []string{"Assets/Kept.cs"})
}

// Verifies the folder of an .asmref naming an assembly definition this build did not produce gives
// its scripts to no assembly, as Unity attaches the folder before dropping the unbuilt assembly.
func TestGlobPredefinedSourcesStopsAtAReferenceToAnUnbuiltAssembly(t *testing.T) {
	projectRoot := t.TempDir()
	writeFileAt(t, filepath.Join(projectRoot, "Assets", "Unbuilt", "Unbuilt.asmdef"), `{"name":"Unbuilt"}`)
	writeFileAt(t, filepath.Join(projectRoot, "Assets", "Extra", "Extra.asmref"), `{"reference":"Unbuilt"}`)
	writeProjectFiles(t, projectRoot, "Assets/Extra/X.cs", "Assets/Kept.cs")

	predefined := predefinedOwnersFor(t, projectRoot).predefinedSources

	assertStrings(t, assemblyCSharp, predefined[assemblyCSharp], []string{"Assets/Kept.cs"})
}

// Verifies the glob of an assembly definition stops at the same folder, so its scripts are not
// compiled into the assembly around it either.
func TestGlobAssemblySourcesStopsAtAReferenceToAnUnbuiltAssembly(t *testing.T) {
	projectRoot := t.TempDir()
	writeFileAt(t, filepath.Join(projectRoot, "Assets", "Outer", "Outer.asmdef"), `{"name":"Outer"}`)
	writeFileAt(t, filepath.Join(projectRoot, "Assets", "Unbuilt", "Unbuilt.asmdef"), `{"name":"Unbuilt"}`)
	writeFileAt(t, filepath.Join(projectRoot, "Assets", "Outer", "Extra", "Extra.asmref"), `{"reference":"Unbuilt"}`)
	writeProjectFiles(t, projectRoot, "Assets/Outer/Outer.cs", "Assets/Outer/Extra/X.cs")
	owners := predefinedOwnersFor(t, projectRoot, "Outer")

	sources, err := globAssemblySources(projectRoot, filepath.Join(projectRoot, "Assets", "Outer"), owners)
	if err != nil {
		t.Fatalf("globAssemblySources returned an error: %v", err)
	}

	assertStrings(t, "sources", sources, []string{"Assets/Outer/Outer.cs"})
}

// Verifies the folder of an .asmref whose GUID resolves to nothing is read into the assembly around
// it, as Unity does.
func TestGlobPredefinedSourcesDescendsIntoAReferenceToNoAssembly(t *testing.T) {
	projectRoot := t.TempDir()
	writeFileAt(t, filepath.Join(projectRoot, "Assets", "Extra", "Extra.asmref"), `{"reference":"GUID:0000000000000000"}`)
	writeProjectFiles(t, projectRoot, "Assets/Extra/X.cs")

	predefined := predefinedOwnersFor(t, projectRoot).predefinedSources

	assertStrings(t, assemblyCSharp, predefined[assemblyCSharp], []string{"Assets/Extra/X.cs"})
}

// Verifies the folder of an .asmref naming an assembly no .asmdef declares is read into the assembly
// around it, as Unity does.
func TestGlobPredefinedSourcesDescendsIntoAReferenceByAnUnknownName(t *testing.T) {
	projectRoot := t.TempDir()
	writeFileAt(t, filepath.Join(projectRoot, "Assets", "Extra", "Extra.asmref"), `{"reference":"Nowhere"}`)
	writeProjectFiles(t, projectRoot, "Assets/Extra/X.cs")

	predefined := predefinedOwnersFor(t, projectRoot).predefinedSources

	assertStrings(t, assemblyCSharp, predefined[assemblyCSharp], []string{"Assets/Extra/X.cs"})
}

// Verifies a script created outside every assembly definition after the build is compiled with
// Assembly-CSharp, after the recorded ones.
func TestRebuildSourcesPicksUpANewScriptOutsideEveryAssemblyDefinition(t *testing.T) {
	projectRoot := t.TempDir()
	writeProjectFiles(t, projectRoot, "Assets/Old.cs", "Assets/A/New.cs")

	sources := rebuildPredefinedSources(t, projectRoot, assemblyCSharp, "Assets/Old.cs")

	assertStrings(t, "sources", sources, []string{"Assets/Old.cs", "Assets/A/New.cs"})
}

// Verifies a new script in an editor folder goes to Assembly-CSharp-Editor and not to
// Assembly-CSharp.
func TestRebuildSourcesRoutesANewEditorScriptToTheEditorAssembly(t *testing.T) {
	projectRoot := t.TempDir()
	writeProjectFiles(t, projectRoot, "Assets/Old.cs", "Assets/Editor/New.cs")

	assertStrings(t, assemblyCSharpEditor,
		rebuildPredefinedSources(t, projectRoot, assemblyCSharpEditor), []string{"Assets/Editor/New.cs"})
	assertStrings(t, assemblyCSharp,
		rebuildPredefinedSources(t, projectRoot, assemblyCSharp, "Assets/Old.cs"), []string{"Assets/Old.cs"})
}

// Verifies a new script in a plugins folder goes to Assembly-CSharp-firstpass.
func TestRebuildSourcesRoutesANewPluginScriptToFirstpass(t *testing.T) {
	projectRoot := t.TempDir()
	writeProjectFiles(t, projectRoot, "Assets/Plugins/New.cs")

	assertStrings(t, assemblyCSharpFirstpass,
		rebuildPredefinedSources(t, projectRoot, assemblyCSharpFirstpass), []string{"Assets/Plugins/New.cs"})
	assertStrings(t, assemblyCSharp, rebuildPredefinedSources(t, projectRoot, assemblyCSharp), []string{})
}

// Verifies a new script in an editor folder under plugins goes to Assembly-CSharp-Editor-firstpass.
func TestRebuildSourcesRoutesANewPluginEditorScriptToEditorFirstpass(t *testing.T) {
	projectRoot := t.TempDir()
	writeProjectFiles(t, projectRoot, "Assets/Plugins/Editor/New.cs")

	assertStrings(t, assemblyCSharpEditorFirstpass,
		rebuildPredefinedSources(t, projectRoot, assemblyCSharpEditorFirstpass),
		[]string{"Assets/Plugins/Editor/New.cs"})
	assertStrings(t, assemblyCSharpEditor, rebuildPredefinedSources(t, projectRoot, assemblyCSharpEditor), []string{})
}

// Verifies a script the response file already recorded is listed once, in its recorded place.
func TestRebuildSourcesKeepsARecordedPredefinedSourceOnce(t *testing.T) {
	projectRoot := t.TempDir()
	writeProjectFiles(t, projectRoot, "Assets/B.cs", "Assets/A.cs", "Assets/C.cs")

	sources := rebuildPredefinedSources(t, projectRoot, assemblyCSharp, "Assets/B.cs", "Assets/A.cs")

	assertStrings(t, "sources", sources, []string{"Assets/B.cs", "Assets/A.cs", "Assets/C.cs"})
}

// Verifies a run refuses a script whose predefined assembly the last Unity build did not produce.
func TestSelectChangedAssembliesRefusesAScriptForAPredefinedAssemblyTheLastBuildLacks(t *testing.T) {
	projectRoot := newPlanProject(t)
	writeProjectFiles(t, projectRoot, "Assets/Editor/FirstEditorScript.cs")

	_, err := BuildCompilePlan(projectRoot, planDagDirectory, false)

	assertBuildRequired(t, err, "Assets/Editor/FirstEditorScript.cs", assemblyCSharpEditor, "did not produce")
}

// Verifies the scripts of the folders Unity imports but never compiles are left out, in any case,
// while a deeper folder of the same name and the other platform folders under Plugins are not.
func TestGlobPredefinedSourcesLeavesOutTheFoldersUnityNeverCompiles(t *testing.T) {
	projectRoot := t.TempDir()
	writeProjectFiles(t, projectRoot,
		"Assets/StreamingAssets/A.cs", "Assets/WebPlayerTemplates/B.cs", "Assets/webgltemplates/C.cs",
		"Assets/Plugins/Android/D.cs", "Assets/Plugins/iOS/X.cs", "Assets/Game/StreamingAssets/X.cs")

	predefined := predefinedOwnersFor(t, projectRoot).predefinedSources

	assertStrings(t, assemblyCSharpFirstpass, predefined[assemblyCSharpFirstpass], []string{"Assets/Plugins/iOS/X.cs"})
	assertStrings(t, assemblyCSharp, predefined[assemblyCSharp], []string{"Assets/Game/StreamingAssets/X.cs"})
	if len(predefined) != 2 {
		t.Errorf("predefined = %v, want only %s and %s", predefined, assemblyCSharp, assemblyCSharpFirstpass)
	}
}

// writeBuiltAssemblyCSharp adds an Assembly-CSharp the last build produced from Assets/Old.cs, with
// the source dated before the build.
func writeBuiltAssemblyCSharp(t *testing.T, projectRoot string) {
	t.Helper()
	writeFileAt(t, filepath.Join(projectRoot, planDagDirectory, assemblyCSharp+".rsp"), joinLines([]string{
		`-out:"` + planDagDirectory + `/` + assemblyCSharp + `.dll"`,
		`-refout:"` + planDagDirectory + `/` + assemblyCSharp + `.ref.dll"`,
		`/additionalfile:"` + planDagDirectory + `/` + assemblyCSharp + `.UnityAdditionalFile.txt"`,
		`"Assets/Old.cs"`,
	}))
	writeFileAt(t, filepath.Join(projectRoot, planDagDirectory, assemblyCSharp+".UnityAdditionalFile.txt"), "")
	writeFileAt(t, filepath.Join(projectRoot, planDagDirectory, assemblyCSharp+".dll"), "")
	writeProjectFiles(t, projectRoot, "Assets/Old.cs")
	buildTime := time.Now()
	setModificationTime(t, filepath.Join(projectRoot, "Assets", "Old.cs"), buildTime.Add(-time.Hour))
	setModificationTime(t, filepath.Join(projectRoot, planDagDirectory, assemblyCSharp+".dll"), buildTime)
}

// Verifies a plan compiles a script created outside every assembly definition with the predefined
// assembly the last build produced, after its recorded sources, rather than refusing the run.
func TestBuildCompilePlanPicksUpANewScriptOutsideEveryAssemblyDefinition(t *testing.T) {
	projectRoot := newPlanProject(t)
	writeBuiltAssemblyCSharp(t, projectRoot)
	writeProjectFiles(t, projectRoot, "Assets/New.cs")

	plan, err := BuildCompilePlan(projectRoot, planDagDirectory, false)
	if err != nil {
		t.Fatalf("expected the plan to build, got error: %v", err)
	}

	for _, unit := range plan.Units {
		if unit.Assembly.AssemblyName == assemblyCSharp {
			assertStrings(t, "sources", unit.Sources, []string{"Assets/Old.cs", "Assets/New.cs"})
			return
		}
	}
	t.Fatalf("units = %v, want %s compiled for its new script", unitNames(plan), assemblyCSharp)
}

// Verifies the sources of a predefined assembly are refused when Assets was never walked, rather
// than rebuilt without the scripts created since the build.
func TestRebuildSourcesRefusesAPredefinedAssemblyWhenAssetsWasNeverWalked(t *testing.T) {
	projectRoot := t.TempDir()
	writeProjectFiles(t, projectRoot, "Assets/Old.cs")
	rsp := ResponseFile{AssemblyName: assemblyCSharp, Sources: []string{"Assets/Old.cs"}}

	if _, err := RebuildSources(projectRoot, rsp, nil, newOwnerIndexForProject(t, projectRoot)); err == nil {
		t.Fatal("expected an unwalked Assets to be refused for a predefined assembly")
	}
}
