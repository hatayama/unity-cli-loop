package compilecheck

import (
	"path/filepath"
	"strings"
	"testing"
	"time"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
)

const (
	contractAssemblyGUID = "1111111111111111111111111111111a"
	referencedGUID       = "2222222222222222222222222222222b"
	unreferencedGUID     = "3333333333333333333333333333333c"
)

// writeAssemblyDefinitionMeta writes the .meta file that gives an .asmdef the GUID others reference.
func writeAssemblyDefinitionMeta(t *testing.T, projectRoot string, name string, guid string) {
	t.Helper()
	writeFileAt(t,
		filepath.Join(projectRoot, "Assets", name, name+".asmdef.meta"),
		"fileFormatVersion: 2\nguid: "+guid+"\n")
}

// newContractProject builds a project of three built assemblies where A references B, and rewrites
// A's assembly definition with the given body, dated after the last build so the contract check runs.
func newContractProject(
	t *testing.T, assemblyDefinitionBody string,
) (string, ResponseFile, AssemblyDefinition, AssemblyContext) {
	t.Helper()
	projectRoot := t.TempDir()
	writePlanAssembly(t, projectRoot, "B", nil)
	writePlanAssembly(t, projectRoot, "C", nil)
	writePlanAssembly(t, projectRoot, "A", []string{"B"})
	settlePlanProject(t, projectRoot, "A", "B", "C")

	writeAssemblyDefinitionMeta(t, projectRoot, "A", contractAssemblyGUID)
	writeAssemblyDefinitionMeta(t, projectRoot, "B", referencedGUID)
	writeAssemblyDefinitionMeta(t, projectRoot, "C", unreferencedGUID)

	assemblyDefinitionPath := filepath.Join(projectRoot, "Assets", "A", "A.asmdef")
	writeFileAt(t, assemblyDefinitionPath, assemblyDefinitionBody)
	setModificationTime(t, assemblyDefinitionPath, time.Now().Add(time.Hour))

	graph, err := loadAssemblyGraph(projectRoot, planDagDirectory)
	if err != nil {
		t.Fatalf("failed to load the assembly graph: %v", err)
	}
	assemblyDefinitions, err := IndexAssemblyDefinitions(projectRoot)
	if err != nil {
		t.Fatalf("failed to index the assembly definitions: %v", err)
	}

	return projectRoot,
		graph.byName["A"],
		assemblyDefinitions["A"],
		NewAssemblyContext(graph, assemblyDefinitions)
}

// Verifies an assembly definition whose timestamp moved but whose content still matches the
// response file is accepted, which is what a branch switch leaves behind.
func TestDetectStructuralChangeAcceptsTouchedButUnchangedAssemblyDefinition(t *testing.T) {
	projectRoot, rsp, asmdef, context := newContractProject(t, `{"name":"A","references":["B"]}`)

	if err := DetectStructuralChange(
		projectRoot, rsp, &asmdef, planDagDirectory, context); err != nil {
		t.Fatalf("expected a timestamp-only change to be accepted, got error: %v", err)
	}
}

// Verifies a reference added to the assembly definition by name is refused.
func TestDetectStructuralChangeRejectsAddedNameReference(t *testing.T) {
	projectRoot, rsp, asmdef, context := newContractProject(t, `{"name":"A","references":["B","C"]}`)

	err := DetectStructuralChange(projectRoot, rsp, &asmdef, planDagDirectory, context)
	if err == nil {
		t.Fatal("expected an added reference to be rejected")
	}
	if !strings.Contains(err.Error(), "C") {
		t.Errorf("the error should name the added reference, got: %v", err)
	}
}

// Verifies a reference added to the assembly definition in GUID form is refused too.
func TestDetectStructuralChangeRejectsAddedGUIDReference(t *testing.T) {
	projectRoot, rsp, asmdef, context := newContractProject(
		t, `{"name":"A","references":["GUID:`+referencedGUID+`","GUID:`+unreferencedGUID+`"]}`)

	if err := DetectStructuralChange(
		projectRoot, rsp, &asmdef, planDagDirectory, context); err == nil {
		t.Fatal("expected an added GUID reference to be rejected")
	}
}

// Verifies a GUID no assembly definition in the project owns is skipped rather than refused, since
// healthy projects reference assemblies that own no indexable .asmdef.
func TestDetectStructuralChangeAcceptsUnresolvableGUIDReference(t *testing.T) {
	projectRoot, rsp, asmdef, context := newContractProject(
		t, `{"name":"A","references":["B","GUID:00000000000000000000000000000000"]}`)

	if err := DetectStructuralChange(
		projectRoot, rsp, &asmdef, planDagDirectory, context); err != nil {
		t.Fatalf("expected an unresolvable GUID to be skipped, got error: %v", err)
	}
}

// Verifies an assembly definition that turned on unsafe code without the response file allowing it
// is refused.
func TestDetectStructuralChangeRejectsNewlyAllowedUnsafeCode(t *testing.T) {
	projectRoot, rsp, asmdef, context := newContractProject(
		t, `{"name":"A","references":["B"],"allowUnsafeCode":true}`)

	if err := DetectStructuralChange(
		projectRoot, rsp, &asmdef, planDagDirectory, context); err == nil {
		t.Fatal("expected newly allowed unsafe code to be rejected")
	}
}

