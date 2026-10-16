package compilecheck

import (
	"context"
	"errors"
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"regexp"
	"strconv"
	"strings"
	"time"
)

const (
	severityError   = "error"
	severityWarning = "warning"

	compilerExecArgument = "exec"
	compilerNoStdLibFlag = "/nostdlib"
	compilerNoConfigFlag = "/noconfig"
	// compilerSharedFlag connects csc to the Roslyn compiler server, which is how Unity's Bee starts
	// it too. Compiling several assemblies in a row then reuses the server's JIT-compiled code and its
	// cached reference metadata instead of paying for both once per assembly. When csc cannot reach
	// the server it compiles in its own process, so this changes speed and nothing else.
	compilerSharedFlag        = "/shared"
	compilerLibraryTargetFlag = "-target:library"
	multiLevelLookupSetting   = "DOTNET_MULTILEVEL_LOOKUP=0"

	responseFilePermissions = 0o600
	outputDirPermissions    = 0o755
)

// diagnosticPattern matches one csc diagnostic line: "<file>(<line>,<column>): <severity> <code>: <message>".
// Why the code is any run without spaces or colons: analyzers choose their own diagnostic IDs, such as
// "Style_ConstName", and an error under an ID the pattern rejects would surface only as a compile that
// failed without saying where.
var diagnosticPattern = regexp.MustCompile(
	`^(?P<file>.+)\((?P<line>\d+),(?P<column>\d+)\): (?P<severity>error|warning) (?P<code>[^\s:]+): (?P<message>.+)$`)

// Diagnostic is one error or warning csc reported at a source position.
type Diagnostic struct {
	Severity string
	Code     string
	Message  string
	File     string // as csc printed it, which is relative to the project root
	Line     int
	Column   int
}

// UnitResult is what compiling one assembly produced.
type UnitResult struct {
	Assembly    string
	Diagnostics []Diagnostic
	Succeeded   bool
	RawOutput   string // filled only when csc failed without saying why in a diagnostic
	Duration    time.Duration
	// Reused marks a result this run replayed from the previous one rather than compiled, because
	// every input the previous compile read is still exactly as it read it.
	Reused bool
	// FastChecked marks a result that came from checking only the changed files against the last
	// successful full compile, instead of compiling the assembly.
	FastChecked bool
	// FastCheckReused marks a fast check this run replayed from the previous run's check of the same
	// changed files, because the inputs, the baseline and the worker build were all as that check saw
	// them.
	FastCheckReused bool
	// FastCheckChangedFiles lists the files a fast check checked, spelled as the response file does.
	FastCheckChangedFiles []string
	// UncheckedCodes lists the diagnostic IDs a fast check could not run; only a full compile does.
	UncheckedCodes []string
	// GeneratorsNotRerun is set when a fast check read the generator output of the last full
	// compile instead of running the source generators.
	GeneratorsNotRerun bool
	// FastPathFallbackReason says why a unit the fast check was tried on was compiled in full.
	FastPathFallbackReason string
	// FastPathNote says why a successful full compile left no baseline for the next fast check.
	FastPathNote string
	// Surface says whether the unit's reference assembly kept the API of the last Unity build, for a
	// successful unit something in the plan references; zero for any other unit.
	Surface SurfaceVerdict
	// AssumedReferences names the referenced assemblies that were still compiling when the compile
	// this result came from started, and that it read from Unity's build on the assumption that they
	// keep Unity's public surface.
	AssumedReferences []string
	// CompiledAgainAfter names the referenced assembly whose changed surface proved an earlier
	// compile of this unit wrong, so this result comes from compiling it again; empty when one
	// compile was enough.
	CompiledAgainAfter string
	// DiscardedDuration is how long the compile this run threw away took.
	DiscardedDuration time.Duration
	// WaitedForRecord marks a unit that waited for the references still compiling rather than
	// starting before them, because its recorded result might be replayed once they finished.
	WaitedForRecord bool
	// ChangedReferences names the referenced assemblies whose changed surface counted for this unit,
	// in the plan's order, each with the first changed name the unit writes.
	ChangedReferences []ChangedReference
}

// CommandRunner runs one compiler invocation, so tests can stand in for the real process.
type CommandRunner func(ctx context.Context, cmd *exec.Cmd) (string, string, int, error)

