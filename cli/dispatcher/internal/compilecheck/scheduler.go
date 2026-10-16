package compilecheck

import (
	"context"
	"fmt"
	"os"
	"path/filepath"
	"runtime"
)

// unitCompletion is one finished compiler invocation, carrying the position it holds in the plan so
// the results can be reported in the plan's order rather than in the order the units happened to
// finish.
type unitCompletion struct {
	index  int
	result UnitResult
	// keptSurface says whether this unit's reference assembly came out describing the same public
	// surface as the one the last Unity build produced, which decides whether its dependents still
	// have to compile and which of the two reference assemblies they read when they do.
	keptSurface bool
	err         error
}

// schedule is the readiness bookkeeping of one run: how many of a unit's references are still
// missing, and which units are waiting on each one.
// A unit's final state - compiled and kept, compiled and changed, failed, skipped or blocked - is
// decided once, after every unit of the plan it references has a final state of its own. A unit may
// start before that, but what it found counts only from then on.
type schedule struct {
	pending []int
	// pendingOwn counts, per unit, the references in pending that were selected for their own
	// sources rather than through a reference of their own.
	pendingOwn []int
	// ownSources marks, per unit, that it was selected for its own sources.
	ownSources []bool
	// sourceCounts holds, per unit, how many sources it compiles.
	sourceCounts []int
	dependents   [][]int
	// references lists, per unit, the units of the plan it references.
	references [][]int
	ready      []int
	// changedInputs counts, per unit, how many of the references it waited for came out with a
	// different public surface than the last Unity build recorded.
	changedInputs []int
	// blocked marks, per unit, that an assembly it references has errors, so this run must not
	// compile it.
	blocked []bool
	// keptSurface marks, per unit, that it finished with the public surface of the last Unity build.
	keptSurface []bool
	// unaffectedBy lists, per unit, the references that changed their surface only in names the unit
	// never writes, and changedBy the ones whose change counted for it.
	unaffectedBy [][]int
	changedBy    [][]changedReference
	unitProgress
}

// compileOutcome is everything one run of the scheduler produced: the results it has, and what it
// left out.
type compileOutcome struct {
	Units []UnitResult
	// Skipped counts the units left out because everything they reference kept the public surface
	// the last Unity build recorded, so compiling them could only repeat what they already say.
	Skipped int
	// Blocked names the units left out because an assembly they reference has errors, in the plan's
	// order. They are kept apart from Skipped: nothing they compile from was read at all.
	Blocked []string
	// NameSkips names the skipped units a reference did change under, in names they never write.
	NameSkips []NameSkip
	// NameIndex says what reading the units' names took.
	NameIndex NameIndexReport
}

// DefaultJobs is how many assemblies compile at once when the caller names no number.
// Why half the cores: one csc process already uses two to three cores of its own, so filling the
// machine with one process per core makes them fight for the same cores instead of finishing sooner.
func DefaultJobs() int {
	half := runtime.NumCPU() / 2
	if half < 1 {
		return 1
	}

	return half
}

// schedulerRun is the state of one run of the scheduler: what is running, what has finished, and
// what was left out because nothing it compiles against changed.
type schedulerRun struct {
	runContext     context.Context
	compiler       Compiler
	plan           BuildPlan
	state          *schedule
	results        []UnitResult
	compiled       []bool
	completions    chan unitCompletion
	running        int
	referenceSkips int
	names          nameIndex
	nameSkips      []NameSkip
}

