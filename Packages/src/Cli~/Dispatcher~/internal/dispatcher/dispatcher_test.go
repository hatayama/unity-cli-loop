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

	dispatchercontract "github.com/hatayama/unity-cli-loop/Packages/src/Cli/Dispatcher"
)

func TestRunVersionPrintsDispatcherVersion(t *testing.T) {
	// Verifies that the global command reports dispatcher compatibility version, not core version.
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := Run(context.Background(), []string{"--version"}, &stdout, &stderr)

	if code != 0 {
		t.Fatalf("exit code mismatch: %d stderr=%s", code, stderr.String())
	}
	if strings.TrimSpace(stdout.String()) != dispatchercontract.Current.DispatcherVersion {
		t.Fatalf("dispatcher version mismatch: %s", stdout.String())
	}
}

func TestRunVersionAfterProjectPathPrintsDispatcherVersion(t *testing.T) {
	// Verifies that --version remains a dispatcher-owned global flag after --project-path parsing.
	projectRoot := t.TempDir()
	createUnityProject(t, projectRoot)
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := Run(context.Background(), []string{"--project-path", projectRoot, "--version"}, &stdout, &stderr)

	if code != 0 {
		t.Fatalf("exit code mismatch: %d stderr=%s", code, stderr.String())
	}
	if strings.TrimSpace(stdout.String()) != dispatchercontract.Current.DispatcherVersion {
		t.Fatalf("dispatcher version mismatch: %s", stdout.String())
	}
}

func TestDispatcherEnvironmentOverridesExistingDispatcherVersion(t *testing.T) {
	// Verifies that nested dispatcher invocations cannot pass stale compatibility versions to core.
	environment := dispatcherEnvironment([]string{
		"PATH=/bin",
		dispatchercontract.Current.DispatcherVersionEnv + "=0.0.0",
		"HOME=/tmp/home",
	})

	matches := 0
	actual := ""
	prefix := dispatchercontract.Current.DispatcherVersionEnv + "="
	for _, entry := range environment {
		if !strings.HasPrefix(entry, prefix) {
			continue
		}
		matches++
		actual = strings.TrimPrefix(entry, prefix)
	}

	if matches != 1 {
		t.Fatalf("dispatcher version env count mismatch: %d in %#v", matches, environment)
	}
	if actual != dispatchercontract.Current.DispatcherVersion {
		t.Fatalf("dispatcher version mismatch: %s", actual)
	}
}

func TestRunMissingProjectLocalCoreReportsStructuredError(t *testing.T) {
	// Verifies that the dispatcher finds a Unity project without importing the core CLI package.
	projectRoot := t.TempDir()
	createUnityProject(t, projectRoot)
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := Run(context.Background(), []string{"--project-path", projectRoot, "list"}, &stdout, &stderr)

	if code != 1 {
		t.Fatalf("exit code mismatch: %d stdout=%s stderr=%s", code, stdout.String(), stderr.String())
	}
	var envelope cliErrorEnvelope
	if err := json.Unmarshal(stderr.Bytes(), &envelope); err != nil {
		t.Fatalf("stderr is not valid JSON: %v\n%s", err, stderr.String())
	}
	if envelope.Error.ErrorCode != errorCodeProjectLocalCLIMissing {
		t.Fatalf("error code mismatch: %#v", envelope.Error)
	}
}

func TestRunSkillsHelpDoesNotResolveProject(t *testing.T) {
	// Verifies that skills help remains available before a Unity project is selected.
	changeDirectory(t, t.TempDir())
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := Run(context.Background(), []string{"skills", "--help"}, &stdout, &stderr)

	if code != 0 {
		t.Fatalf("exit code mismatch: %d stdout=%s stderr=%s", code, stdout.String(), stderr.String())
	}
	if !strings.Contains(stdout.String(), "uloop skills list") {
		t.Fatalf("skills help missing list usage: %s", stdout.String())
	}
}

func TestRunSkillsUsesBundledCoreWhenProjectLocalCoreIsMissing(t *testing.T) {
	// Verifies that dispatcher-owned skills commands can run before .uloop/bin/uloop-core exists.
	projectRoot := t.TempDir()
	createUnityProject(t, projectRoot)
	expectedCorePath := createBundledCorePlaceholder(t, projectRoot)
	previousExec := execCoreForDispatch
	executedCorePath := ""
	executedProjectRoot := ""
	executedArgs := []string{}
	execCoreForDispatch = func(_ context.Context, localPath string, args []string, projectRoot string, _ io.Writer) int {
		executedCorePath = localPath
		executedProjectRoot = projectRoot
		executedArgs = append([]string{}, args...)
		return 0
	}
	t.Cleanup(func() {
		execCoreForDispatch = previousExec
	})
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := Run(context.Background(), []string{"--project-path", projectRoot, "skills", "list"}, &stdout, &stderr)

	if code != 0 {
		t.Fatalf("exit code mismatch: %d stdout=%s stderr=%s", code, stdout.String(), stderr.String())
	}
	if executedCorePath != expectedCorePath {
		t.Fatalf("core path mismatch: %s", executedCorePath)
	}
	if executedProjectRoot != projectRoot {
		t.Fatalf("project root mismatch: %s", executedProjectRoot)
	}
	expectedArgs := []string{"skills", "list", "--project-path", projectRoot}
	if strings.Join(executedArgs, "\n") != strings.Join(expectedArgs, "\n") {
		t.Fatalf("args mismatch: %#v", executedArgs)
	}
}

