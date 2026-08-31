package dispatcher

import (
	"bytes"
	"context"
	"encoding/json"
	"io"
	"testing"

	"github.com/hatayama/unity-cli-loop/common/clicontract"
	"github.com/hatayama/unity-cli-loop/dispatcher/internal/nativepath"
)

func alwaysTerminalStdout() func(io.Writer) bool {
	return func(io.Writer) bool { return true }
}

func TestRunDispatcherStillDelegatesBareVersionForV2ProjectWhenStdoutIsNotTerminal(t *testing.T) {
	// Verifies the machine-facing contract is unchanged: a redirected --version in a V2 project is still delegated.
	projectRoot := createDispatcherUnityProject(t)
	writeV2PackageManifest(t, projectRoot)
	writeV2PackageCachePackageJSON(t, projectRoot, "abc123", "2.2.0")
	t.Chdir(projectRoot)

	deps := defaultDispatcherRunDeps()
	delegated := false
	deps.runV2CLI = func(context.Context, string, []string, io.Writer, io.Writer) (int, error) {
		delegated = true
		return 0, nil
	}

	var stdout bytes.Buffer
	code := runDispatcherWithDeps(context.Background(), []string{"--version"}, &stdout, io.Discard, deps)

	if code != 0 || !delegated {
		t.Fatalf("V2 version was not delegated: code=%d delegated=%v", code, delegated)
	}
	if stdout.String() != "" {
		t.Fatalf("dispatcher must not write stdout for the delegated version request: %q", stdout.String())
	}
}

func TestRunDispatcherPrintsDispatcherVersionAndV2ContextWhenStdoutIsTerminal(t *testing.T) {
	// Verifies an interactive --version in a V2 project is answered locally with the dispatcher version and a V2 context line.
	projectRoot := createDispatcherUnityProject(t)
	writeV2PackageManifest(t, projectRoot)
	writeV2PackageCachePackageJSON(t, projectRoot, "abc123", "2.2.0")
	t.Chdir(projectRoot)

	deps := defaultDispatcherRunDeps()
	deps.stdoutIsTerminal = alwaysTerminalStdout()
	deps.runV2CLI = func(context.Context, string, []string, io.Writer, io.Writer) (int, error) {
		t.Fatal("interactive version request must not delegate to the V2 CLI")
		return 0, nil
	}

	var stdout bytes.Buffer
	code := runDispatcherWithDeps(context.Background(), []string{"--version"}, &stdout, io.Discard, deps)

	if code != 0 {
		t.Fatalf("exit code = %d, want 0", code)
	}
	want := dispatcherVersion + "\n" +
		"This Unity project uses the uloop V2 package 2.2.0, so its commands run through the V2 CLI (" +
		dispatcherV2CLIPackageName + "@2.2.0).\n"
	if stdout.String() != want {
		t.Fatalf("stdout = %q, want %q", stdout.String(), want)
	}
}

func TestRunDispatcherPrintsDispatcherVersionAndPinContextWhenStdoutIsTerminal(t *testing.T) {
	// Verifies an interactive --version in a V3 project prints the pinned project runner version instead of forwarding.
	projectRoot := createDispatcherUnityProject(t)
	writeDispatcherProjectPin(t, projectRoot, clicontract.ProjectRunnerVersion())
	t.Setenv(nativepath.CacheDirEnvName, t.TempDir())
	t.Setenv(dispatcherDisableSelfUpdateEnvName, "1")
	t.Chdir(projectRoot)

	deps := defaultDispatcherRunDeps()
	deps.stdoutIsTerminal = alwaysTerminalStdout()
	deps.runRealCLI = func(context.Context, string, []string, io.Writer, io.Writer) int {
		t.Fatal("interactive version request must not forward to the pinned project runner")
		return 0
	}

	var stdout bytes.Buffer
	code := runDispatcherWithDeps(context.Background(), []string{"--version"}, &stdout, io.Discard, deps)

	if code != 0 {
		t.Fatalf("exit code = %d, want 0", code)
	}
	want := dispatcherVersion + "\n" +
		"This Unity project pins uloop project runner " + clicontract.ProjectRunnerVersion() + ".\n"
	if stdout.String() != want {
		t.Fatalf("stdout = %q, want %q", stdout.String(), want)
	}
}

func TestRunDispatcherPrintsNoProjectContextWhenStdoutIsTerminal(t *testing.T) {
	// Verifies an interactive --version outside any Unity project explains that no project was detected.
	t.Chdir(t.TempDir())

	deps := defaultDispatcherRunDeps()
	deps.stdoutIsTerminal = alwaysTerminalStdout()

	var stdout bytes.Buffer
	code := runDispatcherWithDeps(context.Background(), []string{"--version"}, &stdout, io.Discard, deps)

	if code != 0 {
		t.Fatalf("exit code = %d, want 0", code)
	}
	want := dispatcherVersion + "\n" +
		"No Unity project detected here; run inside a Unity project to see which CLI generation serves it.\n"
	if stdout.String() != want {
		t.Fatalf("stdout = %q, want %q", stdout.String(), want)
	}
}

func TestRunDispatcherPrintsMissingPinContextWhenStdoutIsTerminal(t *testing.T) {
	// Verifies an interactive --version in a pinless V3 project points at the pin file Unity must create.
	projectRoot := createDispatcherUnityProject(t)
	t.Chdir(projectRoot)

	deps := defaultDispatcherRunDeps()
	deps.stdoutIsTerminal = alwaysTerminalStdout()

	var stdout bytes.Buffer
	code := runDispatcherWithDeps(context.Background(), []string{"--version"}, &stdout, io.Discard, deps)

	if code != 0 {
		t.Fatalf("exit code = %d, want 0", code)
	}
	want := dispatcherVersion + "\n" +
		"This Unity project has no readable project runner pin yet; open it in Unity once to create " +
		dispatcherProjectPinRelativePath + ".\n"
	if stdout.String() != want {
		t.Fatalf("stdout = %q, want %q", stdout.String(), want)
	}
}

func TestRunDispatcherKeepsVersionJSONMachineReadableWhenStdoutIsTerminal(t *testing.T) {
	// Verifies --version --json stays the machine payload even on a terminal, since only bare --version gains human output.
	t.Chdir(t.TempDir())

	deps := defaultDispatcherRunDeps()
	deps.stdoutIsTerminal = alwaysTerminalStdout()

	var stdout bytes.Buffer
	code := runDispatcherWithDeps(context.Background(), []string{"--version", "--json"}, &stdout, io.Discard, deps)

	if code != 0 {
		t.Fatalf("exit code = %d, want 0", code)
	}
	payload := map[string]string{}
	if err := json.Unmarshal(stdout.Bytes(), &payload); err != nil {
		t.Fatalf("parse version JSON: %v; stdout=%s", err, stdout.String())
	}
	if payload["DispatcherVersion"] != dispatcherVersion {
		t.Fatalf("DispatcherVersion = %q, want %q", payload["DispatcherVersion"], dispatcherVersion)
	}
}

func TestIsTerminalWriterRejectsNonFileWriter(t *testing.T) {
	// Verifies the default terminal probe treats an in-memory writer as non-interactive.
	if isTerminalWriter(&bytes.Buffer{}) {
		t.Fatal("a bytes.Buffer must not be reported as a terminal")
	}
}
