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
	// changed says whether this unit's reference assembly came out different from the one the last
	// Unity build produced, which is what decides whether its dependents still have to compile.
	changed bool
	err     error
}

// schedule is the readiness bookkeeping of one run: how many of a unit's references are still
// missing, and which units are waiting on each one.
type schedule struct {
	pending    []int
	dependents [][]int
	ready      []int
	// changedInputs counts, per unit, how many of the references it waited for came out with a
	// different public surface than the last Unity build recorded.
	changedInputs []int
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
	runContext          context.Context
	compiler            Compiler
	plan                BuildPlan
	outputDirectoryPath string
	state               *schedule
	results             []UnitResult
	compiled            []bool
	completions         chan unitCompletion
	running             int
	referenceSkips      int
}

// compileUnits compiles a plan's assemblies, starting each one as soon as the assemblies it
// references have been compiled and keeping at most jobs of them running at a time. It reports the
// results of the units it compiled, in the plan's order, and how many it left out.
// Why the units cannot simply run in the plan's order all at once: a dependent reads the reference
// assembly its reference writes, so starting it early would compile it against the previous run's
// output or against nothing at all.
func compileUnits(
	ctx context.Context, compiler Compiler, plan BuildPlan, jobs int,
) ([]UnitResult, int, error) {
	if jobs < 1 {
		jobs = 1
	}
	// Why the output directory is created once here rather than per unit: the units run at the same
	// time, and every one of them would otherwise race to create the same directory.
	outputDirectoryPath := filepath.Join(compiler.ProjectRoot, plan.OutputDir)
	if err := os.MkdirAll(outputDirectoryPath, outputDirPermissions); err != nil {
		return nil, 0, fmt.Errorf("failed to create %s: %w", outputDirectoryPath, err)
	}

	runContext, cancel := context.WithCancel(ctx)
	defer cancel()

	run := &schedulerRun{
		runContext:          runContext,
		compiler:            compiler,
		plan:                plan,
		outputDirectoryPath: outputDirectoryPath,
		state:               newSchedule(plan),
		results:             make([]UnitResult, len(plan.Units)),
		compiled:            make([]bool, len(plan.Units)),
		completions:         make(chan unitCompletion, len(plan.Units)),
	}

	var failure error
	for {
		if failure == nil {
			if err := run.startReadyUnits(jobs); err != nil {
				failure = err
				cancel()
			}
		}
		if run.running == 0 {
			break
		}
		if err := run.collectOneCompletion(); err != nil && failure == nil {
			failure = err
			cancel()
		}
	}

	if failure != nil {
		return nil, 0, failure
	}

	return run.compiledResults(), run.referenceSkips, nil
}

// startReadyUnits launches the units whose references are all done, up to the job limit, skipping
// the ones that have nothing left to learn from compiling.
func (run *schedulerRun) startReadyUnits(jobs int) error {
	for run.running < jobs && len(run.state.ready) > 0 {
		index := run.state.ready[0]
		run.state.ready = run.state.ready[1:]
		skipped, err := run.skipUnchangedDependent(index)
		if err != nil {
			return err
		}
		if skipped {
			continue
		}
		run.running++
		go run.compileOneUnit(index)
	}

	return nil
}

// skipUnchangedDependent leaves out a unit this run picked up only because something it references
// was recompiled, when every one of those came out with the public surface the last Unity build
// recorded: compiling it would read exactly the API it was last compiled against.
// Why its earlier outputs are removed rather than left alone: a reference assembly an earlier run
// wrote would otherwise be picked up as this run's own output, and the units below it would be
// compiled against a result this run never produced.
func (run *schedulerRun) skipUnchangedDependent(index int) (bool, error) {
	unit := run.plan.Units[index]
	if !unit.SelectedAsDependent || run.state.changedInputs[index] != 0 {
		return false, nil
	}
	if err := removeUnitOutputs(run.outputDirectoryPath, unit); err != nil {
		return false, err
	}
	run.referenceSkips++
	run.state.release(index, false)

	return true, nil
}

// compileOneUnit compiles one unit and reports what it produced, including whether its public
// surface moved. Why the comparison happens here rather than in the loop: it reads two files, and
// the loop has to stay free to start the next unit.
func (run *schedulerRun) compileOneUnit(index int) {
	unit := run.plan.Units[index]
	result, err := run.compiler.CompileUnit(run.runContext, run.plan, unit)
	changed := true
	if err == nil {
		changed = !referenceSurfaceUnchanged(run.compiler.ProjectRoot, run.plan, unit)
	}
	run.completions <- unitCompletion{index: index, result: result, changed: changed, err: err}
}

// collectOneCompletion waits for one unit to finish and hands what it produced to the schedule.
// Why a failure stops the run: it means the compiler itself could not be started, which every
// remaining unit would hit as well. Compile errors are not failures here - they arrive as
// diagnostics and the run continues so that one pass reports all of them.
func (run *schedulerRun) collectOneCompletion() error {
	completion := <-run.completions
	run.running--
	if completion.err != nil {
		return completion.err
	}
	run.results[completion.index] = completion.result
	run.compiled[completion.index] = true
	run.state.release(completion.index, completion.changed)

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

// newSchedule counts what every unit is waiting for and queues the units waiting for nothing.
func newSchedule(plan BuildPlan) *schedule {
	indexByName := make(map[string]int, len(plan.Units))
	for index, unit := range plan.Units {
		indexByName[unit.Assembly.AssemblyName] = index
	}

	state := &schedule{
		pending:       make([]int, len(plan.Units)),
		dependents:    make([][]int, len(plan.Units)),
		ready:         []int{},
		changedInputs: make([]int, len(plan.Units)),
	}
	for index, unit := range plan.Units {
		for _, reference := range unit.PlanReferences {
			referenceIndex, compiled := indexByName[reference]
			if !compiled {
				continue
			}
			state.pending[index]++
			state.dependents[referenceIndex] = append(state.dependents[referenceIndex], index)
		}
	}
	for index := range plan.Units {
		if state.pending[index] == 0 {
			state.ready = append(state.ready, index)
		}
	}

	return state
}

// release hands the units that were waiting on a finished one to the ready queue, recording whether
// what it produced still describes the same public surface as the last Unity build.
func (state *schedule) release(index int, changed bool) {
	for _, dependent := range state.dependents[index] {
		state.pending[dependent]--
		if changed {
			state.changedInputs[dependent]++
		}
		if state.pending[dependent] == 0 {
			state.ready = append(state.ready, dependent)
		}
	}
}