func TestRunSkillsUsesBundledCoreFromLegacyPackageAlias(t *testing.T) {
	// Verifies that bundled core fallback preserves the legacy package-name alias.
	projectRoot := t.TempDir()
	createUnityProject(t, projectRoot)
	packageRoot := filepath.Join(projectRoot, "Packages", packageNameAlias)
	expectedCorePath := createBundledCorePlaceholderAt(t, packageRoot, packageNameAlias)
	previousExec := execCoreForDispatch
	executedCorePath := ""
	execCoreForDispatch = func(_ context.Context, localPath string, _ []string, _ string, _ io.Writer) int {
		executedCorePath = localPath
		return 0
	}
	t.Cleanup(func() {
		execCoreForDispatch = previousExec
	})
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := Run(context.Background(), []string{"--project-path", projectRoot, "skills", "list"}, &stdout, &stderr)

	if code != 0 {
		t.Fatalf("exit code mismatch: %d stdout=%s stderr=%s", code, stdout.String(), stderr.String())
	}
	normalizedExecutedCorePath, err := normalizeComparablePath(executedCorePath)
	if err != nil {
		t.Fatalf("failed to normalize executed core path: %v", err)
	}
	normalizedExpectedCorePath, err := normalizeComparablePath(expectedCorePath)
	if err != nil {
		t.Fatalf("failed to normalize expected core path: %v", err)
	}
	if normalizedExecutedCorePath != normalizedExpectedCorePath {
		t.Fatalf("core path mismatch: %s", executedCorePath)
	}
}

func TestRunProjectLocalCoreStatErrorReportsInternalError(t *testing.T) {
	// Verifies that only a missing project-local core is reported as install-required.
	projectRoot := t.TempDir()
	createUnityProject(t, projectRoot)
	localPath := projectLocalPath(projectRoot)
	if err := os.MkdirAll(filepath.Dir(localPath), 0o755); err != nil {
		t.Fatalf("failed to create project-local core directory: %v", err)
	}
	if err := os.Symlink(filepath.Base(localPath), localPath); err != nil {
		t.Skipf("symlink unavailable: %v", err)
	}
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := Run(context.Background(), []string{"--project-path", projectRoot, "list"}, &stdout, &stderr)

	if code != 1 {
		t.Fatalf("exit code mismatch: %d stdout=%s stderr=%s", code, stdout.String(), stderr.String())
	}
	var envelope cliErrorEnvelope
	if err := json.Unmarshal(stderr.Bytes(), &envelope); err != nil {
		t.Fatalf("stderr is not valid JSON: %v\n%s", err, stderr.String())
	}
	if envelope.Error.ErrorCode != errorCodeInternalError {
		t.Fatalf("error code mismatch: %#v", envelope.Error)
	}
}

func TestRunLaunchWithoutProjectLocalCoreUsesBootstrapLaunch(t *testing.T) {
	// Verifies that first-run launch does not fail at the project-local core existence check.
	projectRoot := t.TempDir()
	createUnityProject(t, projectRoot)
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := Run(context.Background(), []string{"--project-path", projectRoot, "launch"}, &stdout, &stderr)

	if code != 1 {
		t.Fatalf("exit code mismatch: %d stdout=%s stderr=%s", code, stdout.String(), stderr.String())
	}
	var envelope cliErrorEnvelope
	if err := json.Unmarshal(stderr.Bytes(), &envelope); err != nil {
		t.Fatalf("stderr is not valid JSON: %v\n%s", err, stderr.String())
	}
	if envelope.Error.ErrorCode == errorCodeProjectLocalCLIMissing {
		t.Fatalf("launch should bootstrap before requiring project-local core: %#v", envelope.Error)
	}
}

func TestRunLaunchBootstrapCleansStaleTempBeforeResolvingUnity(t *testing.T) {
	// Verifies that first-run launch preserves the core launch stale Temp cleanup contract.
	projectRoot := t.TempDir()
	createUnityProject(t, projectRoot)
	tempPath := filepath.Join(projectRoot, launchTempDirectoryName)
	if err := os.MkdirAll(tempPath, 0o755); err != nil {
		t.Fatalf("failed to create Temp: %v", err)
	}
	if err := os.WriteFile(filepath.Join(tempPath, unityLockfileName), []byte{}, 0o644); err != nil {
		t.Fatalf("failed to create UnityLockfile: %v", err)
	}
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := Run(context.Background(), []string{"--project-path", projectRoot, "launch"}, &stdout, &stderr)

	if code != 1 {
		t.Fatalf("exit code mismatch: %d stdout=%s stderr=%s", code, stdout.String(), stderr.String())
	}
	if _, err := os.Stat(tempPath); !os.IsNotExist(err) {
		t.Fatalf("Temp still exists after bootstrap cleanup: %v", err)
	}
}