// Compiler runs the Unity-bundled csc over the units of a build plan.
type Compiler struct {
	Paths       EditorCompilerPaths
	ProjectRoot string
	Timeout     time.Duration
	Run         CommandRunner
	// FastPath checks only the changed files when it can; nil compiles every unit in full.
	FastPath *fastPathRunner
	// compareSurface stands in for the comparison of a unit's reference assembly with Unity's; nil
	// compares the two. Why it can be replaced: the tests have to see which units the run compares at
	// all, and a byte comparison leaves nothing behind to count.
	compareSurface func(projectRoot string, plan BuildPlan, unit CompileUnit) SurfaceVerdict
	// beforeCompilerWork is called right before a unit compiles in full or the worker checks its
	// changed files; nil calls nothing. Why: a run that does either reads what it may need later while
	// that work runs, and one that only replays never does.
	beforeCompilerWork func()
}

// startCompilerWork calls the hook set for the moment a unit's compile or check begins, if any.
func (c Compiler) startCompilerWork() {
	if c.beforeCompilerWork != nil {
		c.beforeCompilerWork()
	}
}

// surfaceVerdict says whether compiling this unit left its reference assembly showing its dependents
// the public surface the last Unity build showed them.
func (c Compiler) surfaceVerdict(ctx context.Context, plan BuildPlan, unit CompileUnit) SurfaceVerdict {
	if c.compareSurface != nil {
		return c.compareSurface(c.ProjectRoot, plan, unit)
	}

	return c.referenceSurfaceVerdict(ctx, plan, unit)
}

// CompileUnit compiles one assembly and reports the diagnostics csc produced for it.
// Why a Compiler value is safe to share between the units running at the same time: it holds no
// mutable state, and every file this touches - the outputs it removes and the response file it
// writes - is named after the assembly, so no two units address the same path. The output directory
// itself is created once by the caller, before any unit starts.
// A VBCSCompiler process may outlive the run: the compiler server stays up for a while so the next
// invocation can reuse it, exactly as it does after a build Unity runs itself.
func (c Compiler) CompileUnit(
	ctx context.Context, plan BuildPlan, unit CompileUnit,
) (UnitResult, error) {
	// Why the previous run's outputs go first: csc writes nothing when it fails, so a leftover
	// reference assembly from an earlier run would keep describing an assembly that no longer
	// compiles, and every dependent would be checked against an API that is gone.
	if err := c.removeUnitOutputs(plan, unit); err != nil {
		return UnitResult{}, err
	}

	// Why the generator output directory is created here: the csc of Roslyn 4.3 (Unity 2022.3) creates
	// the folder of each generator under it but not the directory itself, and fails every generated
	// file with CS0016 when it is missing. Newer Roslyn creates it either way.
	generatedPath := filepath.Join(c.ProjectRoot, c.records(plan).GeneratedDirectory(unit.Assembly.AssemblyName))
	if err := os.MkdirAll(generatedPath, outputDirPermissions); err != nil {
		return UnitResult{}, fmt.Errorf("failed to create %s: %w", generatedPath, err)
	}

	result, err := c.invokeCompiler(ctx, plan, unit)
	// Why the generator output goes when csc did not finish: name narrowing takes the directory as
	// the output of a finished compile, even an empty one, so a directory left by a compile that
	// never ran would let a later run narrow the unit by its hand-written sources alone. A dispatcher
	// killed mid-compile still leaves one, until the unit compiles again and Forget removes it.
	if err != nil || result.RawOutput != "" {
		if removeErr := os.RemoveAll(generatedPath); removeErr != nil {
			return result, errors.Join(err, fmt.Errorf("failed to remove %s: %w", generatedPath, removeErr))
		}
	}

	return result, err
}

// invokeCompiler writes the unit's response file and runs csc on it, once its outputs are cleared
// and its generator output directory exists.
func (c Compiler) invokeCompiler(
	ctx context.Context, plan BuildPlan, unit CompileUnit,
) (UnitResult, error) {
	// Why the generator output is saved: a later fast check reads it instead of running the generators.
	responseFilePath, err := writeRewrittenResponseFile(c.ProjectRoot, plan, unit,
		unit.Assembly.AssemblyName+responseFileExtension,
		[]string{quoteFlag(generatedFilesOutFlagPrefix, filepath.ToSlash(c.records(plan).GeneratedDirectory(unit.Assembly.AssemblyName)))})
	if err != nil {
		return UnitResult{}, err
	}

	invocationContext, cancel := context.WithTimeout(ctx, c.Timeout)
	defer cancel()

	command := exec.CommandContext(invocationContext,
		c.Paths.DotnetHostPath, compilerExecArgument, c.Paths.CompilerDllPath,
		compilerNoStdLibFlag, compilerNoConfigFlag, compilerSharedFlag, "@"+responseFilePath)
	command.Dir = c.ProjectRoot
	command.Env = append(os.Environ(), multiLevelLookupSetting)

	startedAt := time.Now()
	stdout, stderr, exitCode, runErr := c.Run(invocationContext, command)
	duration := time.Since(startedAt)
	// Why a non-zero exit is not an error here: that is how csc reports compile errors, and those
	// belong in the diagnostics. Only a failure to run the compiler at all is an error.
	if runErr != nil && !isExitError(runErr) {
		return UnitResult{}, fmt.Errorf(
			"failed to run the C# compiler for %s: %w: %s",
			unit.Assembly.AssemblyName, runErr, strings.TrimSpace(stderr))
	}
	// Why a stopped invocation is an error rather than its output: killing csc leaves an exit error
	// that reads exactly like the one it produces on a compile error, and whatever it had already
	// printed parses into diagnostics that look complete. Reporting those would understate what is
	// wrong with the assembly, and recording them would replay that understatement on every run
	// after this one.
	if err := invocationContext.Err(); err != nil {
		return UnitResult{}, fmt.Errorf(
			"the C# compiler for %s was stopped before it finished: %w",
			unit.Assembly.AssemblyName, err)
	}

	return buildUnitResult(unit.Assembly.AssemblyName, stdout, stderr, exitCode, duration), nil
}

