package dispatcher

import (
	"bytes"
	"context"
	"encoding/json"
	"io"
	"os"
	"path/filepath"
	"runtime"
	"strings"
	"testing"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	"github.com/hatayama/unity-cli-loop/dispatcher/internal/nativepath"
)

func writeDispatcherTestScript(t *testing.T, body string) string {
	t.Helper()
	if runtime.GOOS == "windows" {
		t.Skip("POSIX shell scripts are not executable on Windows.")
	}
	scriptPath := filepath.Join(t.TempDir(), "fake-cli.sh")
	if err := os.WriteFile(scriptPath, []byte("#!/bin/sh\n"+body+"\n"), 0o755); err != nil {
		t.Fatalf("failed to write fake CLI script: %v", err)
	}
	return scriptPath
}

func decodeDispatcherTestEnvelope(t *testing.T, stderr string) map[string]any {
	t.Helper()
	var envelope map[string]any
	if err := json.Unmarshal([]byte(strings.TrimSpace(stderr)), &envelope); err != nil {
		t.Fatalf("stderr is not a JSON error envelope: %v\n%s", err, stderr)
	}
	return envelope
}

func TestRunRealCLICommandForwardsArgsAndOutput(t *testing.T) {
	// Verifies the resolved runner receives the original arguments and its stdout reaches the caller.
	scriptPath := writeDispatcherTestScript(t, `echo "args:$*"`)

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runRealCLICommand(context.Background(), scriptPath, []string{"compile", "--force-recompile"}, &stdout, &stderr)

	if code != 0 {
		t.Fatalf("exit code mismatch: %d stderr=%s", code, stderr.String())
	}
	if stdout.String() != "args:compile --force-recompile\n" {
		t.Fatalf("stdout mismatch: %q", stdout.String())
	}
}

func TestRunRealCLICommandReturnsRunnerExitCode(t *testing.T) {
	// Verifies a non-zero runner exit code is propagated as-is without a dispatcher error envelope.
	scriptPath := writeDispatcherTestScript(t, "exit 3")

	var stderr bytes.Buffer
	code := runRealCLICommand(context.Background(), scriptPath, nil, io.Discard, &stderr)

	if code != 3 {
		t.Fatalf("exit code mismatch: %d", code)
	}
	if stderr.String() != "" {
		t.Fatalf("dispatcher must not add its own error for a runner exit code: %s", stderr.String())
	}
}

func TestRunRealCLICommandReportsStartFailure(t *testing.T) {
	// Verifies a runner that cannot be started yields exit code 1 and an envelope naming the executable path.
	missingPath := filepath.Join(t.TempDir(), "missing-runner")

	var stderr bytes.Buffer
	code := runRealCLICommand(context.Background(), missingPath, nil, io.Discard, &stderr)

	if code != 1 {
		t.Fatalf("exit code mismatch: %d", code)
	}
	envelope := decodeDispatcherTestEnvelope(t, stderr.String())
	if !strings.Contains(stderr.String(), "Failed to run resolved uloop CLI") {
		t.Fatalf("missing start failure message: %s", stderr.String())
	}
	if !strings.Contains(stderr.String(), missingPath) {
		t.Fatalf("envelope must name the executable path: %v", envelope)
	}
}

func TestShouldKeepDispatcherProcessCommandForEmptyArgs(t *testing.T) {
	// Verifies an empty command line stays in the dispatcher process instead of probing for a v2 project.
	if !shouldKeepDispatcherProcessCommand(nil) {
		t.Fatal("empty args must stay in the dispatcher process")
	}
}

func TestResolveDispatcherProjectRootRejectsInvalidCompileCheckOptions(t *testing.T) {
	// Verifies compile-check option errors surface from project-root resolution before any project lookup.
	_, err := resolveDispatcherProjectRoot(t.TempDir(), "", []string{clicore.CompileCheckCommandName, "--no-such-option"})
	if err == nil {
		t.Fatal("expected an option error for an unknown compile-check option")
	}
}