func TestRunLaunchBootstrapFocusesRunningUnityWithoutDeletingTemp(t *testing.T) {
	// Verifies that bootstrap launch preserves the existing lifecycle shortcut for an active Unity process.
	projectRoot := t.TempDir()
	createUnityProject(t, projectRoot)
	tempPath := filepath.Join(projectRoot, launchTempDirectoryName)
	if err := os.MkdirAll(tempPath, 0o755); err != nil {
		t.Fatalf("failed to create Temp: %v", err)
	}
	if err := os.WriteFile(filepath.Join(tempPath, unityLockfileName), []byte{}, 0o644); err != nil {
		t.Fatalf("failed to create UnityLockfile: %v", err)
	}
	previousList := listUnityProcessesForLaunch
	listUnityProcessesForLaunch = func(context.Context) ([]unityProcess, error) {
		return []unityProcess{{pid: 123, projectPath: projectRoot}}, nil
	}
	t.Cleanup(func() {
		listUnityProcessesForLaunch = previousList
	})
	focusedPID := 0
	previousFocus := focusUnityProcessForLaunch
	focusUnityProcessForLaunch = func(_ context.Context, pid int) error {
		focusedPID = pid
		return nil
	}
	t.Cleanup(func() {
		focusUnityProcessForLaunch = previousFocus
	})
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := Run(context.Background(), []string{"--project-path", projectRoot, "launch"}, &stdout, &stderr)

	if code != 0 {
		t.Fatalf("exit code mismatch: %d stdout=%s stderr=%s", code, stdout.String(), stderr.String())
	}
	if _, err := os.Stat(tempPath); err != nil {
		t.Fatalf("Temp should remain while Unity is running: %v", err)
	}
	if focusedPID != 123 {
		t.Fatalf("focused PID mismatch: %d", focusedPID)
	}
	if !strings.Contains(stdout.String(), "Unity is already running") {
		t.Fatalf("stdout should report running Unity: %s", stdout.String())
	}
}

func TestRunLaunchQuitWithoutProjectLocalCoreReturnsSuccessWhenUnityIsNotRunning(t *testing.T) {
	// Verifies that bootstrap quit preserves the core launch no-running-process contract.
	projectRoot := t.TempDir()
	createUnityProject(t, projectRoot)
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := Run(context.Background(), []string{"--project-path", projectRoot, "launch", "--quit"}, &stdout, &stderr)

	if code != 0 {
		t.Fatalf("exit code mismatch: %d stdout=%s stderr=%s", code, stdout.String(), stderr.String())
	}
	if !strings.Contains(stdout.String(), "No Unity process is running") {
		t.Fatalf("stdout should report no running Unity: %s", stdout.String())
	}
}

func TestRunLaunchQuitWithoutProjectLocalCoreStopsRunningUnity(t *testing.T) {
	// Verifies that bootstrap quit can stop an active Unity process without project-local core.
	projectRoot := t.TempDir()
	createUnityProject(t, projectRoot)
	previousList := listUnityProcessesForLaunch
	listUnityProcessesForLaunch = func(context.Context) ([]unityProcess, error) {
		return []unityProcess{{pid: 123, projectPath: projectRoot}}, nil
	}
	t.Cleanup(func() {
		listUnityProcessesForLaunch = previousList
	})
	killedPID := 0
	previousKill := killUnityProcessForLaunch
	killUnityProcessForLaunch = func(pid int) error {
		killedPID = pid
		return nil
	}
	t.Cleanup(func() {
		killUnityProcessForLaunch = previousKill
	})
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := Run(context.Background(), []string{"--project-path", projectRoot, "launch", "--quit"}, &stdout, &stderr)

	if code != 0 {
		t.Fatalf("exit code mismatch: %d stdout=%s stderr=%s", code, stdout.String(), stderr.String())
	}
	if killedPID != 123 {
		t.Fatalf("killed PID mismatch: %d", killedPID)
	}
	if !strings.Contains(stdout.String(), "Unity process stopped") {
		t.Fatalf("stdout should report stopped Unity: %s", stdout.String())
	}
}

func TestRunLaunchRestartWithoutProjectLocalCoreStopsRunningUnityBeforeLaunch(t *testing.T) {
	// Verifies that bootstrap restart stops Unity before continuing with launch bootstrap.
	projectRoot := t.TempDir()
	createUnityProject(t, projectRoot)
	previousList := listUnityProcessesForLaunch
	listUnityProcessesForLaunch = func(context.Context) ([]unityProcess, error) {
		return []unityProcess{{pid: 123, projectPath: projectRoot}}, nil
	}
	t.Cleanup(func() {
		listUnityProcessesForLaunch = previousList
	})
	killedPID := 0
	previousKill := killUnityProcessForLaunch
	killUnityProcessForLaunch = func(pid int) error {
		killedPID = pid
		return nil
	}
	t.Cleanup(func() {
		killUnityProcessForLaunch = previousKill
	})
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := Run(context.Background(), []string{"--project-path", projectRoot, "launch", "--restart"}, &stdout, &stderr)

	if code != 1 {
		t.Fatalf("exit code mismatch: %d stdout=%s stderr=%s", code, stdout.String(), stderr.String())
	}
	if killedPID != 123 {
		t.Fatalf("killed PID mismatch: %d", killedPID)
	}
	var envelope cliErrorEnvelope
	if err := json.Unmarshal(stderr.Bytes(), &envelope); err != nil {
		t.Fatalf("stderr is not valid JSON: %v\n%s", err, stderr.String())
	}
	if envelope.Error.ErrorCode != errorCodeInternalError {
		t.Fatalf("error code mismatch: %#v", envelope.Error)
	}
}

func TestRunLaunchBootstrapRejectsUnknownOptionBeforeProjectResolution(t *testing.T) {
	// Verifies that malformed launch options are not interpreted as positional project paths.
	projectRoot := t.TempDir()
	createUnityProject(t, projectRoot)
	changeDirectory(t, projectRoot)
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := Run(context.Background(), []string{"launch", "--unknown", "foo"}, &stdout, &stderr)

	if code != 1 {
		t.Fatalf("exit code mismatch: %d stdout=%s stderr=%s", code, stdout.String(), stderr.String())
	}
	var envelope cliErrorEnvelope
	if err := json.Unmarshal(stderr.Bytes(), &envelope); err != nil {
		t.Fatalf("stderr is not valid JSON: %v\n%s", err, stderr.String())
	}
	if envelope.Error.ErrorCode != errorCodeInvalidArgument {
		t.Fatalf("error code mismatch: %#v", envelope.Error)
	}
}

