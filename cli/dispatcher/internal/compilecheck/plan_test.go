package compilecheck

import (
	"os"
	"path/filepath"
	"testing"
	"time"
)

const planDagDirectory = "Library/Bee/artifacts/aaaa.dag"

// writePlanAssembly writes one assembly's response file, source, built dll and Bee additional file.
func writePlanAssembly(t *testing.T, projectRoot string, name string, references []string) {
	t.Helper()
	lines := []string{
		`-out:"` + planDagDirectory + `/` + name + `.dll"`,
		`-refout:"` + planDagDirectory + `/` + name + `.ref.dll"`,
	}
	for _, reference := range references {
		lines = append(lines, `-r:"`+planDagDirectory+`/`+reference+`.ref.dll"`)
	}
	lines = append(lines,
		`/additionalfile:"`+planDagDirectory+`/`+name+`.UnityAdditionalFile.txt"`,
		`"Assets/`+name+`/`+name+`.cs"`)

	writeFileAt(t, filepath.Join(projectRoot, planDagDirectory, name+".rsp"), joinLines(lines))
	writeFileAt(t, filepath.Join(projectRoot, planDagDirectory, name+".UnityAdditionalFile.txt"), "")
	writeFileAt(t, filepath.Join(projectRoot, planDagDirectory, name+".dll"), "")
	writeFileAt(t, filepath.Join(projectRoot, "Assets", name, name+".cs"), "")
	writeFileAt(t, filepath.Join(projectRoot, "Assets", name, name+".asmdef"), `{"name":"`+name+`"}`)
}

// settlePlanProject dates every source and assembly definition before the build that produced the dlls.
func settlePlanProject(t *testing.T, projectRoot string, names ...string) {
	t.Helper()
	buildTime := time.Now()
	for _, name := range names {
		setModificationTime(t,
			filepath.Join(projectRoot, "Assets", name, name+".cs"), buildTime.Add(-time.Hour))
		setModificationTime(t,
			filepath.Join(projectRoot, "Assets", name, name+".asmdef"), buildTime.Add(-time.Hour))
		setModificationTime(t, filepath.Join(projectRoot, planDagDirectory, name+".dll"), buildTime)
	}
}

// joinLines assembles a response file body from its lines.
func joinLines(lines []string) string {
	body := ""
	for index, line := range lines {
		if index > 0 {
			body += "\n"
		}
		body += line
	}

	return body
}

// newPlanProject builds a project with A <- B <- C, all built after their sources were written.
func newPlanProject(t *testing.T) string {
	t.Helper()
	projectRoot := t.TempDir()
	writePlanAssembly(t, projectRoot, "A", nil)
	writePlanAssembly(t, projectRoot, "B", []string{"A"})
	writePlanAssembly(t, projectRoot, "C", []string{"B"})

	settlePlanProject(t, projectRoot, "A", "B", "C")

	return projectRoot
}

// newReversePlanProject builds a project whose dependency order is the reverse of its name order:
// A references B references C, so a correct plan compiles C, B, A.
func newReversePlanProject(t *testing.T) string {
	t.Helper()
	projectRoot := t.TempDir()
	writePlanAssembly(t, projectRoot, "C", nil)
	writePlanAssembly(t, projectRoot, "B", []string{"C"})
	writePlanAssembly(t, projectRoot, "A", []string{"B"})
	settlePlanProject(t, projectRoot, "A", "B", "C")

	return projectRoot
}

// touchSource marks one assembly's source as edited after the last Unity build.
func touchSource(t *testing.T, projectRoot string, name string) {
	t.Helper()
	setModificationTime(t,
		filepath.Join(projectRoot, "Assets", name, name+".cs"), time.Now().Add(time.Hour))
}

// unitNames lists the assemblies a plan compiles, in plan order.
func unitNames(plan BuildPlan) []string {
	names := make([]string, 0, len(plan.Units))
	for _, unit := range plan.Units {
		names = append(names, unit.Assembly.AssemblyName)
	}

	return names
}

// Verifies a change in a referenced assembly pulls its dependents in, in dependency order.
func TestBuildCompilePlanPropagatesToDependentsInDependencyOrder(t *testing.T) {
	projectRoot := newPlanProject(t)
	touchSource(t, projectRoot, "A")

	plan, err := BuildCompilePlan(projectRoot, planDagDirectory, false)
	if err != nil {
		t.Fatalf("expected the plan to build, got error: %v", err)
	}

	assertStrings(t, "units", unitNames(plan), []string{"A", "B", "C"})
	if plan.Units[0].Reason != changeReasonSourceNewer {
		t.Errorf("A reason = %q, want %q", plan.Units[0].Reason, changeReasonSourceNewer)
	}
	if plan.Units[1].Reason != dependencyReasonPrefix+"A" {
		t.Errorf("B reason = %q, want %q", plan.Units[1].Reason, dependencyReasonPrefix+"A")
	}
	if plan.Units[2].Reason != dependencyReasonPrefix+"B" {
		t.Errorf("C reason = %q, want %q", plan.Units[2].Reason, dependencyReasonPrefix+"B")
	}
	if plan.Skipped != 0 {
		t.Errorf("skipped = %d, want 0", plan.Skipped)
	}
}

