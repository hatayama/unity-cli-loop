package cli

import (
	"bytes"
	"context"
	"os"
	"path/filepath"
	"strings"
	"testing"

	"github.com/hatayama/unity-cli-loop/cli/internal/clicore"
)

// Tests that launcher help lists native commands and live-tool discovery guidance without baked-in tools.
func TestPrintLauncherHelpListsNativeCommandsAndLiveToolGuidance(t *testing.T) {
	var stdout bytes.Buffer

	printLauncherHelp(&stdout)

	output := stdout.String()
	for _, expected := range []string{
		"Dispatcher launcher. Finds the Unity project, then dispatches live Unity tool commands.",
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

// Tests that dispatcher help with --project-path includes cached tools from that explicit project.
func TestRunDispatcherHelpWithProjectPathShowsCachedProjectTools(t *testing.T) {
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

	code := RunDispatcher(context.Background(), []string{"--project-path", projectRoot, "--help"}, &stdout, &stderr)

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

// Tests that dispatcher project help keeps cached tool descriptions concise.
func TestRunDispatcherHelpShowsConciseProjectToolDescriptions(t *testing.T) {
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
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := RunDispatcher(context.Background(), []string{"--project-path", projectRoot, "--help"}, &stdout, &stderr)

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
func TestRunDispatcherListHelpShowsGlobalOptions(t *testing.T) {
	t.Chdir(t.TempDir())
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := RunDispatcher(context.Background(), []string{"list", "--help"}, &stdout, &stderr)

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
func TestRunDispatcherLaunchHelpShowsGlobalOptions(t *testing.T) {
	t.Chdir(t.TempDir())
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := RunDispatcher(context.Background(), []string{"launch", "--help"}, &stdout, &stderr)

	if code != 0 {
		t.Fatalf("launch help failed: code=%d stderr=%s", code, stderr.String())
	}
	output := stdout.String()
	for _, expected := range []string{
		"Usage:",
		"uloop launch [options] [project-path]",
		"Compiler errors are ignored by default during Unity startup.",
		"--editor-version <version>",
		"Global options:",
		"--project-path <path>",
	} {
		if !strings.Contains(output, expected) {
			t.Fatalf("launch help missing %q:\n%s", expected, output)
		}
	}
	for _, removed := range []string{"-i, --ignore-compiler-errors", "--ignore-compiler-errors"} {
		if strings.Contains(output, removed) {
			t.Fatalf("launch help still includes removed option %q:\n%s", removed, output)
		}
	}
}

// Tests that first-party tool help is available without Unity project resolution.
func TestRunDispatcherCompileHelpDoesNotRequireUnityProject(t *testing.T) {
	t.Chdir(t.TempDir())
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := RunDispatcher(context.Background(), []string{"compile", "--help"}, &stdout, &stderr)

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

// Tests that execute-dynamic-code help includes CLI-side code-file support without resolving a Unity project.
func TestRunDispatcherExecuteDynamicCodeHelpDoesNotRequireUnityProject(t *testing.T) {
	t.Chdir(t.TempDir())
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := RunDispatcher(context.Background(), []string{clicore.ExecuteDynamicCodeCommandName, "--help"}, &stdout, &stderr)

	if code != 0 {
		t.Fatalf("execute-dynamic-code help failed: code=%d stderr=%s", code, stderr.String())
	}
	output := stdout.String()
	for _, expected := range []string{
		"Usage:",
		"uloop execute-dynamic-code",
		"--code <value>",
		clicore.DynamicCodeFileOptionUsage,
		"--wait-for-domain-reload",
		"Read C# code from a file",
	} {
		if !strings.Contains(output, expected) {
			t.Fatalf("execute-dynamic-code help missing %q:\n%s", expected, output)
		}
	}
	if strings.Contains(output, "--compile-only") {
		t.Fatalf("execute-dynamic-code help exposed internal compile-only flag:\n%s", output)
	}
}

// Tests that command help wins even after other tool options.
func TestRunDispatcherCompileHelpWinsAfterOtherOptions(t *testing.T) {
	t.Chdir(t.TempDir())
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := RunDispatcher(context.Background(), []string{"compile", "--force-recompile", "--help"}, &stdout, &stderr)

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

// Tests that first-party test help lists options without contacting Unity.
func TestRunDispatcherRunTestsHelpDoesNotRequireUnityProject(t *testing.T) {
	t.Chdir(t.TempDir())
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := RunDispatcher(context.Background(), []string{"run-tests", "--help"}, &stdout, &stderr)

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

// Tests that update help documents the --to-version option.
func TestRunDispatcherUpdateHelpListsToVersionOption(t *testing.T) {
	t.Chdir(t.TempDir())
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := RunDispatcher(context.Background(), []string{"update", "--help"}, &stdout, &stderr)

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
func TestRunDispatcherSkillsSubcommandHelpDoesNotRequireUnityProject(t *testing.T) {
	t.Chdir(t.TempDir())
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := RunDispatcher(context.Background(), []string{"skills", "install", "--help"}, &stdout, &stderr)

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

// writeToolCache is duplicated from internal/projectrunner's test helper of
// the same name: test helpers cannot be shared across packages, and both
// packages need a project tool cache fixture for their help/dispatch tests.
func writeToolCache(t *testing.T, projectRoot string, content string) {
	t.Helper()
	cachePath := filepath.Join(projectRoot, clicore.CacheDirectoryName, clicore.CacheFileName)
	if err := os.MkdirAll(filepath.Dir(cachePath), 0o755); err != nil {
		t.Fatalf("failed to create tool cache directory: %v", err)
	}
	if err := os.WriteFile(cachePath, []byte(content), 0o644); err != nil {
		t.Fatalf("failed to write tool cache: %v", err)
	}
}