func TestRunLaunchBootstrapRejectsInvalidMaxDepthBeforeProjectResolution(t *testing.T) {
	// Verifies that malformed launch option values fail before project discovery.
	changeDirectory(t, t.TempDir())
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := Run(context.Background(), []string{"launch", "--max-depth", "nope"}, &stdout, &stderr)

	if code != 1 {
		t.Fatalf("exit code mismatch: %d stdout=%s stderr=%s", code, stdout.String(), stderr.String())
	}
	var envelope cliErrorEnvelope
	if err := json.Unmarshal(stderr.Bytes(), &envelope); err != nil {
		t.Fatalf("stderr is not valid JSON: %v\n%s", err, stderr.String())
	}
	if envelope.Error.ErrorCode != errorCodeInvalidArgument {
		t.Fatalf("error code mismatch: %#v", envelope.Error)
	}
}

func TestParseLaunchBootstrapOptionsPreservesQuitAndRestart(t *testing.T) {
	// Verifies that bootstrap launch does not discard lifecycle flags before deciding whether to run.
	options, err := parseLaunchBootstrapOptions([]string{"--quit", "--restart"}, "")
	if err != nil {
		t.Fatalf("parseLaunchBootstrapOptions failed: %v", err)
	}
	if !options.quit {
		t.Fatal("quit flag was not preserved")
	}
	if !options.restart {
		t.Fatal("restart flag was not preserved")
	}
}

func TestParseLaunchBootstrapOptionsAcceptsNegativeMaxDepth(t *testing.T) {
	// Verifies that bootstrap launch accepts the same unlimited search depth value as core launch.
	_, err := parseLaunchBootstrapOptions([]string{"--max-depth", "-1"}, "")
	if err != nil {
		t.Fatalf("parseLaunchBootstrapOptions failed: %v", err)
	}
}

func TestParseLaunchBootstrapOptionsRejectsEmptyPlatformValueForm(t *testing.T) {
	// Verifies that bootstrap launch does not silently discard an empty platform value.
	_, err := parseLaunchBootstrapOptions([]string{"--platform="}, "")

	if err == nil {
		t.Fatal("empty platform value should fail")
	}
}

func TestParseLaunchBootstrapOptionsRejectsDuplicateProjectPaths(t *testing.T) {
	// Verifies that bootstrap launch preserves the core launch single project path contract.
	_, err := parseLaunchBootstrapOptions([]string{"/tmp/project-a", "/tmp/project-b"}, "")

	if err == nil {
		t.Fatal("duplicate project paths should fail")
	}
}

func TestParseLaunchBootstrapOptionsRejectsProjectPathAfterGlobalProjectPath(t *testing.T) {
	// Verifies that bootstrap launch rejects positional project paths when --project-path already selected one.
	_, err := parseLaunchBootstrapOptions([]string{"/tmp/project-b"}, "/tmp/project-a")

	if err == nil {
		t.Fatal("positional project path after --project-path should fail")
	}
}

func TestWaitForPathReturnsWhenPathExists(t *testing.T) {
	// Verifies that bootstrap launch can block on project-local core creation without timing out.
	targetPath := filepath.Join(t.TempDir(), "uloop-core")
	if err := os.WriteFile(targetPath, []byte("core"), 0o644); err != nil {
		t.Fatalf("failed to write target path: %v", err)
	}

	if err := waitForPath(context.Background(), targetPath, launchCoreReadyTimeout); err != nil {
		t.Fatalf("waitForPath failed: %v", err)
	}
}

func TestNewUnityLaunchCommandIsNotContextCancelable(t *testing.T) {
	// Verifies that bootstrap launch does not tie Unity process lifetime to the CLI context.
	command := newUnityLaunchCommand("/bin/echo", []string{"hello"})

	if command.Cancel != nil {
		t.Fatal("Unity launch command must not be killed when the CLI context is canceled")
	}
}

func TestParseProjectPathAcceptsValueForm(t *testing.T) {
	// Verifies that dispatcher parsing preserves the global --project-path contract.
	remaining, projectPath, err := parseProjectPath([]string{"--project-path=/tmp/project", "list"})
	if err != nil {
		t.Fatalf("parseProjectPath failed: %v", err)
	}

	if projectPath != "/tmp/project" {
		t.Fatalf("project path mismatch: %s", projectPath)
	}
	if len(remaining) != 1 || remaining[0] != "list" {
		t.Fatalf("remaining args mismatch: %#v", remaining)
	}
}

func TestFindUnityProjectRootWithinFindsNestedLaunchProject(t *testing.T) {
	// Verifies that global launch can still discover a nested Unity project before core dispatch.
	workspaceRoot := t.TempDir()
	projectRoot := filepath.Join(workspaceRoot, "nested", "Game")
	createUnityProject(t, projectRoot)

	resolved, err := resolveProjectRoot(workspaceRoot, "", []string{"launch"})
	if err != nil {
		t.Fatalf("resolveProjectRoot failed: %v", err)
	}
	if resolved != projectRoot {
		t.Fatalf("project root mismatch: %s", resolved)
	}
}

func TestFindUnityProjectRootWithinRejectsAmbiguousNestedProjects(t *testing.T) {
	// Verifies that global launch does not silently choose one nested Unity project.
	workspaceRoot := t.TempDir()
	createUnityProject(t, filepath.Join(workspaceRoot, "first", "Game"))
	createUnityProject(t, filepath.Join(workspaceRoot, "second", "Game"))

	_, err := resolveProjectRoot(workspaceRoot, "", []string{"launch"})

	if err == nil {
		t.Fatal("expected ambiguous project error")
	}
	if !strings.Contains(err.Error(), "--project-path") {
		t.Fatalf("error should ask for --project-path: %v", err)
	}
}

