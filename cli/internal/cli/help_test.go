package cli

import (
	"bytes"
	"context"
	"encoding/json"
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
		"  uninstall",
		"Unity tool commands are project-specific.",
		"uloop list",
		"--project-path <path>",
		"uloop --project-path /path/to/project list",
		"uloop <command> --help",
		"Show help for native and Unity tool commands",
		"uloop completion --help",
	} {
		if !strings.Contains(output, expected) {
			t.Fatalf("help output missing %q:\n%s", expected, output)
		}
	}
	for _, unexpected := range []string{
		"  compile",
		"  get-logs",
		"  run-tests",
		"uloop --list-commands",
		"uloop --list-options <command>",
	} {
		if strings.Contains(output, unexpected) {
			t.Fatalf("help output should not include baked-in Unity tool %q:\n%s", unexpected, output)
		}
	}
}

func TestRunProjectLocalVersionJSONIncludesProtocolVersion(t *testing.T) {
	// Verifies Unity setup can inspect protocol compatibility without parsing human help text.
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := RunProjectLocal(context.Background(), []string{"--version", "--json"}, &stdout, &stderr)

	if code != 0 {
		t.Fatalf("version json command failed with code %d: %s", code, stderr.String())
	}
	var payload map[string]any
	if err := json.Unmarshal(stdout.Bytes(), &payload); err != nil {
		t.Fatalf("version json output is not JSON: %v\n%s", err, stdout.String())
	}
	if payload["cliVersion"] != version {
		t.Fatalf("cliVersion mismatch: %#v", payload)
	}
	if payload["protocolVersion"] != float64(protocolVersion) {
		t.Fatalf("protocolVersion mismatch: %#v", payload)
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
		"  uninstall",
		"Unity tool commands are project-specific.",
		"--project-path <path>",
		"uloop --project-path /path/to/project list",
		"uloop list",
		"uloop completion --help",
	} {
		if !strings.Contains(output, expected) {
			t.Fatalf("help output missing %q:\n%s", expected, output)
		}
	}
	for _, unexpected := range []string{
		"  compile",
		"  get-logs",
		"  run-tests",
		"uloop --list-commands",
		"uloop --list-options <command>",
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

// Tests that project help keeps cached tool descriptions concise.
func TestRunProjectLocalHelpShowsConciseProjectToolDescriptions(t *testing.T) {
	projectRoot := createLaunchTestProject(t)
	writeToolCache(t, projectRoot, `{
  "tools": [
    {
      "name": "long-tool",
      "description": "First sentence. Second sentence with operational details that belong in command help.",
      "inputSchema": {"type": "object", "properties": {}}
    }
  ]
}`)
	t.Chdir(projectRoot)
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := RunProjectLocal(context.Background(), []string{"--help"}, &stdout, &stderr)

	if code != 0 {
		t.Fatalf("help failed: code=%d stderr=%s", code, stderr.String())
	}
	output := stdout.String()
	if !strings.Contains(output, "First sentence.") {
		t.Fatalf("help output missing concise summary:\n%s", output)
	}
	if strings.Contains(output, "Second sentence") {
		t.Fatalf("help output should not include long command details:\n%s", output)
	}
}

// Tests that native project commands show the shared project path option.
func TestRunProjectLocalListHelpShowsGlobalOptions(t *testing.T) {
	t.Chdir(t.TempDir())
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := RunProjectLocal(context.Background(), []string{"list", "--help"}, &stdout, &stderr)

	if code != 0 {
		t.Fatalf("list help failed: code=%d stderr=%s", code, stderr.String())
	}
	output := stdout.String()
	for _, expected := range []string{
		"Usage:",
		"uloop list",
		"Global options:",
		"--project-path <path>",
	} {
		if !strings.Contains(output, expected) {
			t.Fatalf("list help missing %q:\n%s", expected, output)
		}
	}
}

// Tests that launch help documents both positional and global project selection.
func TestRunProjectLocalLaunchHelpShowsGlobalOptions(t *testing.T) {
	t.Chdir(t.TempDir())
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := RunProjectLocal(context.Background(), []string{"launch", "--help"}, &stdout, &stderr)

	if code != 0 {
		t.Fatalf("launch help failed: code=%d stderr=%s", code, stderr.String())
	}
	output := stdout.String()
	for _, expected := range []string{
		"Usage:",
		"uloop launch [options] [project-path]",
		"Global options:",
		"--project-path <path>",
	} {
		if !strings.Contains(output, expected) {
			t.Fatalf("launch help missing %q:\n%s", expected, output)
		}
	}
}

// Tests that first-party tool help is available without Unity project resolution.
func TestRunProjectLocalCompileHelpDoesNotRequireUnityProject(t *testing.T) {
	t.Chdir(t.TempDir())
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := RunProjectLocal(context.Background(), []string{"compile", "--help"}, &stdout, &stderr)

	if code != 0 {
		t.Fatalf("compile help failed: code=%d stderr=%s", code, stderr.String())
	}
	output := stdout.String()
	for _, expected := range []string{
		"Usage:",
		"uloop compile",
		"--force-recompile",
		"--no-wait-for-domain-reload",
		"--stop-on-external-scene-changes",
		"Stop before execution if open Scene files changed externally instead of auto-reloading them",
		"default: auto-reload enabled",
	} {
		if !strings.Contains(output, expected) {
			t.Fatalf("compile help missing %q:\n%s", expected, output)
		}
	}
	if strings.Contains(output, "--wait-for-domain-reload") {
		t.Fatalf("compile help exposed removed wait flag:\n%s", output)
	}
}

// Tests that command help wins even after other tool options.
func TestRunProjectLocalCompileHelpWinsAfterOtherOptions(t *testing.T) {
	t.Chdir(t.TempDir())
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := RunProjectLocal(context.Background(), []string{"compile", "--force-recompile", "--help"}, &stdout, &stderr)

	if code != 0 {
		t.Fatalf("compile help failed: code=%d stderr=%s", code, stderr.String())
	}
	output := stdout.String()
	for _, expected := range []string{
		"Usage:",
		"uloop compile",
		"--force-recompile",
	} {
		if !strings.Contains(output, expected) {
			t.Fatalf("compile help missing %q:\n%s", expected, output)
		}
	}
}

// Tests that unknown leading options are reported as global option errors.
func TestRunProjectLocalRejectsUnknownGlobalOption(t *testing.T) {
	t.Chdir(t.TempDir())
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := RunProjectLocal(context.Background(), []string{"--project-pathology"}, &stdout, &stderr)

	if code != 1 {
		t.Fatalf("exit code mismatch: code=%d stdout=%s stderr=%s", code, stdout.String(), stderr.String())
	}
	if !strings.Contains(stderr.String(), "Unknown global option: --project-pathology") {
		t.Fatalf("stderr missing unknown option error:\n%s", stderr.String())
	}
}

// Tests that first-party test help lists options without contacting Unity.
func TestRunProjectLocalRunTestsHelpDoesNotRequireUnityProject(t *testing.T) {
	t.Chdir(t.TempDir())
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := RunProjectLocal(context.Background(), []string{"run-tests", "--help"}, &stdout, &stderr)

	if code != 0 {
		t.Fatalf("run-tests help failed: code=%d stderr=%s", code, stderr.String())
	}
	output := stdout.String()
	for _, expected := range []string{
		"Usage:",
		"uloop run-tests",
		"--test-mode",
		"--filter-type",
		"--filter-value",
		"--fail-on-unsaved-changes",
		"Fail before execution if unsaved editor changes remain instead of auto-saving them",
		"default: auto-save enabled",
	} {
		if !strings.Contains(output, expected) {
			t.Fatalf("run-tests help missing %q:\n%s", expected, output)
		}
	}
}

// Tests that update help is available before installer execution.
func TestRunProjectLocalUpdateHelpDoesNotExecuteInstaller(t *testing.T) {
	t.Chdir(t.TempDir())
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := RunProjectLocal(context.Background(), []string{"update", "--help"}, &stdout, &stderr)

	if code != 0 {
		t.Fatalf("update help failed: code=%d stderr=%s", code, stderr.String())
	}
	output := stdout.String()
	for _, expected := range []string{"Usage:", "uloop update", "--to-version <version>"} {
		if !strings.Contains(output, expected) {
			t.Fatalf("update help missing %q:\n%s", expected, output)
		}
	}
}

// Tests that skills subcommand help is available before project resolution.
func TestRunProjectLocalSkillsSubcommandHelpDoesNotRequireUnityProject(t *testing.T) {
	t.Chdir(t.TempDir())
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := RunProjectLocal(context.Background(), []string{"skills", "install", "--help"}, &stdout, &stderr)

	if code != 0 {
		t.Fatalf("skills install help failed: code=%d stderr=%s", code, stderr.String())
	}
	output := stdout.String()
	for _, expected := range []string{"Usage:", "uloop skills install", "--claude", "--codex"} {
		if !strings.Contains(output, expected) {
			t.Fatalf("skills install help missing %q:\n%s", expected, output)
		}
	}
}