// removeUnitOutputs deletes what a previous run left for this assembly in the check's output
// directory: its outputs, and then every record of the compile that wrote them.
func (c Compiler) removeUnitOutputs(plan BuildPlan, unit CompileUnit) error {
	outputDirectoryPath := filepath.Join(c.ProjectRoot, plan.OutputDir)
	for _, name := range []string{
		unit.Assembly.AssemblyName + assemblyExtension,
		unit.Assembly.AssemblyName + referenceAssemblyExtension,
	} {
		path := filepath.Join(outputDirectoryPath, name)
		if err := os.Remove(path); err != nil && !os.IsNotExist(err) {
			return fmt.Errorf("failed to remove %s: %w", path, err)
		}
	}

	return c.records(plan).Forget(unit.Assembly.AssemblyName)
}

// buildUnitResult turns one compiler invocation's output into the result for its assembly.
func buildUnitResult(
	assemblyName string, stdout string, stderr string, exitCode int, duration time.Duration,
) UnitResult {
	combinedOutput := stdout + "\n" + stderr
	diagnostics := ParseDiagnostics(combinedOutput)

	rawOutput := ""
	// Why a failure with no error diagnostic is kept verbatim: without it the run would report a
	// clean compile for an assembly that never actually compiled.
	if exitCode != 0 && !containsError(diagnostics) {
		rawOutput = fmt.Sprintf(
			"csc exited with code %d without reporting an error diagnostic:\n%s",
			exitCode, strings.TrimSpace(combinedOutput))
	}

	return UnitResult{
		Assembly:    assemblyName,
		Diagnostics: diagnostics,
		Succeeded:   exitCode == 0,
		RawOutput:   rawOutput,
		Duration:    duration,
	}
}

// containsError reports whether any diagnostic is an error rather than a warning.
func containsError(diagnostics []Diagnostic) bool {
	for _, diagnostic := range diagnostics {
		if diagnostic.Severity == severityError {
			return true
		}
	}

	return false
}

// isExitError reports whether a run failure is the compiler exiting non-zero rather than not running.
func isExitError(err error) bool {
	var exitError *exec.ExitError

	return errors.As(err, &exitError)
}

// ParseDiagnostics reads every diagnostic csc printed, dropping repeats of the same position and code.
func ParseDiagnostics(output string) []Diagnostic {
	diagnostics := []Diagnostic{}
	seen := map[string]bool{}
	for _, rawLine := range strings.Split(output, "\n") {
		diagnostic, ok := parseDiagnosticLine(strings.TrimSpace(rawLine))
		if !ok {
			continue
		}
		key := fmt.Sprintf("%s:%d:%d:%s", diagnostic.File, diagnostic.Line, diagnostic.Column, diagnostic.Code)
		if seen[key] {
			continue
		}
		seen[key] = true
		diagnostics = append(diagnostics, diagnostic)
	}

	return diagnostics
}

// parseDiagnosticLine reads one csc output line, if it carries a positioned diagnostic.
func parseDiagnosticLine(line string) (Diagnostic, bool) {
	match := diagnosticPattern.FindStringSubmatch(line)
	if match == nil {
		return Diagnostic{}, false
	}

	lineNumber, lineErr := strconv.Atoi(match[diagnosticPattern.SubexpIndex("line")])
	columnNumber, columnErr := strconv.Atoi(match[diagnosticPattern.SubexpIndex("column")])
	if lineErr != nil || columnErr != nil {
		return Diagnostic{}, false
	}

	return Diagnostic{
		Severity: match[diagnosticPattern.SubexpIndex("severity")],
		Code:     match[diagnosticPattern.SubexpIndex("code")],
		Message:  match[diagnosticPattern.SubexpIndex("message")],
		File:     match[diagnosticPattern.SubexpIndex("file")],
		Line:     lineNumber,
		Column:   columnNumber,
	}, true
}

