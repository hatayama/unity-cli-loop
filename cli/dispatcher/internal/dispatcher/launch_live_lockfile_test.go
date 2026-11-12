package dispatcher

import (
	"bytes"
	"context"
	"errors"
	"os"
	"strings"
	"testing"
	"time"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
)

func TestCleanStaleUnityTempKeepsLockfileHeldByRunningEditor(t *testing.T) {
	// Verifies a lockfile a running Editor still holds is reported and nothing under Temp is removed.
	projectRoot := createLaunchTestProject(t)
	createStaleUnityLockfile(t, projectRoot)
	deps := defaultLaunchDeps()
	useFakeLaunchClock(&deps)
	deps.isUnityLockfileHeld = func(string) (bool, error) {
		return true, nil
	}
	removeCalls := 0
	deps.removePath = func(string) error {
		removeCalls++
		return nil
	}

	_, err := cleanStaleUnityTemp(context.Background(), projectRoot, deps)

	var heldError heldUnityLockfileError
	if !errors.As(err, &heldError) {
		t.Fatalf("expected a held-lockfile error, got %v", err)
	}
	if removeCalls != 0 {
		t.Fatalf("cleanup must not remove anything while the lockfile is held, got %d calls", removeCalls)
	}
}

func TestCleanStaleUnityTempWaitsForAnExitingEditorToReleaseTheLockfile(t *testing.T) {
	// Verifies the cleanup waits out a lock that is released shortly after, as an Editor that just left
	// the process list does, and then removes the lockfile.
	projectRoot := createLaunchTestProject(t)
	createStaleUnityLockfile(t, projectRoot)
	deps := defaultLaunchDeps()
	useFakeLaunchClock(&deps)
	probeCalls := 0
	deps.isUnityLockfileHeld = func(string) (bool, error) {
		probeCalls++
		return probeCalls < 3, nil
	}

	result, err := cleanStaleUnityTemp(context.Background(), projectRoot, deps)
	if err != nil {
		t.Fatalf("cleanStaleUnityTemp failed: %v", err)
	}
	if !result.lockfileRemoved {
		t.Fatal("expected the released lockfile to be removed")
	}
	if probeCalls != 3 {
		t.Fatalf("probe count mismatch: %d", probeCalls)
	}
	if _, err := os.Stat(unityLockfilePath(projectRoot)); !os.IsNotExist(err) {
		t.Fatalf("lockfile still exists after cleanup: %v", err)
	}
}

func TestCleanStaleUnityTempContinuesWhenTheLockfileDisappearsBeforeTheProbe(t *testing.T) {
	// Verifies a lockfile the exiting Editor deleted between the existence check and the lock probe
	// counts as released instead of failing the cleanup.
	projectRoot := createLaunchTestProject(t)
	createStaleUnityLockfile(t, projectRoot)
	deps := defaultLaunchDeps()
	useFakeLaunchClock(&deps)
	deps.isUnityLockfileHeld = func(string) (bool, error) {
		return false, os.ErrNotExist
	}

	result, err := cleanStaleUnityTemp(context.Background(), projectRoot, deps)
	if err != nil {
		t.Fatalf("cleanStaleUnityTemp failed: %v", err)
	}
	if !result.lockfileRemoved {
		t.Fatal("expected the cleanup to finish removing Temp")
	}
}

func TestCleanStaleUnityTempKeepsLockfileWhenTheHolderCannotBeDetermined(t *testing.T) {
	// Verifies a failed lock probe stops the cleanup instead of being read as "nobody holds it".
	projectRoot := createLaunchTestProject(t)
	createStaleUnityLockfile(t, projectRoot)
	deps := defaultLaunchDeps()
	useFakeLaunchClock(&deps)
	probeFailure := errors.New("probe failed")
	deps.isUnityLockfileHeld = func(string) (bool, error) {
		return false, probeFailure
	}
	removeCalls := 0
	deps.removePath = func(string) error {
		removeCalls++
		return nil
	}

	_, err := cleanStaleUnityTemp(context.Background(), projectRoot, deps)

	if !errors.Is(err, probeFailure) {
		t.Fatalf("expected the probe failure, got %v", err)
	}
	if removeCalls != 0 {
		t.Fatalf("cleanup must not remove anything after a failed probe, got %d calls", removeCalls)
	}
}

