package compilecheck

import (
	"context"
	"path/filepath"
	"reflect"
	"testing"
	"time"
)

// namedVerdicts stands in for the surface comparison: the units it names get the verdict it holds,
// and every other unit is compared by bytes, as a run without the worker does.
type namedVerdicts map[string]SurfaceVerdict

func (verdicts namedVerdicts) compare(projectRoot string, plan BuildPlan, unit CompileUnit) SurfaceVerdict {
	if verdict, found := verdicts[unit.Assembly.AssemblyName]; found {
		return verdict
	}

	return Compiler{ProjectRoot: projectRoot}.referenceSurfaceVerdict(context.Background(), plan, unit)
}

// namedChangeOf is the verdict of a reference whose change the worker told by the given names.
func namedChangeOf(names ...string) SurfaceVerdict {
	return SurfaceVerdict{Reason: SurfaceChanged, ChangedNames: names}
}

// compileWithVerdicts runs the whole plan with the given surface verdicts and fails the test when the
// run could not finish.
func (project *cachedSchedulerProject) compileWithVerdicts(
	t *testing.T, recorder *schedulerRecorder, verdicts namedVerdicts,
) compileOutcome {
	t.Helper()
	compiler := Compiler{
		Paths:          project.paths,
		ProjectRoot:    project.root,
		Timeout:        time.Minute,
		Run:            recorder.runner(),
		compareSurface: verdicts.compare,
	}
	outcome, err := compileUnits(context.Background(), compiler, project.plan, 4)
	if err != nil {
		t.Fatalf("expected the run to succeed, got error: %v", err)
	}

	return outcome
}

// newNamesProject is A, selected for its own sources, and B, a dependent of A, whose source is given.
func newNamesProject(t *testing.T, sourceOfB string) (*cachedSchedulerProject, *schedulerRecorder) {
	t.Helper()
	project, recorder := newOptimisticCachedProject(t, map[string][]string{"B": {"A"}}, []string{"B"}, "A", "B")
	project.editSource(t, "B", sourceOfB)

	return project, recorder
}

func assertChangedReferences(t *testing.T, result UnitResult, want []ChangedReference) {
	t.Helper()
	if !reflect.DeepEqual(result.ChangedReferences, want) {
		t.Errorf("%s changed references = %+v, want %+v", result.Assembly, result.ChangedReferences, want)
	}
}

// Verifies a dependent that writes none of the names its reference's change touches is left out the
// way one whose reference kept its surface is, and is reported as left out by names.
func TestCompileUnitsSkipsADependentThatWritesNoChangedName(t *testing.T) {
	project, recorder := newNamesProject(t, "class B { int Bar; }")

	outcome := project.compileWithVerdicts(t, recorder, namedVerdicts{"A": namedChangeOf("A", "Foo")})

	assertStrings(t, "results", compiledNames(outcome.Units), []string{"A"})
	if recorder.started["B"] || outcome.Skipped != 1 {
		t.Errorf("B started = %v, skipped = %d; want B left out", recorder.started["B"], outcome.Skipped)
	}
	want := []NameSkip{{Assembly: "B", ChangedReferences: []string{"A"}}}
	if !reflect.DeepEqual(outcome.NameSkips, want) {
		t.Errorf("name skips = %+v, want %+v", outcome.NameSkips, want)
	}
	if outcome.NameIndex.Units != 1 || outcome.NameIndex.Unreadable != 0 {
		t.Errorf("name index = %+v, want one readable unit", outcome.NameIndex)
	}
}

