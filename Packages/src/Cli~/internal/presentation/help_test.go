package presentation

import (
	"bytes"
	"context"
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
		"uloop --list-options <command>",
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

// Tests that help from a Unity project includes the cached project tool list.
func TestRunProjectLocalHelpShowsCachedProjectTools(t *testing.T) {
	projectRoot := createLaunchTestProject(t)
	writeToolCache(t, projectRoot, `{
  "tools": [
    {
      "name": "project-tool",
      "description": "Project tool first line\nsecond line",
      "inputSchema": {"type": "object", "properties": {}}
    }
  ]
}`)
	t.Chdir(projectRoot)
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := RunProjectLocal(context.Background(), []string{"-h"}, &stdout, &stderr)

	if code != 0 {
		t.Fatalf("help failed: code=%d stderr=%s", code, stderr.String())
	}
	output := stdout.String()
	for _, expected := range []string{
		"Unity tool commands from this project's cache:",
		"project-tool",
		"Project tool first line",
	} {
		if !strings.Contains(output, expected) {
			t.Fatalf("help output missing %q:\n%s", expected, output)
		}
	}
}

// Tests that help with --project-path includes cached tools from that explicit project.
func TestRunProjectLocalHelpWithProjectPathShowsCachedProjectTools(t *testing.T) {
	projectRoot := createLaunchTestProject(t)
	writeToolCache(t, projectRoot, `{
  "tools": [
    {
      "name": "explicit-project-tool",
      "description": "Explicit project tool",
      "inputSchema": {"type": "object", "properties": {}}
    }
  ]
}`)
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := RunProjectLocal(context.Background(), []string{"--project-path", projectRoot, "--help"}, &stdout, &stderr)

	if code != 0 {
		t.Fatalf("help failed: code=%d stderr=%s", code, stderr.String())
	}
	output := stdout.String()
	for _, expected := range []string{
		"Unity tool commands from this project's cache:",
		"explicit-project-tool",
		"Explicit project tool",
	} {
		if !strings.Contains(output, expected) {
			t.Fatalf("help output missing %q:\n%s", expected, output)
		}
	}
}
