package compilecheck

import (
	"context"
	"errors"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
	"sync"
	"testing"
	"time"
)

// newSchedulerPlan builds a plan of named units, each referencing the units named for it.
func newSchedulerPlan(references map[string][]string, names ...string) BuildPlan {
	units := make([]CompileUnit, 0, len(names))
	for _, name := range names {
		units = append(units, CompileUnit{
			Assembly: ResponseFile{
				AssemblyName:  name,
				RefOutputPath: filepath.Join(planDagDirectory, name+referenceAssemblyExtension),
			},
			PlanReferences: references[name],
		})
	}

	return BuildPlan{
		DagDir:    planDagDirectory,
		OutputDir: filepath.Join("Library", "uloop", "compile-check", "aaaa.dag"),
		Units:     units,
	}
}

// schedulerRecorder stands in for csc: it reports what ran, when, and how much ran at once.
type schedulerRecorder struct {
	mutex       sync.Mutex
	hold        time.Duration
	holdByUnit  map[string]time.Duration
	failingUnit string
	running     int
	maxRunning  int
	started     map[string]bool
	finished    map[string]bool
	// surfaces holds, per unit, the bytes the fake compiler writes as that unit's reference
	// assembly. A unit missing from it writes none, which is what a failed compile does.
	surfaces     map[string]string
	finishedWhen map[string][]string // unit -> the units already finished when it started
}

// newSchedulerRecorder prepares a recorder whose units each take the same time to compile.
func newSchedulerRecorder(hold time.Duration) *schedulerRecorder {
	return &schedulerRecorder{
		hold:         hold,
		holdByUnit:   map[string]time.Duration{},
		started:      map[string]bool{},
		surfaces:     map[string]string{},
		finished:     map[string]bool{},
		finishedWhen: map[string][]string{},
	}
}

// runner reports the fake compiler invocation, recording concurrency and dependency order.
func (recorder *schedulerRecorder) runner() CommandRunner {
	return func(_ context.Context, cmd *exec.Cmd) (string, string, int, error) {
		name := assemblyOfFakeCommand(cmd)
		recorder.mutex.Lock()
		recorder.started[name] = true
		recorder.running++
		if recorder.running > recorder.maxRunning {
			recorder.maxRunning = recorder.running
		}
		already := []string{}
		for finishedName := range recorder.finished {
			already = append(already, finishedName)
		}
		recorder.finishedWhen[name] = already
		hold := recorder.hold
		if unitHold, found := recorder.holdByUnit[name]; found {
			hold = unitHold
		}
		failing := recorder.failingUnit == name
		recorder.mutex.Unlock()

		time.Sleep(hold)

		recorder.writeReferenceAssembly(cmd, name)

		recorder.mutex.Lock()
		recorder.running--
		recorder.finished[name] = true
		recorder.mutex.Unlock()

		if failing {
			return "", "no compiler here", 0, errors.New("failed to start the compiler")
		}

		return "", "", 0, nil
	}
}

// writeReferenceAssembly writes the reference assembly this fake invocation is set up to produce,
// beside the response file it was given, which is where csc writes it too.
func (recorder *schedulerRecorder) writeReferenceAssembly(cmd *exec.Cmd, name string) {
	recorder.mutex.Lock()
	surface, produces := recorder.surfaces[name]
	recorder.mutex.Unlock()
	if !produces {
		return
	}

	last := cmd.Args[len(cmd.Args)-1]
	path := filepath.Join(filepath.Dir(strings.TrimPrefix(last, "@")), name+referenceAssemblyExtension)
	if err := os.WriteFile(path, []byte(surface), 0o600); err != nil {
		panic(err)
	}
}

// assemblyOfFakeCommand reads the assembly name out of the response file the invocation was given.
func assemblyOfFakeCommand(cmd *exec.Cmd) string {
	last := cmd.Args[len(cmd.Args)-1]

	return strings.TrimSuffix(filepath.Base(strings.TrimPrefix(last, "@")), responseFileExtension)
}

