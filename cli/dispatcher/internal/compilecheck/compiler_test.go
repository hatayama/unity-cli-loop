package compilecheck

import (
	"context"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
	"testing"
	"time"
)

// newCompilerPlan builds a two-unit plan whose second unit references the first and one skipped one.
func newCompilerPlan() BuildPlan {
	return BuildPlan{
		DagDir:    planDagDirectory,
		OutputDir: filepath.Join("Library", "uloop", "compile-check", "aaaa.dag"),
		Units: []CompileUnit{
			{Assembly: ResponseFile{AssemblyName: "A"}},
			{
				Assembly: ResponseFile{
					AssemblyName:   "B",
					OutputPath:     filepath.Join(planDagDirectory, "B.dll"),
					RefOutputPath:  filepath.Join(planDagDirectory, "B.ref.dll"),
					Defines:        []string{"UNITY_EDITOR"},
					References:     []string{filepath.Join(planDagDirectory, "A.ref.dll"), filepath.Join(planDagDirectory, "Skipped.ref.dll"), filepath.FromSlash("/Editor/UnityEngine.dll")},
					Analyzers:      []string{filepath.FromSlash("/Editor/Unity.SourceGenerators.dll")},
					AdditionalFile: filepath.Join(planDagDirectory, "B.UnityAdditionalFile.txt"),
					OtherFlags:     []string{"-target:library", "-langversion:9.0", "/deterministic"},
				},
				Sources: []string{filepath.Join("Assets", "B", "New.cs")},
				Reason:  changeReasonSourceNewer,
			},
		},
	}
}

// stubRunner returns a CommandRunner that reports fixed output and captures the command it was given.
func stubRunner(stdout string, exitCode int, captured *exec.Cmd) CommandRunner {
	return func(_ context.Context, cmd *exec.Cmd) (string, string, int, error) {
		*captured = *cmd
		if exitCode == 0 {
			return stdout, "", 0, nil
		}

		return stdout, "", exitCode, &exec.ExitError{}
	}
}

// compileSecondUnit runs the plan's referencing unit through a stubbed compiler.
func compileSecondUnit(t *testing.T, stdout string, exitCode int) (string, UnitResult) {
	t.Helper()
	projectRoot := t.TempDir()
	plan := newCompilerPlan()
	captured := exec.Cmd{}
	compiler := Compiler{
		Paths:       EditorCompilerPaths{DotnetHostPath: "/dotnet", CompilerDllPath: "/csc.dll"},
		ProjectRoot: projectRoot,
		Timeout:     time.Minute,
		Run:         stubRunner(stdout, exitCode, &captured),
	}

	result, err := compiler.CompileUnit(context.Background(), plan, plan.Units[1])
	if err != nil {
		t.Fatalf("expected the unit to compile, got error: %v", err)
	}

	written, err := os.ReadFile(filepath.Join(projectRoot, plan.OutputDir, "B.rsp"))
	if err != nil {
		t.Fatalf("failed to read the rewritten response file: %v", err)
	}

	return string(written), result
}

// Verifies positioned diagnostics are read out of csc output and unpositioned lines are ignored.
func TestParseDiagnosticsReadsPositionedLinesOnly(t *testing.T) {
	output := strings.Join([]string{
		`Assets/Foo/A.cs(12,9): error CS0029: Cannot implicitly convert type 'string' to 'int'`,
		`Assets/Foo/B.cs(3,1): warning CS0168: The variable 'x' is declared but never used`,
		`error CS0006: Metadata file 'Missing.dll' could not be found`,
		`Microsoft (R) Visual C# Compiler version 4.1.0`,
	}, "\n")

	diagnostics := ParseDiagnostics(output)

	if len(diagnostics) != 2 {
		t.Fatalf("diagnostics = %+v, want 2", diagnostics)
	}
	first := diagnostics[0]
	if first.Severity != severityError || first.Code != "CS0029" || first.Line != 12 || first.Column != 9 {
		t.Errorf("first diagnostic = %+v", first)
	}
	if first.File != "Assets/Foo/A.cs" {
		t.Errorf("file = %s, want the path csc printed", first.File)
	}
	if diagnostics[1].Severity != severityWarning {
		t.Errorf("second diagnostic = %+v, want a warning", diagnostics[1])
	}
}

