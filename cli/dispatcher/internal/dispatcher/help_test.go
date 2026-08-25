package dispatcher

import (
	"bytes"
	"context"
	"path/filepath"
	"strings"
	"testing"

	"github.com/hatayama/unity-cli-loop/common/tooldocs"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	"github.com/hatayama/unity-cli-loop/common/clitest"
)

// Tests that dispatcher help lists native commands and live-tool discovery guidance without baked-in tools.
func TestPrintDispatcherHelpListsNativeCommandsAndLiveToolGuidance(t *testing.T) {
	var stdout bytes.Buffer

	printDispatcherHelp(&stdout)

	output := stdout.String()
	for _, expected := range []string{
		"Dispatcher. Finds the Unity project, then dispatches live Unity tool commands.",
		"Native commands:",
		"  launch",
		"  focus-window",
		"  list",
		"  skills",
		"  package",
		"  uninstall",
		"  version",
		"Unity tool commands are project-specific.",
		"does not include the full command list",
		"uloop --project-path /path/to/project --help",
		"uloop list",
		"--project-path <path>",
		"uloop --project-path /path/to/project list",
		"uloop <command> --help",
		"Show help for native and Unity tool commands",
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
		"  version",
		"Unity tool commands are project-specific.",
		"does not include the full command list",
		"uloop --project-path /path/to/project --help",
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
		"uloop --list-commands",
		"uloop --list-options <command>",
	} {
		if strings.Contains(output, unexpected) {
			t.Fatalf("help output should not include baked-in Unity tool %q:\n%s", unexpected, output)
		}
	}
}