func TestResolveProjectRootForLaunchUsesPositionalProjectPath(t *testing.T) {
	// Verifies that launch can target a project outside the current directory before core dispatch.
	workspaceRoot := t.TempDir()
	projectRoot := filepath.Join(t.TempDir(), "Game")
	createUnityProject(t, projectRoot)

	resolved, err := resolveProjectRoot(workspaceRoot, "", []string{"launch", projectRoot})
	if err != nil {
		t.Fatalf("resolveProjectRoot failed: %v", err)
	}
	if resolved != projectRoot {
		t.Fatalf("project root mismatch: %s", resolved)
	}
}

func TestResolveProjectRootForLaunchSkipsOptionValuesBeforePositionalProjectPath(t *testing.T) {
	// Verifies that launch option values are not mistaken for the project path used for core dispatch.
	workspaceRoot := t.TempDir()
	projectRoot := filepath.Join(t.TempDir(), "Game")
	createUnityProject(t, projectRoot)

	resolved, err := resolveProjectRoot(workspaceRoot, "", []string{"launch", "--platform", "iOS", projectRoot})
	if err != nil {
		t.Fatalf("resolveProjectRoot failed: %v", err)
	}
	if resolved != projectRoot {
		t.Fatalf("project root mismatch: %s", resolved)
	}
}

func TestForwardedProjectLocalArgsForLaunchUsesResolvedPositionalProjectPath(t *testing.T) {
	// Verifies that Windows core dispatch does not re-resolve relative launch paths from the project root.
	projectRoot := filepath.Join(t.TempDir(), "Game")
	args := []string{"launch", "--platform", "iOS", "relative/Game"}

	forwarded := forwardedProjectLocalArgs(args, "", projectRoot)

	if len(forwarded) != len(args) {
		t.Fatalf("forwarded args length mismatch: %#v", forwarded)
	}
	if forwarded[3] != projectRoot {
		t.Fatalf("forwarded project path mismatch: %#v", forwarded)
	}
	if args[3] != "relative/Game" {
		t.Fatalf("input args were mutated: %#v", args)
	}
}

func TestRunCompletionScriptDoesNotRequireUnityProject(t *testing.T) {
	// Verifies that shell completion stays a dispatcher-owned global command.
	changeDirectory(t, t.TempDir())
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := Run(context.Background(), []string{"completion", "--shell", "bash"}, &stdout, &stderr)

	if code != 0 {
		t.Fatalf("exit code mismatch: %d stderr=%s", code, stderr.String())
	}
	if !strings.Contains(stdout.String(), "complete -F _uloop_completions uloop") {
		t.Fatalf("bash completion script mismatch: %s", stdout.String())
	}
}

func TestRunCompletionInstallDoesNotRequireUnityProject(t *testing.T) {
	// Verifies that installing shell completion does not depend on a project-local core binary.
	temporaryHome := t.TempDir()
	t.Setenv("HOME", temporaryHome)
	changeDirectory(t, t.TempDir())
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := Run(context.Background(), []string{"completion", "--shell", "zsh", "--install"}, &stdout, &stderr)

	if code != 0 {
		t.Fatalf("exit code mismatch: %d stderr=%s", code, stderr.String())
	}
	configPath := filepath.Join(temporaryHome, ".zshrc")
	content, err := os.ReadFile(configPath)
	if err != nil {
		t.Fatalf("failed to read completion config: %v", err)
	}
	if !strings.Contains(string(content), `eval "$(uloop completion --shell zsh)"`) {
		t.Fatalf("completion config mismatch: %s", string(content))
	}
	if !strings.Contains(stdout.String(), "Completion installed") {
		t.Fatalf("install output mismatch: %s", stdout.String())
	}
}

func TestRunCompletionListsDefaultToolCommandsWithoutProject(t *testing.T) {
	// Verifies that dispatcher completion preserves default Unity tool command suggestions outside a project.
	changeDirectory(t, t.TempDir())
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := Run(context.Background(), []string{"--list-commands"}, &stdout, &stderr)

	if code != 0 {
		t.Fatalf("exit code mismatch: %d stderr=%s", code, stderr.String())
	}
	output := stdout.String()
	for _, command := range []string{"compile", "get-logs", "run-tests"} {
		if !strings.Contains(output, command) {
			t.Fatalf("command %s was not listed: %s", command, output)
		}
	}
}

func TestRunCompletionListsDefaultToolOptionsWithoutProject(t *testing.T) {
	// Verifies that dispatcher completion preserves default Unity tool options outside a project.
	changeDirectory(t, t.TempDir())
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := Run(context.Background(), []string{"--list-options", "compile"}, &stdout, &stderr)

	if code != 0 {
		t.Fatalf("exit code mismatch: %d stderr=%s", code, stderr.String())
	}
	output := stdout.String()
	for _, option := range []string{"--force-recompile", "--wait-for-domain-reload"} {
		if !strings.Contains(output, option) {
			t.Fatalf("option %s was not listed: %s", option, output)
		}
	}
}