// newSchedulerCompiler wires a compiler that runs the recorder instead of csc.
func newSchedulerCompiler(t *testing.T, recorder *schedulerRecorder) Compiler {
	t.Helper()

	return Compiler{
		Paths:       EditorCompilerPaths{DotnetHostPath: "/dotnet", CompilerDllPath: "/csc.dll"},
		ProjectRoot: t.TempDir(),
		Timeout:     time.Minute,
		Run:         recorder.runner(),
	}
}

// Verifies a unit waits for every assembly it references in the plan before it starts.
func TestCompileUnitsStartsAUnitOnlyAfterItsReferencesFinished(t *testing.T) {
	plan := newSchedulerPlan(
		map[string][]string{"B": {"A"}, "C": {"B"}}, "A", "B", "C")
	recorder := newSchedulerRecorder(10 * time.Millisecond)

	if _, _, err := compileUnits(
		context.Background(), newSchedulerCompiler(t, recorder), plan, 4); err != nil {
		t.Fatalf("expected the chain to compile, got error: %v", err)
	}

	for _, unit := range []string{"B", "C"} {
		for _, reference := range plan.Units[indexOfUnit(t, plan, unit)].PlanReferences {
			if !containsName(recorder.finishedWhen[unit], reference) {
				t.Errorf("%s started before %s finished", unit, reference)
			}
		}
	}
}

// Verifies no more than the requested number of assemblies compile at the same time.
func TestCompileUnitsRunsNoMoreThanTheRequestedNumberAtOnce(t *testing.T) {
	plan := newSchedulerPlan(nil, "A", "B", "C", "D", "E")
	recorder := newSchedulerRecorder(20 * time.Millisecond)

	if _, _, err := compileUnits(
		context.Background(), newSchedulerCompiler(t, recorder), plan, 2); err != nil {
		t.Fatalf("expected the independent units to compile, got error: %v", err)
	}

	if recorder.maxRunning != 2 {
		t.Errorf("at most 2 units should run at once and 2 should be reached, got %d",
			recorder.maxRunning)
	}
}

// Verifies the results keep the plan's order even when the units finish in another one.
func TestCompileUnitsReturnsResultsInPlanOrder(t *testing.T) {
	plan := newSchedulerPlan(nil, "A", "B", "C")
	recorder := newSchedulerRecorder(0)
	recorder.holdByUnit = map[string]time.Duration{
		"A": 40 * time.Millisecond,
		"B": 20 * time.Millisecond,
	}

	results, _, err := compileUnits(
		context.Background(), newSchedulerCompiler(t, recorder), plan, 3)
	if err != nil {
		t.Fatalf("expected the independent units to compile, got error: %v", err)
	}

	compiled := []string{}
	for _, result := range results {
		compiled = append(compiled, result.Assembly)
	}
	assertStrings(t, "results", compiled, []string{"A", "B", "C"})
}

// Verifies a unit whose compiler cannot be started stops the run and reports that failure.
func TestCompileUnitsStopsTheRunWhenTheCompilerCannotBeStarted(t *testing.T) {
	plan := newSchedulerPlan(map[string][]string{"C": {"A"}}, "A", "B", "C")
	recorder := newSchedulerRecorder(10 * time.Millisecond)
	recorder.failingUnit = "A"

	_, _, err := compileUnits(context.Background(), newSchedulerCompiler(t, recorder), plan, 1)
	if err == nil {
		t.Fatal("expected the failure to start the compiler to be reported")
	}
	if !strings.Contains(err.Error(), "A") {
		t.Errorf("the error should name the assembly that failed, got: %v", err)
	}
	if recorder.finished["C"] {
		t.Error("a unit depending on the failed one should not have been compiled")
	}
	// Why B rather than C alone: C waits for A whatever the scheduler does, so only an independent
	// unit shows that the failure stopped the run instead of merely blocking what depended on it.
	if recorder.started["B"] {
		t.Error("a unit independent of the failed one should not have been started")
	}
}

// indexOfUnit finds one named unit in a plan.
func indexOfUnit(t *testing.T, plan BuildPlan, name string) int {
	t.Helper()
	for index, unit := range plan.Units {
		if unit.Assembly.AssemblyName == name {
			return index
		}
	}
	t.Fatalf("the plan has no unit named %s", name)

	return -1
}

