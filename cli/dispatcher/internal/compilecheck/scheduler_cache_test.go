package compilecheck

import (
	"context"
	"os"
	"path/filepath"
	"reflect"
	"strings"
	"testing"
	"time"

	"github.com/hatayama/unity-cli-loop/dispatcher/internal/compilecheck/resultstore"
)

// cachedSchedulerProject is a project laid out on disk in enough detail for the input key to read
// it: real response files, real sources, real reference assemblies and a compiler to stat.
// Why the existing newSchedulerPlan is not enough: it names assemblies and nothing else, and a key
// computed over a unit with no response file, no sources and no references says nothing about
// whether the inputs of a compile moved.
type cachedSchedulerProject struct {
	root  string
	paths EditorCompilerPaths
	plan  BuildPlan
}

// newCachedSchedulerProject lays out a project of named units, each referencing the units named for
// it, and builds the plan that compiles all of them.
func newCachedSchedulerProject(
	t *testing.T, references map[string][]string, names ...string,
) *cachedSchedulerProject {
	t.Helper()
	project := &cachedSchedulerProject{root: t.TempDir()}
	project.paths = EditorCompilerPaths{
		DotnetHostPath:  project.writeFile(t, filepath.Join("Editor", "dotnet"), "host"),
		CompilerDllPath: project.writeFile(t, filepath.Join("Editor", "csc.dll"), "compiler"),
	}

	units := make([]CompileUnit, 0, len(names))
	for _, name := range names {
		units = append(units, project.newUnit(t, name, references[name]))
	}
	project.plan = BuildPlan{
		DagDir:    planDagDirectory,
		OutputDir: filepath.Join("Library", "uloop", "compile-check", "aaaa.dag"),
		Units:     units,
	}

	return project
}

// newUnit writes what one assembly compiles from and describes it the way a parsed response file does.
func (project *cachedSchedulerProject) newUnit(
	t *testing.T, name string, references []string,
) CompileUnit {
	t.Helper()
	source := filepath.ToSlash(filepath.Join("Assets", name, name+".cs"))
	project.writeFile(t, source, "class "+name+" { }")
	writeUnityReferenceAssembly(t, project.root, name, "unity "+name)

	lines := []string{
		quoteFlag(outputFlagPrefix, filepath.Join(planDagDirectory, name+assemblyExtension)),
		`"` + source + `"`,
	}
	referencePaths := make([]string, 0, len(references))
	for _, reference := range references {
		path := filepath.Join(planDagDirectory, reference+referenceAssemblyExtension)
		referencePaths = append(referencePaths, path)
		lines = append(lines, quoteFlag(referenceFlagPrefix, path))
	}
	responseFilePath := project.writeFile(
		t, filepath.Join(planDagDirectory, name+responseFileExtension), strings.Join(lines, "\n"))

	return CompileUnit{
		Assembly: ResponseFile{
			AssemblyName:  name,
			Path:          responseFilePath,
			OutputPath:    filepath.Join(planDagDirectory, name+assemblyExtension),
			RefOutputPath: filepath.Join(planDagDirectory, name+referenceAssemblyExtension),
			References:    referencePaths,
		},
		Sources:        []string{source},
		PlanReferences: references,
	}
}

// writeFile writes one file at a project-relative path and reports where it landed.
func (project *cachedSchedulerProject) writeFile(
	t *testing.T, relativePath string, content string,
) string {
	t.Helper()
	path := filepath.Join(project.root, relativePath)
	if err := os.MkdirAll(filepath.Dir(path), outputDirPermissions); err != nil {
		t.Fatalf("failed to create the directory of %s: %v", relativePath, err)
	}
	if err := os.WriteFile(path, []byte(content), 0o600); err != nil {
		t.Fatalf("failed to write %s: %v", relativePath, err)
	}

	return path
}

// editSource rewrites what one assembly compiles, which is what moves its input key.
func (project *cachedSchedulerProject) editSource(t *testing.T, name string, content string) {
	t.Helper()
	project.writeFile(t, filepath.Join("Assets", name, name+".cs"), content)
}

// outputDirectory names where this check writes, which is where the manifests live.
func (project *cachedSchedulerProject) outputDirectory() string {
	return filepath.Join(project.root, project.plan.OutputDir)
}

