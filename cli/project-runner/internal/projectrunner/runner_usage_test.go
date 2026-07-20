package projectrunner

import (
	"bytes"
	"context"
	"strings"
	"testing"
)

// Verifies the project runner refuses dispatcher-owned bootstrap and completion
// commands and points the caller at the global uloop launcher.
func TestRunProjectLocalRejectsDispatcherOwnedCommands(t *testing.T) {
	t.Chdir(t.TempDir())

	for _, command := range []string{
		"launch",
		"install",
		"update",
		"uninstall",
		"skills",
		"completion",
	} {
		var stdout bytes.Buffer
		var stderr bytes.Buffer
		code := RunProjectLocal(context.Background(), []string{command}, &stdout, &stderr)

		if code != 1 {
			t.Fatalf("%s: expected rejection exit code, got %d stdout=%s", command, code, stdout.String())
		}
		if !strings.Contains(stderr.String(), "global uloop launcher") {
			t.Fatalf("%s: rejection must point at the global uloop launcher: %s", command, stderr.String())
		}
		if !strings.Contains(stderr.String(), command) {
			t.Fatalf("%s: rejection must name the rejected command: %s", command, stderr.String())
		}
	}
}

// Verifies the project runner keeps its direct help output minimal and defers
// the full help UX to the global uloop launcher.
func TestRunProjectLocalHelpPrintsRunnerUsage(t *testing.T) {
	t.Chdir(t.TempDir())

	for _, args := range [][]string{
		{},
		{"--help"},
		{"-h"},
	} {
		var stdout bytes.Buffer
		var stderr bytes.Buffer
		code := RunProjectLocal(context.Background(), args, &stdout, &stderr)

		if code != 0 {
			t.Fatalf("%v: runner usage failed: code=%d stderr=%s", args, code, stderr.String())
		}
		if !strings.Contains(stdout.String(), "uloop-project-runner") {
			t.Fatalf("%v: runner usage must name the runner binary: %s", args, stdout.String())
		}
		if !strings.Contains(stdout.String(), "uloop --help") {
			t.Fatalf("%v: runner usage must defer to the launcher help: %s", args, stdout.String())
		}
	}
}

// Verifies command-level help requests on the runner print the minimal usage
// instead of executing the command or duplicating the launcher help UX.
func TestRunProjectLocalCommandHelpPrintsRunnerUsage(t *testing.T) {
	t.Chdir(t.TempDir())

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := RunProjectLocal(context.Background(), []string{"compile", "--help"}, &stdout, &stderr)

	if code != 0 {
		t.Fatalf("compile --help failed: code=%d stderr=%s", code, stderr.String())
	}
	if !strings.Contains(stdout.String(), "uloop --help") {
		t.Fatalf("command help must defer to the launcher help: %s", stdout.String())
	}
	if strings.Contains(stdout.String(), "--force-recompile") {
		t.Fatalf("command help must not duplicate the full tool help: %s", stdout.String())
	}
}
