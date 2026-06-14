package cli

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"
)

func TestParseLaunchOptionsSupportsCoreFlags(t *testing.T) {
	options, err := parseLaunchOptions(
		[]string{
			"--restart",
			"--delete-recovery",
			"--platform", "Android",
			"--max-depth", "-1",
			"/tmp/project",
		},
		"",
	)
	if err != nil {
		t.Fatalf("parseLaunchOptions failed: %v", err)
	}

	if !options.restart {
		t.Fatal("restart flag was not parsed")
	}
	if !options.deleteRecovery {
		t.Fatal("delete recovery flag was not parsed")
	}
	if options.platform != "Android" {
		t.Fatalf("platform mismatch: %s", options.platform)
	}
	if options.maxDepth != -1 {
		t.Fatalf("max depth mismatch: %d", options.maxDepth)
	}
	if options.projectPath != "/tmp/project" {
		t.Fatalf("project path mismatch: %s", options.projectPath)
	}
}

func TestParseLaunchOptionsRejectsUnityHubRegistration(t *testing.T) {
	_, err := parseLaunchOptions([]string{"--add-unity-hub"}, "")
	if err == nil {
		t.Fatal("expected Unity Hub registration option error")
	}
}

func TestParseLaunchOptionsRejectsEmptyPlatformEqualsValue(t *testing.T) {
	// Verifies --platform= cannot silently drop the requested build target.
	_, err := parseLaunchOptions([]string{"--platform="}, "")

	if err == nil {
		t.Fatal("expected empty platform value error")
	}
}

func TestReadUnityEditorVersion(t *testing.T) {
	projectRoot := createLaunchTestProject(t)
	projectSettings := filepath.Join(projectRoot, "ProjectSettings")
	if err := os.WriteFile(
		filepath.Join(projectSettings, "ProjectVersion.txt"),
		[]byte("m_EditorVersion: 6000.0.1f1\n"),
		0o644,
	); err != nil {
		t.Fatalf("failed to write ProjectVersion.txt: %v", err)
	}

	version, err := readUnityEditorVersion(projectRoot)
	if err != nil {
		t.Fatalf("readUnityEditorVersion failed: %v", err)
	}
	if version != "6000.0.1f1" {
		t.Fatalf("version mismatch: %s", version)
	}
}

func TestResolveLaunchProjectRootAcceptsUnityProjectWithoutUloopSettings(t *testing.T) {
	projectRoot := createLaunchTestProject(t)

	resolved, err := resolveLaunchProjectRoot(projectRoot, launchOptions{})
	if err != nil {
		t.Fatalf("resolveLaunchProjectRoot failed: %v", err)
	}
	if resolved != projectRoot {
		t.Fatalf("project root mismatch: %s", resolved)
	}
}

func TestRunLaunchQuitDoesNotLaunchWhenUnityIsNotRunning(t *testing.T) {
	projectRoot := createLaunchTestProject(t)
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := runLaunch(
		context.Background(),
		launchOptions{quit: true, projectPath: projectRoot},
		projectRoot,
		&stdout,
		&stderr,
	)

	if code != 0 {
		t.Fatalf("exit code mismatch: %d stderr=%s", code, stderr.String())
	}
	if !strings.Contains(stdout.String(), "No Unity process is running") {
		t.Fatalf("stdout mismatch: %s", stdout.String())
	}
}

