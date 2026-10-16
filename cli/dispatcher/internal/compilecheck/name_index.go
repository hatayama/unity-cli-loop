package compilecheck

import (
	"bytes"
	"errors"
	"fmt"
	"io/fs"
	"os"
	"path/filepath"
	"slices"
	"strings"
	"sync"
	"sync/atomic"
	"time"
	"unicode"
)

// targetTypedNewToken stands for a target-typed new in a dependent's identifiers, the way the worker
// names a changed constructor.
const targetTypedNewToken = "new("

// noSavedGeneratorOutputReason says a unit compiled on a reference's named change without being
// narrowed by names, because nothing tells what its generators would write.
const noSavedGeneratorOutputReason = "no saved generator output"

// ChangedReference names a referenced assembly whose changed surface made a unit compile, and the
// first of its changed names the unit writes; empty when the change could not be told by names.
type ChangedReference struct {
	Assembly string
	Name     string
	// Reason says why the change was not narrowed by names when it was told by names; today only
	// "no saved generator output".
	Reason string
}

// NameSkip names a unit left out although a reference it reads changed its surface, because the unit
// writes none of the names the change touches, and those references.
type NameSkip struct {
	Assembly          string
	ChangedReferences []string
}

// NameIndexReport says what reading the identifiers of the units with references in the plan took.
type NameIndexReport struct {
	// Duration is the time spent reading the generator output and, when they were read, the sources.
	Duration time.Duration
	// Units counts the units whose identifiers were read, WithGenerated those whose saved generator
	// output was read too, and Unreadable those that could not be read and so compile on any change.
	Units         int
	WithGenerated int
	Unreadable    int
	// WithoutGenerated counts the units compiled on a named change without being narrowed by names,
	// because they have analyzers and no saved generator output.
	WithoutGenerated int
	// SourcesRead says whether the run needed the sources, by compiling a unit, by having the worker
	// check its changed files, or by asking a verdict for names.
	SourcesRead bool
}

// identifierSet is the identifiers one unit writes.
type identifierSet map[string]struct{}

// scanIdentifiers adds every identifier content holds to into, cut the way C# cuts one: a letter, a
// letter number or '_', followed by those, decimal digits, combining marks, connectors and formatting
// characters, the last left out of the name as the compiler leaves them out. A \u or \U escape counts
// as the character it spells, as it does for the compiler. Comments, strings and code a #if leaves
// out are read too, which can only make a unit compile more often. A "new" followed by '(' also adds
// the target-typed new token.
func scanIdentifiers(content []byte, into identifierSet) {
	position := 0
	for position < len(content) {
		character, size, escaped := runeAt(content, position)
		if !isIdentifierStart(character) {
			position += size

			continue
		}
		start := position
		position += size
		for position < len(content) {
			next, nextSize, nextEscaped := runeAt(content, position)
			if !isIdentifierPart(next) {
				break
			}
			escaped = escaped || nextEscaped
			position += nextSize
		}
		text := content[start:position]
		// Why the text is decoded only when it holds an escape: nearly no identifier does, and
		// decoding every one would copy each name the scan meets.
		if escaped {
			text = decodedText(text)
		}
		identifier := identifierText(text)
		into[identifier] = struct{}{}
		if identifier == "new" && opensParenthesis(content[position:]) {
			into[targetTypedNewToken] = struct{}{}
		}
	}
}

func isIdentifierStart(character rune) bool {
	return unicode.IsLetter(character) || unicode.Is(unicode.Nl, character) || character == '_'
}

func isIdentifierPart(character rune) bool {
	return isIdentifierStart(character) || unicode.In(character, unicode.Nd, unicode.Mn, unicode.Mc, unicode.Pc, unicode.Cf)
}

// identifierText is the name an identifier's text spells, without its formatting characters.
func identifierText(text []byte) string {
	if !bytes.ContainsFunc(text, isFormatting) {
		return string(text)
	}

	return string(bytes.Map(func(character rune) rune {
		if isFormatting(character) {
			return -1
		}

		return character
	}, text))
}

func isFormatting(character rune) bool {
	return unicode.Is(unicode.Cf, character)
}

