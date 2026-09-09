package dispatcher

import (
	"bytes"
	"context"
	"strings"
	"testing"

	"github.com/hatayama/unity-cli-loop/dispatcher/internal/compilecheck"
)

// Verifies that compile-check defaults to the changed-assembly scope and the global project path.
func TestParseCompileCheckOptionsDefaultsToTheChangedScope(t *testing.T) {
	options, err := parseCompileCheckOptions(nil, "/projects/sample")
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if options.all {
		t.Fatalf("expected the changed scope, got --all")
	}
	if options.projectPath != "/projects/sample" {
		t.Fatalf("expected the global project path, got %q", options.projectPath)
	}
	if options.maxDepth != defaultProjectSearchDepth {
		t.Fatalf("expected the default search depth, got %d", options.maxDepth)
	}
}

// Verifies that every supported option is read, in both the spaced and the equals form.
func TestParseCompileCheckOptionsReadsEverySupportedOption(t *testing.T) {
	options, err := parseCompileCheckOptions(
		[]string{"--all", "--editor-version", "6000.0.1f1", "--max-depth=-1"}, "")
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if !options.all {
		t.Fatalf("expected --all to be read")
	}
	if options.editorVersion != "6000.0.1f1" {
		t.Fatalf("expected the requested Editor version, got %q", options.editorVersion)
	}
	if options.maxDepth != -1 {
		t.Fatalf("expected an unlimited search depth, got %d", options.maxDepth)
	}
}

// Verifies that an option compile-check does not accept is rejected instead of ignored.
func TestParseCompileCheckOptionsRejectsAnUnknownOption(t *testing.T) {
	_, err := parseCompileCheckOptions([]string{"--restart"}, "")
	if err == nil {
		t.Fatalf("expected an unknown option to be rejected")
	}
	if !strings.Contains(err.Error(), "--restart") {
		t.Fatalf("expected the rejected option to be named, got %q", err.Error())
	}
}

// Verifies that a depth that is not an integer at or above -1 is rejected.
func TestParseCompileCheckOptionsRejectsAnInvalidMaxDepth(t *testing.T) {
	_, err := parseCompileCheckOptions([]string{"--max-depth", "-2"}, "")
	if err == nil {
		t.Fatalf("expected an out-of-range depth to be rejected")
	}
}

// Verifies that another command's arguments are left for the handler that owns them.
func TestTryHandleCompileCheckRequestIgnoresAnotherCommand(t *testing.T) {
	stdout := bytes.Buffer{}
	stderr := bytes.Buffer{}
	handled, exitCode := tryHandleCompileCheckRequest(
		context.Background(), []string{"launch"}, "/start", "", &stdout, &stderr)
	if handled || exitCode != 0 {
		t.Fatalf("expected the request to pass through, got handled=%v exitCode=%d", handled, exitCode)
	}
	if stdout.Len() != 0 || stderr.Len() != 0 {
		t.Fatalf("expected no output, got %q %q", stdout.String(), stderr.String())
	}
}

// Verifies that --help documents the command without compiling anything.
func TestTryHandleCompileCheckRequestPrintsHelp(t *testing.T) {
	stdout := bytes.Buffer{}
	stderr := bytes.Buffer{}
	handled, exitCode := tryHandleCompileCheckRequest(
		context.Background(), []string{"compile-check", "--help"}, "/start", "", &stdout, &stderr)
	if !handled || exitCode != 0 {
		t.Fatalf("expected help to be handled, got handled=%v exitCode=%d", handled, exitCode)
	}
	for _, expected := range []string{"uloop compile-check", "--all", "without contacting the Unity Editor"} {
		if !strings.Contains(stdout.String(), expected) {
			t.Fatalf("expected the help to mention %q, got %q", expected, stdout.String())
		}
	}
}

// Verifies that a clean run reports success and names every assembly it compiled.
func TestBuildCompileCheckResponseReportsACleanRun(t *testing.T) {
	response := buildCompileCheckResponse(compilecheck.Result{
		DagDir: "Library/Bee/artifacts/1234Dbg.dag",
		Units: []compilecheck.UnitResult{
			{Assembly: "A", Succeeded: true},
			{Assembly: "B", Succeeded: true},
		},
		Skipped: 3,
	}, "/projects/sample")

	if !response.Success || response.ErrorCount != 0 {
		t.Fatalf("expected a clean run, got %+v", response)
	}
	if len(response.Errors) != 0 || len(response.Warnings) != 0 {
		t.Fatalf("expected empty diagnostic lists, got %+v", response)
	}
	if strings.Join(response.CompiledAssemblies, ",") != "A,B" {
		t.Fatalf("expected both assemblies, got %v", response.CompiledAssemblies)
	}
	if response.ResponseFileSet != "1234Dbg.dag" {
		t.Fatalf("expected the dag name, got %q", response.ResponseFileSet)
	}
	if response.SkippedAssemblies != 3 {
		t.Fatalf("expected the skipped count to carry over, got %d", response.SkippedAssemblies)
	}
}

// Verifies that errors and warnings are split, counted, and attributed to their assembly.
func TestBuildCompileCheckResponseSplitsErrorsFromWarnings(t *testing.T) {
	response := buildCompileCheckResponse(compilecheck.Result{
		DagDir: "Library/Bee/artifacts/1234.dag",
		Units: []compilecheck.UnitResult{{
			Assembly: "A",
			Diagnostics: []compilecheck.Diagnostic{
				{Severity: "error", Code: "CS0029", Message: "cannot convert", File: "Assets/A.cs", Line: 4, Column: 9},
				{Severity: "warning", Code: "CS0168", Message: "unused variable", File: "Assets/A.cs", Line: 7, Column: 2},
			},
		}},
	}, "/projects/sample")

	if response.Success || response.ErrorCount != 1 || response.WarningCount != 1 {
		t.Fatalf("expected one error and one warning, got %+v", response)
	}
	issue := response.Errors[0]
	if issue.Message != "CS0029: cannot convert" || issue.Assembly != "A" || issue.Line != 4 || issue.Column != 9 {
		t.Fatalf("unexpected error issue: %+v", issue)
	}
	if response.Warnings[0].Code != "CS0168" {
		t.Fatalf("unexpected warning issue: %+v", response.Warnings[0])
	}
}

// Verifies that a compiler failure with no error diagnostic still fails the run.
func TestBuildCompileCheckResponseFailsOnUnexplainedCompilerOutput(t *testing.T) {
	response := buildCompileCheckResponse(compilecheck.Result{
		DagDir: "Library/Bee/artifacts/1234.dag",
		Units: []compilecheck.UnitResult{
			{Assembly: "A", RawOutput: "csc exited with code 1 without reporting an error diagnostic:"},
		},
	}, "/projects/sample")

	if response.Success || response.ErrorCount != 1 {
		t.Fatalf("expected the unexplained failure to fail the run, got %+v", response)
	}
	if response.Errors[0].Assembly != "A" {
		t.Fatalf("expected the failure to name its assembly, got %+v", response.Errors[0])
	}
}

// Verifies that a run with nothing to compile says so instead of reporting an empty success.
func TestBuildCompileCheckResponseReportsAnUnchangedProject(t *testing.T) {
	response := buildCompileCheckResponse(compilecheck.Result{
		DagDir: "Library/Bee/artifacts/1234.dag", Skipped: 12,
	}, "/projects/sample")

	if !response.Success {
		t.Fatalf("expected an unchanged project to succeed, got %+v", response)
	}
	if response.Message != compileCheckNoChangeMessage {
		t.Fatalf("expected the no-change message, got %q", response.Message)
	}
}