func TestRunLaunchWritesReadyResponseAfterToolReadiness(t *testing.T) {
	// Verifies launch reports an explicit ready payload after Unity accepts tool requests.
	originalFinder := findRunningUnityProcessForLaunch
	originalResolver := resolveUnityExecutablePathForLaunch
	originalStartupMarkerWait := waitForUnityStartupMarkerForLaunch
	originalReadinessWait := waitForToolReadinessForLaunch
	findRunningUnityProcessForLaunch = func(context.Context, string) (*unityProcess, error) {
		return nil, nil
	}
	resolveUnityExecutablePathForLaunch = func(string) (string, error) {
		return "/usr/bin/true", nil
	}
	waitForUnityStartupMarkerForLaunch = func(context.Context, string, time.Duration, time.Duration) error {
		return nil
	}
	waitForToolReadinessForLaunch = func(context.Context, string, time.Duration) error {
		return nil
	}
	t.Cleanup(func() {
		findRunningUnityProcessForLaunch = originalFinder
		resolveUnityExecutablePathForLaunch = originalResolver
		waitForUnityStartupMarkerForLaunch = originalStartupMarkerWait
		waitForToolReadinessForLaunch = originalReadinessWait
	})

	projectRoot := createLaunchTestProject(t)
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := runLaunch(
		context.Background(),
		launchOptions{projectPath: projectRoot},
		projectRoot,
		&stdout,
		&stderr,
	)

	if code != 0 {
		t.Fatalf("exit code mismatch: %d stderr=%s", code, stderr.String())
	}
	output := stdout.String()
	for _, expected := range []string{
		"Waiting for Unity CLI Loop server readiness...",
		`"Success": true`,
		`"Ready": true`,
		`"ServerReady": true`,
		`"ProjectIpcReady": true`,
		`"Message": "Unity CLI Loop is ready."`,
	} {
		if !strings.Contains(output, expected) {
			t.Fatalf("launch output missing %q:\n%s", expected, output)
		}
	}
}

func TestWaitForLaunchReadinessUsesLaunchTimeout(t *testing.T) {
	// Verifies launch gets a longer startup window without changing shared readiness defaults.
	originalReadinessWait := waitForToolReadinessForLaunch
	var capturedTimeout time.Duration
	waitForToolReadinessForLaunch = func(ctx context.Context, projectRoot string, timeout time.Duration) error {
		capturedTimeout = timeout
		return nil
	}
	t.Cleanup(func() {
		waitForToolReadinessForLaunch = originalReadinessWait
	})

	if err := waitForLaunchReadiness(context.Background(), t.TempDir()); err != nil {
		t.Fatalf("waitForLaunchReadiness failed: %v", err)
	}
	if capturedTimeout != launchReadinessTimeout {
		t.Fatalf("launch readiness timeout mismatch: %s", capturedTimeout)
	}
}

func TestWaitForLaunchReadinessWrapsStartupTimeout(t *testing.T) {
	// Verifies launch timeout errors receive the launch-specific startup classification.
	originalReadinessWait := waitForToolReadinessForLaunch
	waitForToolReadinessForLaunch = func(ctx context.Context, projectRoot string, timeout time.Duration) error {
		return errors.New("timed out waiting for Unity tool readiness")
	}
	t.Cleanup(func() {
		waitForToolReadinessForLaunch = originalReadinessWait
	})

	err := waitForLaunchReadiness(context.Background(), t.TempDir())

	var startupErr launchStartupTimeoutError
	if !errors.As(err, &startupErr) {
		t.Fatalf("expected launch startup timeout error, got %v", err)
	}
}

func TestRunLaunchWritesStructuredResponseForExistingUnityProcess(t *testing.T) {
	// Verifies launch reports machine-readable readiness when Unity was already running.
	originalFinder := findRunningUnityProcessForLaunch
	originalFocus := focusUnityProcessForLaunch
	originalReadinessWait := waitForToolReadinessForLaunch
	readinessChecked := false
	findRunningUnityProcessForLaunch = func(context.Context, string) (*unityProcess, error) {
		return &unityProcess{pid: 111}, nil
	}
	focusUnityProcessForLaunch = func(context.Context, int) error {
		return nil
	}
	waitForToolReadinessForLaunch = func(context.Context, string, time.Duration) error {
		readinessChecked = true
		return nil
	}
	t.Cleanup(func() {
		findRunningUnityProcessForLaunch = originalFinder
		focusUnityProcessForLaunch = originalFocus
		waitForToolReadinessForLaunch = originalReadinessWait
	})

	projectRoot := createLaunchTestProject(t)
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := runLaunch(
		context.Background(),
		launchOptions{projectPath: projectRoot},
		projectRoot,
		&stdout,
		&stderr,
	)

	if code != 0 {
		t.Fatalf("exit code mismatch: %d stderr=%s", code, stderr.String())
	}
	if !readinessChecked {
		t.Fatal("launch should verify tool readiness before reporting an existing Unity process as ready")
	}
	response := decodeLaunchResponseFromOutput(t, stdout.String())
	if !response.Success || !response.Ready || !response.ServerReady || !response.ProjectIpcReady {
		t.Fatalf("ready flags mismatch: %#v", response)
	}
	if !response.AlreadyRunning || response.Launched || response.Restarted {
		t.Fatalf("process state flags mismatch: %#v", response)
	}
	if response.CurrentProcessId == nil || *response.CurrentProcessId != 111 {
		t.Fatalf("current process id mismatch: %#v", response.CurrentProcessId)
	}
	if response.PreviousProcessId != nil {
		t.Fatalf("existing launch should not report a previous process: %#v", response.PreviousProcessId)
	}
}

