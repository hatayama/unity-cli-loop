package compilecheck

import (
	"context"
	"errors"
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
			Assembly:       ResponseFile{AssemblyName: name},
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
	mutex        sync.Mutex
	hold         time.Duration
	holdByUnit   map[string]time.Duration
	failingUnit  string
	running      int
	maxRunning   int
	finished     map[string]bool
	finishedWhen map[string][]string // unit -> the units already finished when it started
}

// newSchedulerRecorder prepares a recorder whose units each take the same time to compile.
func newSchedulerRecorder(hold time.Duration) *schedulerRecorder {
	return &schedulerRecorder{
		hold:         hold,
		holdByUnit:   map[string]time.Duration{},
		finished:     map[string]bool{},
		finishedWhen: map[string][]string{},
	}
}

// runner reports the fake compiler invocation, recording concurrency and dependency order.
func (recorder *schedulerRecorder) runner() CommandRunner {
	return func(_ context.Context, cmd *exec.Cmd) (string, string, int, error) {
		name := assemblyOfFakeCommand(cmd)
		recorder.mutex.Lock()
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

	if _, err := compileUnits(
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

	if _, err := compileUnits(
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

	results, err := compileUnits(
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

	_, err := compileUnits(context.Background(), newSchedulerCompiler(t, recorder), plan, 1)
	if err == nil {
		t.Fatal("expected the failure to start the compiler to be reported")
	}
	if !strings.Contains(err.Error(), "A") {
		t.Errorf("the error should name the assembly that failed, got: %v", err)
	}
	if recorder.finished["C"] {
		t.Error("a unit depending on the failed one should not have been compiled")
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