// writeOutputFile writes one file into the check's own output directory.
func writeOutputFile(t *testing.T, outputDirectoryPath string, name string, content string) {
	t.Helper()
	if err := os.WriteFile(
		filepath.Join(outputDirectoryPath, name), []byte(content), 0o600); err != nil {
		t.Fatalf("failed to write %s: %v", name, err)
	}
}

// records opens the store the runs of this test project write to.
func (project *cachedSchedulerProject) records() resultstore.Store[Diagnostic] {
	return resultstore.Open[Diagnostic](project.root, project.plan.OutputDir)
}

// hasBaselineOf reports whether a baseline of the current format is on disk for the assembly.
// Why the "no baseline:" prefix tells: the store gives it only when the baseline file is missing or
// was written by another version; a baseline file that disagrees with its compile result is
// reported as "baseline compile is no longer on disk" instead.
func (project *cachedSchedulerProject) hasBaselineOf(name string) bool {
	_, reason := project.records().LoadBaseline(name)

	return !strings.HasPrefix(reason, "no baseline:")
}

// hasRecordedCompile reports whether a compile result of the assembly is on record.
// Why the baseline reason tells: these assemblies have fewer sources than the changed-files check
// needs, so they never get a baseline, and the store explains the missing baseline from the
// compile result alone - this reason is the one it gives when there is no compile result.
func (project *cachedSchedulerProject) hasRecordedCompile(name string) bool {
	_, reason := project.records().LoadBaseline(name)

	return reason != "no baseline: no full compile of this assembly is on record"
}

// compile runs the whole plan through the scheduler with a fake compiler.
func (project *cachedSchedulerProject) compile(
	ctx context.Context, recorder *schedulerRecorder,
) (compileOutcome, error) {
	compiler := Compiler{
		Paths:       project.paths,
		ProjectRoot: project.root,
		Timeout:     time.Minute,
		Run:         recorder.runner(),
	}

	return compileUnits(ctx, compiler, project.plan, 4)
}

// compileOrFail runs the whole plan and fails the test when the run could not finish.
func (project *cachedSchedulerProject) compileOrFail(
	t *testing.T, recorder *schedulerRecorder,
) []UnitResult {
	t.Helper()
	outcome, err := project.compile(context.Background(), recorder)
	if err != nil {
		t.Fatalf("expected the run to succeed, got error: %v", err)
	}

	return outcome.Units
}

// newCachedSchedulerRecorder prepares a fake compiler that writes both outputs of every named unit,
// which is what a compile that ran to completion leaves behind.
func newCachedSchedulerRecorder(names ...string) *schedulerRecorder {
	recorder := newSchedulerRecorder(0)
	for _, name := range names {
		recorder.surfaces[name] = "surface " + name
		recorder.assemblies[name] = "assembly " + name
	}

	return recorder
}

// resultOf finds one assembly's result in a run's results.
func resultOf(t *testing.T, results []UnitResult, name string) UnitResult {
	t.Helper()
	for _, result := range results {
		if result.Assembly == name {
			return result
		}
	}
	t.Fatalf("the run reported no result for %s", name)

	return UnitResult{}
}

// Verifies a second run over untouched inputs starts no compiler at all and reports what the first
// run found, marked as reused.
func TestCompileUnitsReusesEveryUnitWhoseInputsDidNotMove(t *testing.T) {
	project := newCachedSchedulerProject(t, nil, "Alpha", "Beta")
	first := project.compileOrFail(t, newCachedSchedulerRecorder("Alpha", "Beta"))

	recorder := newCachedSchedulerRecorder("Alpha", "Beta")
	second := project.compileOrFail(t, recorder)

	if len(recorder.runs) != 0 {
		t.Errorf("the second run should have started no compiler, got %v", recorder.runs)
	}
	assertStrings(t, "results", compiledNames(second), []string{"Alpha", "Beta"})
	for _, name := range []string{"Alpha", "Beta"} {
		reused := resultOf(t, second, name)
		if !reused.Reused {
			t.Errorf("%s should have been reported as reused", name)
		}
		recorded := resultOf(t, first, name)
		if reused.Succeeded != recorded.Succeeded {
			t.Errorf("%s succeeded = %v, want %v", name, reused.Succeeded, recorded.Succeeded)
		}
		if !reflect.DeepEqual(reused.Diagnostics, recorded.Diagnostics) {
			t.Errorf("%s diagnostics = %+v, want %+v", name, reused.Diagnostics, recorded.Diagnostics)
		}
	}
}

