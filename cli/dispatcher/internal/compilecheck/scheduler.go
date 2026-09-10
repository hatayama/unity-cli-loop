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
	err    error
}

// schedule is the readiness bookkeeping of one run: how many of a unit's references are still
// missing, and which units are waiting on each one.
type schedule struct {
	pending    []int
	dependents [][]int
	ready      []int
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

// compileUnits compiles a plan's assemblies, starting each one as soon as the assemblies it
// references have been compiled and keeping at most jobs of them running at a time.
// Why the units cannot simply run in the plan's order all at once: a dependent reads the reference
// assembly its reference writes, so starting it early would compile it against the previous run's
// output or against nothing at all.
func compileUnits(
	ctx context.Context, compiler Compiler, plan BuildPlan, jobs int,
) ([]UnitResult, error) {
	if jobs < 1 {
		jobs = 1
	}
	// Why the output directory is created once here rather than per unit: the units run at the same
	// time, and every one of them would otherwise race to create the same directory.
	outputDirectoryPath := filepath.Join(compiler.ProjectRoot, plan.OutputDir)
	if err := os.MkdirAll(outputDirectoryPath, outputDirPermissions); err != nil {
		return nil, fmt.Errorf("failed to create %s: %w", outputDirectoryPath, err)
	}

	runContext, cancel := context.WithCancel(ctx)
	defer cancel()

	state := newSchedule(plan)
	results := make([]UnitResult, len(plan.Units))
	completions := make(chan unitCompletion, len(plan.Units))
	running := 0
	var failure error

	for {
		for failure == nil && running < jobs && len(state.ready) > 0 {
			index := state.ready[0]
			state.ready = state.ready[1:]
			running++
			go func(index int) {
				result, err := compiler.CompileUnit(runContext, plan, plan.Units[index])
				completions <- unitCompletion{index: index, result: result, err: err}
			}(index)
		}
		if running == 0 {
			break
		}

		completion := <-completions
		running--
		// Why the first failure stops the run: it means the compiler itself could not be started,
		// which every remaining unit would hit as well. Compile errors are not failures here - they
		// arrive as diagnostics and the run continues so that one pass reports all of them.
		if completion.err != nil {
			if failure == nil {
				failure = completion.err
				cancel()
			}

			continue
		}
		results[completion.index] = completion.result
		state.release(completion.index)
	}

	if failure != nil {
		return nil, failure
	}

	return results, nil
}

// newSchedule counts what every unit is waiting for and queues the units waiting for nothing.
func newSchedule(plan BuildPlan) *schedule {
	indexByName := make(map[string]int, len(plan.Units))
	for index, unit := range plan.Units {
		indexByName[unit.Assembly.AssemblyName] = index
	}

	state := &schedule{
		pending:    make([]int, len(plan.Units)),
		dependents: make([][]int, len(plan.Units)),
		ready:      []int{},
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

// release hands the units that were waiting on a finished one to the ready queue.
func (state *schedule) release(index int) {
	for _, dependent := range state.dependents[index] {
		state.pending[dependent]--
		if state.pending[dependent] == 0 {
			state.ready = append(state.ready, dependent)
		}
	}
}
