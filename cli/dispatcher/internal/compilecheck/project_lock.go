package compilecheck

import (
	"context"
	"fmt"
	"os"
	"path/filepath"
	"time"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
)

const (
	// Why the lock sits beside the output root rather than inside it: the output directory is
	// documented as disposable, and deleting a held lock file lets the next run lock a fresh file
	// while the first still writes, which is exactly the overlap the lock exists to prevent.
	projectLockPath = "Library/uloop/compile-check.lock"
	// Why minutes rather than seconds: an --all run of a large project compiles every assembly and
	// takes minutes, so a shorter wait would turn ordinary queuing behind it into a failure.
	defaultProjectLockTimeout = 5 * time.Minute
	projectLockPollInterval   = 100 * time.Millisecond
	projectLockPermissions    = 0o644
)

// ProjectBusyError reports that another compile-check on the same project held the lock for longer
// than this run was willing to wait.
type ProjectBusyError struct {
	LockPath string
	Waited   time.Duration
}

func (err ProjectBusyError) Error() string {
	return fmt.Sprintf(
		"another compile-check is running on this project (lock %s still held after %s)",
		err.LockPath, err.Waited)
}

// ToCLIError lets the shared classifier render this error without knowing the compilecheck package.
func (err ProjectBusyError) ToCLIError(context clierrors.ErrorContext) clierrors.CLIError {
	return clierrors.CLIError{
		ErrorCode:   clierrors.ErrorCodeCompileCheckProjectBusy,
		Phase:       clierrors.ErrorPhaseExecution,
		Message:     err.Error(),
		Retryable:   true,
		SafeToRetry: true,
		ProjectRoot: context.ProjectRoot,
		Command:     context.Command,
		NextActions: []string{
			"Wait for the other `compile-check` on this project to finish, then run `compile-check` once more.",
		},
	}
}

// acquireProjectLock takes the project's compile-check lock, waiting up to timeout for another
// holder, and returns the function that releases it.
// Why an OS advisory lock rather than a PID file: the OS drops it when the holder exits, crashes
// included, so no stale lock is ever left for a later run to judge.
func acquireProjectLock(ctx context.Context, projectRoot string, timeout time.Duration) (func(), error) {
	lockPath := filepath.Join(projectRoot, filepath.FromSlash(projectLockPath))
	if err := os.MkdirAll(filepath.Dir(lockPath), outputDirPermissions); err != nil {
		return nil, fmt.Errorf("failed to create %s: %w", filepath.Dir(lockPath), err)
	}
	file, err := os.OpenFile(lockPath, os.O_RDWR|os.O_CREATE, projectLockPermissions)
	if err != nil {
		return nil, fmt.Errorf("failed to open %s: %w", lockPath, err)
	}

	if err := waitForFileLock(ctx, file, timeout); err != nil {
		_ = file.Close()
		return nil, err
	}

	return func() {
		_ = unlockFile(file)
		_ = file.Close()
	}, nil
}

// waitForFileLock polls for the exclusive lock until it is taken, the timeout passes or ctx ends.
func waitForFileLock(ctx context.Context, file *os.File, timeout time.Duration) error {
	deadline := time.Now().Add(timeout)
	for {
		locked, err := tryLockFile(file)
		if err != nil {
			return fmt.Errorf("failed to lock %s: %w", file.Name(), err)
		}
		if locked {
			return nil
		}

		remaining := time.Until(deadline)
		if remaining <= 0 {
			return ProjectBusyError{LockPath: file.Name(), Waited: timeout}
		}
		timer := time.NewTimer(min(projectLockPollInterval, remaining))
		select {
		case <-ctx.Done():
			timer.Stop()
			return ctx.Err()
		case <-timer.C:
		}
	}
}

// projectLockTimeout is how long a run waits for another run on the same project.
func projectLockTimeout(options Options) time.Duration {
	if options.LockTimeout <= 0 {
		return defaultProjectLockTimeout
	}

	return options.LockTimeout
}