// Verifies a unit whose response file names no reference assembly is recorded by its assembly alone
// and reused on the next run, rather than being taken for a successful compile that lost an output.
func TestCompileUnitsReusesAUnitWithoutAReferenceAssembly(t *testing.T) {
	project := newCachedSchedulerProject(t, nil, "Alpha")
	project.plan.Units[0].Assembly.RefOutputPath = ""
	first := newCachedSchedulerRecorder("Alpha")
	delete(first.surfaces, "Alpha")
	project.compileOrFail(t, first)
	if _, err := os.Stat(filepath.Join(project.outputDirectory(), "Alpha"+referenceAssemblyExtension)); !os.IsNotExist(err) {
		t.Fatalf("expected the compile to write no reference assembly, got %v", err)
	}

	recorder := newCachedSchedulerRecorder("Alpha")
	delete(recorder.surfaces, "Alpha")
	second := project.compileOrFail(t, recorder)

	if !resultOf(t, second, "Alpha").Reused || len(recorder.runs) != 0 {
		t.Errorf("expected Alpha to be reused without a compile, got %+v after %v", resultOf(t, second, "Alpha"), recorder.runs)
	}
}

// Verifies the case the cache exists for: editing inside one assembly recompiles it, and the
// assembly below it is reused because the reference assembly it reads came out byte-identical -
// even though that file was rewritten and now carries a newer timestamp.
func TestCompileUnitsReusesADependentWhoseReferenceWasRewrittenWithTheSameBytes(t *testing.T) {
	project := newCachedSchedulerProject(t, map[string][]string{"Beta": {"Alpha"}}, "Alpha", "Beta")
	project.compileOrFail(t, newCachedSchedulerRecorder("Alpha", "Beta"))

	project.editSource(t, "Alpha", "class Alpha { void Body() { } }")
	recorder := newCachedSchedulerRecorder("Alpha", "Beta")
	results := project.compileOrFail(t, recorder)

	if recorder.runs["Alpha"] != 1 {
		t.Errorf("the edited assembly should have compiled once, got %d", recorder.runs["Alpha"])
	}
	if recorder.runs["Beta"] != 0 {
		t.Errorf("the assembly below it should not have compiled, got %d", recorder.runs["Beta"])
	}
	if resultOf(t, results, "Alpha").Reused {
		t.Error("the edited assembly should not have been reported as reused")
	}
	if !resultOf(t, results, "Beta").Reused {
		t.Error("the assembly below it should have been reported as reused")
	}
}

// Verifies a reused result keeps the errors the compile reported: an assembly that failed goes on
// failing until one of its inputs moves, rather than coming back as a clean compile.
func TestCompileUnitsReplaysTheErrorsOfAFailedUnit(t *testing.T) {
	project := newCachedSchedulerProject(t, nil, "Alpha")
	failing := newCachedSchedulerRecorder("Alpha")
	failing.output["Alpha"] = fakeCompilerOutput{
		stdout: "Assets/Alpha/Alpha.cs(1,7): error CS0103: The name 'Missing' does not exist " +
			"in the current context",
		exitCode: 1,
	}
	project.compileOrFail(t, failing)

	recorder := newCachedSchedulerRecorder("Alpha")
	results := project.compileOrFail(t, recorder)

	replayed := resultOf(t, results, "Alpha")
	if !replayed.Reused {
		t.Fatal("the recorded failure should have been reused")
	}
	if replayed.Succeeded {
		t.Error("a failed compile must not be replayed as a successful one")
	}
	if len(replayed.Diagnostics) != 1 || replayed.Diagnostics[0].Code != "CS0103" {
		t.Errorf("diagnostics = %+v, want the recorded CS0103", replayed.Diagnostics)
	}
}