func TestRunCompletionListsCachedToolOptions(t *testing.T) {
	// Verifies that dispatcher completion probes can use project-local tool cache without importing core packages.
	projectRoot := t.TempDir()
	createUnityProject(t, projectRoot)
	createToolCache(t, projectRoot)
	changeDirectory(t, projectRoot)
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := Run(context.Background(), []string{"--list-options", "compile"}, &stdout, &stderr)

	if code != 0 {
		t.Fatalf("exit code mismatch: %d stderr=%s", code, stderr.String())
	}
	output := stdout.String()
	for _, option := range []string{"--force-recompile", "--no-wait-for-domain-reload"} {
		if !strings.Contains(output, option) {
			t.Fatalf("option %s was not listed: %s", option, output)
		}
	}
}

func TestRunCompletionListsCompletionCommandOptions(t *testing.T) {
	// Verifies that shell completion suggests dispatcher-owned completion flags.
	changeDirectory(t, t.TempDir())
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := Run(context.Background(), []string{"completion", "--list-options", "completion"}, &stdout, &stderr)

	if code != 0 {
		t.Fatalf("exit code mismatch: %d stderr=%s", code, stderr.String())
	}
	output := stdout.String()
	for _, option := range []string{"--install", "--shell"} {
		if !strings.Contains(output, option) {
			t.Fatalf("option %s was not listed: %s", option, output)
		}
	}
}

func TestRunCompletionListsLaunchNativeOptions(t *testing.T) {
	// Verifies that dispatcher-owned launch completion reflects bootstrap launch flags.
	changeDirectory(t, t.TempDir())
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := Run(context.Background(), []string{"--list-options", launchCommandName}, &stdout, &stderr)

	if code != 0 {
		t.Fatalf("exit code mismatch: %d stderr=%s", code, stderr.String())
	}
	output := stdout.String()
	for _, option := range []string{"--project-path", "--restart", "--quit", "--delete-recovery", "--platform", "--max-depth"} {
		if !strings.Contains(output, option) {
			t.Fatalf("launch option %s was not listed: %s", option, output)
		}
	}
}

func TestRunCompletionListsNoUpdateOptions(t *testing.T) {
	// Verifies that update completion does not advertise unsupported dispatcher options.
	changeDirectory(t, t.TempDir())
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := Run(context.Background(), []string{"--list-options", updateCommandName}, &stdout, &stderr)

	if code != 0 {
		t.Fatalf("exit code mismatch: %d stderr=%s", code, stderr.String())
	}
	if strings.TrimSpace(stdout.String()) != "" {
		t.Fatalf("update should not list options: %s", stdout.String())
	}
}

func TestDetectShellForPlatformPrefersPwshOnWindows(t *testing.T) {
	// Verifies that Windows completion install targets PowerShell 7 when it is available.
	shell := detectShellForPlatform("windows", "", func(name string) (string, error) {
		if name == "pwsh" {
			return filepath.Join("bin", "pwsh"), nil
		}
		return "", os.ErrNotExist
	})

	if shell != "pwsh" {
		t.Fatalf("shell mismatch: %s", shell)
	}
}

func TestDetectShellForPlatformHonorsWindowsPowerShellEnvironment(t *testing.T) {
	// Verifies that Windows PowerShell is not mistaken for a pwsh profile.
	shell := detectShellForPlatform("windows", `C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe`, func(string) (string, error) {
		return "", os.ErrNotExist
	})

	if shell != "powershell" {
		t.Fatalf("shell mismatch: %s", shell)
	}
}

