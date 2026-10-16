package compilecheck

import (
	"context"
	"fmt"
	"os"
	"path/filepath"
	"strings"
	"sync"
	"testing"
	"time"
)

// keptSurfaceOf is what the fake compiler writes for a unit whose reference assembly comes out
// byte-identical to the one the last Unity build left, which newUnit writes as "unity <name>".
func keptSurfaceOf(name string) string {
	return "unity " + name
}

// withOnlyUnits narrows the project's plan to the named units, keeping their order and dropping
// every plan reference to a unit left out, the way a run that does not rebuild those assemblies
// plans them.
func (project *cachedSchedulerProject) withOnlyUnits(names ...string) BuildPlan {
	included := map[string]bool{}
	for _, name := range names {
		included[name] = true
	}
	narrowed := project.plan
	narrowed.Units = []CompileUnit{}
	for _, unit := range project.plan.Units {
		if !included[unit.Assembly.AssemblyName] {
			continue
		}
		references := []string{}
		for _, reference := range unit.PlanReferences {
			if included[reference] {
				references = append(references, reference)
			}
		}
		unit.PlanReferences = references
		narrowed.Units = append(narrowed.Units, unit)
	}

	return narrowed
}

// compileOnly runs the named units alone through the scheduler, leaving the project's full plan as
// it was, and fails the test when the run could not finish.
func (project *cachedSchedulerProject) compileOnly(
	t *testing.T, recorder *schedulerRecorder, names ...string,
) []UnitResult {
	t.Helper()
	full := project.plan
	project.plan = project.withOnlyUnits(names...)
	defer func() { project.plan = full }()

	return project.compileOrFail(t, recorder)
}

// writtenResponseFile reads a response file the check wrote into its output directory.
func (project *cachedSchedulerProject) writtenResponseFile(t *testing.T, fileName string) string {
	t.Helper()
	content, err := os.ReadFile(filepath.Join(project.outputDirectory(), fileName))
	if err != nil {
		t.Fatalf("failed to read %s: %v", fileName, err)
	}

	return string(content)
}

// unityReferenceFlag is the reference flag that points a dependent at Unity's own reference
// assembly of the named unit.
func unityReferenceFlag(name string) string {
	return quoteFlag(referenceFlagPrefix, filepath.Join(planDagDirectory, name+referenceAssemblyExtension))
}

// producedReferenceFlag is the reference flag that points a dependent at the reference assembly
// this check produced for the named unit.
func producedReferenceFlag(plan BuildPlan, name string) string {
	return quoteFlag(referenceFlagPrefix,
		filepath.ToSlash(filepath.Join(plan.OutputDir, name+referenceAssemblyExtension)))
}

// assertReferenceFlags checks that a written response file points at the given references and not
// at the other spelling of the same assemblies.
func assertReferenceFlags(t *testing.T, written string, want []string, notWant []string) {
	t.Helper()
	lines := strings.Split(written, "\n")
	for _, flag := range want {
		if !containsLine(lines, flag) {
			t.Errorf("the response file is missing %q\ngot:\n%s", flag, written)
		}
	}
	for _, flag := range notWant {
		if containsLine(lines, flag) {
			t.Errorf("the response file should not contain %q\ngot:\n%s", flag, written)
		}
	}
}

// containsLine reports whether one of the lines is exactly line.
func containsLine(lines []string, line string) bool {
	for _, candidate := range lines {
		if candidate == line {
			return true
		}
	}

	return false
}

// Verifies a unit the plan holds for its own sources reuses the result it recorded while the
// assembly it references was not rebuilt, once a later run rebuilds that assembly into a reference
// assembly byte-identical to Unity's: the dependent compiles against exactly what it read before.
func TestCompileUnitsReusesADependentInThePlanWhenItsReferenceKeptTheSurface(t *testing.T) {
	project := newCachedSchedulerProject(t, map[string][]string{"Beta": {"Alpha"}}, "Alpha", "Beta")
	project.compileOnly(t, newCachedSchedulerRecorder("Beta"), "Beta")

	project.editSource(t, "Alpha", "class Alpha { void Body() { } }")
	recorder := newCachedSchedulerRecorder("Alpha", "Beta")
	recorder.surfaces["Alpha"] = keptSurfaceOf("Alpha")
	results := project.compileOrFail(t, recorder)

	if recorder.runs["Alpha"] != 1 {
		t.Errorf("the edited assembly should have compiled once, got %d", recorder.runs["Alpha"])
	}
	if recorder.runs["Beta"] != 0 || !resultOf(t, results, "Beta").Reused {
		t.Errorf("expected Beta to be reused without a compile, got %+v after %d compiles",
			resultOf(t, results, "Beta"), recorder.runs["Beta"])
	}
}