// Verifies a dependent that writes one of the changed names compiles and says which name made it,
// whether it writes the member's name or only the type's.
func TestCompileUnitsCompilesADependentThatWritesAChangedName(t *testing.T) {
	cases := map[string]struct {
		source string
		names  []string
		want   string
	}{
		"member": {source: "class B { int x = Make().Foo; }", names: []string{"A", "Foo"}, want: "Foo"},
		"type":   {source: "class B { C c; }", names: []string{"C", "Foo"}, want: "C"},
	}
	for label, testCase := range cases {
		t.Run(label, func(t *testing.T) {
			project, recorder := newNamesProject(t, testCase.source)

			outcome := project.compileWithVerdicts(t, recorder, namedVerdicts{"A": namedChangeOf(testCase.names...)})

			assertStrings(t, "results", compiledNames(outcome.Units), []string{"A", "B"})
			assertChangedReferences(t, resultOf(t, outcome.Units, "B"), []ChangedReference{{Assembly: "A", Name: testCase.want}})
			if len(outcome.NameSkips) != 0 {
				t.Errorf("name skips = %+v, want none", outcome.NameSkips)
			}
		})
	}
}

// Verifies a dependent whose names cannot be read compiles on a named change, and is counted as
// unreadable.
func TestCompileUnitsCompilesADependentWhoseNamesCannotBeRead(t *testing.T) {
	project, recorder := newNamesProject(t, "class B { }")
	index := planIndexOf(t, project.plan, "B")
	project.plan.Units[index].Sources = append(project.plan.Units[index].Sources, "Assets/B/Missing.cs")

	outcome := project.compileWithVerdicts(t, recorder, namedVerdicts{"A": namedChangeOf("Foo")})

	assertStrings(t, "results", compiledNames(outcome.Units), []string{"A", "B"})
	if outcome.NameIndex.Unreadable != 1 {
		t.Errorf("name index = %+v, want one unreadable unit", outcome.NameIndex)
	}
}

// Verifies a change no name tells makes every dependent compile, even one that carries names as
// well, and the change is reported without a name.
func TestCompileUnitsCompilesEveryDependentOnAnUnnamedChange(t *testing.T) {
	verdicts := []SurfaceVerdict{
		{Reason: SurfaceChanged, Unnamed: "operator op_Addition"},
		{Reason: SurfaceChanged, Unnamed: "operator op_Addition", ChangedNames: []string{"Foo"}},
	}
	for _, verdict := range verdicts {
		project, recorder := newNamesProject(t, "class B { }")

		outcome := project.compileWithVerdicts(t, recorder, namedVerdicts{"A": verdict})

		assertStrings(t, "results", compiledNames(outcome.Units), []string{"A", "B"})
		assertChangedReferences(t, resultOf(t, outcome.Units, "B"), []ChangedReference{{Assembly: "A"}})
	}
}

// Verifies a dependent that started on the assumption that a reference keeps its surface keeps that
// compile when the reference changed only names the dependent never writes, whether it was selected
// as a dependent or for its own sources.
func TestCompileUnitsKeepsAnEarlyCompileWhenTheAssumedReferenceChangedNoNameItWrites(t *testing.T) {
	for label, dependents := range map[string][]string{"dependent": {"D1", "D2"}, "own sources": {"D1"}} {
		t.Run(label, func(t *testing.T) {
			project, recorder := newOptimisticCachedProject(t, chainReferences, dependents, "X", "K", "D1", "D2")
			recorder.holdUntilBegun(t, "K", "D2")
			recorder.surfaces["K"] = keptSurfaceOf("K")
			project.editSource(t, "D2", "class D2 { int Bar; }")

			outcome := project.compileWithVerdicts(t, recorder, namedVerdicts{"D1": namedChangeOf("D1", "Foo")})

			if recorder.runs["D2"] != 1 {
				t.Fatalf("D2 compiled %d times, want 1", recorder.runs["D2"])
			}
			d2 := resultOf(t, outcome.Units, "D2")
			if d2.CompiledAgainAfter != "" || !reflect.DeepEqual(d2.AssumedReferences, []string{"D1"}) {
				t.Errorf("D2 = %+v, want one compile that assumed D1", d2)
			}
			assertChangedReferences(t, d2, []ChangedReference{{Assembly: "X"}})
		})
	}
}