func TestRunLaunchRestartWritesProcessTransitionResponse(t *testing.T) {
	// Verifies restart reports both the stopped process and the newly launched process.
	originalFinder := findRunningUnityProcessForLaunch
	originalKiller := killUnityProcessForLaunch
	originalResolver := resolveUnityExecutablePathForLaunch
	originalExitWait := waitForUnityProcessExitForLaunch
	originalStartupMarkerWait := waitForUnityStartupMarkerForLaunch
	originalReadinessWait := waitForToolReadinessForLaunch
	killedPid := 0
	waitedPid := 0
	findRunningUnityProcessForLaunch = func(context.Context, string) (*unityProcess, error) {
		return &unityProcess{pid: 222}, nil
	}
	killUnityProcessForLaunch = func(pid int) error {
		killedPid = pid
		return nil
	}
	waitForUnityProcessExitForLaunch = func(ctx context.Context, projectRoot string, pid int, pollInterval time.Duration, timeout time.Duration) error {
		waitedPid = pid
		return nil
	}
	resolveUnityExecutablePathForLaunch = func(string) (string, error) {
		return "/usr/bin/true", nil
	}
	waitForUnityStartupMarkerForLaunch = func(context.Context, string, time.Duration, time.Duration) error {
		return nil
	}
	waitForToolReadinessForLaunch = func(context.Context, string, time.Duration) error {
		return nil
	}
	t.Cleanup(func() {
		findRunningUnityProcessForLaunch = originalFinder
		killUnityProcessForLaunch = originalKiller
		resolveUnityExecutablePathForLaunch = originalResolver
		waitForUnityProcessExitForLaunch = originalExitWait
		waitForUnityStartupMarkerForLaunch = originalStartupMarkerWait
		waitForToolReadinessForLaunch = originalReadinessWait
	})

	projectRoot := createLaunchTestProject(t)
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := runLaunch(
		context.Background(),
		launchOptions{projectPath: projectRoot, restart: true},
		projectRoot,
		&stdout,
		&stderr,
	)

	if code != 0 {
		t.Fatalf("exit code mismatch: %d stderr=%s", code, stderr.String())
	}
	if killedPid != 222 {
		t.Fatalf("restart killed pid mismatch: %d", killedPid)
	}
	if waitedPid != 222 {
		t.Fatalf("restart waited pid mismatch: %d", waitedPid)
	}
	response := decodeLaunchResponseFromOutput(t, stdout.String())
	if !response.Success || !response.Ready || !response.ServerReady || !response.ProjectIpcReady {
		t.Fatalf("ready flags mismatch: %#v", response)
	}
	if !response.Launched || !response.Restarted || response.AlreadyRunning {
		t.Fatalf("process state flags mismatch: %#v", response)
	}
	if response.PreviousProcessId == nil || *response.PreviousProcessId != 222 {
		t.Fatalf("previous process id mismatch: %#v", response.PreviousProcessId)
	}
	if response.CurrentProcessId == nil || *response.CurrentProcessId <= 0 {
		t.Fatalf("current process id mismatch: %#v", response.CurrentProcessId)
	}
}

