package compilecheck

import (
	"context"
	"testing"
	"time"
)

// withAnalyzer gives the named unit of the project an analyzer, which is how a source generator
// reaches the compiler.
func withAnalyzer(t *testing.T, project *cachedSchedulerProject, name string) {
	t.Helper()
	index := planIndexOf(t, project.plan, name)
	project.plan.Units[index].Assembly.Analyzers = []string{"Library/Generators/Generator.dll"}
}

// Verifies a dependent with analyzers and no saved generator output that writes a changed name
// compiles for that name, and is reported with the name rather than the reason.
func TestCompileUnitsNamesTheChangedNameEvenWithoutSavedGeneratorOutput(t *testing.T) {
	project, recorder := newNamesProject(t, "class B { Foo f; }")
	withAnalyzer(t, project, "B")

	outcome := project.compileWithVerdicts(t, recorder, namedVerdicts{"A": namedChangeOf("Foo")})

	assertStrings(t, "results", compiledNames(outcome.Units), []string{"A", "B"})
	assertChangedReferences(t, resultOf(t, outcome.Units, "B"), []ChangedReference{{Assembly: "A", Name: "Foo"}})
	if outcome.NameIndex.WithoutGenerated != 0 {
		t.Errorf("name index = %+v, want no unit compiled for want of generator output", outcome.NameIndex)
	}
}

// Verifies a dependent with analyzers and no saved generator output compiles on a named change it
// writes no name of, because its generators might, and is counted and reported with the reason.
func TestCompileUnitsCompilesADependentWithoutSavedGeneratorOutput(t *testing.T) {
	project, recorder := newNamesProject(t, "class B { int Bar; }")
	withAnalyzer(t, project, "B")

	outcome := project.compileWithVerdicts(t, recorder, namedVerdicts{"A": namedChangeOf("Foo")})

	assertStrings(t, "results", compiledNames(outcome.Units), []string{"A", "B"})
	assertChangedReferences(t, resultOf(t, outcome.Units, "B"),
		[]ChangedReference{{Assembly: "A", Reason: noSavedGeneratorOutputReason}})
	if outcome.NameIndex.WithoutGenerated != 1 || len(outcome.NameSkips) != 0 {
		t.Errorf("name index = %+v, name skips = %+v; want one unit compiled for want of generator output",
			outcome.NameIndex, outcome.NameSkips)
	}
}

// Verifies a dependent without analyzers is still left out by its sources alone when it has no saved
// generator output, since it has no generators to miss.
func TestCompileUnitsLeavesOutADependentWithoutAnalyzersByItsSourcesAlone(t *testing.T) {
	project, recorder := newNamesProject(t, "class B { int Bar; }")

	outcome := project.compileWithVerdicts(t, recorder, namedVerdicts{"A": namedChangeOf("Foo")})

	assertStrings(t, "results", compiledNames(outcome.Units), []string{"A"})
	if recorder.started["B"] || outcome.NameIndex.WithoutGenerated != 0 {
		t.Errorf("B started = %v, name index = %+v; want B left out by names", recorder.started["B"], outcome.NameIndex)
	}
}

// Verifies a dependent compiled once for want of saved generator output is left out by names on the
// next named change it writes no name of, since that compile saved its generator output.
func TestCompileUnitsLeavesOutADependentOnceItHasSavedGeneratorOutput(t *testing.T) {
	project, recorder := newNamesProject(t, "class B { int Bar; }")
	withAnalyzer(t, project, "B")
	project.compileWithVerdicts(t, recorder, namedVerdicts{"A": namedChangeOf("Foo")})
	if recorder.runs["B"] != 1 {
		t.Fatalf("expected the first run to compile B once, got %d", recorder.runs["B"])
	}

	project.editSource(t, "A", "class A { void Body() { } }")
	outcome := project.compileWithVerdicts(t, recorder, namedVerdicts{"A": namedChangeOf("Foo")})

	if recorder.runs["B"] != 1 || outcome.NameIndex.WithoutGenerated != 0 {
		t.Errorf("B compiled %d times in all, name index = %+v; want B left out by names the second time",
			recorder.runs["B"], outcome.NameIndex)
	}
	if outcome.NameIndex.WithGenerated != 1 {
		t.Errorf("name index = %+v, want B read with its saved generator output", outcome.NameIndex)
	}
}