// Verifies a change in a leaf assembly compiles that assembly alone.
func TestBuildCompilePlanCompilesOnlyTheChangedLeaf(t *testing.T) {
	projectRoot := newPlanProject(t)
	touchSource(t, projectRoot, "C")

	plan, err := BuildCompilePlan(projectRoot, planDagDirectory, false)
	if err != nil {
		t.Fatalf("expected the plan to build, got error: %v", err)
	}

	assertStrings(t, "units", unitNames(plan), []string{"C"})
	if plan.Skipped != 2 {
		t.Errorf("skipped = %d, want 2", plan.Skipped)
	}
}

// Verifies an unchanged project produces an empty plan rather than recompiling everything.
func TestBuildCompilePlanSkipsEverythingWhenNothingChanged(t *testing.T) {
	projectRoot := newPlanProject(t)

	plan, err := BuildCompilePlan(projectRoot, planDagDirectory, false)
	if err != nil {
		t.Fatalf("expected the plan to build, got error: %v", err)
	}

	if len(plan.Units) != 0 {
		t.Errorf("units = %v, want none", unitNames(plan))
	}
	if plan.Skipped != 3 {
		t.Errorf("skipped = %d, want 3", plan.Skipped)
	}
}

// Verifies --all compiles every assembly and still respects dependency order.
func TestBuildCompilePlanWithAllKeepsDependencyOrder(t *testing.T) {
	projectRoot := newPlanProject(t)

	plan, err := BuildCompilePlan(projectRoot, planDagDirectory, true)
	if err != nil {
		t.Fatalf("expected the plan to build, got error: %v", err)
	}

	assertStrings(t, "units", unitNames(plan), []string{"A", "B", "C"})
	if plan.Units[0].Reason != "" {
		t.Errorf("an --all unit should carry no change reason, got %q", plan.Units[0].Reason)
	}
	if plan.OutputDir != filepath.Join("Library", "uloop", "compile-check", "aaaa.dag") {
		t.Errorf("output directory = %s", plan.OutputDir)
	}
}

// Verifies the Bee framework-move response files are not mistaken for assemblies.
func TestBuildCompilePlanIgnoresMovedFrameworkResponseFiles(t *testing.T) {
	projectRoot := newPlanProject(t)
	writeFileAt(t, filepath.Join(projectRoot, planDagDirectory, "A.dll.mvfrm.rsp"), "not a response file")

	plan, err := BuildCompilePlan(projectRoot, planDagDirectory, true)
	if err != nil {
		t.Fatalf("expected the plan to build, got error: %v", err)
	}

	assertStrings(t, "units", unitNames(plan), []string{"A", "B", "C"})
}

// Verifies a dag directory with no response files is rejected with recovery advice.
func TestBuildCompilePlanRejectsEmptyDagDirectory(t *testing.T) {
	projectRoot := t.TempDir()
	if err := os.MkdirAll(filepath.Join(projectRoot, planDagDirectory), 0o755); err != nil {
		t.Fatalf("failed to create the dag directory: %v", err)
	}

	if _, err := BuildCompilePlan(projectRoot, planDagDirectory, true); err == nil {
		t.Fatal("expected an empty dag directory to be rejected")
	}
}

// Verifies a cycle between assembly references is reported instead of silently dropping assemblies.
func TestBuildCompilePlanRejectsCyclicReferences(t *testing.T) {
	projectRoot := t.TempDir()
	writePlanAssembly(t, projectRoot, "A", []string{"B"})
	writePlanAssembly(t, projectRoot, "B", []string{"A"})
	buildTime := time.Now()
	for _, name := range []string{"A", "B"} {
		setModificationTime(t, filepath.Join(projectRoot, "Assets", name, name+".cs"), buildTime.Add(-time.Hour))
		setModificationTime(t, filepath.Join(projectRoot, planDagDirectory, name+".dll"), buildTime)
	}

	if _, err := BuildCompilePlan(projectRoot, planDagDirectory, true); err == nil {
		t.Fatal("expected cyclic assembly references to be rejected")
	}
}

// Verifies dependency order wins over name order when a change propagates to dependents.
func TestBuildCompilePlanOrdersAgainstNameOrder(t *testing.T) {
	projectRoot := newReversePlanProject(t)
	touchSource(t, projectRoot, "C")

	plan, err := BuildCompilePlan(projectRoot, planDagDirectory, false)
	if err != nil {
		t.Fatalf("expected the plan to build, got error: %v", err)
	}

	assertStrings(t, "plan order", unitNames(plan), []string{"C", "B", "A"})
}