func TestRunLaunchQuitWaitsForKilledUnityProcess(t *testing.T) {
	// Verifies quit does not report success before the killed Unity process disappears.
	originalFinder := findRunningUnityProcessForLaunch
	originalKiller := killUnityProcessForLaunch
	originalExitWait := waitForUnityProcessExitForLaunch
	waitedPid := 0
	findRunningUnityProcessForLaunch = func(context.Context, string) (*unityProcess, error) {
		return &unityProcess{pid: 333}, nil
	}
	killUnityProcessForLaunch = func(pid int) error {
		return nil
	}
	waitForUnityProcessExitForLaunch = func(ctx context.Context, projectRoot string, pid int, pollInterval time.Duration, timeout time.Duration) error {
		waitedPid = pid
		return nil
	}
	t.Cleanup(func() {
		findRunningUnityProcessForLaunch = originalFinder
		killUnityProcessForLaunch = originalKiller
		waitForUnityProcessExitForLaunch = originalExitWait
	})

	projectRoot := createLaunchTestProject(t)
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := runLaunch(
		context.Background(),
		launchOptions{projectPath: projectRoot, quit: true},
		projectRoot,
		&stdout,
		&stderr,
	)

	if code != 0 {
		t.Fatalf("exit code mismatch: %d stderr=%s", code, stderr.String())
	}
	if waitedPid != 333 {
		t.Fatalf("quit waited pid mismatch: %d", waitedPid)
	}
	response := decodeLaunchResponseFromOutput(t, stdout.String())
	if !response.Success || !response.Quit {
		t.Fatalf("quit response mismatch: %#v", response)
	}
	if response.PreviousProcessId == nil || *response.PreviousProcessId != 333 {
		t.Fatalf("previous process id mismatch: %#v", response.PreviousProcessId)
	}
}

func TestRunLaunchRestartReportsProcessExitWaitFailure(t *testing.T) {
	// Verifies restart stops before Temp cleanup when the killed Unity process still holds files.
	originalFinder := findRunningUnityProcessForLaunch
	originalKiller := killUnityProcessForLaunch
	originalExitWait := waitForUnityProcessExitForLaunch
	originalResolver := resolveUnityExecutablePathForLaunch
	resolverCalled := false
	findRunningUnityProcessForLaunch = func(context.Context, string) (*unityProcess, error) {
		return &unityProcess{pid: 444}, nil
	}
	killUnityProcessForLaunch = func(pid int) error {
		return nil
	}
	waitForUnityProcessExitForLaunch = func(ctx context.Context, projectRoot string, pid int, pollInterval time.Duration, timeout time.Duration) error {
		return errors.New("still exiting")
	}
	resolveUnityExecutablePathForLaunch = func(string) (string, error) {
		resolverCalled = true
		return "/usr/bin/true", nil
	}
	t.Cleanup(func() {
		findRunningUnityProcessForLaunch = originalFinder
		killUnityProcessForLaunch = originalKiller
		waitForUnityProcessExitForLaunch = originalExitWait
		resolveUnityExecutablePathForLaunch = originalResolver
	})

	projectRoot := createLaunchTestProject(t)
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := runLaunch(
		context.Background(),
		launchOptions{projectPath: projectRoot, restart: true},
		projectRoot,
		&stdout,
		&stderr,
	)

	if code != 1 {
		t.Fatalf("expected failure, got %d stdout=%s", code, stdout.String())
	}
	if resolverCalled {
		t.Fatal("restart should not launch a new Unity process before the old one exits")
	}
	if !strings.Contains(stderr.String(), "still exiting") {
		t.Fatalf("stderr should include wait failure: %s", stderr.String())
	}
}

