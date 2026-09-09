package compilecheck

import (
	"fmt"
	"path/filepath"
	"sort"
	"strings"
)

const (
	movedFrameworkResponseSuffix = ".mvfrm.rsp"
	compileCheckOutputRoot       = "Library/uloop/compile-check"
	dependencyReasonPrefix       = "depends on "
)

// CompileUnit is one assembly the offline compile has to rebuild, and why.
type CompileUnit struct {
	Assembly ResponseFile
	Sources  []string
	Reason   string // empty when the unit was selected by --all rather than by a change
}

// BuildPlan is the ordered set of assemblies to compile for one run.
type BuildPlan struct {
	DagDir    string        // relative to the project root
	OutputDir string        // relative to the project root
	Units     []CompileUnit // dependency order: an assembly comes after everything it references
	Skipped   int           // assemblies left out because nothing they compile from changed
}

// assemblyGraph holds every response file in the dag together with the edges between them.
type assemblyGraph struct {
	byName     map[string]ResponseFile
	sources    map[string][]string
	references map[string][]string // assembly -> the project assemblies it references
	dependents map[string][]string // assembly -> the project assemblies that reference it
	names      []string
}

// BuildCompilePlan decides which assemblies to compile, in which order, for one project.
func BuildCompilePlan(projectRoot string, dagDir string, all bool) (BuildPlan, error) {
	graph, err := loadAssemblyGraph(projectRoot, dagDir)
	if err != nil {
		return BuildPlan{}, err
	}

	reasons, err := selectChangedAssemblies(projectRoot, dagDir, graph, all)
	if err != nil {
		return BuildPlan{}, err
	}
	propagateToDependents(graph, reasons)

	order, err := topologicalOrder(graph, reasons)
	if err != nil {
		return BuildPlan{}, err
	}

	units := make([]CompileUnit, 0, len(order))
	for _, name := range order {
		units = append(units, CompileUnit{
			Assembly: graph.byName[name],
			Sources:  graph.sources[name],
			Reason:   reasons[name],
		})
	}

	return BuildPlan{
		DagDir:    dagDir,
		OutputDir: filepath.Join(filepath.FromSlash(compileCheckOutputRoot), filepath.Base(dagDir)),
		Units:     units,
		Skipped:   len(graph.names) - len(units),
	}, nil
}

// loadAssemblyGraph parses every response file in the dag and links the assemblies to each other.
func loadAssemblyGraph(projectRoot string, dagDir string) (assemblyGraph, error) {
	responseFilePaths, err := listResponseFiles(filepath.Join(projectRoot, dagDir))
	if err != nil {
		return assemblyGraph{}, err
	}

	graph := assemblyGraph{
		byName:     map[string]ResponseFile{},
		sources:    map[string][]string{},
		references: map[string][]string{},
		dependents: map[string][]string{},
	}
	for _, path := range responseFilePaths {
		rsp, parseErr := ParseResponseFile(path)
		if parseErr != nil {
			return assemblyGraph{}, parseErr
		}
		graph.byName[rsp.AssemblyName] = rsp
		graph.names = append(graph.names, rsp.AssemblyName)
	}
	sort.Strings(graph.names)

	for _, name := range graph.names {
		for _, reference := range graph.byName[name].ProjectAssemblyReferences(dagDir) {
			if _, known := graph.byName[reference]; !known {
				continue
			}
			graph.references[name] = append(graph.references[name], reference)
			graph.dependents[reference] = append(graph.dependents[reference], name)
		}
	}

	return graph, nil
}

// listResponseFiles names the response files in a dag directory that describe a real assembly.
// Why .mvfrm.rsp is dropped: Bee writes those for its own framework-move step, and they carry no
// assembly of their own.
func listResponseFiles(dagDirectoryPath string) ([]string, error) {
	matches, err := filepath.Glob(filepath.Join(dagDirectoryPath, "*"+responseFileExtension))
	if err != nil {
		return nil, fmt.Errorf("failed to list response files in %s: %w", dagDirectoryPath, err)
	}

	paths := make([]string, 0, len(matches))
	for _, path := range matches {
		if strings.HasSuffix(path, movedFrameworkResponseSuffix) {
			continue
		}
		paths = append(paths, path)
	}
	if len(paths) == 0 {
		return nil, fmt.Errorf(
			"no Bee response files found in %s; %s", dagDirectoryPath, runCompileFirstAdvice)
	}

	return paths, nil
}