// Verifies the same reuse holds when the assembly the dependent references was itself replayed
// from an earlier run: its reference assembly on disk is still byte-identical to Unity's, so the
// dependent still reads Unity's.
func TestCompileUnitsReusesADependentWhenItsReplayedReferenceKeptTheSurface(t *testing.T) {
	project := newCachedSchedulerProject(t, map[string][]string{"Beta": {"Alpha"}}, "Alpha", "Beta")
	project.compileOnly(t, newCachedSchedulerRecorder("Beta"), "Beta")
	project.editSource(t, "Alpha", "class Alpha { void Body() { } }")
	alphaAlone := newCachedSchedulerRecorder("Alpha")
	alphaAlone.surfaces["Alpha"] = keptSurfaceOf("Alpha")
	project.compileOnly(t, alphaAlone, "Alpha")

	recorder := newCachedSchedulerRecorder("Alpha", "Beta")
	recorder.surfaces["Alpha"] = keptSurfaceOf("Alpha")
	results := project.compileOrFail(t, recorder)

	if !resultOf(t, results, "Alpha").Reused {
		t.Fatalf("expected Alpha to be replayed, got %+v", resultOf(t, results, "Alpha"))
	}
	if recorder.runs["Beta"] != 0 || !resultOf(t, results, "Beta").Reused {
		t.Errorf("expected Beta to be reused without a compile, got %+v after %d compiles",
			resultOf(t, results, "Beta"), recorder.runs["Beta"])
	}
}

// Verifies a dependent that has to compile reads Unity's reference assembly for the reference that
// kept its surface and this run's output for the one that changed, each decided on its own.
func TestCompileUnitsRewritesOnlyTheReferencesWhoseSurfaceChanged(t *testing.T) {
	project := newCachedSchedulerProject(t,
		map[string][]string{"Beta": {"Alpha", "Gamma"}}, "Alpha", "Gamma", "Beta")
	project.plan.Units[2].SelectedAsDependent = true
	recorder := newCachedSchedulerRecorder("Alpha", "Gamma", "Beta")
	recorder.surfaces["Alpha"] = keptSurfaceOf("Alpha")
	recorder.surfaces["Gamma"] = "changed Gamma"

	project.compileOrFail(t, recorder)

	if recorder.runs["Beta"] != 1 {
		t.Fatalf("expected Beta to compile once, got %d", recorder.runs["Beta"])
	}
	assertReferenceFlags(t, project.writtenResponseFile(t, "Beta"+responseFileExtension),
		[]string{unityReferenceFlag("Alpha"), producedReferenceFlag(project.plan, "Gamma")},
		[]string{producedReferenceFlag(project.plan, "Alpha"), unityReferenceFlag("Gamma")})
}

// Verifies --all keys a reference that kept its surface the way an ordinary run keys it, so the
// ordinary run after --all reuses what --all compiled instead of compiling it once more.
func TestCompileUnitsKeysAKeptReferenceTheSameWayWithAll(t *testing.T) {
	project := newCachedSchedulerProject(t, map[string][]string{"Beta": {"Alpha"}}, "Alpha", "Beta")
	project.plan.All = true
	all := newCachedSchedulerRecorder("Alpha", "Beta")
	all.surfaces["Alpha"] = keptSurfaceOf("Alpha")
	project.compileOrFail(t, all)
	project.plan.All = false

	recorder := newCachedSchedulerRecorder("Beta")
	results := project.compileOnly(t, recorder, "Beta")

	if recorder.runs["Beta"] != 0 || !resultOf(t, results, "Beta").Reused {
		t.Errorf("expected Beta to be reused after --all, got %+v after %d compiles",
			resultOf(t, results, "Beta"), recorder.runs["Beta"])
	}
}

