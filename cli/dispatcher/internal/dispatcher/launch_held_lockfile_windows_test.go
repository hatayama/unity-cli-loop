//go:build windows

package dispatcher

import (
	"bytes"
	"context"
	"errors"
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
	"golang.org/x/sys/windows"
)

func TestCleanStaleUnityTempReportsALockfileOpenedWithoutDeleteSharing(t *testing.T) {
	// Verifies a lockfile another process keeps open without delete sharing, as a running Editor does,
	// is reported as held at the first failed delete, and nothing under Temp is removed.
	projectRoot := createLaunchTestProject(t)
	createStaleUnityLockfile(t, projectRoot)
	holdFileWithoutDeleteSharing(t, unityLockfilePath(projectRoot))
	deps := defaultLaunchDeps()
	sleepCalls := countLaunchSleeps(&deps)

	_, err := cleanStaleUnityTemp(context.Background(), projectRoot, deps)

	var heldError heldUnityLockfileError
	if !errors.As(err, &heldError) {
		t.Fatalf("expected a held-lockfile error, got %v", err)
	}
	if *sleepCalls != 0 {
		t.Fatalf("a held lockfile does not clear by waiting, but the cleanup slept %d times", *sleepCalls)
	}
	assertUnityTempKept(t, projectRoot)
}

func TestRunLaunchDoesNotStartUnityWhileTheLockfileIsOpenWithoutDeleteSharing(t *testing.T) {
	// Verifies launch reports the project as already open, keeps Temp, and starts no Editor when the
	// process scan finds no Editor but a process keeps the lockfile open without delete sharing.
	projectRoot := createLaunchTestProject(t)
	createStaleUnityLockfile(t, projectRoot)
	holdFileWithoutDeleteSharing(t, unityLockfilePath(projectRoot))
	deps := defaultLaunchDeps()
	countLaunchSleeps(&deps)
	deps.findRunningUnityProcess = func(context.Context, string) (*clicore.UnityProcess, error) {
		return nil, nil
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
	if !strings.Contains(stderr.String(), errorCodeUnityProjectAlreadyOpen) {
		t.Fatalf("stderr should carry the already-open error code: %s", stderr.String())
	}
	assertUnityTempKept(t, projectRoot)
}

func TestCleanStaleUnityTempKeepsTheDeleteErrorWhenAccessIsDenied(t *testing.T) {
	// Verifies a delete refused for lack of permission is not mistaken for a held lockfile: it is still
	// retried and reported as the delete failure it is.
	projectRoot := createLaunchTestProject(t)
	createStaleUnityLockfile(t, projectRoot)
	deps := defaultLaunchDeps()
	sleepCalls := countLaunchSleeps(&deps)
	deps.removePath = func(path string) error {
		return &os.PathError{Op: "unlinkat", Path: path, Err: windows.ERROR_ACCESS_DENIED}
	}

	_, err := cleanStaleUnityTemp(context.Background(), projectRoot, deps)

	var heldError heldUnityLockfileError
	if errors.As(err, &heldError) {
		t.Fatalf("an access-denied delete must not be reported as a held lockfile: %v", err)
	}
	if !errors.Is(err, windows.ERROR_ACCESS_DENIED) {
		t.Fatalf("expected the access-denied failure, got %v", err)
	}
	cliErr := clierrors.ClassifyError(err, clierrors.ErrorContext{ProjectRoot: projectRoot, Command: clicore.LaunchCommandName})
	if cliErr.ErrorCode != clierrors.ErrorCodeInternalError {
		t.Fatalf("error code mismatch: %#v", cliErr)
	}
	if *sleepCalls == 0 {
		t.Fatal("an access-denied delete should still be retried")
	}
}

// holdFileWithoutDeleteSharing opens path the way a running Editor opens its lockfile, sharing read and
// write but not delete, and keeps it open until the test ends.
func holdFileWithoutDeleteSharing(t *testing.T, path string) {
	t.Helper()

	pathPointer, err := windows.UTF16PtrFromString(path)
	if err != nil {
		t.Fatalf("failed to convert %s: %v", path, err)
	}
	handle, err := windows.CreateFile(
		pathPointer,
		windows.GENERIC_READ|windows.GENERIC_WRITE,
		windows.FILE_SHARE_READ|windows.FILE_SHARE_WRITE,
		nil,
		windows.OPEN_EXISTING,
		windows.FILE_ATTRIBUTE_NORMAL,
		0,
	)
	if err != nil {
		t.Fatalf("failed to open %s: %v", path, err)
	}
	t.Cleanup(func() {
		if err := windows.CloseHandle(handle); err != nil {
			t.Errorf("failed to close %s: %v", path, err)
		}
	})
}

// countLaunchSleeps installs the fake launch clock and returns the number of times the cleanup slept.
func countLaunchSleeps(deps *launchDeps) *int {
	useFakeLaunchClock(deps)
	advance := deps.sleep
	sleepCalls := 0
	deps.sleep = func(duration time.Duration) {
		sleepCalls++
		advance(duration)
	}
	return &sleepCalls
}

func assertUnityTempKept(t *testing.T, projectRoot string) {
	t.Helper()

	if _, err := os.Stat(unityLockfilePath(projectRoot)); err != nil {
		t.Fatalf("the held lockfile must stay in place: %v", err)
	}
	if _, err := os.Stat(filepath.Join(projectRoot, launchTempDirectoryName)); err != nil {
		t.Fatalf("Temp must stay in place: %v", err)
	}
}