// selectChangedAssemblies rebuilds each assembly's source list and records why it needs compiling.
func selectChangedAssemblies(
	projectRoot string, dagDir string, graph assemblyGraph, all bool,
) (map[string]string, error) {
	assemblyDefinitions, err := IndexAssemblyDefinitions(projectRoot)
	if err != nil {
		return nil, err
	}

	if setErr := DetectAssemblySetChange(projectRoot, dagDir, graph, assemblyDefinitions); setErr != nil {
		return nil, setErr
	}

	reasons := map[string]string{}
	for _, name := range graph.names {
		rsp := graph.byName[name]
		asmdef := lookupAssemblyDefinition(assemblyDefinitions, name)
		if structuralErr := DetectStructuralChange(projectRoot, rsp, asmdef, dagDir); structuralErr != nil {
			return nil, structuralErr
		}

		sources, sourceErr := RebuildSources(projectRoot, rsp, asmdef)
		if sourceErr != nil {
			return nil, sourceErr
		}
		graph.sources[name] = sources

		report, changeErr := DetectSourceChange(projectRoot, rsp, sources, dagDir)
		if changeErr != nil {
			return nil, changeErr
		}
		if all || report.Changed {
			reasons[name] = report.Reason
		}
	}

	return reasons, nil
}

// lookupAssemblyDefinition finds the .asmdef for an assembly, if the assembly has one at all.
func lookupAssemblyDefinition(
	assemblyDefinitions map[string]AssemblyDefinition, name string,
) *AssemblyDefinition {
	definition, found := assemblyDefinitions[name]
	if !found {
		return nil
	}

	return &definition
}

// propagateToDependents adds every assembly that references a changed one.
// Why: a changed assembly can alter the public API its dependents compile against, so compiling it
// alone would miss the errors that only appear on the other side of the reference.
func propagateToDependents(graph assemblyGraph, reasons map[string]string) {
	queue := make([]string, 0, len(reasons))
	for _, name := range graph.names {
		if _, changed := reasons[name]; changed {
			queue = append(queue, name)
		}
	}

	for len(queue) > 0 {
		current := queue[0]
		queue = queue[1:]
		for _, dependent := range graph.dependents[current] {
			if _, changed := reasons[dependent]; changed {
				continue
			}
			reasons[dependent] = dependencyReasonPrefix + current
			queue = append(queue, dependent)
		}
	}
}

// topologicalOrder orders the selected assemblies so every reference is compiled before its user.
func topologicalOrder(graph assemblyGraph, reasons map[string]string) ([]string, error) {
	selected := map[string]bool{}
	remaining := map[string]int{}
	for name := range reasons {
		selected[name] = true
	}
	for name := range selected {
		for _, reference := range graph.references[name] {
			if selected[reference] {
				remaining[name]++
			}
		}
	}

	ready := []string{}
	for _, name := range graph.names {
		if selected[name] && remaining[name] == 0 {
			ready = append(ready, name)
		}
	}

	order := make([]string, 0, len(selected))
	for len(ready) > 0 {
		// Why the ready set is re-sorted: assemblies at the same depth have no order between them,
		// and sorting keeps the plan identical from run to run.
		sort.Strings(ready)
		current := ready[0]
		ready = ready[1:]
		order = append(order, current)
		for _, dependent := range graph.dependents[current] {
			if !selected[dependent] {
				continue
			}
			remaining[dependent]--
			if remaining[dependent] == 0 {
				ready = append(ready, dependent)
			}
		}
	}

	if len(order) != len(selected) {
		return nil, fmt.Errorf(
			"cyclic assembly references involving %s", strings.Join(unresolvedNames(selected, order), ", "))
	}

	return order, nil
}

// unresolvedNames lists the selected assemblies the topological sort could not place.
func unresolvedNames(selected map[string]bool, order []string) []string {
	placed := map[string]bool{}
	for _, name := range order {
		placed[name] = true
	}

	names := []string{}
	for name := range selected {
		if !placed[name] {
			names = append(names, name)
		}
	}
	sort.Strings(names)

	return names
}