func TestRunLauncherPrintsHelpAfterProjectPathOption(t *testing.T) {
	// Verifies that dispatcher help handles --project-path before dispatching to project-local core.
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := Run(
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

func TestRunLauncherPrintsProjectCachedToolsAfterProjectPathOption(t *testing.T) {
	// Verifies that dispatcher help shows cached tool commands for an explicit Unity project path.
	projectRoot := t.TempDir()
	createUnityProject(t, projectRoot)
	createProjectHelpToolCache(t, projectRoot)
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := Run(
		context.Background(),
		[]string{"--project-path", projectRoot, "-h"},
		&stdout,
		&stderr)

	assertProjectToolHelp(t, code, stdout.String(), stderr.String())
}

func TestRunLauncherPrintsProjectCachedToolsFromCurrentDirectory(t *testing.T) {
	// Verifies that dispatcher help shows cached tool commands when run inside a Unity project.
	projectRoot := t.TempDir()
	createUnityProject(t, projectRoot)
	createProjectHelpToolCache(t, projectRoot)
	changeDirectory(t, projectRoot)
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := Run(
		context.Background(),
		[]string{"-h"},
		&stdout,
		&stderr)

	assertProjectToolHelp(t, code, stdout.String(), stderr.String())
}

func TestLoadCachedToolsFiltersInternalSkillTools(t *testing.T) {
	// Verifies that dispatcher help and completion do not advertise tools the core will reject.
	projectRoot := t.TempDir()
	createUnityProject(t, projectRoot)
	createSkill(t, projectRoot, "Assets/Editor/InternalTool/Skill", `---
name: uloop-internal-tool
internal: true
---

# internal
`)
	createToolCache(t, projectRoot)

	cache, ok := loadCachedTools(projectRoot)
	if !ok {
		t.Fatal("expected cached tools")
	}
	if containsCachedTool(cache, "internal-tool") {
		t.Fatalf("internal tool was not filtered: %#v", cache.Tools)
	}
	if !containsCachedTool(cache, "compile") {
		t.Fatalf("public tool was filtered: %#v", cache.Tools)
	}
}

func TestLoadCachedToolsFiltersManifestLocalInternalSkillTools(t *testing.T) {
	// Verifies that dispatcher filtering follows manifest local package skill roots.
	projectRoot := t.TempDir()
	createUnityProject(t, projectRoot)
	localPackageRoot := filepath.Join(t.TempDir(), "LocalPackage")
	createSkillAt(t, filepath.Join(localPackageRoot, "Editor", "SecretTool", "Skill"), `---
name: uloop-secret
internal: true
---

# internal
`)
	writeProjectManifestData(t, projectRoot, map[string]any{
		"dependencies": map[string]string{
			"com.example.local": "file:" + localPackageRoot,
		},
	})
	writeToolCache(t, projectRoot, `{
  "version": "test",
  "tools": [
    {
      "name": "secret",
      "description": "internal",
      "inputSchema": {"type": "object", "properties": {}}
    },
    {
      "name": "public-tool",
      "description": "public",
      "inputSchema": {"type": "object", "properties": {}}
    }
  ]
}`)

	cache, ok := loadCachedTools(projectRoot)
	if !ok {
		t.Fatal("expected cached tools")
	}
	if containsCachedTool(cache, "secret") {
		t.Fatalf("manifest-local internal tool was not filtered: %#v", cache.Tools)
	}
	if !containsCachedTool(cache, "public-tool") {
		t.Fatalf("public tool was filtered: %#v", cache.Tools)
	}
}

func TestResolveLocalDependencyPathPreservesUNCPath(t *testing.T) {
	// Verifies that local package discovery does not corrupt UNC-style file URLs.
	projectRoot := filepath.Join("workspace", "Project")

	resolved := resolveLocalDependencyPath("file://server/share/Package", projectRoot)

	if resolved != "//server/share/Package" {
		t.Fatalf("UNC path mismatch: %s", resolved)
	}
}

func TestResolveLocalDependencyPathAcceptsTripleSlashAbsolutePath(t *testing.T) {
	// Verifies that file URLs with POSIX absolute paths keep resolving as absolute paths.
	projectRoot := filepath.Join("workspace", "Project")

	resolved := resolveLocalDependencyPath("file:///Applications/Package", projectRoot)

	if resolved != "/Applications/Package" {
		t.Fatalf("absolute path mismatch: %s", resolved)
	}
}

func TestReadInternalSkillToolNameDerivesMissingNameFromSkillDirectory(t *testing.T) {
	// Verifies that legacy internal skills without frontmatter names still hide their cached tools.
	projectRoot := t.TempDir()
	createSkill(t, projectRoot, "Assets/Editor/uloop-derived-internal/Skill", `---
internal: true
---

# internal
`)
	skillDirectory := filepath.Join(projectRoot, "Assets/Editor/uloop-derived-internal/Skill")

	toolName, ok := readInternalSkillToolName(skillDirectory)

	if !ok {
		t.Fatal("internal skill tool name was not discovered")
	}
	if toolName != "derived-internal" {
		t.Fatalf("tool name mismatch: %s", toolName)
	}
}

func TestReadInternalSkillToolNameAcceptsSingleQuotedFrontmatter(t *testing.T) {
	// Verifies that YAML single-quoted frontmatter does not expose internal tools.
	projectRoot := t.TempDir()
	createSkill(t, projectRoot, "Assets/Editor/Secret/Skill", `---
name: 'uloop-secret'
internal: 'true'
---

# internal
`)
	skillDirectory := filepath.Join(projectRoot, "Assets/Editor/Secret/Skill")

	toolName, ok := readInternalSkillToolName(skillDirectory)

	if !ok {
		t.Fatal("internal skill tool name was not discovered")
	}
	if toolName != "secret" {
		t.Fatalf("tool name mismatch: %s", toolName)
	}
}

func TestResolveExistingUnityExecutablePathReportsSearchedCandidates(t *testing.T) {
	// Verifies that missing Unity installs fail before command execution with actionable paths.
	missingPath := filepath.Join(t.TempDir(), "Unity")

	_, err := resolveExistingUnityExecutablePath("9999.9.9f9", []string{missingPath})

	if err == nil {
		t.Fatal("expected missing Unity executable error")
	}
	if !strings.Contains(err.Error(), missingPath) {
		t.Fatalf("error should include searched candidate: %v", err)
	}
}

func TestUpdateCommandForDarwinDownloadsBeforeExecutingInstaller(t *testing.T) {
	// Verifies that curl failures do not become successful empty shell executions.
	commandName, args, err := updateCommandForOS("darwin")
	if err != nil {
		t.Fatalf("updateCommandForOS failed: %v", err)
	}

	if commandName != "sh" {
		t.Fatalf("command mismatch: %s", commandName)
	}
	joinedArgs := strings.Join(args, " ")
	for _, expected := range []string{"mktemp", "curl -fSL", "-o \"$tmp\"", "sh \"$tmp\"", "exit $ec"} {
		if !strings.Contains(joinedArgs, expected) {
			t.Fatalf("update command missing %q: %s", expected, joinedArgs)
		}
	}
	if strings.Contains(joinedArgs, "curl -fsSL") || strings.Contains(joinedArgs, "| sh") {
		t.Fatalf("update command still hides curl failures: %s", joinedArgs)
	}
}

func createProjectHelpToolCache(t *testing.T, projectRoot string) {
	t.Helper()

	cachePath := filepath.Join(projectRoot, ".uloop", "tools.json")
	if err := os.MkdirAll(filepath.Dir(cachePath), 0o755); err != nil {
		t.Fatalf("failed to create tool cache directory: %v", err)
	}
	content := `{
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
}`
	if err := os.WriteFile(cachePath, []byte(content), 0o644); err != nil {
		t.Fatalf("failed to write tool cache: %v", err)
	}
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

func createUnityProject(t *testing.T, projectRoot string) {
	t.Helper()

	if err := os.MkdirAll(filepath.Join(projectRoot, assetsDirectoryName), 0o755); err != nil {
		t.Fatalf("failed to create Assets: %v", err)
	}
	if err := os.MkdirAll(filepath.Join(projectRoot, projectSettingsDirectory), 0o755); err != nil {
		t.Fatalf("failed to create ProjectSettings: %v", err)
	}
	if err := os.WriteFile(
		filepath.Join(projectRoot, projectSettingsDirectory, "ProjectVersion.txt"),
		[]byte("m_EditorVersion: 9999.9.9f9\n"),
		0o644,
	); err != nil {
		t.Fatalf("failed to write ProjectVersion.txt: %v", err)
	}
}

func createBundledCorePlaceholder(t *testing.T, projectRoot string) string {
	t.Helper()

	packageRoot := filepath.Join(projectRoot, "Packages", "src")
	return createBundledCorePlaceholderAt(t, packageRoot, packageName)
}

func createBundledCorePlaceholderAt(t *testing.T, packageRoot string, manifestPackageName string) string {
	t.Helper()

	if err := os.MkdirAll(packageRoot, 0o755); err != nil {
		t.Fatalf("failed to create package root: %v", err)
	}
	manifest := `{"name":"` + manifestPackageName + `"}`
	if err := os.WriteFile(filepath.Join(packageRoot, "package.json"), []byte(manifest), 0o644); err != nil {
		t.Fatalf("failed to write package manifest: %v", err)
	}
	corePath := filepath.Join(packageRoot, "Cli~", "Core~", "dist", runtime.GOOS+"-"+runtime.GOARCH, coreBinaryName())
	if err := os.MkdirAll(filepath.Dir(corePath), 0o755); err != nil {
		t.Fatalf("failed to create bundled core directory: %v", err)
	}
	if err := os.WriteFile(corePath, []byte("placeholder"), 0o755); err != nil {
		t.Fatalf("failed to write bundled core placeholder: %v", err)
	}
	return corePath
}

func writeProjectManifest(t *testing.T, projectRoot string, content string) {
	t.Helper()

	manifestPath := filepath.Join(projectRoot, "Packages", manifestFileName)
	if err := os.MkdirAll(filepath.Dir(manifestPath), 0o755); err != nil {
		t.Fatalf("failed to create manifest directory: %v", err)
	}
	if err := os.WriteFile(manifestPath, []byte(content), 0o644); err != nil {
		t.Fatalf("failed to write project manifest: %v", err)
	}
}

func writeProjectManifestData(t *testing.T, projectRoot string, data any) {
	t.Helper()

	content, err := json.MarshalIndent(data, "", "  ")
	if err != nil {
		t.Fatalf("failed to encode project manifest: %v", err)
	}
	writeProjectManifest(t, projectRoot, string(content))
}

func writeToolCache(t *testing.T, projectRoot string, content string) {
	t.Helper()

	cachePath := filepath.Join(projectRoot, ".uloop", "tools.json")
	if err := os.MkdirAll(filepath.Dir(cachePath), 0o755); err != nil {
		t.Fatalf("failed to create tool cache directory: %v", err)
	}
	if err := os.WriteFile(cachePath, []byte(content), 0o644); err != nil {
		t.Fatalf("failed to write tool cache: %v", err)
	}
}

func createToolCache(t *testing.T, projectRoot string) {
	t.Helper()

	cachePath := filepath.Join(projectRoot, ".uloop", "tools.json")
	if err := os.MkdirAll(filepath.Dir(cachePath), 0o755); err != nil {
		t.Fatalf("failed to create tool cache directory: %v", err)
	}
	content := `{
  "tools": [
    {
      "name": "internal-tool",
      "description": "Internal",
      "inputSchema": {
        "properties": {}
      }
    },
    {
      "name": "compile",
      "description": "Compile Unity scripts",
      "inputSchema": {
        "properties": {
          "forceRecompile": { "type": "boolean" },
          "waitForDomainReload": { "type": "boolean", "default": true }
        }
      }
    }
  ]
}`
	if err := os.WriteFile(cachePath, []byte(content), 0o644); err != nil {
		t.Fatalf("failed to write tool cache: %v", err)
	}
}

func createSkill(t *testing.T, projectRoot string, relativePath string, content string) {
	t.Helper()

	createSkillAt(t, filepath.Join(projectRoot, relativePath), content)
}

func createSkillAt(t *testing.T, skillDirectory string, content string) {
	t.Helper()

	skillPath := filepath.Join(skillDirectory, skillFileName)
	if err := os.MkdirAll(filepath.Dir(skillPath), 0o755); err != nil {
		t.Fatalf("failed to create skill directory: %v", err)
	}
	if err := os.WriteFile(skillPath, []byte(content), 0o644); err != nil {
		t.Fatalf("failed to write skill: %v", err)
	}
}

func containsCachedTool(cache cachedTools, name string) bool {
	for _, tool := range cache.Tools {
		if tool.Name == name {
			return true
		}
	}
	return false
}

func changeDirectory(t *testing.T, path string) {
	t.Helper()

	currentDirectory, err := os.Getwd()
	if err != nil {
		t.Fatalf("failed to read current directory: %v", err)
	}
	if err := os.Chdir(path); err != nil {
		t.Fatalf("failed to change directory: %v", err)
	}
	t.Cleanup(func() {
		if err := os.Chdir(currentDirectory); err != nil {
			t.Fatalf("failed to restore directory: %v", err)
		}
	})
}