// opensParenthesis reports whether rest starts with '(' after any white space.
// Why it walks the bytes in place: copying the rest of the file at every "new" makes a file full of
// them take time in the square of its length.
func opensParenthesis(rest []byte) bool {
	trimmed := bytes.TrimLeftFunc(rest, unicode.IsSpace)

	return len(trimmed) > 0 && trimmed[0] == '('
}

// readSourceIdentifiers adds the identifiers of every source of a unit to into.
// Why a unit without sources is an error rather than an empty set: an empty set would read as a unit
// that writes no changed name, and skip it on every change.
func readSourceIdentifiers(projectRoot string, unit CompileUnit, into identifierSet) error {
	if len(unit.Sources) == 0 {
		return fmt.Errorf("%s has no sources to read names from", unit.Assembly.AssemblyName)
	}
	for _, source := range unit.Sources {
		content, err := os.ReadFile(sourcePath(projectRoot, filepath.FromSlash(source)))
		if err != nil {
			return fmt.Errorf("failed to read the names of %s: %w", source, err)
		}
		scanIdentifiers(content, into)
	}

	return nil
}

// readGeneratedIdentifiers reads the identifiers of the generator output a unit's last full compile
// saved, and reports whether there was any.
// Why generator output is read: a generator can write a referenced name the unit's own sources never
// do, and that code compiles with the unit.
func readGeneratedIdentifiers(generatedDirectory string) (identifierSet, bool, error) {
	identifiers := identifierSet{}
	if _, err := os.Stat(generatedDirectory); errors.Is(err, fs.ErrNotExist) {
		return identifiers, false, nil
	}
	err := filepath.WalkDir(generatedDirectory, func(path string, entry fs.DirEntry, walkErr error) error {
		if walkErr != nil {
			return walkErr
		}
		if entry.IsDir() || !strings.EqualFold(filepath.Ext(path), ".cs") {
			return nil
		}
		content, err := os.ReadFile(path)
		if err != nil {
			return err
		}
		scanIdentifiers(content, identifiers)

		return nil
	})
	if err != nil {
		return nil, false, fmt.Errorf("failed to read the generated names in %s: %w", generatedDirectory, err)
	}

	return identifiers, true, nil
}

// nameIndex reads the identifiers of every unit that references another unit of the plan, so a
// reference that changed only names a unit never writes need not make it compile.
// Why every such unit and not only the dependents: a unit selected for its own sources compiles
// anyway, but one that started on the assumption that a reference keeps its surface need not compile
// again when that reference changed only names it never writes.
type nameIndex struct {
	projectRoot   string
	units         []CompileUnit
	sets          []identifierSet
	errs          []error
	withGenerated []bool
	indexed       []bool
	done          chan struct{}
	// generatedDuration and sourcesDuration are how long each read took, measured from its own start
	// so the time the run spent before asking for the sources is not counted.
	generatedDuration time.Duration
	sourcesDuration   time.Duration
	started           bool
	sourcesOnce       sync.Once
	sourcesRead       atomic.Bool
}

// unitIdentifiers is what was read of one unit's identifiers.
type unitIdentifiers struct {
	set           identifierSet
	withGenerated bool
	err           error
}

// start reads the generator output of the units marked in wanted, at most once per run, and leaves
// their sources for readSources. generated holds the generator output directory of each unit.
// Why the generator output is read here, before anything compiles: a unit's compile removes and
// rewrites that directory, and a read racing it would lose the names only the generator wrote.
// Sources are never removed, so they can wait until a run needs them.
func (index *nameIndex) start(projectRoot string, units []CompileUnit, generated []string, wanted []bool) {
	if index.started {
		return
	}
	index.started = true
	index.projectRoot = projectRoot
	index.units = units
	index.done = make(chan struct{})
	index.indexed = wanted
	startedAt := time.Now()
	generatedNames := computeInParallel(len(units), func(position int) unitIdentifiers {
		if !wanted[position] {
			return unitIdentifiers{}
		}
		set, withGenerated, err := readGeneratedIdentifiers(generated[position])

		return unitIdentifiers{set: set, withGenerated: withGenerated, err: err}
	})
	index.keep(generatedNames)
	index.generatedDuration = time.Since(startedAt)
}