// Verifies a replayed failure blocks just as a fresh one does: the second run starts no compiler at
// all, and the assembly below the failed one is still left out. A run that decided this from the
// invocation it had just made would compile the whole tree below a failure as soon as the cache
// answered for it.
func TestCompileUnitsBlocksTheDependentsOfAReplayedFailure(t *testing.T) {
	project := newCachedSchedulerProject(t, map[string][]string{"Beta": {"Alpha"}}, "Alpha", "Beta")
	failing := newCachedSchedulerRecorder("Alpha", "Beta")
	failing.output["Alpha"] = failingCompilerOutput("Alpha")
	project.compileOrFail(t, failing)

	recorder := newCachedSchedulerRecorder("Alpha", "Beta")
	recorder.output["Alpha"] = failingCompilerOutput("Alpha")
	outcome, err := project.compile(context.Background(), recorder)
	if err != nil {
		t.Fatalf("expected the run to succeed, got error: %v", err)
	}

	if len(recorder.runs) != 0 {
		t.Errorf("the second run should have started no compiler, got %v", recorder.runs)
	}
	if !resultOf(t, outcome.Units, "Alpha").Reused {
		t.Error("the recorded failure should have been reused")
	}
	assertStrings(t, "results", compiledNames(outcome.Units), []string{"Alpha"})
	assertStrings(t, "blocked", outcome.Blocked, []string{"Beta"})
}

// Verifies the order the manifest is written in: while a unit is compiling, the manifest of the
// compile it replaces is already gone and the new one is not there yet. A run that wrote the
// manifest before starting the compiler would leave a record of a compile that never finished.
func TestCompileUnitsHasNoManifestWhileAUnitIsCompiling(t *testing.T) {
	project := newCachedSchedulerProject(t, nil, "Alpha")
	project.compileOrFail(t, newCachedSchedulerRecorder("Alpha"))
	if !project.hasRecordedCompile("Alpha") {
		t.Fatal("the first run should have recorded a result")
	}

	project.editSource(t, "Alpha", "class Alpha { void Body() { } }")
	recorder := newCachedSchedulerRecorder("Alpha")
	present := map[string]bool{}
	recorder.during = func(name string) { present[name] = project.hasRecordedCompile(name) }
	project.compileOrFail(t, recorder)

	if present["Alpha"] {
		t.Error("the manifest of the previous compile should be gone while the unit compiles")
	}
	if !project.hasRecordedCompile("Alpha") {
		t.Error("the finished compile should have been recorded")
	}
}

// Verifies a unit whose compiler could not be started records nothing, so the next run compiles it
// again rather than replaying a result no compile produced.
func TestCompileUnitsRecordsNothingForAUnitThatCouldNotBeStarted(t *testing.T) {
	project := newCachedSchedulerProject(t, nil, "Alpha")
	failing := newCachedSchedulerRecorder("Alpha")
	failing.failingUnit = "Alpha"
	if _, err := project.compile(context.Background(), failing); err == nil {
		t.Fatal("expected the failure to start the compiler to be reported")
	}
	if project.hasRecordedCompile("Alpha") {
		t.Error("a unit whose compiler never ran should have no recorded result")
	}

	recorder := newCachedSchedulerRecorder("Alpha")
	project.compileOrFail(t, recorder)

	if recorder.runs["Alpha"] != 1 {
		t.Errorf("the next run should have compiled the unit, got %d runs", recorder.runs["Alpha"])
	}
}

// Verifies a run cut short records nothing for the unit it interrupted.
func TestCompileUnitsRecordsNothingForACancelledUnit(t *testing.T) {
	project := newCachedSchedulerProject(t, nil, "Alpha")
	cancelled := newCachedSchedulerRecorder("Alpha")
	runContext, cancel := context.WithCancel(context.Background())
	cancelled.during = func(string) { cancel() }
	if _, err := project.compile(runContext, cancelled); err == nil {
		cancel()
		t.Fatal("expected the cancelled run to be reported as a failure")
	}
	cancel()
	if project.hasRecordedCompile("Alpha") {
		t.Error("a cancelled compile should have no recorded result")
	}

	recorder := newCachedSchedulerRecorder("Alpha")
	project.compileOrFail(t, recorder)

	if recorder.runs["Alpha"] != 1 {
		t.Errorf("the next run should have compiled the unit, got %d runs", recorder.runs["Alpha"])
	}
}