// Verifies a unit with analyzers and no saved generator output that started ahead of a reference
// compiles again when the reference changed only names it never writes, whether it was picked up as
// a dependent or started on its own sources: without that output, names cannot say it is untouched.
func TestCompileUnitsCompilesAgainAnEarlyUnitWithoutSavedGeneratorOutput(t *testing.T) {
	for label, dependents := range map[string][]string{"dependent": {"D1", "D2"}, "own sources": {"D1"}} {
		t.Run(label, func(t *testing.T) {
			project, recorder := newOptimisticCachedProject(t, chainReferences, dependents, "X", "K", "D1", "D2")
			recorder.holdUntilBegun(t, "K", "D2")
			recorder.surfaces["K"] = keptSurfaceOf("K")
			project.editSource(t, "D2", "class D2 { int Bar; }")
			withAnalyzer(t, project, "D2")

			outcome := project.compileWithVerdicts(t, recorder, namedVerdicts{"D1": namedChangeOf("D1", "Foo")})

			if recorder.runs["D2"] != 2 {
				t.Fatalf("D2 compiled %d times, want 2", recorder.runs["D2"])
			}
			d2 := resultOf(t, outcome.Units, "D2")
			if d2.CompiledAgainAfter != "D1" {
				t.Errorf("D2 compiled again after %q, want D1", d2.CompiledAgainAfter)
			}
			assertChangedReferences(t, d2,
				[]ChangedReference{{Assembly: "X"}, {Assembly: "D1", Reason: noSavedGeneratorOutputReason}})
			if outcome.NameIndex.WithoutGenerated != 1 {
				t.Errorf("name index = %+v, want D2 counted once", outcome.NameIndex)
			}
		})
	}
}

// Verifies a dependent that started on the assumption that a reference keeps its surface compiles
// again when the reference changed a name it writes.
func TestCompileUnitsCompilesAgainWhenTheAssumedReferenceChangedANameItWrites(t *testing.T) {
	project, recorder := newOptimisticCachedProject(t, chainReferences, []string{"D1", "D2"}, "X", "K", "D1", "D2")
	recorder.holdUntilBegun(t, "K", "D2")
	recorder.surfaces["K"] = keptSurfaceOf("K")
	project.editSource(t, "D2", "class D2 { int x = new D1().Foo; }")

	outcome := project.compileWithVerdicts(t, recorder, namedVerdicts{"D1": namedChangeOf("D1", "Foo")})

	if recorder.runs["D2"] != 2 {
		t.Fatalf("D2 compiled %d times, want 2", recorder.runs["D2"])
	}
	d2 := resultOf(t, outcome.Units, "D2")
	if d2.CompiledAgainAfter != "D1" {
		t.Errorf("D2 compiled again after %q, want D1", d2.CompiledAgainAfter)
	}
	assertChangedReferences(t, d2, []ChangedReference{{Assembly: "X"}, {Assembly: "D1", Name: "D1"}})
}

// Verifies a unit selected for its own sources still compiles against the reference assembly this
// run produced for a reference that changed only names it never writes: whether a reference touches
// a unit never decides which file the unit reads.
func TestCompileUnitsReadsTheProducedReferenceOfANameChangeItDoesNotWrite(t *testing.T) {
	project, recorder := newOptimisticCachedProject(t, map[string][]string{"B": {"A"}}, nil, "A", "B")
	project.editSource(t, "B", "class B { }")

	outcome := project.compileWithVerdicts(t, recorder, namedVerdicts{"A": namedChangeOf("Foo")})

	assertStrings(t, "results", compiledNames(outcome.Units), []string{"A", "B"})
	assertReferenceFlags(t, project.writtenResponseFile(t, "B"+responseFileExtension),
		[]string{producedReferenceFlag(project.plan, "A")}, []string{unityReferenceFlag("A")})
	assertChangedReferences(t, resultOf(t, outcome.Units, "B"), nil)
}