// markSelectedAsDependent marks the named units as ones this run picked up only through a reference.
func markSelectedAsDependent(t *testing.T, plan BuildPlan, names ...string) {
	t.Helper()
	for _, name := range names {
		plan.Units[indexOfUnit(t, plan, name)].SelectedAsDependent = true
	}
}

// writeUnityReferenceAssembly writes the reference assembly the last Unity build left for an assembly.
func writeUnityReferenceAssembly(t *testing.T, projectRoot string, name string, surface string) {
	t.Helper()
	directory := filepath.Join(projectRoot, planDagDirectory)
	if err := os.MkdirAll(directory, outputDirPermissions); err != nil {
		t.Fatalf("failed to create the dag directory: %v", err)
	}
	path := filepath.Join(directory, name+referenceAssemblyExtension)
	if err := os.WriteFile(path, []byte(surface), 0o600); err != nil {
		t.Fatalf("failed to write %s: %v", path, err)
	}
}

// compiledNames lists the assemblies a run reported results for, in the order it returned them.
func compiledNames(results []UnitResult) []string {
	names := make([]string, 0, len(results))
	for _, result := range results {
		names = append(names, result.Assembly)
	}

	return names
}

// Verifies a dependent is left out when the assembly it waited for came out byte-identical to the
// one the last Unity build produced, and that the stale output of an earlier run is removed with it.
func TestCompileUnitsSkipsADependentWhoseReferenceKeptItsSurface(t *testing.T) {
	plan := newSchedulerPlan(map[string][]string{"B": {"A"}}, "A", "B")
	markSelectedAsDependent(t, plan, "B")
	recorder := newSchedulerRecorder(0)
	recorder.surfaces["A"] = "surface"
	recorder.surfaces["B"] = "b"
	compiler := newSchedulerCompiler(t, recorder)
	writeUnityReferenceAssembly(t, compiler.ProjectRoot, "A", "surface")
	staleOutput := filepath.Join(
		compiler.ProjectRoot, plan.OutputDir, "B"+referenceAssemblyExtension)
	if err := os.MkdirAll(filepath.Dir(staleOutput), outputDirPermissions); err != nil {
		t.Fatalf("failed to create the output directory: %v", err)
	}
	if err := os.WriteFile(staleOutput, []byte("stale"), 0o600); err != nil {
		t.Fatalf("failed to write the stale output: %v", err)
	}

	results, skipped, err := compileUnits(context.Background(), compiler, plan, 2)
	if err != nil {
		t.Fatalf("expected the run to succeed, got error: %v", err)
	}

	assertStrings(t, "results", compiledNames(results), []string{"A"})
	if skipped != 1 {
		t.Errorf("reference skips = %d, want 1", skipped)
	}
	if recorder.started["B"] {
		t.Error("a dependent whose reference kept its surface should not have been compiled")
	}
	if _, statErr := os.Stat(staleOutput); !os.IsNotExist(statErr) {
		t.Error("the skipped unit's earlier output should have been removed")
	}
}

// Verifies a dependent is compiled when its reference produced a different reference assembly.
func TestCompileUnitsCompilesADependentWhoseReferenceChangedItsSurface(t *testing.T) {
	plan := newSchedulerPlan(map[string][]string{"B": {"A"}}, "A", "B")
	markSelectedAsDependent(t, plan, "B")
	recorder := newSchedulerRecorder(0)
	recorder.surfaces["A"] = "changed surface"
	compiler := newSchedulerCompiler(t, recorder)
	writeUnityReferenceAssembly(t, compiler.ProjectRoot, "A", "surface")

	results, skipped, err := compileUnits(context.Background(), compiler, plan, 2)
	if err != nil {
		t.Fatalf("expected the run to succeed, got error: %v", err)
	}

	assertStrings(t, "results", compiledNames(results), []string{"A", "B"})
	if skipped != 0 {
		t.Errorf("reference skips = %d, want 0", skipped)
	}
}

