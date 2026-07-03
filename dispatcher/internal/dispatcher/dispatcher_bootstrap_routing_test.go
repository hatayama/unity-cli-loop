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

// Verifies the global launcher reports dispatcher identity fields in JSON version output.
func TestRunDispatcherVersionJSONReportsDispatcherIdentity(t *testing.T) {
	t.Chdir(t.TempDir())

	payload := clitest.RunVersionJSON(t, RunDispatcher)

	if payload["DispatcherVersion"] != dispatcherVersion {
		t.Fatalf("DispatcherVersion mismatch: %v", payload["DispatcherVersion"])
	}
	if _, ok := payload["DispatcherContractVersion"]; !ok {
		t.Fatalf("DispatcherContractVersion missing: %#v", payload)
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
	defer func() {
		updateRunCommand = previousRun
		dispatcherReadInstalledVersion = previousReader
	}()
	updateExecuted := false
	updateRunCommand = func(context.Context, update.Command, io.Writer, io.Writer) error {
		updateExecuted = true
		return nil
	}
	dispatcherReadInstalledVersion = func(context.Context) (string, error) {
		return dispatcherVersion, nil
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
	if !strings.Contains(stdout.String(), "Updating global uloop launcher") {
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

// Verifies command name completion works in the dispatcher process from the embedded tool catalog.
func TestRunDispatcherListCommandsDoesNotRequireProjectPin(t *testing.T) {
	t.Chdir(t.TempDir())

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := RunDispatcher(context.Background(), []string{"--list-commands"}, &stdout, &stderr)

	if code != 0 {
		t.Fatalf("dispatcher list-commands failed: code=%d stderr=%s", code, stderr.String())
	}
	if !strings.Contains(stdout.String(), "compile") {
		t.Fatalf("list-commands output mismatch: %s", stdout.String())
	}
}

// Verifies completion script generation works in the dispatcher process without a project pin.
func TestRunDispatcherCompletionScriptDoesNotRequireProjectPin(t *testing.T) {
	t.Chdir(t.TempDir())

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := RunDispatcher(context.Background(), []string{"completion", "--shell", "bash"}, &stdout, &stderr)

	if code != 0 {
		t.Fatalf("dispatcher completion failed: code=%d stderr=%s", code, stderr.String())
	}
	if !strings.Contains(stdout.String(), "complete -F _uloop_completions uloop") {
		t.Fatalf("completion script output mismatch: %s", stdout.String())
	}
}