func TestRunLaunchDoesNotStartUnityWhileLockfileIsHeld(t *testing.T) {
	// Verifies launch neither deletes the lockfile nor starts a second Editor when the process scan
	// finds no Editor but the project's lockfile is still held.
	projectRoot := createLaunchTestProject(t)
	createStaleUnityLockfile(t, projectRoot)
	deps := defaultLaunchDeps()
	useFakeLaunchClock(&deps)
	deps.findRunningUnityProcess = func(context.Context, string) (*clicore.UnityProcess, error) {
		return nil, nil
	}
	deps.isUnityLockfileHeld = func(string) (bool, error) {
		return true, nil
	}
	resolveCalls := 0
	deps.resolveUnityExecutablePath = func(string) (string, error) {
		resolveCalls++
		return fakeUnityExecutablePath(t), nil
	}

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
		t.Fatalf("exit code mismatch: %d stdout=%s", code, stdout.String())
	}
	if resolveCalls != 0 {
		t.Fatalf("launch must stop before starting Unity, resolved the Editor %d times", resolveCalls)
	}
	if _, err := os.Stat(unityLockfilePath(projectRoot)); err != nil {
		t.Fatalf("the held lockfile must stay in place: %v", err)
	}
	if !strings.Contains(stderr.String(), errorCodeUnityProjectAlreadyOpen) {
		t.Fatalf("stderr should carry the already-open error code: %s", stderr.String())
	}
}

func TestClassifyHeldUnityLockfileError(t *testing.T) {
	// Verifies a held lockfile is reported as a non-retryable failure that names the lockfile.
	cliErr := clierrors.ClassifyError(
		heldUnityLockfileError{lockfilePath: "/tmp/MyProject/Temp/UnityLockfile"},
		clierrors.ErrorContext{ProjectRoot: "/tmp/MyProject", Command: clicore.LaunchCommandName},
	)

	if cliErr.ErrorCode != errorCodeUnityProjectAlreadyOpen {
		t.Fatalf("error code mismatch: %#v", cliErr)
	}
	if cliErr.Retryable || cliErr.SafeToRetry {
		t.Fatalf("a held lockfile does not clear by retrying: %#v", cliErr)
	}
	if cliErr.Details["LockfilePath"] != "/tmp/MyProject/Temp/UnityLockfile" {
		t.Fatalf("lockfile details mismatch: %#v", cliErr.Details)
	}
}

func TestCleanStaleUnityTempKeepsTheDeleteErrorForAPermissionFailure(t *testing.T) {
	// Verifies a delete that fails for lack of permission stays the retried delete failure instead of
	// being reported as a project that is already open.
	projectRoot := createLaunchTestProject(t)
	createStaleUnityLockfile(t, projectRoot)
	deps := defaultLaunchDeps()
	useFakeLaunchClock(&deps)
	deps.isUnityLockfileHeld = func(string) (bool, error) {
		return false, nil
	}
	removeCalls := 0
	deps.removePath = func(path string) error {
		removeCalls++
		return &os.PathError{Op: "unlinkat", Path: path, Err: os.ErrPermission}
	}

	_, err := cleanStaleUnityTemp(context.Background(), projectRoot, deps)

	var heldError heldUnityLockfileError
	if errors.As(err, &heldError) {
		t.Fatalf("a permission failure must not be reported as a held lockfile: %v", err)
	}
	if !errors.Is(err, os.ErrPermission) || !strings.Contains(err.Error(), "could not delete the stale") {
		t.Fatalf("expected the stale-lockfile delete failure, got %v", err)
	}
	if removeCalls < 2 {
		t.Fatalf("a permission failure should still be retried, got %d delete attempts", removeCalls)
	}
}

func TestHeldUnityLockfileNextActionsNameWindowsTools(t *testing.T) {
	// Verifies the next actions point a Windows user at Windows tools instead of ps and lsof.
	actions := strings.Join(heldUnityLockfileNextActions("windows"), "\n")

	for _, expected := range []string{"Get-CimInstance", "Task Manager", "Resource Monitor"} {
		if !strings.Contains(actions, expected) {
			t.Fatalf("Windows next actions should mention %s: %s", expected, actions)
		}
	}
	for _, unexpected := range []string{"ps -axo", "lsof"} {
		if strings.Contains(actions, unexpected) {
			t.Fatalf("Windows next actions should not mention %s: %s", unexpected, actions)
		}
	}
}

func TestHeldUnityLockfileNextActionsKeepUnixTools(t *testing.T) {
	// Verifies the next actions outside Windows keep pointing at ps and lsof.
	actions := strings.Join(heldUnityLockfileNextActions("darwin"), "\n")

	for _, expected := range []string{"ps -axo", "lsof"} {
		if !strings.Contains(actions, expected) {
			t.Fatalf("next actions should mention %s: %s", expected, actions)
		}
	}
}

// useFakeLaunchClock makes sleeping advance the clock instead of waiting, so a test can run a
// retry budget out instantly.
func useFakeLaunchClock(deps *launchDeps) {
	current := time.Unix(0, 0)
	deps.now = func() time.Time {
		return current
	}
	deps.sleep = func(duration time.Duration) {
		current = current.Add(duration)
	}
}