// Verifies a dependent whose compile never ran is compiled again for want of saved generator output
// on the next named change, since the failed compile saved none.
func TestCompileUnitsCompilesAgainADependentWhoseCompileNeverRan(t *testing.T) {
	project, recorder := newNamesProject(t, "class B { int Bar; }")
	withAnalyzer(t, project, "B")
	recorder.failingUnit = "B"
	failing := Compiler{
		Paths: project.paths, ProjectRoot: project.root, Timeout: time.Minute, Run: recorder.runner(),
		compareSurface: namedVerdicts{"A": namedChangeOf("Foo")}.compare,
	}
	if _, err := compileUnits(context.Background(), failing, project.plan, 4); err == nil {
		t.Fatal("expected the first run to fail when B's compiler cannot start")
	}

	recorder.failingUnit = ""
	project.editSource(t, "A", "class A { void Body() { } }")
	outcome := project.compileWithVerdicts(t, recorder, namedVerdicts{"A": namedChangeOf("Foo")})

	assertChangedReferences(t, resultOf(t, outcome.Units, "B"),
		[]ChangedReference{{Assembly: "A", Reason: noSavedGeneratorOutputReason}})
	if outcome.NameIndex.WithoutGenerated != 1 || len(outcome.NameSkips) != 0 {
		t.Errorf("name index = %+v, name skips = %+v; want B compiled for want of generator output",
			outcome.NameIndex, outcome.NameSkips)
	}
}

// Verifies a dependent touched for want of saved generator output but blocked by another reference's
// errors is not counted, since it never compiled.
func TestCompileUnitsDoesNotCountABlockedDependentAsCompiledWithoutGeneratorOutput(t *testing.T) {
	project, recorder := newOptimisticCachedProject(t, map[string][]string{"D": {"A", "C"}}, []string{"D"}, "A", "C", "D")
	project.editSource(t, "D", "class D { int Bar; }")
	withAnalyzer(t, project, "D")
	recorder.output["C"] = fakeCompilerOutput{stdout: "Assets/C/C.cs(1,1): error CS1002: ; expected", exitCode: 1}

	outcome := project.compileWithVerdicts(t, recorder, namedVerdicts{"A": namedChangeOf("Foo")})

	assertStrings(t, "blocked", outcome.Blocked, []string{"D"})
	if outcome.NameIndex.WithoutGenerated != 0 {
		t.Errorf("name index = %+v, want the blocked dependent left out of the count", outcome.NameIndex)
	}
}

// Verifies a dependent left out by names keeps its saved generator output, so a later named change
// it writes no name of still leaves it out with that output read.
func TestCompileUnitsKeepsTheSavedGeneratorOutputOfASkippedDependent(t *testing.T) {
	project, recorder := newNamesProject(t, "class B { int Bar; }")
	withAnalyzer(t, project, "B")
	project.compileWithVerdicts(t, recorder, namedVerdicts{"A": namedChangeOf("Foo")})
	project.editSource(t, "A", "class A { void Body() { } }")
	project.compileWithVerdicts(t, recorder, namedVerdicts{"A": namedChangeOf("Foo")})

	project.editSource(t, "A", "class A { void Body() { int x; } }")
	outcome := project.compileWithVerdicts(t, recorder, namedVerdicts{"A": namedChangeOf("Foo")})

	if recorder.runs["B"] != 1 {
		t.Errorf("B compiled %d times in all, want once", recorder.runs["B"])
	}
	if outcome.NameIndex.WithGenerated != 1 || outcome.NameIndex.WithoutGenerated != 0 {
		t.Errorf("name index = %+v, want B read with its saved generator output", outcome.NameIndex)
	}
}
