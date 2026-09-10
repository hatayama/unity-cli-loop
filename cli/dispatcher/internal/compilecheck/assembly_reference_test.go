package compilecheck

import (
	"path/filepath"
	"strings"
	"testing"
	"time"
)

// newAssemblyReferenceProject lays out two built assemblies, P owning a subfolder whose source P's
// response file records, and returns the graph and context the reference check compares against.
func newAssemblyReferenceProject(t *testing.T) (string, assemblyGraph, AssemblyContext, time.Time) {
	t.Helper()
	projectRoot := t.TempDir()
	writeFileAt(t, filepath.Join(projectRoot, "Assets", "P", "P.asmdef"), `{"name":"P"}`)
	writeFileAt(t, filepath.Join(projectRoot, "Assets", "P", "Sub", "B.cs"), "")
	writeFileAt(t, filepath.Join(projectRoot, "Assets", "Q", "Q.asmdef"), `{"name":"Q"}`)
	writeFileAt(t, filepath.Join(projectRoot, "Assets", "Q", "C.cs"), "")

	buildTime := time.Now()
	for _, name := range []string{"P", "Q"} {
		assemblyPath := filepath.Join(projectRoot, stalenessDagDirectory, name+".dll")
		writeFileAt(t, assemblyPath, "")
		setModificationTime(t, assemblyPath, buildTime)
	}

	graph := assemblyGraph{
		names: []string{"P", "Q"},
		byName: map[string]ResponseFile{
			"P": {
				AssemblyName: "P",
				Sources:      []string{filepath.Join("Assets", "P", "Sub", "B.cs")},
			},
			"Q": {
				AssemblyName: "Q",
				Sources:      []string{filepath.Join("Assets", "Q", "C.cs")},
			},
		},
	}
	context := AssemblyContext{
		BuiltAssemblies: map[string]bool{"P": true, "Q": true},
		AssemblyByGUID:  map[string]string{"qqqq": "Q"},
	}

	return projectRoot, graph, context, buildTime
}

// detectReferenceChange runs the reference check the way one run does: the .asmref files and the
// owner index are built once and shared.
func detectReferenceChange(
	t *testing.T, projectRoot string, graph assemblyGraph, context AssemblyContext,
) error {
	t.Helper()
	references, err := IndexAssemblyReferences(projectRoot)
	if err != nil {
		t.Fatalf("failed to index the assembly references: %v", err)
	}
	definitions, definitionErr := IndexAssemblyDefinitions(projectRoot)
	if definitionErr != nil {
		t.Fatalf("failed to index the assembly definitions: %v", definitionErr)
	}
	owners := newAssemblyOwnerIndex(definitions, references, context)

	return DetectAssemblyReferenceChange(projectRoot, graph, context, references, owners)
}

// Verifies an .asmref added after the last build, over sources another assembly still records,
// stops the run instead of compiling with the old membership.
func TestDetectAssemblyReferenceChangeRejectsAnAsmrefAddedAfterTheLastBuild(t *testing.T) {
	projectRoot, graph, context, buildTime := newAssemblyReferenceProject(t)
	referencePath := filepath.Join(projectRoot, "Assets", "P", "Sub", "Q.Ref.asmref")
	writeFileAt(t, referencePath, `{"reference":"GUID:qqqq"}`)
	setModificationTime(t, referencePath, buildTime.Add(time.Hour))

	err := detectReferenceChange(t, projectRoot, graph, context)
	if err == nil {
		t.Fatal("expected an assembly reference added after the last build to be rejected")
	}
	if !strings.Contains(err.Error(), "uloop compile") {
		t.Errorf("the error should tell the user how to recover, got: %v", err)
	}
}

// Verifies an .asmref whose timestamp moved but whose folder the target already records passes,
// so a branch switch that rewrites every file does not refuse the project forever.
func TestDetectAssemblyReferenceChangeAcceptsAnAsmrefTheResponseFileAlreadyAgreesWith(t *testing.T) {
	projectRoot, graph, context, buildTime := newAssemblyReferenceProject(t)
	referencePath := filepath.Join(projectRoot, "Assets", "P", "Sub", "Q.Ref.asmref")
	writeFileAt(t, referencePath, `{"reference":"GUID:qqqq"}`)
	setModificationTime(t, referencePath, buildTime.Add(time.Hour))
	graph.byName["Q"] = ResponseFile{
		AssemblyName: "Q",
		Sources: []string{
			filepath.Join("Assets", "Q", "C.cs"),
			filepath.Join("Assets", "P", "Sub", "B.cs"),
		},
	}

	if err := detectReferenceChange(t, projectRoot, graph, context); err != nil {
		t.Fatalf("expected the consistent assembly reference to pass, got error: %v", err)
	}
}

// Verifies an .asmref folder holding no C# source passes, since it moves no source between
// assemblies whatever its timestamp says.
func TestDetectAssemblyReferenceChangeAcceptsAnAsmrefFolderWithoutSources(t *testing.T) {
	projectRoot, graph, context, buildTime := newAssemblyReferenceProject(t)
	referencePath := filepath.Join(projectRoot, "Assets", "P", "Empty", "Q.Ref.asmref")
	writeFileAt(t, referencePath, `{"reference":"GUID:qqqq"}`)
	setModificationTime(t, referencePath, buildTime.Add(time.Hour))

	if err := detectReferenceChange(t, projectRoot, graph, context); err != nil {
		t.Fatalf("expected an .asmref folder without sources to pass, got error: %v", err)
	}
}

// Verifies an .asmref naming an assembly the last build never compiled passes, since there is no
// response file to compare its folder against.
func TestDetectAssemblyReferenceChangeIgnoresAnAsmrefTargetingAnUnbuiltAssembly(t *testing.T) {
	projectRoot, graph, context, buildTime := newAssemblyReferenceProject(t)
	referencePath := filepath.Join(projectRoot, "Assets", "P", "Sub", "Other.Ref.asmref")
	writeFileAt(t, referencePath, `{"reference":"Other"}`)
	setModificationTime(t, referencePath, buildTime.Add(time.Hour))

	if err := detectReferenceChange(t, projectRoot, graph, context); err != nil {
		t.Fatalf("expected an .asmref of an unbuilt assembly to pass, got error: %v", err)
	}
}