// Verifies a dependent another reference touches compiles, and names only that reference.
func TestCompileUnitsNamesOnlyTheReferencesThatTouchedADependent(t *testing.T) {
	project, recorder := newOptimisticCachedProject(t, map[string][]string{"D": {"X", "P"}}, []string{"D"}, "X", "P", "D")
	project.editSource(t, "D", "class D { }")

	outcome := project.compileWithVerdicts(t, recorder, namedVerdicts{
		"X": namedChangeOf("Foo"),
		"P": {Reason: SurfaceChanged, Unnamed: "operator op_Addition"},
	})

	assertStrings(t, "results", compiledNames(outcome.Units), []string{"X", "P", "D"})
	assertChangedReferences(t, resultOf(t, outcome.Units, "D"), []ChangedReference{{Assembly: "P"}})
}

// Verifies a dependent left out by names counts as keeping its surface, so the units below it are
// left out too.
func TestCompileUnitsSkipsTheChainBelowADependentLeftOutByNames(t *testing.T) {
	project, recorder := newOptimisticCachedProject(t,
		map[string][]string{"D": {"X"}, "E": {"D"}}, []string{"D", "E"}, "X", "D", "E")
	project.editSource(t, "D", "class D { }")
	project.editSource(t, "E", "class E { Foo f; }")

	outcome := project.compileWithVerdicts(t, recorder, namedVerdicts{"X": namedChangeOf("Foo")})

	assertStrings(t, "results", compiledNames(outcome.Units), []string{"X"})
	if outcome.Skipped != 2 || recorder.started["E"] {
		t.Errorf("skipped = %d, E started = %v; want D and E left out", outcome.Skipped, recorder.started["E"])
	}
}

// Verifies a changed name only the dependent's saved generator output writes makes it compile.
func TestCompileUnitsCompilesADependentWhoseGeneratedCodeWritesAChangedName(t *testing.T) {
	project, recorder := newNamesProject(t, "class B { }")
	project.writeFile(t, filepath.Join(project.records().GeneratedDirectory("B"), "sub", "Gen.g.cs"), "partial class B { Foo f; }")

	outcome := project.compileWithVerdicts(t, recorder, namedVerdicts{"A": namedChangeOf("Foo")})

	assertStrings(t, "results", compiledNames(outcome.Units), []string{"A", "B"})
	if outcome.NameIndex.WithGenerated != 1 {
		t.Errorf("name index = %+v, want one unit read with its generated code", outcome.NameIndex)
	}
}

// planIndexOf finds a unit's position in the plan.
func planIndexOf(t *testing.T, plan BuildPlan, name string) int {
	t.Helper()
	for index, unit := range plan.Units {
		if unit.Assembly.AssemblyName == name {
			return index
		}
	}
	t.Fatalf("the plan has no unit %s", name)

	return -1
}

// Verifies a unit that started on the assumption that a reference keeps its surface compiles again
// when the reference changed a name only the unit's saved generator output writes, although the unit's
// own compile removes that output as soon as it starts.
func TestCompileUnitsCompilesAgainOnANameOnlyGeneratedCodeWritesWhenTheEarlyCompileRemovesIt(t *testing.T) {
	project, recorder := newOptimisticCachedProject(t,
		map[string][]string{"D1": {"X"}, "D2": {"D1"}}, []string{"D1"}, "X", "D1", "D2")
	project.editSource(t, "D2", "class D2 { int Bar; }")
	project.writeFile(t, filepath.Join(project.records().GeneratedDirectory("D2"), "sub", "Gen.g.cs"), "partial class D2 { Foo f; }")

	outcome := project.compileWithVerdicts(t, recorder, namedVerdicts{
		"X":  {Reason: SurfaceChanged, Unnamed: "operator op_Addition"},
		"D1": namedChangeOf("Foo"),
	})

	if recorder.runs["D2"] != 2 {
		t.Fatalf("D2 compiled %d times, want 2", recorder.runs["D2"])
	}
	d2 := resultOf(t, outcome.Units, "D2")
	if d2.CompiledAgainAfter != "D1" {
		t.Errorf("D2 compiled again after %q, want D1", d2.CompiledAgainAfter)
	}
	assertChangedReferences(t, d2, []ChangedReference{{Assembly: "D1", Name: "Foo"}})
}

