package compilecheck

import (
	"context"
	"errors"
	"os"
	"os/exec"
	"path/filepath"
	"testing"
	"time"
)

// compileSecondUnitRunning compiles the second unit of the compiler plan with the given runner and
// reports whether its generator output directory is there afterwards.
func compileSecondUnitRunning(t *testing.T, run CommandRunner) (UnitResult, error, bool) {
	t.Helper()
	projectRoot := t.TempDir()
	plan := newCompilerPlan()
	compiler := Compiler{
		Paths:       EditorCompilerPaths{DotnetHostPath: "/dotnet", CompilerDllPath: "/csc.dll"},
		ProjectRoot: projectRoot,
		Timeout:     time.Minute,
		Run:         run,
	}
	writeFileAt(t, filepath.Join(projectRoot, plan.OutputDir, "A.ref.dll"), "")

	result, err := compiler.CompileUnit(context.Background(), plan, plan.Units[1])
	_, statErr := os.Stat(filepath.Join(projectRoot, compiler.records(plan).GeneratedDirectory("B")))

	return result, err, statErr == nil
}

// Verifies a compile whose compiler never ran leaves no generator output directory, so a later run
// does not take it for the saved output of a finished compile.
func TestCompileUnitLeavesNoGeneratorOutputWhenTheCompilerDoesNotRun(t *testing.T) {
	failToStart := func(context.Context, *exec.Cmd) (string, string, int, error) {
		return "", "", -1, errors.New("cannot start")
	}

	_, err, kept := compileSecondUnitRunning(t, failToStart)

	if err == nil {
		t.Fatal("expected the failure to start the compiler to be reported")
	}
	if kept {
		t.Error("expected the generator output directory to be removed")
	}
}

// Verifies a compile that failed without an error diagnostic leaves no generator output directory
// either, while one that finished with compile errors keeps it.
func TestCompileUnitKeepsGeneratorOutputOnlyWhenTheCompilerFinished(t *testing.T) {
	var captured exec.Cmd
	result, err, kept := compileSecondUnitRunning(t, stubRunner("Unhandled exception.", 1, &captured))
	if err != nil || result.RawOutput == "" {
		t.Fatalf("expected a raw-output failure, got result %+v and error %v", result, err)
	}
	if kept {
		t.Error("expected a compile that did not finish to remove its generator output directory")
	}

	_, err, kept = compileSecondUnitRunning(t,
		stubRunner("Assets/B/B.cs(1,1): error CS0029: Cannot convert", 1, &captured))
	if err != nil {
		t.Fatalf("expected compile errors as diagnostics, got error %v", err)
	}
	if !kept {
		t.Error("expected a compile that finished with errors to keep its generator output directory")
	}
}