func TestResolveDispatcherProjectRootResolvesCompileCheckProject(t *testing.T) {
	// Verifies compile-check resolves the explicit project path without requiring a running Editor.
	projectRoot := createDispatcherUnityProject(t)

	resolved, err := resolveDispatcherProjectRoot(t.TempDir(), projectRoot, []string{clicore.CompileCheckCommandName})
	if err != nil {
		t.Fatalf("resolveDispatcherProjectRoot failed: %v", err)
	}
	if resolved != projectRoot {
		t.Fatalf("project root mismatch: got %s want %s", resolved, projectRoot)
	}
}

func TestResolveDispatcherProjectRootRejectsInvalidLaunchOptions(t *testing.T) {
	// Verifies launch option errors surface from project-root resolution.
	_, err := resolveDispatcherProjectRoot(t.TempDir(), "", []string{clicore.LaunchCommandName, "--no-such-option"})
	if err == nil {
		t.Fatal("expected an option error for an unknown launch option")
	}
}

func TestRunDispatcherRejectsMalformedGlobalProjectPath(t *testing.T) {
	// Verifies a --project-path without a value fails before any routing and exits with code 1.
	t.Chdir(t.TempDir())

	var stderr bytes.Buffer
	code := runDispatcherWithDeps(context.Background(), []string{"compile", "--project-path"}, io.Discard, &stderr, defaultDispatcherRunDeps())

	if code != 1 {
		t.Fatalf("exit code mismatch: %d", code)
	}
	if !strings.Contains(stderr.String(), "--project-path") {
		t.Fatalf("error must name the malformed option: %s", stderr.String())
	}
}

func TestRunDispatcherReportsUnresolvableProjectForRunnerCommand(t *testing.T) {
	// Verifies a runner-owned command outside any Unity project fails with exit code 1 before resolving a runner.
	t.Chdir(t.TempDir())
	deps := defaultDispatcherRunDeps()
	deps.runRealCLI = func(context.Context, string, []string, io.Writer, io.Writer) int {
		t.Fatal("runner must not be executed when the project cannot be resolved")
		return 0
	}

	var stderr bytes.Buffer
	code := runDispatcherWithDeps(context.Background(), []string{"compile"}, io.Discard, &stderr, deps)

	if code != 1 {
		t.Fatalf("exit code mismatch: %d", code)
	}
	if stderr.String() == "" {
		t.Fatal("expected an error envelope on stderr")
	}
}

func TestRunDispatcherReportsRealCLIResolutionFailure(t *testing.T) {
	// Verifies an unusable ULOOP_PROJECT_RUNNER_PATH override is reported as a runner resolution error.
	projectRoot := createDispatcherUnityProject(t)
	writeDispatcherProjectPin(t, projectRoot, "3.0.0")
	t.Setenv(dispatcherDisableSelfUpdateEnvName, "1")
	t.Setenv(nativepath.ProjectRunnerPathEnvName, filepath.Join(t.TempDir(), "missing-runner"))
	t.Chdir(projectRoot)
	deps := defaultDispatcherRunDeps()
	deps.runRealCLI = func(context.Context, string, []string, io.Writer, io.Writer) int {
		t.Fatal("runner must not be executed when it cannot be resolved")
		return 0
	}

	var stderr bytes.Buffer
	code := runDispatcherWithDeps(context.Background(), []string{"compile"}, io.Discard, &stderr, deps)

	if code != 1 {
		t.Fatalf("exit code mismatch: %d", code)
	}
	if !strings.Contains(stderr.String(), "no executable file exists there") {
		t.Fatalf("expected the override failure in the envelope: %s", stderr.String())
	}
}

func writeDispatcherExecutable(t *testing.T, filePath string, content string) {
	t.Helper()
	if err := os.WriteFile(filePath, []byte(content), 0o755); err != nil {
		t.Fatalf("failed to write %s: %v", filePath, err)
	}
}