// Verifies --all also orders by dependency rather than by name.
func TestBuildCompilePlanWithAllOrdersAgainstNameOrder(t *testing.T) {
	projectRoot := newReversePlanProject(t)

	plan, err := BuildCompilePlan(projectRoot, planDagDirectory, true)
	if err != nil {
		t.Fatalf("expected the plan to build, got error: %v", err)
	}

	assertStrings(t, "plan order", unitNames(plan), []string{"C", "B", "A"})
}

// Verifies an assembly definition added since the last build stops the run instead of being ignored.
func TestBuildCompilePlanRejectsAnAssemblyDefinitionAddedAfterTheBuild(t *testing.T) {
	projectRoot := newPlanProject(t)
	writeFileAt(t, filepath.Join(projectRoot, "Assets", "D", "D.asmdef"), `{"name":"D"}`)
	setModificationTime(t,
		filepath.Join(projectRoot, "Assets", "D", "D.asmdef"), time.Now().Add(time.Hour))

	if _, err := BuildCompilePlan(projectRoot, planDagDirectory, false); err == nil {
		t.Fatal("expected a newly added assembly definition to be refused")
	}
}

// Verifies an assembly definition Bee never built is accepted while it predates the build.
// Platform-excluded and test-only assemblies legitimately have no response file.
func TestBuildCompilePlanAcceptsAnAssemblyDefinitionOlderThanTheBuild(t *testing.T) {
	projectRoot := newPlanProject(t)
	writeFileAt(t, filepath.Join(projectRoot, "Assets", "D", "D.asmdef"), `{"name":"D"}`)
	setModificationTime(t,
		filepath.Join(projectRoot, "Assets", "D", "D.asmdef"), time.Now().Add(-2*time.Hour))

	if _, err := BuildCompilePlan(projectRoot, planDagDirectory, false); err != nil {
		t.Fatalf("expected an assembly definition older than the build to be accepted, got: %v", err)
	}
}

// Verifies an assembly definition deleted since the last build stops the run.
func TestBuildCompilePlanRejectsARemovedAssemblyDefinition(t *testing.T) {
	projectRoot := newPlanProject(t)
	if err := os.Remove(filepath.Join(projectRoot, "Assets", "C", "C.asmdef")); err != nil {
		t.Fatalf("failed to remove the assembly definition: %v", err)
	}

	if _, err := BuildCompilePlan(projectRoot, planDagDirectory, false); err == nil {
		t.Fatal("expected a removed assembly definition to be refused")
	}
}

// selectedAsDependentByName maps every unit of a plan to whether it was selected only through a reference.
func selectedAsDependentByName(plan BuildPlan) map[string]bool {
	flags := map[string]bool{}
	for _, unit := range plan.Units {
		flags[unit.Assembly.AssemblyName] = unit.SelectedAsDependent
	}

	return flags
}

// assertSelectedAsDependent checks the dependent-only mark of every unit the plan compiles.
func assertSelectedAsDependent(t *testing.T, plan BuildPlan, want map[string]bool) {
	t.Helper()
	got := selectedAsDependentByName(plan)
	if len(got) != len(want) {
		t.Fatalf("plan compiles %v, want the assemblies %v", unitNames(plan), want)
	}
	for name, expected := range want {
		if got[name] != expected {
			t.Errorf("%s selected as dependent = %t, want %t", name, got[name], expected)
		}
	}
}

// Verifies an assembly whose own sources changed is never marked as selected through a reference,
// even when it also references another assembly this run compiles.
func TestBuildCompilePlanMarksAChangedDependentAsChanged(t *testing.T) {
	projectRoot := newPlanProject(t)
	touchSource(t, projectRoot, "A")
	touchSource(t, projectRoot, "B")

	plan, err := BuildCompilePlan(projectRoot, planDagDirectory, false)
	if err != nil {
		t.Fatalf("expected a plan, got error: %v", err)
	}

	assertSelectedAsDependent(t, plan, map[string]bool{"A": false, "B": false, "C": true})
}

// Verifies the assemblies pulled in only because they reference a changed one are marked as such.
func TestBuildCompilePlanMarksTheAssembliesSelectedThroughAReference(t *testing.T) {
	projectRoot := newPlanProject(t)
	touchSource(t, projectRoot, "A")

	plan, err := BuildCompilePlan(projectRoot, planDagDirectory, false)
	if err != nil {
		t.Fatalf("expected a plan, got error: %v", err)
	}

	assertSelectedAsDependent(t, plan, map[string]bool{"A": false, "B": true, "C": true})
}

// Verifies --all marks nothing as dependent-only, so no unit of such a run can ever be skipped.
func TestBuildCompilePlanMarksNothingAsDependentWhenCompilingEverything(t *testing.T) {
	projectRoot := newPlanProject(t)

	plan, err := BuildCompilePlan(projectRoot, planDagDirectory, true)
	if err != nil {
		t.Fatalf("expected a plan, got error: %v", err)
	}

	assertSelectedAsDependent(t, plan, map[string]bool{"A": false, "B": false, "C": false})
}
