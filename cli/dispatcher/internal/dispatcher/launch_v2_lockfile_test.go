package dispatcher

import (
	"bytes"
	"context"
	"errors"
	"io"
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
)

// Verifies V2 launch succeeds only after Unity creates or updates its lockfile after the new process starts.
func TestWaitForFreshUnityLockfileWaitsForPostLaunchWrite(t *testing.T) {
	lockfilePath := filepath.Join(t.TempDir(), unityLockfileName)
	startedAt := time.Now()
	go func() {
		time.Sleep(20 * time.Millisecond)
		if err := os.WriteFile(lockfilePath, []byte("lock"), 0o644); err != nil {
			panic(err)
		}
	}()

	err := waitForFreshUnityLockfile(context.Background(), lockfilePath, startedAt, 5*time.Millisecond, time.Second)
	if err != nil {
		t.Fatalf("wait for fresh lockfile: %v", err)
	}
}

// Verifies a stale UnityLockfile left by an earlier Editor session cannot satisfy V2 launch readiness.
func TestWaitForFreshUnityLockfileRejectsStaleLockfile(t *testing.T) {
	lockfilePath := filepath.Join(t.TempDir(), unityLockfileName)
	if err := os.WriteFile(lockfilePath, []byte("stale"), 0o644); err != nil {
		t.Fatalf("write stale lockfile: %v", err)
	}
	staleTime := time.Now().Add(-time.Second)
	if err := os.Chtimes(lockfilePath, staleTime, staleTime); err != nil {
		t.Fatalf("set stale lockfile time: %v", err)
	}

	err := waitForFreshUnityLockfile(context.Background(), lockfilePath, time.Now(), 5*time.Millisecond, 30*time.Millisecond)
	var timeoutErr v2LaunchLockfileTimeoutError
	if !errors.As(err, &timeoutErr) {
		t.Fatalf("expected V2 lockfile timeout, got %v", err)
	}
}

func TestWaitForFreshUnityLockfileReportsInspectionFailureAndCancellation(t *testing.T) {
	// Verifies an uninspectable lockfile path is returned at once and the caller's cancellation wins over the timeout error.
	canceled, cancel := context.WithCancel(context.Background())
	cancel()
	err := waitForFreshUnityLockfile(canceled, filepath.Join(t.TempDir(), unityLockfileName), time.Now(), time.Millisecond, time.Second)
	if !errors.Is(err, context.Canceled) {
		t.Fatalf("expected the caller's cancellation, got %v", err)
	}

	skipDispatcherTestOnWindows(t, "Windows reports a path below a file as not found rather than ENOTDIR.")
	parentFile := filepath.Join(t.TempDir(), "file")
	writeDispatcherTestFile(t, parentFile, "x")
	err = waitForFreshUnityLockfile(context.Background(), filepath.Join(parentFile, unityLockfileName), time.Now(), time.Millisecond, time.Second)
	if err == nil || !strings.HasSuffix(err.Error(), "not a directory") {
		t.Fatalf("expected the stat error, got %v", err)
	}
}

func TestV2LaunchLockfileTimeoutErrorDescribesTheLockfile(t *testing.T) {
	// Verifies the lockfile timeout names the lockfile and maps to a retryable startup timeout envelope.
	err := v2LaunchLockfileTimeoutError{lockfilePath: "<PROJECT_ROOT>/Temp/UnityLockfile"}

	if err.Error() != "timed out waiting for Unity to update <PROJECT_ROOT>/Temp/UnityLockfile" {
		t.Fatalf("unexpected message: %s", err.Error())
	}
	cliError := err.ToCLIError(clierrors.ErrorContext{ProjectRoot: "<PROJECT_ROOT>", Command: "launch"})
	if cliError.ErrorCode != clierrors.ErrorCodeUnityStartupTimeout || !cliError.Retryable || cliError.ProjectRoot != "<PROJECT_ROOT>" {
		t.Fatalf("unexpected envelope: %+v", cliError)
	}
}

// createV2LaunchTestProject builds a project the launch flow detects as a V2 project.
func createV2LaunchTestProject(t *testing.T) string {
	t.Helper()
	projectRoot := createVersionedLaunchTestProject(t)
	writeV2PackageManifest(t, projectRoot)
	writeV2PackageCachePackageJSON(t, projectRoot, "abc123", "2.2.0")
	if project, err := detectV2DispatcherProject(projectRoot); err != nil || !project.IsV2 {
		t.Fatalf("precondition failed: fixture must be a V2 project, got project=%+v err=%v", project, err)
	}
	return projectRoot
}

func TestRunLaunchReportsV2ReadinessFailures(t *testing.T) {
	// Verifies a V2 project stops with code 1 when its fresh lockfile or its already running server never becomes ready.
	t.Run("fresh launch lockfile", func(t *testing.T) {
		deps := isolatedLaunchTestDeps(t)
		deps.waitForFreshUnityLockfile = func(context.Context, string, time.Time, time.Duration, time.Duration) error {
			return errors.New("lockfile never refreshed")
		}
		projectRoot := createV2LaunchTestProject(t)
		var stderr bytes.Buffer

		code := runLaunchWithDeps(context.Background(), launchOptions{projectPath: projectRoot}, projectRoot, io.Discard, &stderr, deps)

		if code != 1 || !strings.Contains(stderr.String(), "lockfile never refreshed") {
			t.Fatalf("expected the lockfile error: code=%d stderr=%s", code, stderr.String())
		}
	})
	t.Run("existing editor server", func(t *testing.T) {
		deps := isolatedLaunchTestDeps(t)
		deps.findRunningUnityProcess = func(context.Context, string) (*clicore.UnityProcess, error) {
			return &clicore.UnityProcess{Pid: 4242}, nil
		}
		deps.waitForV2ServerReady = func(context.Context, string, string, time.Duration, time.Duration) error {
			return errors.New("v2 server not ready")
		}
		projectRoot := createV2LaunchTestProject(t)
		var stderr bytes.Buffer

		code := runLaunchWithDeps(context.Background(), launchOptions{projectPath: projectRoot}, projectRoot, io.Discard, &stderr, deps)

		if code != 1 || !strings.Contains(stderr.String(), "v2 server not ready") {
			t.Fatalf("expected the server readiness error: code=%d stderr=%s", code, stderr.String())
		}
	})
}