// Verifies --all compiles everything even when nothing moved, and still records what it compiled so
// the next ordinary run can reuse it.
func TestCompileUnitsCompilesEverythingForAllAndStillRecordsIt(t *testing.T) {
	project := newCachedSchedulerProject(t, nil, "Alpha", "Beta")
	project.compileOrFail(t, newCachedSchedulerRecorder("Alpha", "Beta"))

	project.plan.All = true
	all := newCachedSchedulerRecorder("Alpha", "Beta")
	project.compileOrFail(t, all)
	if len(all.runs) != 2 {
		t.Errorf("--all should have compiled both assemblies, got %v", all.runs)
	}

	project.plan.All = false
	recorder := newCachedSchedulerRecorder("Alpha", "Beta")
	project.compileOrFail(t, recorder)

	if len(recorder.runs) != 0 {
		t.Errorf("the run after --all should have reused everything, got %v", recorder.runs)
	}
}

// Verifies a compile that exited non-zero without naming a diagnostic records nothing: what went
// wrong is unknown, and replaying it would report a clean compile for an assembly that never built.
func TestCompileUnitsRecordsNothingWhenTheCompilerFailedWithoutADiagnostic(t *testing.T) {
	project := newCachedSchedulerProject(t, nil, "Alpha")
	broken := newCachedSchedulerRecorder("Alpha")
	broken.output["Alpha"] = fakeCompilerOutput{stdout: "csc : fatal error", exitCode: 1}
	project.compileOrFail(t, broken)

	if project.hasRecordedCompile("Alpha") {
		t.Error("a compile that failed without a diagnostic should have no recorded result")
	}

	recorder := newCachedSchedulerRecorder("Alpha")
	project.compileOrFail(t, recorder)

	if recorder.runs["Alpha"] != 1 {
		t.Errorf("the next run should have compiled the unit, got %d runs", recorder.runs["Alpha"])
	}
}

// Verifies that either output - the assembly or the reference assembly - no longer holding what the
// compile wrote sends the unit back through the compiler: the assemblies below it read those bytes.
func TestCompileUnitsCompilesAgainWhenAnOutputWasChanged(t *testing.T) {
	for name, output := range map[string]string{
		"assembly":           "Alpha" + assemblyExtension,
		"reference assembly": "Alpha" + referenceAssemblyExtension,
	} {
		t.Run(name, func(t *testing.T) {
			project := newCachedSchedulerProject(t, nil, "Alpha")
			project.compileOrFail(t, newCachedSchedulerRecorder("Alpha"))

			outputPath := filepath.Join(project.outputDirectory(), output)
			if _, err := os.Stat(outputPath); err != nil {
				t.Fatalf("expected the compile to write %s: %v", output, err)
			}
			if err := os.WriteFile(outputPath, []byte("something else"), 0o600); err != nil {
				t.Fatalf("failed to change the output: %v", err)
			}
			recorder := newCachedSchedulerRecorder("Alpha")
			project.compileOrFail(t, recorder)

			if recorder.runs["Alpha"] != 1 {
				t.Errorf("a unit whose %s changed should have compiled, got %d runs", name, recorder.runs["Alpha"])
			}
		})
	}
}

// Verifies the skip of a dependent still wins over the reuse and keeps the recorded result of its
// earlier compile, so a later run can still reuse or check against it.
func TestCompileUnitsKeepsTheRecordOfASkippedDependent(t *testing.T) {
	project := newCachedSchedulerProject(t, map[string][]string{"Beta": {"Alpha"}}, "Alpha", "Beta")
	project.compileOrFail(t, newCachedSchedulerRecorder("Alpha", "Beta"))
	if !project.hasRecordedCompile("Beta") {
		t.Fatal("the first run should have recorded a result for the dependent")
	}

	markSelectedAsDependent(t, project.plan, "Beta")
	project.editSource(t, "Alpha", "class Alpha { void Body() { } }")
	recorder := newCachedSchedulerRecorder("Alpha", "Beta")
	// Why the surface now matches Unity's: that is what makes the dependent skippable at all.
	recorder.surfaces["Alpha"] = "unity Alpha"
	outcome, err := project.compile(context.Background(), recorder)
	if err != nil {
		t.Fatalf("expected the run to succeed, got error: %v", err)
	}

	assertStrings(t, "results", compiledNames(outcome.Units), []string{"Alpha"})
	if outcome.Skipped != 1 {
		t.Errorf("reference skips = %d, want 1", outcome.Skipped)
	}
	if recorder.runs["Beta"] != 0 {
		t.Errorf("the skipped dependent should not have compiled, got %d runs", recorder.runs["Beta"])
	}
	if !project.hasRecordedCompile("Beta") {
		t.Error("the skipped dependent's recorded result should have been kept")
	}
}