// Verifies launch logs when it focuses an already-running Unity process.
func TestRunLaunchWritesExistingFocusSuccessVibeLog(t *testing.T) {
	enableCliVibeLog(t)

	originalFinder := findRunningUnityProcessForLaunch
	originalFocus := focusUnityProcessForLaunch
	originalReadinessWait := waitForToolReadinessForLaunch
	findRunningUnityProcessForLaunch = func(context.Context, string) (*unityProcess, error) {
		return &unityProcess{pid: 111}, nil
	}
	focusUnityProcessForLaunch = func(context.Context, int) error {
		return nil
	}
	waitForToolReadinessForLaunch = func(context.Context, string, time.Duration) error {
		return nil
	}
	t.Cleanup(func() {
		findRunningUnityProcessForLaunch = originalFinder
		focusUnityProcessForLaunch = originalFocus
		waitForToolReadinessForLaunch = originalReadinessWait
	})

	projectRoot := createLaunchTestProject(t)
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := runLaunch(
		context.Background(),
		launchOptions{projectPath: projectRoot},
		projectRoot,
		&stdout,
		&stderr,
	)

	if code != 0 {
		t.Fatalf("exit code mismatch: %d stderr=%s", code, stderr.String())
	}
	logContent := readOnlyCliVibeLog(t, projectRoot)
	for _, expected := range []string{
		`"operation":"cli_launch_existing_focus_attempt"`,
		`"operation":"cli_launch_existing_focus_success"`,
		`"command":"launch"`,
		`"pid":111`,
	} {
		if !strings.Contains(logContent, expected) {
			t.Fatalf("CLI Vibe log missing %q:\n%s", expected, logContent)
		}
	}
}

// Verifies launch logs focus failures without changing its existing success behavior.
func TestRunLaunchWritesExistingFocusFailureVibeLog(t *testing.T) {
	enableCliVibeLog(t)

	originalFinder := findRunningUnityProcessForLaunch
	originalFocus := focusUnityProcessForLaunch
	originalReadinessWait := waitForToolReadinessForLaunch
	findRunningUnityProcessForLaunch = func(context.Context, string) (*unityProcess, error) {
		return &unityProcess{pid: 222}, nil
	}
	focusUnityProcessForLaunch = func(context.Context, int) error {
		return fmt.Errorf("activation denied")
	}
	waitForToolReadinessForLaunch = func(context.Context, string, time.Duration) error {
		return nil
	}
	t.Cleanup(func() {
		findRunningUnityProcessForLaunch = originalFinder
		focusUnityProcessForLaunch = originalFocus
		waitForToolReadinessForLaunch = originalReadinessWait
	})

	projectRoot := createLaunchTestProject(t)
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := runLaunch(
		context.Background(),
		launchOptions{projectPath: projectRoot},
		projectRoot,
		&stdout,
		&stderr,
	)

	if code != 0 {
		t.Fatalf("launch should preserve existing focus failure behavior: code=%d stderr=%s", code, stderr.String())
	}
	logContent := readOnlyCliVibeLog(t, projectRoot)
	for _, expected := range []string{
		`"operation":"cli_launch_existing_focus_attempt"`,
		`"operation":"cli_launch_existing_focus_failed"`,
		`"command":"launch"`,
		`"pid":222`,
		`"focusError":"activation denied"`,
	} {
		if !strings.Contains(logContent, expected) {
			t.Fatalf("CLI Vibe log missing %q:\n%s", expected, logContent)
		}
	}
}

// Verifies that readiness probes exercise the same foreground warmup path as user executions.
func TestExecuteDynamicCodeReadinessProbeParamsUseForegroundWarmup(t *testing.T) {
	params := executeDynamicCodeReadinessProbeParams()

	if params["YieldToForegroundRequests"] != false {
		t.Fatalf("readiness probe should use foreground warmup: %#v", params["YieldToForegroundRequests"])
	}
	if params[domainReloadWaitParam] != false {
		t.Fatalf("readiness probe should not wait for its own reload check: %#v", params[domainReloadWaitParam])
	}
}

func TestNewUnityLaunchCommandIsNotContextCancelable(t *testing.T) {
	command := newUnityLaunchCommand("/bin/echo", []string{"hello"})

	if command.Cancel != nil {
		t.Fatal("Unity launch command must not be killed when the CLI context is canceled")
	}
}

