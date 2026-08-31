package dispatcher

import (
	"bytes"
	"context"
	"io"
	"strings"
	"testing"

	"github.com/hatayama/unity-cli-loop/common/clitest"
	"github.com/hatayama/unity-cli-loop/dispatcher/internal/update"
)

// These tests lock the dispatcher-process routing of bootstrap commands across
// the dispatcher slimming refactor. Every command here must complete without a
// project pin, so each test runs from an empty working directory where any
// accidental forwarding would fail with a pin resolution error.

// Verifies the global uloop binary reports dispatcher identity fields in JSON version output.
func TestRunDispatcherVersionJSONReportsDispatcherIdentity(t *testing.T) {
	t.Chdir(t.TempDir())

	payload := clitest.RunVersionJSON(t, RunDispatcher)

	if payload["DispatcherVersion"] != dispatcherVersion {
		t.Fatalf("DispatcherVersion mismatch: %v", payload["DispatcherVersion"])
	}
	if _, ok := payload["DispatcherContractVersion"]; ok {
		t.Fatalf("DispatcherContractVersion must not be emitted: %#v", payload)
	}
}

// Verifies update help is handled in the dispatcher process without running the installer.
func TestRunDispatcherUpdateHelpDoesNotExecuteInstaller(t *testing.T) {
	t.Chdir(t.TempDir())

	previousRun := updateRunCommand
	defer func() {
		updateRunCommand = previousRun
	}()
	updateExecuted := false
	updateRunCommand = func(context.Context, update.Command, io.Writer, io.Writer) error {
		updateExecuted = true
		return nil
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := RunDispatcher(context.Background(), []string{"update", "--help"}, &stdout, &stderr)

	if code != 0 {
		t.Fatalf("dispatcher update help failed: code=%d stderr=%s", code, stderr.String())
	}
	if updateExecuted {
		t.Fatal("update help must not execute the installer")
	}
	if !strings.Contains(stdout.String(), "uloop update") {
		t.Fatalf("update help output mismatch: %s", stdout.String())
	}
}

// Verifies the update command executes the dispatcher updater without forwarding to a project runner.
func TestRunDispatcherUpdateRunsInDispatcherProcess(t *testing.T) {
	skipWhenNativeUpdateIsUnsupported(t)
	t.Chdir(t.TempDir())

	previousRun := updateRunCommand
	previousReader := dispatcherReadInstalledVersion
	previousResolver := resolveUpdateTargetVersionFunc
	previousManifest := fetchAttestationSubjectManifestFunc
	previousExecutablePath := resolveUpdateExecutablePathFunc
	defer func() {
		updateRunCommand = previousRun
		dispatcherReadInstalledVersion = previousReader
		resolveUpdateTargetVersionFunc = previousResolver
		fetchAttestationSubjectManifestFunc = previousManifest
		resolveUpdateExecutablePathFunc = previousExecutablePath
	}()
	updateExecuted := false
	updateRunCommand = func(context.Context, update.Command, io.Writer, io.Writer) error {
		updateExecuted = true
		return nil
	}
	dispatcherReadInstalledVersion = func(context.Context) (string, error) {
		return "999.0.0", nil
	}
	resolveUpdateTargetVersionFunc = func(ctx context.Context, options update.Options) (update.Options, error) {
		if options.TargetVersion == "" {
			options.TargetVersion = "999.0.0"
		}
		return options, nil
	}
	fetchAttestationSubjectManifestFunc = func(ctx context.Context, tag string) (string, error) {
		return "deadbeef  install.sh\n", nil
	}
	// Why: keep this routing test off os.Executable so a Cellar-hosted binary cannot
	// trip the Homebrew guard and hide a real dispatcher-process regression.
	resolveUpdateExecutablePathFunc = func() (string, error) {
		return "/Users/someone/.local/bin/uloop", nil
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := RunDispatcher(context.Background(), []string{"update"}, &stdout, &stderr)

	if code != 0 {
		t.Fatalf("dispatcher update failed: code=%d stderr=%s", code, stderr.String())
	}
	if !updateExecuted {
		t.Fatal("expected update to run in the dispatcher process")
	}
	if !strings.Contains(stdout.String(), "Updating global uloop dispatcher") {
		t.Fatalf("update output mismatch: %s", stdout.String())
	}
}

// Verifies skills help is handled in the dispatcher process before project pin resolution.
func TestRunDispatcherSkillsHelpDoesNotRequireProjectPin(t *testing.T) {
	t.Chdir(t.TempDir())

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := RunDispatcher(context.Background(), []string{"skills", "--help"}, &stdout, &stderr)

	if code != 0 {
		t.Fatalf("dispatcher skills help failed: code=%d stderr=%s", code, stderr.String())
	}
	if !strings.Contains(stdout.String(), "uloop skills install") {
		t.Fatalf("skills help output mismatch: %s", stdout.String())
	}
}