// replayedNamesProject is newNamesProject after a first run that compiled A with a kept surface, so
// the next run replays A. B's sources are moved to a file that does not exist, so any read of them
// is counted as unreadable.
func replayedNamesProject(t *testing.T) (*cachedSchedulerProject, *schedulerRecorder) {
	t.Helper()
	project, recorder := newNamesProject(t, "class B { int Bar; }")
	project.compileWithVerdicts(t, recorder, namedVerdicts{"A": {Kept: true, Reason: SurfaceSameAPI}})
	index := planIndexOf(t, project.plan, "B")
	project.plan.Units[index].Sources = []string{"Assets/B/Missing.cs"}

	return project, recorder
}

// Verifies a run that compiles nothing in full and whose every verdict is kept never reads the
// sources of the plan.
func TestCompileUnitsReadsNoSourcesWhenNothingCompilesAndEveryVerdictIsKept(t *testing.T) {
	project, recorder := replayedNamesProject(t)

	outcome := project.compileWithVerdicts(t, recorder, namedVerdicts{"A": {Kept: true, Reason: SurfaceSameAPI}})

	if !resultOf(t, outcome.Units, "A").Reused || outcome.Skipped != 1 {
		t.Fatalf("A reused = %v, skipped = %d; want A replayed and B left out",
			resultOf(t, outcome.Units, "A").Reused, outcome.Skipped)
	}
	if outcome.NameIndex.SourcesRead || outcome.NameIndex.Unreadable != 0 {
		t.Errorf("name index = %+v, want the sources never read", outcome.NameIndex)
	}
}

// Verifies a replayed verdict that names its change reads the sources of the dependents it asks
// about, and a dependent writing none of the names is left out by names.
func TestCompileUnitsReadsTheSourcesWhenAReplayedVerdictNamesAChange(t *testing.T) {
	project, recorder := replayedNamesProject(t)
	project.plan.Units[planIndexOf(t, project.plan, "B")].Sources = []string{"Assets/B/B.cs"}

	outcome := project.compileWithVerdicts(t, recorder, namedVerdicts{"A": namedChangeOf("Foo")})

	if !resultOf(t, outcome.Units, "A").Reused {
		t.Fatal("expected A to be replayed")
	}
	want := []NameSkip{{Assembly: "B", ChangedReferences: []string{"A"}}}
	if !reflect.DeepEqual(outcome.NameSkips, want) {
		t.Errorf("name skips = %+v, want %+v", outcome.NameSkips, want)
	}
	if !outcome.NameIndex.SourcesRead || outcome.NameIndex.Unreadable != 0 {
		t.Errorf("name index = %+v, want the sources read", outcome.NameIndex)
	}
}

// Verifies a run that compiles a unit in full reads the sources while it does, even when no verdict
// needs the names.
func TestCompileUnitsReadsTheSourcesOnceAUnitCompilesInFull(t *testing.T) {
	project, recorder := newNamesProject(t, "class B { int Bar; }")

	outcome := project.compileWithVerdicts(t, recorder, namedVerdicts{"A": {Kept: true, Reason: SurfaceSameAPI}})

	assertStrings(t, "results", compiledNames(outcome.Units), []string{"A"})
	if !outcome.NameIndex.SourcesRead {
		t.Errorf("name index = %+v, want the sources read", outcome.NameIndex)
	}
}

// Verifies a dependent that writes a changed name only with a \u escape compiles for that name.
func TestCompileUnitsCompilesADependentThatWritesTheChangedNameWithAnEscape(t *testing.T) {
	project, recorder := newNamesProject(t, "class B { int x = "+escapeOf("u0046")+"oo(); }")

	outcome := project.compileWithVerdicts(t, recorder, namedVerdicts{"A": namedChangeOf("Foo")})

	assertStrings(t, "results", compiledNames(outcome.Units), []string{"A", "B"})
	assertChangedReferences(t, resultOf(t, outcome.Units, "B"), []ChangedReference{{Assembly: "A", Name: "Foo"}})
}
