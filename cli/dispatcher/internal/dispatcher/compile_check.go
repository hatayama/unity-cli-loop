package dispatcher

import (
	"context"
	"encoding/json"
	"fmt"
	"io"
	"path/filepath"
	"strconv"
	"strings"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
	"github.com/hatayama/unity-cli-loop/common/project"
	"github.com/hatayama/unity-cli-loop/dispatcher/internal/compilecheck"
)

const (
	compileCheckNoChangeMessage = "No assemblies changed since the last Unity build."
	compileCheckAllFlag         = "--all"
	compileCheckEditorFlag      = "--editor-version"
	compileCheckMaxDepthFlag    = "--max-depth"
)

// compileCheckOptions are the command-line inputs of one compile-check run.
type compileCheckOptions struct {
	projectPath   string
	all           bool
	editorVersion string
	maxDepth      int
}

// compileCheckIssue is one diagnostic in the response, shaped like the ones `uloop compile` returns.
type compileCheckIssue struct {
	Message  string `json:"Message"`
	Code     string `json:"Code"`
	File     string `json:"File"`
	Line     int    `json:"Line"`
	Column   int    `json:"Column"`
	Assembly string `json:"Assembly"`
}

// compileCheckResponse is the JSON envelope one compile-check run prints.
type compileCheckResponse struct {
	Success            bool                `json:"Success"`
	ErrorCount         int                 `json:"ErrorCount"`
	WarningCount       int                 `json:"WarningCount"`
	Errors             []compileCheckIssue `json:"Errors"`
	Warnings           []compileCheckIssue `json:"Warnings"`
	CompiledAssemblies []string            `json:"CompiledAssemblies"`
	SkippedAssemblies  int                 `json:"SkippedAssemblies"`
	ResponseFileSet    string              `json:"ResponseFileSet"`
	ProjectRoot        string              `json:"ProjectRoot"`
	Message            string              `json:"Message"`
}

// tryHandleCompileCheckRequest answers `uloop compile-check` inside the dispatcher process, because
// the command never talks to the Editor and must work while Unity is closed.
func tryHandleCompileCheckRequest(
	ctx context.Context,
	args []string,
	startPath string,
	globalProjectPath string,
	stdout io.Writer,
	stderr io.Writer,
) (bool, int) {
	if len(args) == 0 || args[0] != clicore.CompileCheckCommandName {
		return false, 0
	}
	if clicore.ContainsHelpRequest(args[1:]) {
		printCompileCheckHelp(stdout)
		return true, 0
	}

	options, err := parseCompileCheckOptions(args[1:], globalProjectPath)
	if err != nil {
		clierrors.WriteClassifiedError(
			stderr, err, clierrors.ErrorContext{Command: clicore.CompileCheckCommandName})
		return true, 1
	}

	return true, runCompileCheck(ctx, options, startPath, stdout, stderr)
}

// runCompileCheck resolves the project and its Editor, then prints what the offline compile found.
func runCompileCheck(
	ctx context.Context,
	options compileCheckOptions,
	startPath string,
	stdout io.Writer,
	stderr io.Writer,
) int {
	projectRoot, err := resolveCompileCheckProjectRoot(startPath, options)
	if err != nil {
		clierrors.WriteClassifiedError(
			stderr, err, clierrors.ErrorContext{Command: clicore.CompileCheckCommandName})
		return 1
	}

	editorPath, err := resolveCompileCheckEditorPath(projectRoot, options)
	if err != nil {
		clierrors.WriteClassifiedError(stderr, err, clierrors.ErrorContext{
			Command: clicore.CompileCheckCommandName, ProjectRoot: projectRoot,
		})
		return 1
	}

	result, err := compilecheck.Run(ctx, compilecheck.Options{
		ProjectRoot:          projectRoot,
		EditorExecutablePath: editorPath,
		All:                  options.all,
	})
	if err != nil {
		clierrors.WriteClassifiedError(stderr, err, clierrors.ErrorContext{
			Command: clicore.CompileCheckCommandName, ProjectRoot: projectRoot,
		})
		return 1
	}

	response := buildCompileCheckResponse(result, projectRoot)
	encoded, err := json.Marshal(response)
	if err != nil {
		clierrors.WriteClassifiedError(stderr, err, clierrors.ErrorContext{
			Command: clicore.CompileCheckCommandName, ProjectRoot: projectRoot,
		})
		return 1
	}
	clicore.WriteJSON(stdout, encoded)

	if response.ErrorCount > 0 {
		return 1
	}

	return 0
}