// Verifies the unsafe check accepts the /unsafe+ spelling Bee actually writes.
func TestDetectStructuralChangeAcceptsUnsafeCodeAlreadyInTheResponseFile(t *testing.T) {
	projectRoot, rsp, asmdef, context := newContractProject(
		t, `{"name":"A","references":["B"],"allowUnsafeCode":true}`)
	rsp.OtherFlags = append(rsp.OtherFlags, "/unsafe+")

	if err := DetectStructuralChange(
		projectRoot, rsp, &asmdef, planDagDirectory, context); err != nil {
		t.Fatalf("expected /unsafe+ to satisfy allowUnsafeCode, got error: %v", err)
	}
}

// Verifies a precompiled reference the response file never received is refused.
func TestDetectStructuralChangeRejectsMissingPrecompiledReference(t *testing.T) {
	projectRoot, rsp, asmdef, context := newContractProject(t,
		`{"name":"A","references":["B"],"overrideReferences":true,"precompiledReferences":["Some.dll"]}`)

	err := DetectStructuralChange(projectRoot, rsp, &asmdef, planDagDirectory, context)
	if err == nil {
		t.Fatal("expected a missing precompiled reference to be rejected")
	}
	if !strings.Contains(err.Error(), "Some.dll") {
		t.Errorf("the error should name the missing reference, got: %v", err)
	}
}

// Verifies a precompiled reference the response file already carries is accepted.
func TestDetectStructuralChangeAcceptsSatisfiedPrecompiledReference(t *testing.T) {
	projectRoot, rsp, asmdef, context := newContractProject(t,
		`{"name":"A","references":["B"],"overrideReferences":true,"precompiledReferences":["Some.dll"]}`)
	rsp.References = append(rsp.References, filepath.Join("Assets", "Plugins", "Some.dll"))

	if err := DetectStructuralChange(
		projectRoot, rsp, &asmdef, planDagDirectory, context); err != nil {
		t.Fatalf("expected the satisfied precompiled reference to pass, got error: %v", err)
	}
}

// Verifies an assembly definition that no longer builds for the Editor is refused, which is the one
// removal this check can observe.
func TestDetectStructuralChangeRejectsAssemblyDefinitionExcludedFromTheEditor(t *testing.T) {
	projectRoot, rsp, asmdef, context := newContractProject(
		t, `{"name":"A","references":["B"],"includePlatforms":["iOS"]}`)

	if err := DetectStructuralChange(
		projectRoot, rsp, &asmdef, planDagDirectory, context); err == nil {
		t.Fatal("expected an assembly definition excluded from the Editor to be rejected")
	}
}

// Verifies an assembly definition Unity does not build for the Editor is not reported as added,
// however recently its file was written.
func TestBuildCompilePlanAcceptsTouchedAssemblyDefinitionThatSkipsTheEditor(t *testing.T) {
	projectRoot := newPlanProject(t)
	addedPath := filepath.Join(projectRoot, "Assets", "Mobile", "Mobile.asmdef")
	writeFileAt(t, addedPath, `{"name":"Mobile","includePlatforms":["Android"]}`)
	setModificationTime(t, addedPath, time.Now().Add(time.Hour))

	if _, err := BuildCompilePlan(projectRoot, planDagDirectory, true); err != nil {
		t.Fatalf("expected a platform-excluded assembly definition to be accepted, got error: %v", err)
	}
}

// Verifies an assembly definition that does build for the Editor and has no response file is still
// reported as added.
func TestBuildCompilePlanRejectsTouchedAssemblyDefinitionThatBuildsForTheEditor(t *testing.T) {
	projectRoot := newPlanProject(t)
	addedPath := filepath.Join(projectRoot, "Assets", "Extra", "Extra.asmdef")
	writeFileAt(t, addedPath, `{"name":"Extra"}`)
	setModificationTime(t, addedPath, time.Now().Add(time.Hour))

	if _, err := BuildCompilePlan(projectRoot, planDagDirectory, true); err == nil {
		t.Fatal("expected a newly added assembly definition to be rejected")
	}
}

// Verifies a precondition failure reaches the shared classifier as its own error code, so agents
// can tell "build in Unity first" apart from a real internal failure.
func TestUnityBuildRequiredErrorClassifiesToItsOwnErrorCode(t *testing.T) {
	projectRoot, rsp, asmdef, context := newContractProject(
		t, `{"name":"A","references":["B"],"includePlatforms":["iOS"]}`)

	err := DetectStructuralChange(projectRoot, rsp, &asmdef, planDagDirectory, context)
	if err == nil {
		t.Fatal("expected the excluded assembly definition to be rejected")
	}

	classified := clierrors.ClassifyError(err, clierrors.ErrorContext{Command: "compile-check"})
	if classified.ErrorCode != clierrors.ErrorCodeCompileCheckUnityBuildRequired {
		t.Errorf("ErrorCode = %q, want %q",
			classified.ErrorCode, clierrors.ErrorCodeCompileCheckUnityBuildRequired)
	}
	if classified.Retryable {
		t.Error("a precondition only a Unity build clears must not be reported as retryable")
	}
	if len(classified.NextActions) == 0 {
		t.Error("the error must tell the user to run uloop compile")
	}
}
