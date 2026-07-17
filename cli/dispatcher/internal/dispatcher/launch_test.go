package dispatcher

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"os"
	"path/filepath"
	"runtime"
	"slices"
	"strings"
	"testing"
	"time"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
	"github.com/hatayama/unity-cli-loop/common/vibelog"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

func TestParseLaunchOptionsSupportsCoreFlags(t *testing.T) {
	// Verifies launch parses every supported core flag without dropping the project path.
	options, err := parseLaunchOptions(
		[]string{
			"--restart",
			"--delete-recovery",
			"--editor-version", "6000.0.0f1",
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
	if options.editorVersion != "6000.0.0f1" {
		t.Fatalf("editor version mismatch: %s", options.editorVersion)
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

func TestParseLaunchOptionsRejectsRemovedIgnoreCompilerErrorsFlags(t *testing.T) {
	// Verifies removed compiler-error ignore flags no longer remain in the launch API.
	for _, arg := range []string{"-i", "--ignore-compiler-errors"} {
		_, err := parseLaunchOptions([]string{arg}, "")
		if err == nil {
			t.Fatalf("expected removed ignore compiler errors flag error for %s", arg)
		}

		var argErr *clierrors.ArgumentError
		if !errors.As(err, &argErr) {
			t.Fatalf("expected argumentError for %s, got %T", arg, err)
		}
		if argErr.Option != arg {
			t.Fatalf("option mismatch for %s: %s", arg, argErr.Option)
		}
	}
}

func TestBuildUnityLaunchArgsIncludesIgnoreCompilerErrorsByDefault(t *testing.T) {
	// Verifies every Unity launch ignores project compiler errors during Editor startup.
	args := buildUnityLaunchArgs(
		"/tmp/project",
		launchOptions{platform: "Android"},
	)
	expectedArgs := []string{
		"-projectPath",
		"/tmp/project",
		"-buildTarget",
		"Android",
		"-ignorecompilererrors",
	}

	if !slices.Equal(args, expectedArgs) {
		t.Fatalf("Unity launch args mismatch: got %#v want %#v", args, expectedArgs)
	}
}

func TestBuildUnityLaunchArgsIncludesIgnoreCompilerErrorsForRestart(t *testing.T) {
	// Verifies restarted Unity launches use the same compiler-error ignore startup argument.
	args := buildUnityLaunchArgs(
		"/tmp/project",
		launchOptions{restart: true},
	)
	expectedArgs := []string{
		"-projectPath",
		"/tmp/project",
		"-ignorecompilererrors",
	}

	if !slices.Equal(args, expectedArgs) {
		t.Fatalf("Unity launch args mismatch: got %#v want %#v", args, expectedArgs)
	}
}

func TestParseLaunchOptionsSupportsEditorVersionEqualsValue(t *testing.T) {
	// Verifies --editor-version=value matches Unity CLI's value form.
	options, err := parseLaunchOptions([]string{"--editor-version=6000.0.1f1", "/tmp/project"}, "")
	if err != nil {
		t.Fatalf("parseLaunchOptions failed: %v", err)
	}

	if options.editorVersion != "6000.0.1f1" {
		t.Fatalf("editor version mismatch: %s", options.editorVersion)
	}
}

func TestParseLaunchOptionsRejectsEmptyEditorVersionEqualsValue(t *testing.T) {
	// Verifies --editor-version= cannot silently fall back to ProjectVersion.txt.
	_, err := parseLaunchOptions([]string{"--editor-version="}, "")

	if err == nil {
		t.Fatal("expected empty editor version value error")
	}
}

func TestParseLaunchOptionsRejectsMissingEditorVersionValue(t *testing.T) {
	// Verifies --editor-version requires an explicit Editor version.
	_, err := parseLaunchOptions([]string{"--editor-version"}, "")

	if err == nil {
		t.Fatal("expected missing editor version value error")
	}
}

func TestResolveLaunchEditorVersionUsesOptionBeforeProjectVersion(t *testing.T) {
	// Verifies --editor-version does not require or mutate ProjectVersion.txt.
	projectRoot := createLaunchTestProject(t)

	version, err := resolveLaunchEditorVersion(projectRoot, launchOptions{editorVersion: "6000.0.2f1"})
	if err != nil {
		t.Fatalf("resolveLaunchEditorVersion failed: %v", err)
	}
	if version != "6000.0.2f1" {
		t.Fatalf("editor version mismatch: %s", version)
	}
}

func TestParseLaunchOptionsRejectsUnityHubRegistration(t *testing.T) {
	_, err := parseLaunchOptions([]string{"--add-unity-hub"}, "")
	if err == nil {
		t.Fatal("expected Unity Hub registration option error")
	}
}

func TestParseLaunchOptionsRejectsUnityHubRegistrationEqualsValues(t *testing.T) {
	// Verifies deprecated Unity Hub flags keep launch-specific guidance in --flag=value form.
	for _, arg := range []string{"--add-unity-hub=true", "--favorite=true", "--unity-hub-entry=sample"} {
		_, err := parseLaunchOptions([]string{arg}, "")
		if err == nil {
			t.Fatalf("expected Unity Hub registration option error for %s", arg)
		}

		var argErr *clierrors.ArgumentError
		if !errors.As(err, &argErr) {
			t.Fatalf("expected argumentError for %s, got %T", arg, err)
		}
		if argErr.Message != "Native launch does not support Unity Hub registration options." {
			t.Fatalf("message mismatch for %s: %s", arg, argErr.Message)
		}
	}
}

func TestParseLaunchOptionsRejectsMaxDepthBelowUnlimitedSentinel(t *testing.T) {
	// Verifies --max-depth only accepts -1 as the unlimited sentinel.
	_, err := parseLaunchOptions([]string{"--max-depth", "-2"}, "")

	if err == nil {
		t.Fatal("expected invalid max-depth error")
	}

	var argErr *clierrors.ArgumentError
	if !errors.As(err, &argErr) {
		t.Fatalf("expected argumentError, got %T", err)
	}
	if argErr.ExpectedType != "integer >= -1" {
		t.Fatalf("expectedType mismatch: %s", argErr.ExpectedType)
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
	if !strings.Contains(stdout.String(), launchNoProcessMessage) {
		t.Fatalf("stdout mismatch: %s", stdout.String())
	}
}

func TestRunLaunchWritesReadyResponseAfterToolReadiness(t *testing.T) {
	// Verifies launch reports an explicit ready payload after Unity accepts tool requests.
	deps := defaultLaunchDeps()
	deps.findRunningUnityProcess = func(context.Context, string) (*clicore.UnityProcess, error) {
		return nil, nil
	}
	fakeUnityPath := fakeUnityExecutablePath(t)
	deps.resolveUnityExecutablePath = func(string) (string, error) {
		return fakeUnityPath, nil
	}
	deps.waitForUnityStartupMarker = func(context.Context, string, time.Duration, time.Duration) error {
		return nil
	}
	deps.waitForToolReadiness = func(context.Context, string, time.Duration) error {
		return nil
	}

	projectRoot := createLaunchTestProject(t)
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := runLaunchWithDeps(
		context.Background(),
		launchOptions{projectPath: projectRoot, editorVersion: "6000.0.0f1"},
		projectRoot,
		&stdout,
		&stderr,
		deps,
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

// Verifies a V2 launch confirms only that Unity opened the project and never waits for the V3 named pipe.
func TestRunLaunchForV2ProjectWaitsForFreshLockfileWithoutServerProbe(t *testing.T) {
	projectRoot := createLaunchTestProject(t)
	writeV2PackageManifest(t, projectRoot)
	writeV2PackageCachePackageJSON(t, projectRoot, "abc123", "2.2.0")
	deps := defaultLaunchDeps()
	deps.findRunningUnityProcess = func(context.Context, string) (*clicore.UnityProcess, error) { return nil, nil }
	deps.resolveUnityExecutablePath = func(string) (string, error) { return fakeUnityExecutablePath(t), nil }
	deps.waitForFreshUnityLockfile = func(context.Context, string, time.Time, time.Duration, time.Duration) error { return nil }
	deps.waitForToolReadiness = func(context.Context, string, time.Duration) error {
		t.Fatal("V2 launch must not wait for the V3 server")
		return nil
	}

	var stdout bytes.Buffer
	code := runLaunchWithDeps(context.Background(), launchOptions{projectPath: projectRoot, editorVersion: "6000.0.0f1"}, projectRoot, &stdout, io.Discard, deps)
	if code != 0 {
		t.Fatalf("V2 launch exit code = %d", code)
	}
	response := decodeLaunchResponseFromOutput(t, stdout.String())
	if !response.Success || !response.Ready || response.ServerReady || response.ProjectIpcReady {
		t.Fatalf("V2 launch readiness flags = %#v", response)
	}
	if !strings.Contains(response.Message, "V2 server readiness was not checked") {
		t.Fatalf("V2 launch message = %q", response.Message)
	}
}

func TestWaitForLaunchReadinessUsesLaunchTimeout(t *testing.T) {
	// Verifies launch gets a longer startup window without changing shared readiness defaults.
	deps := defaultLaunchDeps()
	var capturedTimeout time.Duration
	deps.waitForToolReadiness = func(ctx context.Context, projectRoot string, timeout time.Duration) error {
		capturedTimeout = timeout
		return nil
	}

	if err := waitForLaunchReadinessWithDeps(context.Background(), t.TempDir(), deps); err != nil {
		t.Fatalf("waitForLaunchReadinessWithDeps failed: %v", err)
	}
	if capturedTimeout != launchReadinessTimeout {
		t.Fatalf("launch readiness timeout mismatch: %s", capturedTimeout)
	}
}

func TestWaitForLaunchReadinessWrapsStartupTimeout(t *testing.T) {
	// Verifies launch timeout errors receive the launch-specific startup classification.
	deps := defaultLaunchDeps()
	deps.waitForToolReadiness = func(ctx context.Context, projectRoot string, timeout time.Duration) error {
		return clierrors.UnityServerNotRespondingError{
			ProjectRoot: projectRoot,
			Endpoint:    "/tmp/uloop/UnityCliLoop-sample.sock",
			Cause:       errors.New("timed out waiting for Unity tool readiness"),
		}
	}

	err := waitForLaunchReadinessWithDeps(context.Background(), t.TempDir(), deps)

	var startupErr launchStartupTimeoutError
	if !errors.As(err, &startupErr) {
		t.Fatalf("expected launch startup timeout error, got %v", err)
	}
}

func TestWaitForLaunchReadinessWrapsInternalProbeDeadline(t *testing.T) {
	// Verifies probe deadlines are classified as launch startup timeouts while the parent context is active.
	deps := defaultLaunchDeps()
	deps.waitForToolReadiness = func(ctx context.Context, projectRoot string, timeout time.Duration) error {
		return clierrors.UnityServerNotRespondingError{
			ProjectRoot: projectRoot,
			Endpoint:    "/tmp/uloop/UnityCliLoop-sample.sock",
			Cause:       fmt.Errorf("probe deadline: %w", context.DeadlineExceeded),
		}
	}

	err := waitForLaunchReadinessWithDeps(context.Background(), t.TempDir(), deps)

	var startupErr launchStartupTimeoutError
	if !errors.As(err, &startupErr) {
		t.Fatalf("expected launch startup timeout error, got %v", err)
	}
	if !errors.Is(startupErr.Unwrap(), context.DeadlineExceeded) {
		t.Fatalf("startup timeout should preserve probe deadline cause: %v", startupErr.Unwrap())
	}
}

func TestWaitForLaunchReadinessPreservesNoProcessReachability(t *testing.T) {
	// Verifies a launch whose Editor exited before readiness does not report that Unity is running.
	deps := defaultLaunchDeps()
	deps.waitForToolReadiness = func(ctx context.Context, projectRoot string, timeout time.Duration) error {
		return fmt.Errorf("timed out waiting for Unity tool readiness: %w", &unityipc.ConnectionAttemptError{
			ProjectRoot: projectRoot,
			Endpoint:    "/tmp/uloop/UnityCliLoop-sample.sock",
			Cause:       errors.New("connect failed"),
		})
	}

	err := waitForLaunchReadinessWithDeps(context.Background(), t.TempDir(), deps)

	var startupErr launchStartupTimeoutError
	if errors.As(err, &startupErr) {
		t.Fatalf("exited launch should not be classified as startup timeout: %v", err)
	}
	cliErr := clierrors.ClassifyError(err, clierrors.ErrorContext{Command: clicore.LaunchCommandName})
	if cliErr.ErrorCode != clierrors.ErrorCodeUnityNotReachable {
		t.Fatalf("error code mismatch: %#v", cliErr)
	}
	if strings.Contains(cliErr.Message, "Unity is running") {
		t.Fatalf("message should not claim Unity is running: %#v", cliErr)
	}
}

func TestWaitForLaunchReadinessPreservesParentCancellation(t *testing.T) {
	// Verifies caller cancellation is not converted into a launch startup timeout.
	deps := defaultLaunchDeps()
	deps.waitForToolReadiness = func(ctx context.Context, projectRoot string, timeout time.Duration) error {
		return ctx.Err()
	}
	ctx, cancel := context.WithCancel(context.Background())
	cancel()

	err := waitForLaunchReadinessWithDeps(ctx, t.TempDir(), deps)

	if !errors.Is(err, context.Canceled) {
		t.Fatalf("expected parent cancellation, got %v", err)
	}
}

func TestRunLaunchWritesStructuredResponseForExistingUnityProcess(t *testing.T) {
	// Verifies launch reports machine-readable readiness when Unity was already running.
	deps := defaultLaunchDeps()
	readinessChecked := false
	deps.findRunningUnityProcess = func(context.Context, string) (*clicore.UnityProcess, error) {
		return &clicore.UnityProcess{Pid: 111}, nil
	}
	deps.focusUnityProcess = func(context.Context, int) error {
		return nil
	}
	deps.waitForToolReadiness = func(context.Context, string, time.Duration) error {
		readinessChecked = true
		return nil
	}

	projectRoot := createLaunchTestProject(t)
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := runLaunchWithDeps(
		context.Background(),
		launchOptions{projectPath: projectRoot},
		projectRoot,
		&stdout,
		&stderr,
		deps,
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

func TestRunLaunchRequiresRestartForEditorVersionWithExistingUnityProcess(t *testing.T) {
	// Verifies --editor-version cannot silently reuse an already running Editor process.
	deps := defaultLaunchDeps()
	readinessChecked := false
	deps.findRunningUnityProcess = func(context.Context, string) (*clicore.UnityProcess, error) {
		return &clicore.UnityProcess{Pid: 222}, nil
	}
	deps.waitForToolReadiness = func(context.Context, string, time.Duration) error {
		readinessChecked = true
		return nil
	}

	projectRoot := createLaunchTestProject(t)
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := runLaunchWithDeps(
		context.Background(),
		launchOptions{projectPath: projectRoot, editorVersion: "6000.0.0f1"},
		projectRoot,
		&stdout,
		&stderr,
		deps,
	)

	if code != 1 {
		t.Fatalf("exit code mismatch: %d stdout=%s stderr=%s", code, stdout.String(), stderr.String())
	}
	if readinessChecked {
		t.Fatal("launch should not report an existing process as ready for a different requested Editor version")
	}
	if !strings.Contains(stderr.String(), "`uloop launch --restart --editor-version 6000.0.0f1`") {
		t.Fatalf("stderr should guide restart with the requested version:\n%s", stderr.String())
	}
}

func TestRunLaunchRestartWritesProcessTransitionResponse(t *testing.T) {
	// Verifies restart reports both the stopped process and the newly launched process.
	deps := defaultLaunchDeps()
	killedPid := 0
	waitedPid := 0
	deps.findRunningUnityProcess = func(context.Context, string) (*clicore.UnityProcess, error) {
		return &clicore.UnityProcess{Pid: 222}, nil
	}
	deps.killUnityProcess = func(pid int) error {
		killedPid = pid
		return nil
	}
	deps.waitForUnityProcessExit = func(ctx context.Context, projectRoot string, pid int, pollInterval time.Duration, timeout time.Duration) error {
		waitedPid = pid
		return nil
	}
	fakeUnityPath := fakeUnityExecutablePath(t)
	deps.resolveUnityExecutablePath = func(string) (string, error) {
		return fakeUnityPath, nil
	}
	deps.waitForUnityStartupMarker = func(context.Context, string, time.Duration, time.Duration) error {
		return nil
	}
	deps.waitForToolReadiness = func(context.Context, string, time.Duration) error {
		return nil
	}

	projectRoot := createLaunchTestProject(t)
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := runLaunchWithDeps(
		context.Background(),
		launchOptions{projectPath: projectRoot, restart: true, editorVersion: "6000.0.0f1"},
		projectRoot,
		&stdout,
		&stderr,
		deps,
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
	deps := defaultLaunchDeps()
	waitedPid := 0
	deps.findRunningUnityProcess = func(context.Context, string) (*clicore.UnityProcess, error) {
		return &clicore.UnityProcess{Pid: 333}, nil
	}
	deps.killUnityProcess = func(pid int) error {
		return nil
	}
	deps.waitForUnityProcessExit = func(ctx context.Context, projectRoot string, pid int, pollInterval time.Duration, timeout time.Duration) error {
		waitedPid = pid
		return nil
	}

	projectRoot := createLaunchTestProject(t)
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := runLaunchWithDeps(
		context.Background(),
		launchOptions{projectPath: projectRoot, quit: true},
		projectRoot,
		&stdout,
		&stderr,
		deps,
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
	deps := defaultLaunchDeps()
	resolverCalled := false
	deps.findRunningUnityProcess = func(context.Context, string) (*clicore.UnityProcess, error) {
		return &clicore.UnityProcess{Pid: 444}, nil
	}
	deps.killUnityProcess = func(pid int) error {
		return nil
	}
	deps.waitForUnityProcessExit = func(ctx context.Context, projectRoot string, pid int, pollInterval time.Duration, timeout time.Duration) error {
		return errors.New("still exiting")
	}
	fakeUnityPath := fakeUnityExecutablePath(t)
	deps.resolveUnityExecutablePath = func(string) (string, error) {
		resolverCalled = true
		return fakeUnityPath, nil
	}

	projectRoot := createLaunchTestProject(t)
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := runLaunchWithDeps(
		context.Background(),
		launchOptions{projectPath: projectRoot, restart: true},
		projectRoot,
		&stdout,
		&stderr,
		deps,
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

	deps := defaultLaunchDeps()
	deps.findRunningUnityProcess = func(context.Context, string) (*clicore.UnityProcess, error) {
		return &clicore.UnityProcess{Pid: 111}, nil
	}
	deps.focusUnityProcess = func(context.Context, int) error {
		return nil
	}
	deps.waitForToolReadiness = func(context.Context, string, time.Duration) error {
		return nil
	}

	projectRoot := createLaunchTestProject(t)
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := runLaunchWithDeps(
		context.Background(),
		launchOptions{projectPath: projectRoot},
		projectRoot,
		&stdout,
		&stderr,
		deps,
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

	deps := defaultLaunchDeps()
	deps.findRunningUnityProcess = func(context.Context, string) (*clicore.UnityProcess, error) {
		return &clicore.UnityProcess{Pid: 222}, nil
	}
	deps.focusUnityProcess = func(context.Context, int) error {
		return fmt.Errorf("activation denied")
	}
	deps.waitForToolReadiness = func(context.Context, string, time.Duration) error {
		return nil
	}

	projectRoot := createLaunchTestProject(t)
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := runLaunchWithDeps(
		context.Background(),
		launchOptions{projectPath: projectRoot},
		projectRoot,
		&stdout,
		&stderr,
		deps,
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

func TestWaitForUnityProcessExitBoundsProcessScan(t *testing.T) {
	// Verifies exit waiting applies the exit timeout to each running-process scan.
	deps := defaultLaunchDeps()
	deps.findRunningUnityProcess = func(ctx context.Context, projectRoot string) (*clicore.UnityProcess, error) {
		if _, ok := ctx.Deadline(); !ok {
			return nil, errors.New("missing process scan deadline")
		}
		<-ctx.Done()
		return nil, ctx.Err()
	}

	err := waitForUnityProcessExitWithDeps(context.Background(), t.TempDir(), 123, time.Hour, 10*time.Millisecond, deps)

	var timeoutErr launchProcessExitTimeoutError
	if !errors.As(err, &timeoutErr) {
		t.Fatalf("process exit timeout mismatch: %v", err)
	}
	if timeoutErr.pid != 123 {
		t.Fatalf("process exit timeout pid mismatch: %#v", timeoutErr)
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

// fakeUnityExecutablePath returns a no-op executable standing in for Unity in
// launch tests that really spawn the resolved path. Why: /usr/bin/true does
// not exist on Windows, so Windows needs a native no-op batch file instead.
func fakeUnityExecutablePath(t *testing.T) string {
	t.Helper()

	if runtime.GOOS != "windows" {
		return "/usr/bin/true"
	}
	path := filepath.Join(t.TempDir(), "fake-unity.bat")
	if err := os.WriteFile(path, []byte("@exit /b 0\r\n"), 0o755); err != nil {
		t.Fatalf("failed to write fake Unity executable: %v", err)
	}
	return path
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

// readOnlyCliVibeLog and enableCliVibeLog are duplicated from
// internal/projectrunner's test helpers of the same name: test helpers
// cannot be shared across packages, and both packages exercise CLI Vibe log
// writes around launch and connection retry behavior.
func readOnlyCliVibeLog(t *testing.T, projectRoot string) string {
	t.Helper()
	logFiles, err := filepath.Glob(filepath.Join(projectRoot, vibelog.CLIVibeLogDirectory, vibelog.CLIVibeLogPrefix+"_*.json"))
	if err != nil {
		t.Fatalf("failed to glob CLI Vibe logs: %v", err)
	}
	if len(logFiles) != 1 {
		t.Fatalf("expected one CLI Vibe log, got %d: %#v", len(logFiles), logFiles)
	}
	content, err := os.ReadFile(logFiles[0])
	if err != nil {
		t.Fatalf("failed to read CLI Vibe log: %v", err)
	}
	return string(content)
}

func enableCliVibeLog(t *testing.T) {
	t.Helper()
	t.Setenv(vibelog.CLIVibeLogEnvName, "1")
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
	deps := defaultLaunchDeps()
	deps.findRunningUnityProcess = func(context.Context, string) (*clicore.UnityProcess, error) {
		return nil, errors.New("failed to retrieve Unity process list: /bin/ps: operation not permitted")
	}
	deps.probeProjectIpcFallback = func(context.Context, string) error {
		return nil
	}

	projectRoot := createLaunchTestProject(t)
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := runLaunchWithDeps(context.Background(), launchOptions{projectPath: projectRoot}, projectRoot, &stdout, &stderr, deps)

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
	deps := defaultLaunchDeps()
	deps.findRunningUnityProcess = func(context.Context, string) (*clicore.UnityProcess, error) {
		return nil, errors.New("failed to retrieve Unity process list: /bin/ps: operation not permitted")
	}
	deps.probeProjectIpcFallback = func(context.Context, string) error {
		return errors.New("connection refused")
	}

	projectRoot := createLaunchTestProject(t)
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := runLaunchWithDeps(context.Background(), launchOptions{projectPath: projectRoot}, projectRoot, &stdout, &stderr, deps)

	if code != 1 {
		t.Fatalf("expected failure, got %d stdout=%s", code, stdout.String())
	}
	if !strings.Contains(stderr.String(), "operation not permitted") {
		t.Fatalf("stderr should carry the scan error: %s", stderr.String())
	}
}

// Verifies restart and quit refuse the fallback because they must kill a known process id.
func TestRunLaunchRestartDoesNotUseIpcProbeFallback(t *testing.T) {
	deps := defaultLaunchDeps()
	deps.findRunningUnityProcess = func(context.Context, string) (*clicore.UnityProcess, error) {
		return nil, errors.New("failed to retrieve Unity process list: /bin/ps: operation not permitted")
	}
	deps.probeProjectIpcFallback = func(context.Context, string) error {
		t.Fatal("restart must not consult the IPC probe fallback")
		return nil
	}

	projectRoot := createLaunchTestProject(t)
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := runLaunchWithDeps(context.Background(), launchOptions{restart: true, projectPath: projectRoot}, projectRoot, &stdout, &stderr, deps)

	if code != 1 {
		t.Fatalf("expected failure, got %d stdout=%s", code, stdout.String())
	}
}
