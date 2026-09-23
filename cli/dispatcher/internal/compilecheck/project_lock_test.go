package compilecheck

import (
	"bufio"
	"context"
	"errors"
	"os"
	"os/exec"
	"strings"
	"sync"
	"testing"
	"time"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
)

const (
	lockHelperProjectRootEnv = "ULOOP_COMPILE_CHECK_LOCK_HELPER_PROJECT_ROOT"
	lockHelperReadyLine      = "locked"
	shortLockTimeout         = 150 * time.Millisecond
)

// holdProjectLock takes the project lock the way a run does and releases it when the test ends.
func holdProjectLock(t *testing.T, projectRoot string) func() {
	t.Helper()

	release, err := acquireProjectLock(context.Background(), projectRoot, shortLockTimeout)
	if err != nil {
		t.Fatalf("expected the free project lock to be taken, got error: %v", err)
	}
	// Why sync.Once: a test may release from a goroutine while the cleanup releases on the test's own.
	var once sync.Once
	releaseOnce := func() { once.Do(release) }
	t.Cleanup(releaseOnce)

	return releaseOnce
}

// Verifies a second run against one project gives up with a busy error once the lock wait times out.
func TestRunRefusesWhileAnotherRunHoldsTheProjectLock(t *testing.T) {
	projectRoot := newDagProject(t, "aaaa.dag")
	holdProjectLock(t, projectRoot)

	started := time.Now()
	_, err := Run(context.Background(), Options{ProjectRoot: projectRoot, LockTimeout: shortLockTimeout})

	var busy ProjectBusyError
	if !errors.As(err, &busy) {
		t.Fatalf("expected a ProjectBusyError, got %v", err)
	}
	if elapsed := time.Since(started); elapsed < shortLockTimeout {
		t.Errorf("expected the run to wait %v before giving up, it returned after %v", shortLockTimeout, elapsed)
	}
}

// Verifies a second run waits for the first to release the lock and then carries on with its work.
func TestRunWaitsForTheProjectLockAndProceedsOnceReleased(t *testing.T) {
	projectRoot := newDagProject(t, "aaaa.dag")
	release := holdProjectLock(t, projectRoot)
	const holdFor = 200 * time.Millisecond
	go func() {
		time.Sleep(holdFor)
		release()
	}()

	started := time.Now()
	// The Editor path is empty, so a run that got past the lock fails resolving the compiler.
	_, err := Run(context.Background(), Options{ProjectRoot: projectRoot, LockTimeout: 10 * time.Second})

	var busy ProjectBusyError
	if errors.As(err, &busy) {
		t.Fatalf("expected the run to proceed after the lock was released, got %v", err)
	}
	if err == nil {
		t.Fatal("expected the run to fail resolving the compiler after taking the lock")
	}
	if elapsed := time.Since(started); elapsed < holdFor {
		t.Errorf("expected the run to wait for the holder (%v), it returned after %v", holdFor, elapsed)
	}
}

// Verifies a run releases the project lock when it returns, including when it fails.
func TestRunReleasesTheProjectLockWhenItReturns(t *testing.T) {
	projectRoot := newDagProject(t, "aaaa.dag")

	if _, err := Run(context.Background(), Options{ProjectRoot: projectRoot}); err == nil {
		t.Fatal("expected the run to fail resolving the compiler")
	}

	release, err := acquireProjectLock(context.Background(), projectRoot, shortLockTimeout)
	if err != nil {
		t.Fatalf("expected the lock to be free after the run returned, got %v", err)
	}
	release()
}

// Verifies a run against another project does not wait for the lock this project's run holds.
func TestProjectLockDoesNotBlockAnotherProject(t *testing.T) {
	holdProjectLock(t, newDagProject(t, "aaaa.dag"))
	otherProjectRoot := newDagProject(t, "aaaa.dag")

	release, err := acquireProjectLock(context.Background(), otherProjectRoot, shortLockTimeout)
	if err != nil {
		t.Fatalf("expected another project's lock to be free, got %v", err)
	}
	release()
}