// resolveCompileCheckProjectRoot finds the project the same way `launch` does, without connecting.
func resolveCompileCheckProjectRoot(startPath string, options compileCheckOptions) (string, error) {
	if options.projectPath != "" {
		return project.ResolveExplicitProjectRoot(options.projectPath)
	}

	return project.FindUnityProjectRootWithin(startPath, options.maxDepth)
}

// resolveCompileCheckEditorPath finds the Editor whose bundled compiler this run has to use.
func resolveCompileCheckEditorPath(projectRoot string, options compileCheckOptions) (string, error) {
	version := options.editorVersion
	if version == "" {
		readVersion, err := readUnityEditorVersion(projectRoot)
		if err != nil {
			return "", err
		}
		version = readVersion
	}

	return resolveUnityExecutablePath(version)
}

// buildCompileCheckResponse turns one run's results into the JSON envelope, split out so the
// mapping can be tested without an Editor.
func buildCompileCheckResponse(result compilecheck.Result, projectRoot string) compileCheckResponse {
	response := compileCheckResponse{
		Errors:             make([]compileCheckIssue, 0),
		Warnings:           make([]compileCheckIssue, 0),
		CompiledAssemblies: make([]string, 0, len(result.Units)),
		SkippedAssemblies:  result.Skipped,
		ResponseFileSet:    filepath.Base(result.DagDir),
		ProjectRoot:        projectRoot,
	}

	for _, unit := range result.Units {
		response.CompiledAssemblies = append(response.CompiledAssemblies, unit.Assembly)
		for _, diagnostic := range unit.Diagnostics {
			issue := newCompileCheckIssue(unit.Assembly, diagnostic)
			if diagnostic.Severity == "error" {
				response.Errors = append(response.Errors, issue)
				continue
			}
			response.Warnings = append(response.Warnings, issue)
		}
		// Why a failure with no error diagnostic still becomes an error: the assembly did not
		// compile, and reporting success for it would hide a broken build behind a clean envelope.
		if unit.RawOutput != "" {
			response.Errors = append(response.Errors, compileCheckIssue{
				Message: unit.RawOutput, Assembly: unit.Assembly,
			})
		}
	}

	response.ErrorCount = len(response.Errors)
	response.WarningCount = len(response.Warnings)
	response.Success = response.ErrorCount == 0
	response.Message = compileCheckMessage(response)

	return response
}

// newCompileCheckIssue shapes one diagnostic the way `uloop compile` shapes its own.
func newCompileCheckIssue(assembly string, diagnostic compilecheck.Diagnostic) compileCheckIssue {
	return compileCheckIssue{
		Message:  diagnostic.Code + ": " + diagnostic.Message,
		Code:     diagnostic.Code,
		File:     diagnostic.File,
		Line:     diagnostic.Line,
		Column:   diagnostic.Column,
		Assembly: assembly,
	}
}

// compileCheckMessage states what the run did in one line.
func compileCheckMessage(response compileCheckResponse) string {
	count := len(response.CompiledAssemblies)
	if count == 0 {
		return compileCheckNoChangeMessage
	}
	if response.ErrorCount == 0 {
		return fmt.Sprintf("Compiled %d assemblies with 0 errors.", count)
	}

	return fmt.Sprintf("Compiled %d assemblies with %d errors and %d warnings.",
		count, response.ErrorCount, response.WarningCount)
}