// compileUnits compiles a plan's assemblies, keeping at most jobs of them running at a time. It
// reports the results of the units it compiled, in the plan's order, and how many it left out.
// A unit starts once every assembly it references has a final state, or - when it has to compile
// anyway - once every one of them selected for its own sources has, assuming the dependents still
// compiling keep Unity's public surface. A unit whose assumption turns out wrong compiles again.
// Why the units cannot simply run in the plan's order all at once: a dependent reads the reference
// assembly its reference writes when that one changed its surface, so starting it before anything is
// known would mostly compile it against the wrong file.
func compileUnits(
	ctx context.Context, compiler Compiler, plan BuildPlan, jobs int,
) (compileOutcome, error) {
	if jobs < 1 {
		jobs = 1
	}
	// Why the output directory is created once here rather than per unit: the units run at the same
	// time, and every one of them would otherwise race to create the same directory.
	outputDirectoryPath := filepath.Join(compiler.ProjectRoot, plan.OutputDir)
	if err := os.MkdirAll(outputDirectoryPath, outputDirPermissions); err != nil {
		return compileOutcome{}, fmt.Errorf("failed to create %s: %w", outputDirectoryPath, err)
	}

	runContext, cancel := context.WithCancel(ctx)
	defer cancel()

	run := &schedulerRun{
		runContext:  runContext,
		compiler:    compiler,
		plan:        plan,
		state:       newSchedule(plan),
		results:     make([]UnitResult, len(plan.Units)),
		compiled:    make([]bool, len(plan.Units)),
		completions: make(chan unitCompletion, len(plan.Units)),
	}
	run.startNameIndex()
	// Why waited for on every way out: the read runs on files a caller may remove once the run
	// returns.
	defer run.names.wait()
	for index := range plan.Units {
		run.offer(index)
	}

	var failure error
	for {
		if failure == nil {
			if err := run.startAndSettle(jobs); err != nil {
				failure = err
				cancel()
			}
		}
		if run.running == 0 {
			if failure == nil {
				failure = run.unfinishedError()
			}

			break
		}
		if err := run.collectOneCompletion(); err != nil && failure == nil {
			failure = err
			cancel()
		}
	}

	if failure != nil {
		return compileOutcome{}, failure
	}

	return compileOutcome{
		Units:     run.compiledResults(),
		Skipped:   run.referenceSkips,
		Blocked:   run.blockedNames(),
		NameSkips: run.nameSkips,
		NameIndex: run.nameIndexReport(),
	}, nil
}

// startReadyUnits launches the units whose references are all done, up to the job limit, skipping
// the ones that have nothing left to learn from compiling.
func (run *schedulerRun) startReadyUnits(jobs int) error {
	for run.running < jobs && len(run.state.ready) > 0 {
		index := run.state.takeNext()
		// Why the block is read before the skip: a blocked unit has no result to compare against
		// anything, so asking whether its references kept their surface answers nothing.
		blocked, err := run.skipBlockedUnit(index)
		if err != nil {
			return err
		}
		if blocked {
			continue
		}
		// Why the skip is asked only once every reference is final: until then a reference may
		// still change its surface, and a unit queued early was queued because it compiles anyway.
		if run.state.pending[index] == 0 {
			skipped, err := run.skipUnchangedDependent(index)
			if err != nil {
				return err
			}
			if skipped {
				continue
			}
		}
		unit, assumed := run.startingUnit(index)
		run.state.assumed[index] = assumed
		run.state.started[index] = true
		run.running++
		go run.compileOneUnit(index, unit, len(run.state.dependents[index]) > 0)
	}

	return nil
}

// skipBlockedUnit leaves out a unit an assembly with errors is below, and carries the block on down
// to what waits on this one, which is where Unity's own build stops as well: the reference assembly
// this unit compiles against was never produced, so all it could report is the metadata it cannot
// find rather than anything about itself.
// Why its earlier outputs and records are kept: nothing in this run reads them, since every unit
// that references this one is blocked too, and a later run uses them only when the records still
// match: a replayed compile needs its input key, which reads every reference this run rebuilt into
// a different surface by content and the others by timestamp, and outputs that still hash as
// recorded; a changed-files check needs the baseline's compile result on disk and the same
// non-source key.
// Removing them made the first successful run after an error compile every dependent again even
// when the assembly with errors came back with the same reference assembly.
func (run *schedulerRun) skipBlockedUnit(index int) (bool, error) {
	if !run.state.blocked[index] {
		return false, nil
	}
	// Why a unit queued early is only taken out of the queue here: it is decided once every
	// reference it has is, like any other unit, and the last of them to finish offers it again.
	if run.state.pending[index] != 0 {
		return true, nil
	}
	if err := run.markFinal(index); err != nil {
		return false, err
	}
	run.state.blockDependents(index)
	run.releaseAndOffer(index, SurfaceVerdict{Kept: true})

	return true, nil
}