// newLargeDependentProject lays out Alpha and a Beta that references it with enough sources for the
// changed-files check, each source split into a declaration and a body the fake worker tells apart.
func newLargeDependentProject(t *testing.T) (*cachedSchedulerProject, *fakeFastCheckWorker) {
	t.Helper()
	project := newCachedSchedulerProject(t, map[string][]string{"Beta": {"Alpha"}}, "Alpha", "Beta")
	sources := make([]string, 0, fastPathMinimumSources)
	for index := 0; index < fastPathMinimumSources; index++ {
		source := fmt.Sprintf("Assets/Beta/File%03d.cs", index)
		project.writeFile(t, source, bodyContent(index, "body"))
		sources = append(sources, source)
	}
	project.plan.Units[1].Sources = sources

	return project, newFakeFastCheckWorker()
}

// compileWithFastPath runs the project's plan, narrowed to the named units, with the changed-files
// check in place.
func compileWithFastPath(
	t *testing.T, project *cachedSchedulerProject, worker *fakeFastCheckWorker, recorder *schedulerRecorder,
	names ...string,
) compileOutcome {
	t.Helper()
	compiler := Compiler{
		Paths:       project.paths,
		ProjectRoot: project.root,
		Timeout:     time.Minute,
		Run:         recorder.runner(),
		FastPath:    &fastPathRunner{Worker: worker},
	}
	outcome, err := compileUnits(context.Background(), compiler, project.withOnlyUnits(names...), 4)
	if err != nil {
		t.Fatalf("expected the run to succeed, got error: %v", err)
	}

	return outcome
}

// Verifies a dependent the plan holds for its own edit can still be checked on its changed files
// when the assembly it references was rebuilt into a reference assembly byte-identical to Unity's,
// and that the check reads Unity's reference assembly, as the baseline did.
func TestCompileUnitsLetsADependentInThePlanFastCheckWhenItsReferenceKeptTheSurface(t *testing.T) {
	project, worker := newLargeDependentProject(t)
	compileWithFastPath(t, project, worker, newCachedSchedulerRecorder("Beta"), "Beta")
	if !project.hasBaselineOf("Beta") {
		t.Fatal("expected the first full compile of Beta to record a baseline")
	}

	project.editSource(t, "Alpha", "class Alpha { void Body() { } }")
	project.writeFile(t, "Assets/Beta/File007.cs", bodyContent(7, "edited body"))
	recorder := newCachedSchedulerRecorder("Alpha", "Beta")
	recorder.surfaces["Alpha"] = keptSurfaceOf("Alpha")
	outcome := compileWithFastPath(t, project, worker, recorder, "Alpha", "Beta")

	beta := resultOf(t, outcome.Units, "Beta")
	if !beta.FastChecked || beta.FastPathFallbackReason != "" || recorder.runs["Beta"] != 0 {
		t.Fatalf("expected Beta to be checked on its changed files, got %+v after %d compiles",
			beta, recorder.runs["Beta"])
	}
	assertReferenceFlags(t, project.writtenResponseFile(t, "Beta"+fastCheckResponseFileSuffix),
		[]string{unityReferenceFlag("Alpha")}, []string{producedReferenceFlag(project.plan, "Alpha")})
}

// surfaceComparisonCounter stands in for the surface comparison and counts which units a run
// asked it about, answering that every surface changed.
type surfaceComparisonCounter struct {
	mutex    sync.Mutex
	compared map[string]int
}

// compare records one comparison of the unit's surface.
func (counter *surfaceComparisonCounter) compare(_ string, _ BuildPlan, unit CompileUnit) SurfaceVerdict {
	counter.mutex.Lock()
	defer counter.mutex.Unlock()
	counter.compared[unit.Assembly.AssemblyName]++

	return SurfaceVerdict{Reason: SurfaceBytesDiffer, Unnamed: SurfaceBytesDiffer}
}

