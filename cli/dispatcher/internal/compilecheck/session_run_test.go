package compilecheck

import (
	"context"
	"errors"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
	"testing"
)

const defaultRunHelperEnv = "ULOOP_COMPILECHECK_DEFAULT_RUN_HELPER"

// writeFakeEditor lays out a minimal macOS-shaped Editor with a bundled compiler and returns its
// executable path. Nothing in it can run; it only has to resolve.
func writeFakeEditor(t *testing.T) string {
	t.Helper()
	contentsPath := filepath.Join(t.TempDir(), "Unity.app", "Contents")
	writeCompilerFiles(t, filepath.Join(contentsPath, dotNetSdkRoslynDirectoryName), "{}")
	writeSharedRuntime(t, filepath.Join(contentsPath, netCoreRuntimeDirectoryName), "6.0.21")
	return filepath.Join(contentsPath, "MacOS", "Unity")
}

// Verifies a run whose plan has nothing to compile reports every assembly as skipped and the dag
// it read, without starting the compiler.
func TestRunReportsAnUpToDateProjectAsSkipped(t *testing.T) {
	projectRoot := newPlanProject(t)

	result, err := Run(context.Background(), Options{ProjectRoot: projectRoot, EditorExecutablePath: writeFakeEditor(t)})
	if err != nil {
		t.Fatalf("Run failed: %v", err)
	}

	if result.DagDir != filepath.FromSlash(planDagDirectory) || result.Skipped != 3 || len(result.Units) != 0 || len(result.Blocked) != 0 {
		t.Fatalf("unexpected result: %#v", result)
	}
}

// Verifies a run stops with the compiler-layout error when the Editor path does not resolve.
func TestRunReportsAnUnresolvableEditor(t *testing.T) {
	projectRoot := newPlanProject(t)

	_, err := Run(context.Background(), Options{ProjectRoot: projectRoot, EditorExecutablePath: filepath.Join(t.TempDir(), "Unity")})

	if err == nil || !strings.HasPrefix(err.Error(), "unrecognized Unity Editor install layout for ") {
		t.Fatalf("err = %v", err)
	}
}

// Verifies a run stops with the response-file error when the plan cannot be built.
func TestRunReportsAPlanThatCannotBeBuilt(t *testing.T) {
	projectRoot := newDagProject(t, "aaaa.dag")
	writeFileAt(t, filepath.Join(projectRoot, planDagDirectory, "Broken.rsp"), `"Assets/Broken.cs"`)

	_, err := Run(context.Background(), Options{ProjectRoot: projectRoot, EditorExecutablePath: writeFakeEditor(t)})

	if err == nil || !strings.Contains(err.Error(), "has no -out flag") {
		t.Fatalf("err = %v", err)
	}
}

// Verifies a run on a project Unity never built reports the missing build before anything else.
func TestRunReportsAProjectWithoutABuild(t *testing.T) {
	_, err := Run(context.Background(), Options{ProjectRoot: t.TempDir()})

	var required UnityBuildRequiredError
	if !errors.As(err, &required) {
		t.Fatalf("err = %v, want UnityBuildRequiredError", err)
	}
}

// Verifies the default compiler runner returns the process output and its non-zero exit code
// together with the exit error.
func TestDefaultRunReportsOutputAndExitCode(t *testing.T) {
	cmd := exec.Command(os.Args[0], "-test.run=^TestDefaultRunHelperProcess$")
	cmd.Env = append(os.Environ(), defaultRunHelperEnv+"=1")

	stdout, stderr, exitCode, err := defaultRun(context.Background(), cmd)

	var exitError *exec.ExitError
	if !errors.As(err, &exitError) || exitCode != 3 {
		t.Fatalf("exitCode=%d err=%v, want exit code 3 with an ExitError", exitCode, err)
	}
	if !strings.Contains(stdout, "helper stdout") || !strings.Contains(stderr, "helper stderr") {
		t.Fatalf("stdout=%q stderr=%q", stdout, stderr)
	}
}

// TestDefaultRunHelperProcess is not a test: it is the process TestDefaultRunReportsOutputAndExitCode
// starts, and it writes to both streams before exiting with code 3.
func TestDefaultRunHelperProcess(t *testing.T) {
	if os.Getenv(defaultRunHelperEnv) == "" {
		t.Skip("helper process for TestDefaultRunReportsOutputAndExitCode")
	}
	_, _ = os.Stdout.WriteString("helper stdout\n")
	_, _ = os.Stderr.WriteString("helper stderr\n")
	os.Exit(3)
}

// Verifies the artifacts scan ignores entries that are not dag directories and reports a build as
// missing when none remain.
func TestResolveActiveDagDirIgnoresEntriesThatAreNotDagDirectories(t *testing.T) {
	projectRoot := newDagProject(t, "other")
	artifactsDirectory := filepath.Join(projectRoot, libraryDirectoryName, beeDirectoryName, beeArtifactsDirectoryName)
	writeFileAt(t, filepath.Join(artifactsDirectory, "file.dag"), "")

	_, err := ResolveActiveDagDir(projectRoot)

	var required UnityBuildRequiredError
	if !errors.As(err, &required) || !strings.Contains(required.Reason, "no Bee build artifacts found in") {
		t.Fatalf("err = %v", err)
	}
}

// Verifies a Bee log or a script debug setting that fails to decode is ignored, leaving an
// ambiguous project unresolved instead of guessed.
func TestResolveActiveDagDirIgnoresMalformedLogAndSetting(t *testing.T) {
	t.Run("Bee log", func(t *testing.T) {
		projectRoot := newDagProject(t, "aaaa.dag", "aaaaDbg.dag")
		// Why the duplicate key: the decoder keeps the first dagFile when the second one fails, so
		// only the decode-error check keeps the half-read log from deciding the dag.
		writeFileAt(t, filepath.Join(projectRoot, libraryDirectoryName, beeDirectoryName, tundraLogFileName),
			`{"dagFile":"Library/Bee/aaaa.dag","dagFile":5}`)

		assertDagDirUnresolved(t, projectRoot)
	})
	t.Run("script debug setting", func(t *testing.T) {
		projectRoot := newDagProject(t, "aaaa.dag", "aaaaDbg.dag")
		writeFileAt(t, filepath.Join(projectRoot, libraryDirectoryName, editorScriptingSettingsFile),
			`{"m_ScriptDebugInfoEnabled":{"m_Value":false},"m_ScriptDebugInfoEnabled":5}`)

		assertDagDirUnresolved(t, projectRoot)
	})
}

// assertDagDirUnresolved requires the dag resolution to refuse the project as ambiguous.
func assertDagDirUnresolved(t *testing.T, projectRoot string) {
	t.Helper()
	_, err := ResolveActiveDagDir(projectRoot)

	var required UnityBuildRequiredError
	if !errors.As(err, &required) || !strings.Contains(required.Reason, "cannot tell which Bee build to check in") {
		t.Fatalf("err = %v", err)
	}
}