func TestCleanStaleUnityTempDeletesTempWhenLockfileExists(t *testing.T) {
	projectRoot := createLaunchTestProject(t)
	tempPath := filepath.Join(projectRoot, launchTempDirectoryName)
	if err := os.MkdirAll(tempPath, 0o755); err != nil {
		t.Fatalf("failed to create Temp: %v", err)
	}
	if err := os.WriteFile(filepath.Join(tempPath, unityLockfileName), []byte{}, 0o644); err != nil {
		t.Fatalf("failed to create UnityLockfile: %v", err)
	}

	removed, err := cleanStaleUnityTemp(projectRoot)
	if err != nil {
		t.Fatalf("cleanStaleUnityTemp failed: %v", err)
	}
	if !removed {
		t.Fatal("expected stale Temp removal")
	}
	if _, err := os.Stat(tempPath); !os.IsNotExist(err) {
		t.Fatalf("Temp still exists after cleanup: %v", err)
	}
}

func TestWaitForUnityStartupMarkerReturnsAfterLockfileAppears(t *testing.T) {
	// Verifies the startup marker wait returns as soon as Unity creates the lockfile.
	projectRoot := createLaunchTestProject(t)
	lockfilePath := unityLockfilePath(projectRoot)
	errChan := make(chan error, 1)

	go func() {
		errChan <- waitForUnityStartupMarkerOrTimeout(context.Background(), lockfilePath, time.Millisecond, time.Second)
	}()

	if err := os.MkdirAll(filepath.Dir(lockfilePath), 0o755); err != nil {
		t.Fatalf("failed to create Temp: %v", err)
	}
	if err := os.WriteFile(lockfilePath, []byte{}, 0o644); err != nil {
		t.Fatalf("failed to create UnityLockfile: %v", err)
	}

	select {
	case err := <-errChan:
		if err != nil {
			t.Fatalf("waitForUnityStartupMarkerOrTimeout failed: %v", err)
		}
	case <-time.After(time.Second):
		t.Fatal("timed out waiting for waitForUnityStartupMarkerOrTimeout")
	}
}

func TestWaitForUnityStartupMarkerReturnsNilWhenLockfileDoesNotAppear(t *testing.T) {
	// Verifies the startup marker is only a short hint before the real readiness wait.
	lockfilePath := filepath.Join(t.TempDir(), launchTempDirectoryName, unityLockfileName)

	err := waitForUnityStartupMarkerOrTimeout(context.Background(), lockfilePath, time.Millisecond, time.Millisecond)
	if err != nil {
		t.Fatalf("missing startup marker should not fail launch: %v", err)
	}
}

func TestResolveExistingUnityExecutablePathReportsSearchedCandidates(t *testing.T) {
	// Verifies missing Unity installs fail before command execution with actionable paths.
	missingPath := filepath.Join(t.TempDir(), "Unity")

	_, err := resolveExistingUnityExecutablePath("9999.9.9f9", []string{missingPath})

	if err == nil {
		t.Fatal("expected missing Unity executable error")
	}
	if !strings.Contains(err.Error(), missingPath) {
		t.Fatalf("error should include searched candidate: %v", err)
	}
}

func createLaunchTestProject(t *testing.T) string {
	t.Helper()

	projectRoot := t.TempDir()
	for _, directory := range []string{"Assets", "ProjectSettings"} {
		if err := os.MkdirAll(filepath.Join(projectRoot, directory), 0o755); err != nil {
			t.Fatalf("failed to create %s: %v", directory, err)
		}
	}
	return projectRoot
}

func decodeLaunchResponseFromOutput(t *testing.T, output string) launchReadyResponse {
	t.Helper()

	jsonStart := strings.LastIndex(output, "{")
	if jsonStart < 0 {
		t.Fatalf("launch output did not contain a JSON object:\n%s", output)
	}
	var response launchReadyResponse
	if err := json.Unmarshal([]byte(output[jsonStart:]), &response); err != nil {
		t.Fatalf("failed to decode launch JSON: %v\n%s", err, output)
	}
	return response
}

