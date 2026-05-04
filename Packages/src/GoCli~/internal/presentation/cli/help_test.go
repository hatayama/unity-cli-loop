package cli

import (
	"bytes"
	"context"
	"os"
	"path/filepath"
	"strings"
	"testing"
)

// Tests that launcher help lists native commands and live-tool discovery guidance without baked-in tools.
func TestPrintLauncherHelpListsNativeCommandsAndLiveToolGuidance(t *testing.T) {
	var stdout bytes.Buffer

	printLauncherHelp(&stdout)

	output := stdout.String()
	for _, expected := range []string{
		"Native commands:",
		"  launch",
		"  focus-window",
		"  list",
		"  skills",
		"Unity tool commands are project-specific.",
		"uloop list",
		"--project-path <path>",
		"uloop --project-path /path/to/project list",
		"uloop completion --list-options <command>",
	} {
		if !strings.Contains(output, expected) {
			t.Fatalf("help output missing %q:\n%s", expected, output)
		}
	}
	for _, unexpected := range []string{
		"  compile",
		"  get-logs",
		"  run-tests",
	} {
		if strings.Contains(output, unexpected) {
			t.Fatalf("help output should not include baked-in Unity tool %q:\n%s", unexpected, output)
		}
	}
}

// Tests that project-local help lists native commands and points users to live tool discovery.
func TestPrintProjectLocalHelpListsNativeCommandsAndLiveToolGuidance(t *testing.T) {
	var stdout bytes.Buffer

	printHelp(&stdout)

	output := stdout.String()
	for _, expected := range []string{
		"Native commands:",
		"  launch",
		"  focus-window",
		"  list",
		"  sync",
		"Unity tool commands are project-specific.",
		"--project-path <path>",
		"uloop --project-path /path/to/project list",
		"uloop list",
	} {
		if !strings.Contains(output, expected) {
			t.Fatalf("help output missing %q:\n%s", expected, output)
		}
	}
	for _, unexpected := range []string{
		"  compile",
		"  get-logs",
		"  run-tests",
	} {
		if strings.Contains(output, unexpected) {
			t.Fatalf("help output should not include baked-in Unity tool %q:\n%s", unexpected, output)
		}
	}
}

// Tests that launcher help handles --project-path before dispatching to project-local core.
func TestRunLauncherPrintsHelpAfterProjectPathOption(t *testing.T) {
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := RunLauncher(
		context.Background(),
		[]string{"--project-path", "/does/not/need/to/exist", "-h"},
		&stdout,
		&stderr)

	if code != 0 {
		t.Fatalf("exit code mismatch: %d stderr=%s", code, stderr.String())
	}
	output := stdout.String()
	for _, expected := range []string{
		"Native commands:",
		"--project-path <path>",
		"Unity tool commands are project-specific.",
	} {
		if !strings.Contains(output, expected) {
			t.Fatalf("help output missing %q:\n%s", expected, output)
		}
	}
	if strings.Contains(output, "Native Go CLI preview") {
		t.Fatalf("launcher should not dispatch help to project-local core:\n%s", output)
	}
}

// Tests that launcher help shows cached tool commands for an explicit Unity project path.
func TestRunLauncherPrintsProjectCachedToolsAfterProjectPathOption(t *testing.T) {
	projectRoot := t.TempDir()
	writeProjectWithToolCache(t, projectRoot)
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := RunLauncher(
		context.Background(),
		[]string{"--project-path", projectRoot, "-h"},
		&stdout,
		&stderr)

	assertProjectToolHelp(t, code, stdout.String(), stderr.String())
}

// Tests that launcher help shows cached tool commands when run inside a Unity project.
func TestRunLauncherPrintsProjectCachedToolsFromCurrentDirectory(t *testing.T) {
	projectRoot := t.TempDir()
	writeProjectWithToolCache(t, projectRoot)
	t.Chdir(projectRoot)
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := RunLauncher(
		context.Background(),
		[]string{"-h"},
		&stdout,
		&stderr)

	assertProjectToolHelp(t, code, stdout.String(), stderr.String())
}

func writeProjectWithToolCache(t *testing.T, projectRoot string) {
	t.Helper()
	if err := os.MkdirAll(filepath.Join(projectRoot, "Assets"), 0o755); err != nil {
		t.Fatalf("failed to create Assets: %v", err)
	}
	if err := os.MkdirAll(filepath.Join(projectRoot, "ProjectSettings"), 0o755); err != nil {
		t.Fatalf("failed to create ProjectSettings: %v", err)
	}
	writeToolCache(t, projectRoot, `{
  "version": "test",
  "tools": [
    {
      "name": "project-tool",
      "description": "Project specific tool",
      "inputSchema": {"type": "object", "properties": {}}
    },
    {
      "name": "focus-window",
      "description": "Cached focus-window should not be listed because native command wins",
      "inputSchema": {"type": "object", "properties": {}}
    }
  ]
}`)
}

func assertProjectToolHelp(t *testing.T, code int, output string, stderr string) {
	t.Helper()
	if code != 0 {
		t.Fatalf("exit code mismatch: %d stderr=%s", code, stderr)
	}
	for _, expected := range []string{
		"Unity tool commands from this project's cache:",
		"project-tool",
		"Project specific tool",
	} {
		if !strings.Contains(output, expected) {
			t.Fatalf("help output missing %q:\n%s", expected, output)
		}
	}
	if strings.Contains(output, "Cached focus-window should not be listed") {
		t.Fatalf("help output should not list cached tools shadowed by native commands:\n%s", output)
	}
}