// Verifies the skip carries down the chain: a dependent left out changes nothing for its own
// dependents, so they are left out too, which is what Unity's own build does.
func TestCompileUnitsSkipsTheWholeChainBelowAnUnchangedSurface(t *testing.T) {
	plan := newSchedulerPlan(map[string][]string{"B": {"A"}, "C": {"B"}}, "A", "B", "C")
	markSelectedAsDependent(t, plan, "B", "C")
	recorder := newSchedulerRecorder(0)
	recorder.surfaces["A"] = "surface"
	compiler := newSchedulerCompiler(t, recorder)
	writeUnityReferenceAssembly(t, compiler.ProjectRoot, "A", "surface")

	results, skipped, err := compileUnits(context.Background(), compiler, plan, 3)
	if err != nil {
		t.Fatalf("expected the run to succeed, got error: %v", err)
	}

	assertStrings(t, "results", compiledNames(results), []string{"A"})
	if skipped != 2 {
		t.Errorf("reference skips = %d, want 2", skipped)
	}
	if recorder.started["C"] {
		t.Error("a unit below a skipped one should not have been compiled")
	}
}

// Verifies an assembly asked for on its own - by --all, or because its own sources changed - is
// compiled even when everything it references kept its surface.
func TestCompileUnitsNeverSkipsAUnitThatWasNotSelectedThroughAReference(t *testing.T) {
	plan := newSchedulerPlan(map[string][]string{"B": {"A"}}, "A", "B")
	recorder := newSchedulerRecorder(0)
	recorder.surfaces["A"] = "surface"
	compiler := newSchedulerCompiler(t, recorder)
	writeUnityReferenceAssembly(t, compiler.ProjectRoot, "A", "surface")

	results, skipped, err := compileUnits(context.Background(), compiler, plan, 2)
	if err != nil {
		t.Fatalf("expected the run to succeed, got error: %v", err)
	}

	assertStrings(t, "results", compiledNames(results), []string{"A", "B"})
	if skipped != 0 {
		t.Errorf("reference skips = %d, want 0", skipped)
	}
}

// Verifies one changed reference is enough: a dependent waiting on two assemblies is compiled when
// either of them changed its surface.
func TestCompileUnitsCompilesADependentWhenOnlyOneReferenceChanged(t *testing.T) {
	plan := newSchedulerPlan(map[string][]string{"B": {"A", "D"}}, "A", "D", "B")
	markSelectedAsDependent(t, plan, "B")
	recorder := newSchedulerRecorder(0)
	recorder.surfaces["A"] = "surface"
	recorder.surfaces["D"] = "changed surface"
	compiler := newSchedulerCompiler(t, recorder)
	writeUnityReferenceAssembly(t, compiler.ProjectRoot, "A", "surface")
	writeUnityReferenceAssembly(t, compiler.ProjectRoot, "D", "surface")

	results, skipped, err := compileUnits(context.Background(), compiler, plan, 3)
	if err != nil {
		t.Fatalf("expected the run to succeed, got error: %v", err)
	}

	assertStrings(t, "results", compiledNames(results), []string{"A", "D", "B"})
	if skipped != 0 {
		t.Errorf("reference skips = %d, want 0", skipped)
	}
}

// Verifies a dependent is compiled when its reference failed to compile and wrote no reference
// assembly, so the run still reports the errors that failure causes on the other side.
func TestCompileUnitsCompilesADependentWhoseReferenceProducedNothing(t *testing.T) {
	plan := newSchedulerPlan(map[string][]string{"B": {"A"}}, "A", "B")
	markSelectedAsDependent(t, plan, "B")
	recorder := newSchedulerRecorder(0)
	compiler := newSchedulerCompiler(t, recorder)
	writeUnityReferenceAssembly(t, compiler.ProjectRoot, "A", "surface")

	results, skipped, err := compileUnits(context.Background(), compiler, plan, 2)
	if err != nil {
		t.Fatalf("expected the run to succeed, got error: %v", err)
	}

	assertStrings(t, "results", compiledNames(results), []string{"A", "B"})
	if skipped != 0 {
		t.Errorf("reference skips = %d, want 0", skipped)
	}
}