// Verifies a run compares the surface only of the units whose answer something in the plan reads:
// a unit nothing in the plan references, and a unit that failed, whose dependents are blocked
// whatever it produced, are not compared at all.
func TestCompileUnitsDoesNotCompareASurfaceNobodyInThePlanReads(t *testing.T) {
	project := newCachedSchedulerProject(t,
		map[string][]string{"Beta": {"Alpha"}, "Delta": {"Gamma"}}, "Alpha", "Gamma", "Beta", "Delta")
	recorder := newCachedSchedulerRecorder("Alpha", "Gamma", "Beta", "Delta")
	recorder.output["Gamma"] = fakeCompilerOutput{
		stdout: "Assets/Gamma/Gamma.cs(1,1): error CS1002: ; expected", exitCode: 1,
	}
	counter := &surfaceComparisonCounter{compared: map[string]int{}}
	compiler := Compiler{
		Paths:          project.paths,
		ProjectRoot:    project.root,
		Timeout:        time.Minute,
		Run:            recorder.runner(),
		compareSurface: counter.compare,
	}

	if _, err := compileUnits(context.Background(), compiler, project.plan, 4); err != nil {
		t.Fatalf("expected the run to succeed, got error: %v", err)
	}

	if counter.compared["Alpha"] != 1 {
		t.Errorf("expected Alpha, which Beta reads, to be compared once, got %d", counter.compared["Alpha"])
	}
	for _, name := range []string{"Gamma", "Beta", "Delta"} {
		if counter.compared[name] != 0 {
			t.Errorf("expected %s not to be compared, got %d", name, counter.compared[name])
		}
	}
}

// Verifies a dependent left out because its reference kept the surface keeps the baseline of its own
// earlier full compile, so its next edit is checked on the changed files instead of compiled in full.
func TestCompileUnitsKeepsTheBaselineOfASkippedDependent(t *testing.T) {
	project, worker := newLargeDependentProject(t)
	compileWithFastPath(t, project, worker, newCachedSchedulerRecorder("Beta"), "Beta")
	if !project.hasBaselineOf("Beta") {
		t.Fatal("expected the first full compile of Beta to record a baseline")
	}

	project.editSource(t, "Alpha", "class Alpha { void Body() { } }")
	markSelectedAsDependent(t, project.plan, "Beta")
	skipping := newCachedSchedulerRecorder("Alpha", "Beta")
	skipping.surfaces["Alpha"] = keptSurfaceOf("Alpha")
	skipped := compileWithFastPath(t, project, worker, skipping, "Alpha", "Beta")
	if skipping.runs["Beta"] != 0 || skipped.Skipped != 1 {
		t.Fatalf("expected Beta to be left out, got %d compiles and %d skips", skipping.runs["Beta"], skipped.Skipped)
	}

	project.plan.Units[indexOfUnit(t, project.plan, "Beta")].SelectedAsDependent = false
	project.writeFile(t, "Assets/Beta/File007.cs", bodyContent(7, "edited body"))
	recorder := newCachedSchedulerRecorder("Beta")
	outcome := compileWithFastPath(t, project, worker, recorder, "Beta")

	beta := resultOf(t, outcome.Units, "Beta")
	if !beta.FastChecked || strings.Contains(beta.FastPathFallbackReason, "no baseline") {
		t.Fatalf("expected Beta's edit to be checked against the kept baseline, got %+v", beta)
	}
}

// Verifies a dependent compiled in the same run reads Unity's reference assembly for a skipped
// dependent it references, not the output an earlier run left for it, while it reads this run's
// output for a reference whose surface changed.
func TestCompileUnitsReadsUnitysReferenceForASkippedDependent(t *testing.T) {
	project := newCachedSchedulerProject(t,
		map[string][]string{"Beta": {"Alpha"}, "Delta": {"Beta", "Gamma"}}, "Alpha", "Gamma", "Beta", "Delta")
	markSelectedAsDependent(t, project.plan, "Beta", "Delta")
	writeStaleUnitOutput(t, project.root, project.plan, "Beta")
	recorder := newCachedSchedulerRecorder("Alpha", "Gamma", "Beta", "Delta")
	recorder.surfaces["Alpha"] = keptSurfaceOf("Alpha")
	recorder.surfaces["Gamma"] = "changed Gamma"

	project.compileOrFail(t, recorder)

	if recorder.runs["Beta"] != 0 || recorder.runs["Delta"] != 1 {
		t.Fatalf("expected Beta left out and Delta compiled once, got %d and %d",
			recorder.runs["Beta"], recorder.runs["Delta"])
	}
	assertReferenceFlags(t, project.writtenResponseFile(t, "Delta"+responseFileExtension),
		[]string{unityReferenceFlag("Beta"), producedReferenceFlag(project.plan, "Gamma")},
		[]string{producedReferenceFlag(project.plan, "Beta")})
}