// writeRewrittenResponseFile writes the response file that compiles one unit into the check's own
// output directory, so nothing this run produces can reach the assemblies Unity loads. It is named
// fileName inside that directory, and extraLines are appended after Bee's own flags.
func writeRewrittenResponseFile(
	projectRoot string, plan BuildPlan, unit CompileUnit, fileName string, extraLines []string,
) (string, error) {
	rsp := unit.Assembly
	lines := []string{
		compilerLibraryTargetFlag,
		quoteFlag(outputFlagPrefix,
			filepath.ToSlash(filepath.Join(plan.OutputDir, filepath.Base(rsp.OutputPath)))),
	}
	// Why the guard: an assembly built without a reference assembly has no base name to join, and
	// joining an empty one would point -refout at the output directory itself.
	if rsp.RefOutputPath != "" {
		lines = append(lines, quoteFlag(referenceOutputFlagPref,
			filepath.ToSlash(filepath.Join(plan.OutputDir, filepath.Base(rsp.RefOutputPath)))))
	}
	for _, define := range rsp.Defines {
		lines = append(lines, defineFlagPrefix+define)
	}
	for _, reference := range rsp.References {
		lines = append(lines,
			quoteFlag(referenceFlagPrefix, rewriteReference(projectRoot, plan, unit, reference)))
	}
	for _, analyzer := range rsp.Analyzers {
		lines = append(lines, quoteFlag(analyzerFlagPrefix, analyzer))
	}
	for _, source := range unit.Sources {
		lines = append(lines, `"`+source+`"`)
	}
	for _, flag := range rsp.OtherFlags {
		if flag == compilerLibraryTargetFlag {
			continue
		}
		lines = append(lines, flag)
	}
	if rsp.AdditionalFile != "" {
		lines = append(lines, quoteFlag(additionalFileFlagPrefix, rsp.AdditionalFile))
	}
	// Why Bee's second response file is replayed here: it carries flags that decide what the compiled
	// assembly contains - a /pathmap that maps the project root away, so a source path an attribute
	// bakes into metadata comes out relative - and Bee writes it per assembly, with nothing in it for
	// some of them. Passing back exactly what it wrote is what makes this check's output match Unity's
	// own; synthesizing the flag instead would be wrong for every assembly Bee left it out of. The
	// diagnostics are untouched: csc prints the paths the response file lists, already project-relative.
	lines = append(lines, rsp.CompanionFlags...)
	lines = append(lines, extraLines...)

	path := filepath.Join(projectRoot, plan.OutputDir, fileName)
	if err := os.WriteFile(path, []byte(strings.Join(lines, "\n")), responseFilePermissions); err != nil {
		return "", fmt.Errorf("failed to write %s: %w", path, err)
	}

	return path, nil
}

// rewriteReference points a reference of the unit at this run's own output when this run rebuilt
// that assembly into a different public surface.
// Why the others stay: an assembly this run skipped is unchanged, so Unity's own reference assembly
// still describes it exactly. An assembly this run failed to compile wrote no reference assembly at
// all, so it falls back to Unity's too rather than failing the dependent on a missing file.
// Why a rebuilt one that kept its surface stays as well: the dependent then reads the same API from
// either file, and Unity's is the one an earlier run read whenever that assembly was not rebuilt, so
// staying on it keeps the dependent's key and its changed-files baseline where those runs left them.
// Pointing it at the output instead would recompile the dependent in full for nothing.
func rewriteReference(projectRoot string, plan BuildPlan, unit CompileUnit, reference string) string {
	name, ok := projectAssemblyReferenceName(reference, plan.DagDir)
	if !ok || !planCompiles(plan, name) || unit.KeptSurfaceReferences[name] {
		return reference
	}

	rewritten := filepath.Join(plan.OutputDir, name+referenceAssemblyExtension)
	if !fileExists(filepath.Join(projectRoot, rewritten)) {
		return reference
	}

	return filepath.ToSlash(rewritten)
}

// planCompiles reports whether a plan rebuilds a given assembly.
func planCompiles(plan BuildPlan, name string) bool {
	for _, unit := range plan.Units {
		if unit.Assembly.AssemblyName == name {
			return true
		}
	}

	return false
}

// quoteFlag writes one response file flag with its value quoted the way Bee quotes paths.
func quoteFlag(prefix string, value string) string {
	return prefix + `"` + value + `"`
}