// Verifies the same diagnostic printed twice is reported once.
func TestParseDiagnosticsDropsRepeatedDiagnostics(t *testing.T) {
	line := `Assets/Foo/A.cs(12,9): error CS0029: Cannot implicitly convert type 'string' to 'int'`

	diagnostics := ParseDiagnostics(line + "\n" + line)

	if len(diagnostics) != 1 {
		t.Errorf("diagnostics = %+v, want 1", diagnostics)
	}
}

// Verifies the rewritten response file writes into the check's own directory and repoints references.
func TestCompileUnitRewritesTheResponseFile(t *testing.T) {
	written, _ := compileSecondUnit(t, "", 0)

	outputDir := filepath.Join("Library", "uloop", "compile-check", "aaaa.dag")
	expected := []string{
		`-out:"` + filepath.Join(outputDir, "B.dll") + `"`,
		`-refout:"` + filepath.Join(outputDir, "B.ref.dll") + `"`,
		`-r:"` + filepath.Join(outputDir, "A.ref.dll") + `"`,
		`-r:"` + filepath.Join(planDagDirectory, "Skipped.ref.dll") + `"`,
		`-r:"` + filepath.FromSlash("/Editor/UnityEngine.dll") + `"`,
		`-define:UNITY_EDITOR`,
		`"` + filepath.Join("Assets", "B", "New.cs") + `"`,
		`-langversion:9.0`,
		`/deterministic`,
		`/additionalfile:"` + filepath.Join(planDagDirectory, "B.UnityAdditionalFile.txt") + `"`,
	}
	for _, line := range expected {
		if !strings.Contains(written, line+"\n") && !strings.HasSuffix(written, line) {
			t.Errorf("the rewritten response file is missing %q\ngot:\n%s", line, written)
		}
	}
	if strings.Count(written, "-target:library") != 1 {
		t.Errorf("-target:library should appear once, got:\n%s", written)
	}
	if strings.Contains(written, filepath.Join(planDagDirectory, "B.dll")) {
		t.Error("the rewritten response file must not write into the Bee artifacts directory")
	}
}

// Verifies a failing compile with an error diagnostic is reported as diagnostics, not as an error.
func TestCompileUnitReportsCompileErrorsAsDiagnostics(t *testing.T) {
	stdout := `Assets/B/New.cs(1,1): error CS0029: Cannot implicitly convert type 'string' to 'int'`

	_, result := compileSecondUnit(t, stdout, 1)

	if result.Succeeded {
		t.Error("a non-zero exit must not be reported as success")
	}
	if len(result.Diagnostics) != 1 {
		t.Errorf("diagnostics = %+v, want 1", result.Diagnostics)
	}
	if result.RawOutput != "" {
		t.Errorf("raw output should stay empty when csc explained itself, got: %s", result.RawOutput)
	}
}

// Verifies a failing compile that reported nothing keeps the compiler output so the run is not silent.
func TestCompileUnitKeepsOutputWhenTheCompilerFailsWithoutADiagnostic(t *testing.T) {
	_, result := compileSecondUnit(t, "Unhandled exception. System.IO.FileNotFoundException", 1)

	if result.RawOutput == "" {
		t.Fatal("expected the compiler output to be kept")
	}
	if !strings.Contains(result.RawOutput, "FileNotFoundException") {
		t.Errorf("raw output should carry the compiler message, got: %s", result.RawOutput)
	}
}

// Verifies a failing compile that reported only warnings is still surfaced as an infrastructure failure.
func TestCompileUnitKeepsOutputWhenOnlyWarningsAccompanyAFailure(t *testing.T) {
	stdout := `Assets/B/New.cs(3,1): warning CS0168: The variable 'x' is declared but never used`

	_, result := compileSecondUnit(t, stdout, 1)

	if result.RawOutput == "" {
		t.Fatal("a failure with only warnings must still be reported")
	}
	if len(result.Diagnostics) != 1 || result.Diagnostics[0].Severity != severityWarning {
		t.Errorf("diagnostics = %+v, want the warning to survive", result.Diagnostics)
	}
}