// readSources starts reading, in the background, the sources of the units start read the generator
// output of, at most once per run; nothing when start never ran.
// Why the read waits for this call: a run that only replays and asks no verdict for names would
// otherwise spend most of its time reading names it never uses. A run that compiles or checks reads
// them while that runs, and a verdict that needs them waits for them.
// Safe to call from any goroutine.
func (index *nameIndex) readSources() {
	if !index.started {
		return
	}
	index.sourcesOnce.Do(func() {
		index.sourcesRead.Store(true)
		go func() {
			defer close(index.done)
			startedAt := time.Now()
			defer func() { index.sourcesDuration = time.Since(startedAt) }()
			index.keep(computeInParallel(len(index.units), func(position int) unitIdentifiers {
				names := unitIdentifiers{set: index.sets[position], withGenerated: index.withGenerated[position], err: index.errs[position]}
				if !index.indexed[position] || names.err != nil {
					return names
				}
				if err := readSourceIdentifiers(index.projectRoot, index.units[position], names.set); err != nil {
					return unitIdentifiers{err: err}
				}

				return names
			}))
		}()
	})
}

// keep holds what was read so far.
func (index *nameIndex) keep(results []unitIdentifiers) {
	index.sets = make([]identifierSet, len(results))
	index.errs = make([]error, len(results))
	index.withGenerated = make([]bool, len(results))
	for position, result := range results {
		index.sets[position] = result.set
		index.errs[position] = result.err
		index.withGenerated[position] = result.withGenerated
	}
}

// lookup reads the sources when nothing has yet, waits for them and returns the identifiers of one
// unit and whether its saved generator output was among them, or an error when it has none.
func (index *nameIndex) lookup(position int) (identifierSet, bool, error) {
	if !index.started {
		return nil, false, errors.New("the names of the plan were never read")
	}
	index.readSources()
	<-index.done
	if !index.indexed[position] {
		return nil, false, errors.New("the names of this unit were not read")
	}
	if index.errs[position] != nil {
		return nil, false, index.errs[position]
	}

	return index.sets[position], index.withGenerated[position], nil
}

// wait returns once the background read of the sources is over, so nothing outlives the run that
// started it; at once when the sources were never asked for.
func (index *nameIndex) wait() {
	if index.started && index.sourcesRead.Load() {
		<-index.done
	}
}

// report says what the read took; zero when nothing was read. It waits for the sources when they are
// being read.
// Why it waits itself rather than rely on wait: the run builds its outcome, and with it this report,
// before its deferred wait runs.
func (index *nameIndex) report() NameIndexReport {
	if !index.started {
		return NameIndexReport{}
	}
	sourcesRead := index.sourcesRead.Load()
	if sourcesRead {
		<-index.done
	}
	report := NameIndexReport{Duration: index.generatedDuration + index.sourcesDuration, SourcesRead: sourcesRead}
	for position, indexed := range index.indexed {
		if !indexed {
			continue
		}
		report.Units++
		if index.errs[position] != nil {
			report.Unreadable++
		} else if index.withGenerated[position] {
			report.WithGenerated++
		}
	}

	return report
}

// startNameIndex reads the generator output of the units that reference another unit of the plan,
// when there is any, and has the run read their sources once it first compiles a unit in full or has
// the worker check its changed files.
func (run *schedulerRun) startNameIndex() {
	wanted := make([]bool, len(run.plan.Units))
	any := false
	for position := range run.plan.Units {
		wanted[position] = len(run.state.references[position]) > 0
		any = any || wanted[position]
	}
	if !any {
		return
	}
	records := run.compiler.records(run.plan)
	generated := make([]string, len(run.plan.Units))
	for position, unit := range run.plan.Units {
		generated[position] = filepath.Join(run.compiler.ProjectRoot, records.GeneratedDirectory(unit.Assembly.AssemblyName))
	}
	run.names.start(run.compiler.ProjectRoot, run.plan.Units, generated, wanted)
	// Why a compile or a check of changed files starts the read of the sources: either runs long
	// enough to hide the read, while a run that only replays would wait for it.
	run.compiler.beforeCompilerWork = run.names.readSources
}

// referenceEffect is what one reference's final state means for one unit that reads it: whether it
// counts as a changed input, and the first of its changed names the unit writes.
type referenceEffect struct {
	dependent int
	touches   bool
	// named marks a reference whose change was told by names, so one that does not touch was
	// changed rather than kept.
	named  bool
	name   string
	reason string // why a named change touched the unit without a name it writes
}