// Verifies a compile killed partway through records nothing, even though the diagnostics it had
// already printed parse cleanly and look exactly like a finished compile's.
func TestCompileUnitsRecordsNothingForAUnitKilledWhileItWasReportingDiagnostics(t *testing.T) {
	project := newCachedSchedulerProject(t, nil, "Alpha")
	killed := newCachedSchedulerRecorder("Alpha")
	killed.killedUnits["Alpha"] = true
	killed.output["Alpha"] = fakeCompilerOutput{
		stdout:   "Assets/Alpha/Alpha.cs(1,1): error CS0001: interrupted",
		exitCode: -1,
	}
	runContext, cancel := context.WithCancel(context.Background())
	killed.during = func(string) { cancel() }

	if _, err := project.compile(runContext, killed); err == nil {
		cancel()
		t.Fatal("a killed compile should fail the run rather than report partial diagnostics")
	}
	cancel()

	if project.hasRecordedCompile("Alpha") {
		t.Fatal("a killed compile must leave no recorded result to replay")
	}

	recorder := newCachedSchedulerRecorder("Alpha")
	project.compileOrFail(t, recorder)
	if recorder.runs["Alpha"] != 1 {
		t.Errorf("the next run should have compiled the unit, got %d runs", recorder.runs["Alpha"])
	}
}

// Verifies a compile that reported success without leaving the outputs it was told to produce
// records nothing: the manifest describes those files, and one that names none of them would
// replay a success no compile ever backed.
func TestCompileUnitsRecordsNothingWhenASuccessfulCompileWroteNoOutput(t *testing.T) {
	project := newCachedSchedulerProject(t, nil, "Alpha")
	silent := newCachedSchedulerRecorder("Alpha")
	delete(silent.assemblies, "Alpha")
	delete(silent.surfaces, "Alpha")

	project.compileOrFail(t, silent)

	if project.hasRecordedCompile("Alpha") {
		t.Fatal("a compile that produced no output must leave no recorded result")
	}

	recorder := newCachedSchedulerRecorder("Alpha")
	project.compileOrFail(t, recorder)
	if recorder.runs["Alpha"] != 1 {
		t.Errorf("the next run should have compiled the unit, got %d runs", recorder.runs["Alpha"])
	}
}

// Verifies a compile of sources edited while csc ran is not recorded under the key of the bytes
// before the edit, so putting those bytes back compiles them again rather than reusing what the
// edited file reported.
func TestCompileUnitsDoesNotRecordACompileOfSourcesEditedDuringIt(t *testing.T) {
	project := newCachedSchedulerProject(t, nil, "Alpha")
	project.compileOrFail(t, newCachedSchedulerRecorder("Alpha"))
	project.editSource(t, "Alpha", "class Alpha { void X() { } }")
	edited := newCachedSchedulerRecorder("Alpha")
	var editErr error
	edited.during = func(name string) {
		if name == "Alpha" {
			editErr = os.WriteFile(filepath.Join(project.root, "Assets", "Alpha", "Alpha.cs"),
				[]byte("class Alpha { void Y() { } }"), 0o600)
		}
	}
	project.compileOrFail(t, edited)
	if editErr != nil {
		t.Fatalf("failed to edit the source during the compile: %v", editErr)
	}
	if edited.runs["Alpha"] != 1 {
		t.Fatalf("expected the edited unit to compile, got %d runs", edited.runs["Alpha"])
	}
	project.editSource(t, "Alpha", "class Alpha { void X() { } }")

	recorder := newCachedSchedulerRecorder("Alpha")
	project.compileOrFail(t, recorder)

	if recorder.runs["Alpha"] != 1 {
		t.Errorf("expected the reverted source to compile again, got %d runs", recorder.runs["Alpha"])
	}
}
