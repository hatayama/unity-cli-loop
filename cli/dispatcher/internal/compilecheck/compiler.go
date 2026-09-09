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

	compilerExecArgument      = "exec"
	compilerNoStdLibFlag      = "/nostdlib"
	compilerNoConfigFlag      = "/noconfig"
	compilerLibraryTargetFlag = "-target:library"
	multiLevelLookupSetting   = "DOTNET_MULTILEVEL_LOOKUP=0"

	responseFilePermissions = 0o600
	outputDirPermissions    = 0o755
)

// diagnosticPattern matches one csc diagnostic line: "<file>(<line>,<column>): <severity> <code>: <message>".
var diagnosticPattern = regexp.MustCompile(
	`^(?P<file>.+)\((?P<line>\d+),(?P<column>\d+)\): (?P<severity>error|warning) (?P<code>[A-Z]+\d+): (?P<message>.+)$`)

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
}

// CommandRunner runs one compiler invocation, so tests can stand in for the real process.
type CommandRunner func(ctx context.Context, cmd *exec.Cmd) (string, string, int, error)

// Compiler runs the Unity-bundled csc over the units of a build plan.
type Compiler struct {
	Paths       EditorCompilerPaths
	ProjectRoot string
	Timeout     time.Duration
	Run         CommandRunner
}

// CompileUnit compiles one assembly and reports the diagnostics csc produced for it.
func (c Compiler) CompileUnit(
	ctx context.Context, plan BuildPlan, unit CompileUnit,
) (UnitResult, error) {
	outputDirectoryPath := filepath.Join(c.ProjectRoot, plan.OutputDir)
	if err := os.MkdirAll(outputDirectoryPath, outputDirPermissions); err != nil {
		return UnitResult{}, fmt.Errorf("failed to create %s: %w", outputDirectoryPath, err)
	}

	// Why the previous run's outputs go first: csc writes nothing when it fails, so a leftover
	// reference assembly from an earlier run would keep describing an assembly that no longer
	// compiles, and every dependent would be checked against an API that is gone.
	if err := removeUnitOutputs(outputDirectoryPath, unit); err != nil {
		return UnitResult{}, err
	}

	responseFilePath, err := writeRewrittenResponseFile(c.ProjectRoot, plan, unit)
	if err != nil {
		return UnitResult{}, err
	}

	invocationContext, cancel := context.WithTimeout(ctx, c.Timeout)
	defer cancel()

	command := exec.CommandContext(invocationContext,
		c.Paths.DotnetHostPath, compilerExecArgument, c.Paths.CompilerDllPath,
		compilerNoStdLibFlag, compilerNoConfigFlag, "@"+responseFilePath)
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

	return buildUnitResult(unit.Assembly.AssemblyName, stdout, stderr, exitCode, duration), nil
}

// removeUnitOutputs deletes what a previous run left for this assembly in the check's output directory.
func removeUnitOutputs(outputDirectoryPath string, unit CompileUnit) error {
	names := []string{
		unit.Assembly.AssemblyName + assemblyExtension,
		unit.Assembly.AssemblyName + referenceAssemblyExtension,
	}
	for _, name := range names {
		path := filepath.Join(outputDirectoryPath, name)
		if err := os.Remove(path); err != nil && !os.IsNotExist(err) {
			return fmt.Errorf("failed to remove %s: %w", path, err)
		}
	}

	return nil
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
// output directory, so nothing this run produces can reach the assemblies Unity loads.
func writeRewrittenResponseFile(
	projectRoot string, plan BuildPlan, unit CompileUnit,
) (string, error) {
	rsp := unit.Assembly
	lines := []string{
		compilerLibraryTargetFlag,
		quoteFlag(outputFlagPrefix, filepath.Join(plan.OutputDir, filepath.Base(rsp.OutputPath))),
	}
	// Why the guard: an assembly built without a reference assembly has no base name to join, and
	// joining an empty one would point -refout at the output directory itself.
	if rsp.RefOutputPath != "" {
		lines = append(lines, quoteFlag(
			referenceOutputFlagPref, filepath.Join(plan.OutputDir, filepath.Base(rsp.RefOutputPath))))
	}
	for _, define := range rsp.Defines {
		lines = append(lines, defineFlagPrefix+define)
	}
	for _, reference := range rsp.References {
		lines = append(lines,
			quoteFlag(referenceFlagPrefix, rewriteReference(projectRoot, plan, reference)))
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

	path := filepath.Join(projectRoot, plan.OutputDir, rsp.AssemblyName+responseFileExtension)
	if err := os.WriteFile(path, []byte(strings.Join(lines, "\n")), responseFilePermissions); err != nil {
		return "", fmt.Errorf("failed to write %s: %w", path, err)
	}

	return path, nil
}

// rewriteReference points a reference at this run's own output when this run rebuilds that assembly.
// Why the others stay: an assembly this run skipped is unchanged, so Unity's own reference assembly
// still describes it exactly. An assembly this run failed to compile wrote no reference assembly at
// all, so it falls back to Unity's too rather than failing the dependent on a missing file.
func rewriteReference(projectRoot string, plan BuildPlan, reference string) string {
	name, ok := projectAssemblyReferenceName(reference, plan.DagDir)
	if !ok || !planCompiles(plan, name) {
		return reference
	}

	rewritten := filepath.Join(plan.OutputDir, name+referenceAssemblyExtension)
	if !fileExists(filepath.Join(projectRoot, rewritten)) {
		return reference
	}

	return rewritten
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