// Verifies the names-only list guidance is a complete aligned footer line, so agents can discover
// the compact command listing without parsing the full live tool catalog.
func TestPrintDispatcherHelpListsNamesOnlyFooterLine(t *testing.T) {
	var stdout bytes.Buffer

	printDispatcherHelp(&stdout)

	const expected = "  uloop list --names                          Show command names only, one per line"
	for _, line := range strings.Split(stdout.String(), "\n") {
		if !strings.HasPrefix(line, "  uloop list --names") {
			continue
		}
		if line != expected {
			t.Fatalf("names-only footer line mismatch: got %q want %q", line, expected)
		}
		return
	}
	t.Fatalf("help output missing names-only footer line %q:\n%s", expected, stdout.String())
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
		"--timeout-seconds",
		"--no-wait-for-domain-reload",
		"--stop-on-external-scene-changes",
		"Stop before compilation if open Scene files changed externally instead of auto-reloading them",
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

// Verifies embedded help exposes the renamed maximum result count option.
func TestRunDispatcherFindGameObjectsHelpShowsMaxCount(t *testing.T) {
	t.Chdir(t.TempDir())
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := RunDispatcher(context.Background(), []string{"find-game-objects", "--help"}, &stdout, &stderr)
	if code != 0 {
		t.Fatalf("find-game-objects help failed: code=%d stderr=%s", code, stderr.String())
	}
	output := stdout.String()
	if !strings.Contains(output, "--max-count") {
		t.Fatalf("find-game-objects help missing --max-count:\n%s", output)
	}
	if strings.Contains(output, "--max-results") {
		t.Fatalf("find-game-objects help still exposes --max-results:\n%s", output)
	}
}

func TestCommandHelpPrefersProjectCacheForDefaultToolNames(t *testing.T) {
	// Verifies command help uses synced project tool metadata before embedded defaults.
	projectRoot := createLaunchTestProject(t)
	writeToolCache(t, projectRoot, `{
  "tools": [
    {
      "name": "compile",
      "description": "Cached compile help",
      "inputSchema": {
        "type": "object",
        "properties": {
          "CachedOnly": {"type": "boolean", "description": "Cached-only option"}
        }
      }
    }
  ]
}`)
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	handled, code := tryHandleCommandHelp("compile", projectRoot, projectRoot, &stdout, &stderr)

	if !handled {
		t.Fatal("compile help request was not handled")
	}
	if code != 0 {
		t.Fatalf("compile help failed: code=%d stderr=%s", code, stderr.String())
	}
	output := stdout.String()
	if !strings.Contains(output, "Cached compile help") {
		t.Fatalf("cached compile help was not used:\n%s", output)
	}
	if !strings.Contains(output, "--cached-only") {
		t.Fatalf("cached compile option was not listed:\n%s", output)
	}
}

// Verifies a tool's help closes with the instruction to load its skill, which is the only pointer
// from --help to the workflow rules and response shapes that --help itself cannot carry.
func TestCommandHelpPointsAtTheToolSkill(t *testing.T) {
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	handled, code := tryHandleCommandHelp("simulate-keyboard", "", "", &stdout, &stderr)

	if !handled || code != 0 {
		t.Fatalf("simulate-keyboard help was not handled: handled=%v code=%d stderr=%s", handled, code, stderr.String())
	}
	if !strings.Contains(stdout.String(), "Load the uloop-simulate-keyboard skill") {
		t.Fatalf("simulate-keyboard help does not point at its skill:\n%s", stdout.String())
	}
}

// Verifies the watch commands point at the pause-point skill, which is where watch expressions are
// documented, rather than at a per-command skill that does not exist.
func TestCommandHelpPointsWatchCommandsAtThePausePointSkill(t *testing.T) {
	for _, command := range []string{"enable-watch", "clear-watch", "get-watch-values"} {
		t.Run(command, func(t *testing.T) {
			var stdout bytes.Buffer
			var stderr bytes.Buffer

			handled, code := tryHandleCommandHelp(command, "", "", &stdout, &stderr)

			if !handled || code != 0 {
				t.Fatalf("%s help was not handled: handled=%v code=%d stderr=%s", command, handled, code, stderr.String())
			}
			if !strings.Contains(stdout.String(), "Load the uloop-pause-point skill") {
				t.Fatalf("%s help does not point at the pause-point skill:\n%s", command, stdout.String())
			}
		})
	}
}

// Verifies dispatcher-owned native commands get the guidance line too: launch renders its own help,
// so it would otherwise be the only command with a skill that never mentions it.
func TestLaunchHelpPointsAtTheLaunchSkill(t *testing.T) {
	t.Chdir(t.TempDir())
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := RunDispatcher(context.Background(), []string{clicore.LaunchCommandName, "--help"}, &stdout, &stderr)

	if code != 0 {
		t.Fatalf("launch help failed: code=%d stderr=%s", code, stderr.String())
	}
	if !strings.Contains(stdout.String(), "Load the uloop-launch skill") {
		t.Fatalf("launch help does not point at its skill:\n%s", stdout.String())
	}
}

// Verifies a native command whose skill is internal-only (or absent) gets no guidance line.
func TestVersionHelpOmitsSkillLine(t *testing.T) {
	t.Chdir(t.TempDir())
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := RunDispatcher(context.Background(), []string{clicore.VersionCommandName, "--help"}, &stdout, &stderr)

	if code != 0 {
		t.Fatalf("version help failed: code=%d stderr=%s", code, stderr.String())
	}
	if strings.Contains(stdout.String(), "skill") {
		t.Fatalf("version help mentions a skill:\n%s", stdout.String())
	}
}

// Verifies a command with no matching skill gets no guidance line, so a custom command is never
// told to load a skill nobody installed.
func TestCommandHelpOmitsSkillLineForCustomCommands(t *testing.T) {
	projectRoot := createLaunchTestProject(t)
	writeToolCache(t, projectRoot, `{
  "tools": [
    {
      "name": "my-custom-command",
      "description": "A project-local custom command",
      "inputSchema": {
        "type": "object",
        "properties": {
          "Value": {"type": "string", "description": "Some value"}
        }
      }
    }
  ]
}`)
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	handled, code := tryHandleCommandHelp("my-custom-command", projectRoot, projectRoot, &stdout, &stderr)

	if !handled || code != 0 {
		t.Fatalf("custom command help was not handled: handled=%v code=%d stderr=%s", handled, code, stderr.String())
	}
	if strings.Contains(stdout.String(), "skill") {
		t.Fatalf("custom command help mentions a skill:\n%s", stdout.String())
	}
}

// Verifies enable-watch stays a plain default tool and still exposes schema-driven option help.
func TestCommandHelpUsesWatchToolSchemaForDefaultWatchCommands(t *testing.T) {
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	handled, code := tryHandleCommandHelp("enable-watch", "", "", &stdout, &stderr)

	if !handled || code != 0 {
		t.Fatalf("watch help was not handled: handled=%v code=%d stderr=%s", handled, code, stderr.String())
	}
	output := stdout.String()
	for _, expected := range []string{"--expression", "--id", "--max-history"} {
		if !strings.Contains(output, expected) {
			t.Fatalf("watch help missing %q:\n%s", expected, output)
		}
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
		tooldocs.DynamicCodeFileOptionUsage,
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
		"Fail before test execution if unsaved editor changes remain instead of auto-saving them",
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

// Tests that a runner-owned command's --help outside a Unity project reports a
// friendly project-resolution error instead of a raw forwarding failure, since
// its help is only available from the pinned runner once a project resolves.
func TestRunDispatcherRunnerOwnedCommandHelpOutsideProjectGivesProjectGuidance(t *testing.T) {
	t.Chdir(t.TempDir())
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := RunDispatcher(context.Background(), []string{clicore.PausePointAwaitCommandName, "--help"}, &stdout, &stderr)

	if code != 1 {
		t.Fatalf("expected failure outside a Unity project: code=%d stdout=%s", code, stdout.String())
	}
	output := stderr.String()
	for _, expected := range []string{
		"PROJECT_NOT_FOUND",
		"Run the command from inside a Unity project.",
		"when targeting another Unity project.",
	} {
		if !strings.Contains(output, expected) {
			t.Fatalf("runner-owned command help guidance missing %q:\n%s", expected, output)
		}
	}
}

// Tests that a Unity tool command without an embedded default definition gives
// the same project-resolution guidance as runner-owned commands when run
// outside a Unity project, since both depend on resolving a project.
func TestRunDispatcherUnityToolCommandHelpOutsideProjectGivesProjectGuidance(t *testing.T) {
	t.Chdir(t.TempDir())
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := RunDispatcher(context.Background(), []string{"some-project-only-tool", "--help"}, &stdout, &stderr)

	if code != 1 {
		t.Fatalf("expected failure outside a Unity project: code=%d stdout=%s", code, stdout.String())
	}
	output := stderr.String()
	for _, expected := range []string{
		"PROJECT_NOT_FOUND",
		"Run the command from inside a Unity project.",
		"when targeting another Unity project.",
	} {
		if !strings.Contains(output, expected) {
			t.Fatalf("Unity tool command help guidance missing %q:\n%s", expected, output)
		}
	}
}

// writeToolCache seeds the project tool cache fixture used by help/dispatch tests.
func writeToolCache(t *testing.T, projectRoot string, content string) {
	t.Helper()
	clitest.WriteProjectFile(t, projectRoot, filepath.Join(clicore.CacheDirectoryName, clicore.CacheFileName), content)
}
