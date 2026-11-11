package dispatcher

import (
	"bytes"
	"context"
	"errors"
	"os"
	"strings"
	"testing"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
)

func TestCleanStaleUnityTempKeepsLockfileHeldByRunningEditor(t *testing.T) {
	// Verifies a lockfile a running Editor still holds is reported and nothing under Temp is removed.
	projectRoot := createLaunchTestProject(t)
	createStaleUnityLockfile(t, projectRoot)
	deps := defaultLaunchDeps()
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

func TestCleanStaleUnityTempKeepsLockfileWhenTheHolderCannotBeDetermined(t *testing.T) {
	// Verifies a failed lock probe stops the cleanup instead of being read as "nobody holds it".
	projectRoot := createLaunchTestProject(t)
	createStaleUnityLockfile(t, projectRoot)
	deps := defaultLaunchDeps()
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