// skipUnchangedDependent leaves out a unit this run picked up only because something it references
// was recompiled, when every one of those came out with the public surface the last Unity build
// recorded, or changed only names the unit never writes: compiling it would read exactly the API it
// was last compiled against, as far as anything it writes can tell. It counts as
// having kept Unity's surface, so the units that started before it finished read Unity's reference
// assembly for it, as they assumed.
// Why its earlier outputs and records are left alone: the units of this run read Unity's reference
// assembly for it because it counts as having kept the surface, a later run trusts them only through
// the input key and the output hashes its record holds, and its baseline and saved generator output are what later
// changed-files checks and name narrowing read. Removing them made the next edit of the unit compile
// it in full, and left the names of its generated code unread.
func (run *schedulerRun) skipUnchangedDependent(index int) (bool, error) {
	unit := run.plan.Units[index]
	if !unit.SelectedAsDependent || run.state.changedInputs[index] != 0 {
		return false, nil
	}
	if err := run.markFinal(index); err != nil {
		return false, err
	}
	run.referenceSkips++
	run.recordNameSkip(index)
	run.state.keptSurface[index] = true
	run.releaseAndOffer(index, SurfaceVerdict{Kept: true})

	return true, nil
}

// startingUnit is the unit at index as it would start now, carrying which of its references it
// reads from Unity's build: the final ones that kept the surface of the last Unity build, and the
// ones without a final state yet, which it assumes will. It also reports the latter.
// Why a copy made here rather than a lookup from the unit's goroutine: the loop writes those answers
// as units finish, while the units it already started are still running. Why on the unit rather than
// passed alongside: the response file, the changed-files check and every key read it from the unit,
// so none of them can end up pointing at a different reference assembly than the compile does.
// Why an assumed reference is named rather than left to the missing-output fallback: an earlier run
// may have left its output on disk, and reading that would compile against a result this run never
// produced.
func (run *schedulerRun) startingUnit(index int) (CompileUnit, []int) {
	unit := run.plan.Units[index]
	kept := map[string]bool{}
	assumed := []int{}
	for _, reference := range run.state.references[index] {
		name := run.plan.Units[reference].Assembly.AssemblyName
		if !run.state.final[reference] {
			assumed = append(assumed, reference)
			kept[name] = true

			continue
		}
		if run.state.keptSurface[reference] {
			kept[name] = true
		}
	}
	unit.KeptSurfaceReferences = kept

	return unit, assumed
}

// compileOneUnit produces one unit's result - by compiling it, or by replaying the previous run's
// answer when nothing it reads has moved - and reports whether its public surface stayed the same.
// Why the comparison happens here rather than in the loop: it reads two files and may ask the
// worker, and the loop has to stay free to start the next unit. Why a replayed unit is compared just
// the same: it kept the outputs of the previous run, so the reference assembly on disk is still the
// one to hold against Unity's.
// Why only a successful unit that something in the plan references is compared: nothing reads the
// answer of any other unit - the dependents of a failed one are blocked whatever it wrote - and
// comparing costs a read of two reference assemblies, and possibly a worker call, for every edit of
// a leaf assembly.
func (run *schedulerRun) compileOneUnit(index int, unit CompileUnit, readByDependents bool) {
	result, err := run.compiler.reuseOrCompile(run.runContext, run.plan, unit)
	if err == nil && readByDependents && result.Succeeded {
		result.Surface = run.compiler.surfaceVerdict(run.runContext, run.plan, unit)
	}
	run.completions <- unitCompletion{index: index, result: result, keptSurface: result.Surface.Kept, err: err}
}

// collectOneCompletion waits for one unit to finish and holds what it produced until the unit's
// final state can be decided.
// Why a failure stops the run: it means the compiler itself could not be started, which every
// remaining unit would hit as well. Compile errors are not failures here - they arrive as
// diagnostics and the run continues so that one pass reports all of them.
// Why nothing is released or blocked here: a unit that started before its references finished may
// still have its result thrown away, and the errors or the surface of a result nobody keeps must not
// decide anything for the units below it.
func (run *schedulerRun) collectOneCompletion() error {
	completion := <-run.completions
	run.running--
	if completion.err != nil {
		return completion.err
	}
	run.results[completion.index] = completion.result
	run.state.pendingKept[completion.index] = completion.keptSurface
	run.state.finished[completion.index] = true

	return nil
}