// printCompileCheckHelp documents the command and the build it replays.
func printCompileCheckHelp(stdout io.Writer) {
	clicore.WriteLine(stdout, "Usage:")
	clicore.WriteLine(stdout, "  uloop compile-check [options]")
	clicore.WriteLine(stdout, "")
	clicore.WriteLine(stdout, "Options:")
	clicore.WriteLine(stdout, "      --all                Compile every assembly instead of only the changed ones")
	clicore.WriteLine(stdout, "      --editor-version <version>")
	clicore.WriteLine(stdout, "                           Use this Unity Editor version instead of ProjectVersion.txt")
	clicore.WriteLine(stdout, "      --max-depth <n>      Max directory depth when auto-searching for the project (default: 3, -1 = unlimited)")
	clicore.WriteLine(stdout, "      --project-path <path>")
	clicore.WriteLine(stdout, "                           Compile this project instead of searching from the working directory")
	clicore.WriteLine(stdout, "")
	clicore.WriteLine(stdout, "Compiles without contacting the Unity Editor. Uses the response files Unity wrote on its")
	clicore.WriteLine(stdout, "last build; new asmdefs or define changes require a Unity build first.")
	clicore.WriteLine(stdout, "")
	printGlobalOptionsHelp(stdout)
	printSkillGuidanceHelp(clicore.CompileCheckCommandName, stdout)
}

// parseCompileCheckOptions reads the command line, rejecting anything compile-check does not accept.
func parseCompileCheckOptions(args []string, globalProjectPath string) (compileCheckOptions, error) {
	options := compileCheckOptions{
		projectPath: globalProjectPath,
		maxDepth:    defaultProjectSearchDepth,
	}

	for index := 0; index < len(args); index++ {
		nextIndex, err := applyCompileCheckOption(&options, args, index)
		if err != nil {
			return compileCheckOptions{}, err
		}
		index = nextIndex
	}

	return options, nil
}

// applyCompileCheckOption reads one argument into the options.
func applyCompileCheckOption(options *compileCheckOptions, args []string, index int) (int, error) {
	arg := args[index]
	switch {
	case arg == compileCheckAllFlag:
		options.all = true
		return index, nil
	case isCompileCheckKeyedOption(arg, compileCheckEditorFlag):
		return applyCompileCheckEditorVersion(options, args, index)
	case isCompileCheckKeyedOption(arg, compileCheckMaxDepthFlag):
		return applyCompileCheckMaxDepth(options, args, index)
	default:
		return index, unknownCompileCheckOptionError(arg)
	}
}

// isCompileCheckKeyedOption reports whether an argument is one option, spaced or in its equals form.
func isCompileCheckKeyedOption(arg string, option string) bool {
	return arg == option || strings.HasPrefix(arg, option+"=")
}

// applyCompileCheckEditorVersion reads the Editor version whose compiler this run must use.
func applyCompileCheckEditorVersion(
	options *compileCheckOptions, args []string, index int,
) (int, error) {
	value, consumed, err := readLaunchOptionValue(args[index], args, index)
	if err != nil {
		return index, err
	}
	options.editorVersion = value

	return nextLaunchOptionIndex(index, consumed), nil
}

// applyCompileCheckMaxDepth reads how far the project search may descend.
func applyCompileCheckMaxDepth(options *compileCheckOptions, args []string, index int) (int, error) {
	value, consumed, err := readLaunchOptionValue(args[index], args, index)
	if err != nil {
		return index, err
	}
	maxDepth, convertErr := strconv.Atoi(value)
	if convertErr != nil || maxDepth < -1 {
		return index, clierrors.InvalidValueArgumentError(compileCheckMaxDepthFlag, value, "integer >= -1")
	}
	options.maxDepth = maxDepth

	return nextLaunchOptionIndex(index, consumed), nil
}

// unknownCompileCheckOptionError rejects an argument compile-check has no meaning for.
func unknownCompileCheckOptionError(arg string) error {
	return &clierrors.ArgumentError{
		Message:     "Unknown compile-check option: " + arg,
		Option:      arg,
		Command:     clicore.CompileCheckCommandName,
		NextActions: []string{"Run `uloop compile-check --help` to inspect supported options."},
	}
}