// Verifies a cancelled wait returns the cancellation rather than a busy error.
func TestAcquireProjectLockStopsWaitingWhenTheContextIsCancelled(t *testing.T) {
	projectRoot := newDagProject(t, "aaaa.dag")
	holdProjectLock(t, projectRoot)
	ctx, cancel := context.WithCancel(context.Background())
	cancel()

	_, err := acquireProjectLock(ctx, projectRoot, 10*time.Second)

	if !errors.Is(err, context.Canceled) {
		t.Fatalf("expected context.Canceled, got %v", err)
	}
}

// Verifies the shared classifier renders the busy error with its dedicated, retryable error code.
func TestProjectBusyErrorRendersItsOwnRetryableCode(t *testing.T) {
	cliError := clierrors.ClassifyError(
		ProjectBusyError{LockPath: "lock", Waited: time.Second},
		clierrors.ErrorContext{Command: "compile-check", ProjectRoot: "root"})

	if cliError.ErrorCode != clierrors.ErrorCodeCompileCheckProjectBusy {
		t.Errorf("expected %s, got %s", clierrors.ErrorCodeCompileCheckProjectBusy, cliError.ErrorCode)
	}
	if !cliError.Retryable || !cliError.SafeToRetry {
		t.Errorf("expected a busy project to be safe to retry, got %+v", cliError)
	}
	if len(cliError.NextActions) == 0 {
		t.Error("expected a next action telling the caller what to do")
	}
}

// Verifies the lock excludes another process and is freed when that process dies without releasing it.
func TestProjectLockIsHeldAcrossProcessesAndFreedWhenTheHolderDies(t *testing.T) {
	projectRoot := newDagProject(t, "aaaa.dag")
	helper := exec.Command(os.Args[0], "-test.run=^TestProjectLockHelperProcess$")
	helper.Env = append(os.Environ(), lockHelperProjectRootEnv+"="+projectRoot)
	stdout, err := helper.StdoutPipe()
	if err != nil {
		t.Fatalf("stdout pipe: %v", err)
	}
	if err := helper.Start(); err != nil {
		t.Fatalf("start helper: %v", err)
	}
	t.Cleanup(func() {
		_ = helper.Process.Kill()
		_ = helper.Wait()
	})
	waitForHelperLine(t, stdout)

	_, err = acquireProjectLock(context.Background(), projectRoot, shortLockTimeout)
	var busy ProjectBusyError
	if !errors.As(err, &busy) {
		t.Fatalf("expected the lock another process holds to be busy, got %v", err)
	}

	// Why a kill rather than a clean exit: a crashed run never releases its lock itself.
	if err := helper.Process.Kill(); err != nil {
		t.Fatalf("kill helper: %v", err)
	}
	_ = helper.Wait()

	release, err := acquireProjectLock(context.Background(), projectRoot, 5*time.Second)
	if err != nil {
		t.Fatalf("expected the dead holder's lock to be free, got %v", err)
	}
	release()
}

// waitForHelperLine blocks until the helper process reports that it holds the lock.
func waitForHelperLine(t *testing.T, stdout interface{ Read([]byte) (int, error) }) {
	t.Helper()

	lines := make(chan string, 1)
	go func() {
		line, _ := bufio.NewReader(stdout).ReadString('\n')
		lines <- strings.TrimSpace(line)
	}()
	select {
	case line := <-lines:
		if line != lockHelperReadyLine {
			t.Fatalf("expected the helper to report %q, got %q", lockHelperReadyLine, line)
		}
	case <-time.After(30 * time.Second):
		t.Fatal("helper process never reported holding the lock")
	}
}

// TestProjectLockHelperProcess is not a test: it is the second process the cross-process test starts.
func TestProjectLockHelperProcess(t *testing.T) {
	projectRoot := os.Getenv(lockHelperProjectRootEnv)
	if projectRoot == "" {
		t.Skip("helper process for TestProjectLockIsHeldAcrossProcessesAndFreedWhenTheHolderDies")
	}

	if _, err := acquireProjectLock(context.Background(), projectRoot, shortLockTimeout); err != nil {
		t.Fatalf("helper could not take the lock: %v", err)
	}
	_, _ = os.Stdout.WriteString(lockHelperReadyLine + "\n")
	// Held until the parent kills this process; the lock is deliberately never released.
	time.Sleep(time.Minute)
}