// changedReference is a reference that touched a unit, by its position in the plan, with the first
// changed name the unit writes; empty when no name told the change.
type changedReference struct {
	reference int
	name      string
	reason    string
}

// effectsOf decides, for every unit reading the unit at index, whether its verdict touches that unit:
// never when kept; always when the change cannot be told by names, when nothing compared it, or when
// the unit's names could not be read; and otherwise when the unit writes one of the changed names.
func (run *schedulerRun) effectsOf(index int, verdict SurfaceVerdict) []referenceEffect {
	dependents := run.state.dependents[index]
	effects := make([]referenceEffect, 0, len(dependents))
	for _, dependent := range dependents {
		switch {
		case verdict.Kept:
			effects = append(effects, referenceEffect{dependent: dependent})
		case !verdict.namedChange():
			effects = append(effects, referenceEffect{dependent: dependent, touches: true})
		default:
			effects = append(effects, run.namedEffect(dependent, verdict.ChangedNames))
		}
	}

	return effects
}

// namedEffect decides whether a change told by names touches one unit: when the unit writes one of
// the names, or when it has analyzers and no saved generator output.
// Why the names are matched first: a name the unit writes tells a reader more than the reason does.
// Why a unit without saved generator output compiles: its generators may write a changed name its
// own sources never do, and nothing on disk says what they write. A compile saves that output, even
// empty, so this happens once per unit. Without analyzers there are no generators to miss.
func (run *schedulerRun) namedEffect(dependent int, changedNames []string) referenceEffect {
	identifiers, withGenerated, err := run.names.lookup(dependent)
	if err != nil {
		return referenceEffect{dependent: dependent, touches: true}
	}
	for _, name := range changedNames {
		if _, found := identifiers[name]; found {
			return referenceEffect{dependent: dependent, touches: true, named: true, name: name}
		}
	}
	if !withGenerated && len(run.plan.Units[dependent].Assembly.Analyzers) > 0 {
		return referenceEffect{dependent: dependent, touches: true, reason: noSavedGeneratorOutputReason}
	}

	return referenceEffect{dependent: dependent, named: true}
}

// nameIndexReport is what reading the names took, with the units compiled for want of saved
// generator output.
func (run *schedulerRun) nameIndexReport() NameIndexReport {
	report := run.names.report()
	report.WithoutGenerated = run.withoutGeneratedCount()

	return report
}

// withoutGeneratedCount counts the units compiled because a named change touched them for want of
// saved generator output.
func (run *schedulerRun) withoutGeneratedCount() int {
	count := 0
	for index, touched := range run.state.changedBy {
		// Why only compiled units count: a unit touched for this reason and then blocked by another
		// reference's errors never compiled, and its changed references are never reported either.
		if !run.compiled[index] {
			continue
		}
		if slices.ContainsFunc(touched, func(reference changedReference) bool { return reference.reason != "" }) {
			count++
		}
	}

	return count
}

// changedReferencesOf names the references that touched the unit at index, in the plan's order.
func (run *schedulerRun) changedReferencesOf(index int) []ChangedReference {
	touched := slices.Clone(run.state.changedBy[index])
	if len(touched) == 0 {
		return nil
	}
	slices.SortFunc(touched, func(a changedReference, b changedReference) int { return a.reference - b.reference })
	references := make([]ChangedReference, 0, len(touched))
	for _, reference := range touched {
		references = append(references, ChangedReference{
			Assembly: run.plan.Units[reference.reference].Assembly.AssemblyName, Name: reference.name,
			Reason: reference.reason,
		})
	}

	return references
}

// recordNameSkip notes a unit left out although a reference changed, because it writes none of the
// names that reference's change touches.
func (run *schedulerRun) recordNameSkip(index int) {
	unaffected := run.state.unaffectedBy[index]
	if len(unaffected) == 0 {
		return
	}
	run.nameSkips = append(run.nameSkips, NameSkip{
		Assembly: run.plan.Units[index].Assembly.AssemblyName, ChangedReferences: run.namesOf(slices.Sorted(slices.Values(unaffected))),
	})
}