// Verifies launch survives a blocked process scan (e.g. sandboxed /bin/ps) by
// probing the project IPC and reporting the running Editor instead of failing.
func TestRunLaunchFallsBackToIpcProbeWhenProcessScanFails(t *testing.T) {
	originalFinder := findRunningUnityProcessForLaunch
	originalProbe := probeProjectIpcForLaunchFallback
	findRunningUnityProcessForLaunch = func(context.Context, string) (*unityProcess, error) {
		return nil, errors.New("failed to retrieve Unity process list: /bin/ps: operation not permitted")
	}
	probeProjectIpcForLaunchFallback = func(context.Context, string) error {
		return nil
	}
	t.Cleanup(func() {
		findRunningUnityProcessForLaunch = originalFinder
		probeProjectIpcForLaunchFallback = originalProbe
	})

	projectRoot := createLaunchTestProject(t)
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := runLaunch(context.Background(), launchOptions{projectPath: projectRoot}, projectRoot, &stdout, &stderr)

	if code != 0 {
		t.Fatalf("exit code mismatch: %d stderr=%s", code, stderr.String())
	}
	response := decodeLaunchResponseFromOutput(t, stdout.String())
	if !response.Success || !response.Ready || !response.AlreadyRunning {
		t.Fatalf("fallback response mismatch: %+v", response)
	}
	if response.CurrentProcessId != nil {
		t.Fatalf("fallback cannot know the process id: %+v", response)
	}
	if !strings.Contains(response.Message, "window was not focused") {
		t.Fatalf("message should explain the skipped focus: %+v", response)
	}
}

// Verifies launch still fails when the process scan is blocked and the project IPC is silent.
func TestRunLaunchReportsScanErrorWhenIpcProbeAlsoFails(t *testing.T) {
	originalFinder := findRunningUnityProcessForLaunch
	originalProbe := probeProjectIpcForLaunchFallback
	findRunningUnityProcessForLaunch = func(context.Context, string) (*unityProcess, error) {
		return nil, errors.New("failed to retrieve Unity process list: /bin/ps: operation not permitted")
	}
	probeProjectIpcForLaunchFallback = func(context.Context, string) error {
		return errors.New("connection refused")
	}
	t.Cleanup(func() {
		findRunningUnityProcessForLaunch = originalFinder
		probeProjectIpcForLaunchFallback = originalProbe
	})

	projectRoot := createLaunchTestProject(t)
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := runLaunch(context.Background(), launchOptions{projectPath: projectRoot}, projectRoot, &stdout, &stderr)

	if code != 1 {
		t.Fatalf("expected failure, got %d stdout=%s", code, stdout.String())
	}
	if !strings.Contains(stderr.String(), "operation not permitted") {
		t.Fatalf("stderr should carry the scan error: %s", stderr.String())
	}
}

// Verifies restart and quit refuse the fallback because they must kill a known process id.
func TestRunLaunchRestartDoesNotUseIpcProbeFallback(t *testing.T) {
	originalFinder := findRunningUnityProcessForLaunch
	originalProbe := probeProjectIpcForLaunchFallback
	findRunningUnityProcessForLaunch = func(context.Context, string) (*unityProcess, error) {
		return nil, errors.New("failed to retrieve Unity process list: /bin/ps: operation not permitted")
	}
	probeProjectIpcForLaunchFallback = func(context.Context, string) error {
		t.Fatal("restart must not consult the IPC probe fallback")
		return nil
	}
	t.Cleanup(func() {
		findRunningUnityProcessForLaunch = originalFinder
		probeProjectIpcForLaunchFallback = originalProbe
	})

	projectRoot := createLaunchTestProject(t)
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := runLaunch(context.Background(), launchOptions{restart: true, projectPath: projectRoot}, projectRoot, &stdout, &stderr)

	if code != 1 {
		t.Fatalf("expected failure, got %d stdout=%s", code, stdout.String())
	}
}