// compiledResults reports the units this run actually compiled, in the plan's order.
func (run *schedulerRun) compiledResults() []UnitResult {
	results := make([]UnitResult, 0, len(run.results))
	for index, compiled := range run.compiled {
		if compiled {
			results = append(results, run.results[index])
		}
	}

	return results
}

// blockedNames lists the assemblies this run did not compile because one they reference has errors,
// in the plan's order.
func (run *schedulerRun) blockedNames() []string {
	names := make([]string, 0, len(run.plan.Units))
	for index, blocked := range run.state.blocked {
		if blocked {
			names = append(names, run.plan.Units[index].Assembly.AssemblyName)
		}
	}

	return names
}

// newSchedule counts what every unit is waiting for. The caller offers every unit once, which queues
// the ones waiting for nothing.
func newSchedule(plan BuildPlan) *schedule {
	indexByName := make(map[string]int, len(plan.Units))
	for index, unit := range plan.Units {
		indexByName[unit.Assembly.AssemblyName] = index
	}

	state := &schedule{
		pending:       make([]int, len(plan.Units)),
		pendingOwn:    make([]int, len(plan.Units)),
		ownSources:    make([]bool, len(plan.Units)),
		sourceCounts:  make([]int, len(plan.Units)),
		dependents:    make([][]int, len(plan.Units)),
		references:    make([][]int, len(plan.Units)),
		ready:         []int{},
		changedInputs: make([]int, len(plan.Units)),
		blocked:       make([]bool, len(plan.Units)),
		keptSurface:   make([]bool, len(plan.Units)),
		unaffectedBy:  make([][]int, len(plan.Units)),
		changedBy:     make([][]changedReference, len(plan.Units)),
		unitProgress:  newUnitProgress(len(plan.Units)),
	}
	for index, unit := range plan.Units {
		state.ownSources[index] = !unit.SelectedAsDependent
		state.sourceCounts[index] = len(unit.Sources)
	}
	for index, unit := range plan.Units {
		for _, reference := range unit.PlanReferences {
			referenceIndex, compiled := indexByName[reference]
			if !compiled {
				continue
			}
			state.pending[index]++
			if state.ownSources[referenceIndex] {
				state.pendingOwn[index]++
			}
			state.dependents[referenceIndex] = append(state.dependents[referenceIndex], index)
			state.references[index] = append(state.references[index], referenceIndex)
		}
	}

	return state
}

// blockDependents marks every unit waiting on this one as one this run must not compile.
// Why marking rather than removing from the queue: a queued unit is read as it comes up, and one that
// already started is read when its final state is decided.
func (state *schedule) blockDependents(index int) {
	for _, dependent := range state.dependents[index] {
		state.blocked[dependent] = true
	}
}

// release tells the units waiting on one that just got its final state that it has, recording for
// each whether what it produced touches it, and reports those units so the caller can decide which
// of them to queue. effects holds one entry per unit waiting on it, in the order of dependents.
// Why a unit that assumed this one is marked stale only when touched: it read Unity's reference
// assembly for it, which is exactly right when the surface was kept, and compiles the same when the
// change touches no name it writes. A failed or blocked one blocks it through blockDependents instead.
func (state *schedule) release(index int, effects []referenceEffect) []int {
	for _, effect := range effects {
		dependent := effect.dependent
		state.pending[dependent]--
		if state.ownSources[index] {
			state.pendingOwn[dependent]--
		}
		if !effect.touches {
			if effect.named {
				state.unaffectedBy[dependent] = append(state.unaffectedBy[dependent], index)
			}

			continue
		}
		state.changedInputs[dependent]++
		state.changedBy[dependent] = append(state.changedBy[dependent],
			changedReference{reference: index, name: effect.name, reason: effect.reason})
		if !state.stale[dependent] && containsIndex(state.assumed[dependent], index) {
			state.stale[dependent] = true
			state.staleCause[dependent] = index
		}
	}

	return state.dependents[index]
}
