package dispatcher

import (
	"context"
	"errors"
	"fmt"
	"io"
	"os"
	"path/filepath"
	"time"

	"github.com/hatayama/unity-cli-loop/common/clicore"
)

const (
	// Why a retry: on Windows a helper process Unity spawned (asset import, shader compiler)
	// or a virus scanner can still hold files under Temp for a moment after Unity.exe itself
	// has exited, and an operation on such a file fails until it lets go.
	launchTempLockfileRetryTimeout = 5 * time.Second
	launchTempLockfileRetryPoll    = 250 * time.Millisecond
)

// staleUnityTempCleanupResult reports what the stale-Temp cleanup managed to delete.
// Only the lockfile is mandatory; Unity recreates everything else under Temp on startup.
type staleUnityTempCleanupResult struct {
	// lockfileRemoved is true when a stale lockfile existed and is now gone.
	lockfileRemoved bool
	// leftoverError is non-nil when part of Temp could not be removed. It is a warning,
	// not a failure: the launch continues.
	leftoverError error
}

// cleanStaleUnityTemp removes the lockfile a previous Unity left behind and, on a best-effort
// basis, the rest of the Temp directory. It returns an error only when the lockfile survives,
// because the next Editor would then refuse to open the project as already opened. A lockfile a
// running process still holds is not stale: it is left in place and reported as an error.
func cleanStaleUnityTemp(ctx context.Context, projectRoot string, deps launchDeps) (staleUnityTempCleanupResult, error) {
	lockfilePath := unityLockfilePath(projectRoot)
	lockfileExists, err := staleUnityLockfileExists(ctx, lockfilePath, deps)
	if err != nil {
		return staleUnityTempCleanupResult{}, err
	}
	if !lockfileExists {
		return staleUnityTempCleanupResult{}, nil
	}
	if err := waitForUnityLockfileRelease(ctx, lockfilePath, deps); err != nil {
		return staleUnityTempCleanupResult{}, err
	}

	removeLockfile := func() error {
		err := deps.removePath(lockfilePath)
		// Why it is not retried: a running Editor keeps the lockfile open for as long as it runs, so
		// waiting only delays the report. A restart has already waited for its Editor to exit by now.
		if isLockfileDeleteRefusedForOpenHandle(err) {
			return unretryableTempError{cause: heldUnityLockfileError{lockfilePath: lockfilePath}}
		}
		return err
	}
	if err := retryWhileTempFileIsHeld(ctx, deps, removeLockfile); err != nil {
		var heldError heldUnityLockfileError
		if errors.As(err, &heldError) {
			return staleUnityTempCleanupResult{}, heldError
		}
		return staleUnityTempCleanupResult{}, staleUnityLockfileError("delete", err)
	}

	result := staleUnityTempCleanupResult{lockfileRemoved: true}
	if err := deps.removePath(filepath.Join(projectRoot, launchTempDirectoryName)); err != nil {
		result.leftoverError = err
	}
	return result, nil
}

// waitForUnityLockfileRelease returns once no process holds a lock on the lockfile, and reports the
// lockfile as held when one still does after the retry budget.
// Why the lock is checked although the caller found no Editor: the process scan can miss a running
// one (batch mode, no -projectPath), and removing a lockfile a running Editor holds lets a second
// Editor open the same project.
// Why it waits: an Editor that was just stopped leaves the process list before it has let go of the
// lockfile, and a restart must not be refused for that.
func waitForUnityLockfileRelease(ctx context.Context, lockfilePath string, deps launchDeps) error {
	confirmReleased := func() error {
		held, err := deps.isUnityLockfileHeld(lockfilePath)
		// An exiting Editor may delete the lockfile itself, which leaves nothing to hold.
		if os.IsNotExist(err) {
			return nil
		}
		if err != nil {
			return fmt.Errorf(
				"could not check whether %s is still held by a running process: %w",
				filepath.Join(launchTempDirectoryName, unityLockfileName),
				err,
			)
		}
		if held {
			return heldUnityLockfileError{lockfilePath: lockfilePath}
		}
		return nil
	}
	return retryWhileTempFileIsHeld(ctx, deps, confirmReleased)
}

// staleUnityLockfileExists reports whether a previous Unity left its lockfile behind. A failure
// to even inspect the path is retried too: an answer of "not there" and an answer of "cannot
// tell" must not lead to the same silent continue.
func staleUnityLockfileExists(ctx context.Context, lockfilePath string, deps launchDeps) (bool, error) {
	lockfileExists := false
	inspect := func() error {
		_, statError := deps.statPath(lockfilePath)
		if statError == nil {
			lockfileExists = true
			return nil
		}
		if os.IsNotExist(statError) {
			lockfileExists = false
			return nil
		}
		return statError
	}
	if err := retryWhileTempFileIsHeld(ctx, deps, inspect); err != nil {
		return false, staleUnityLockfileError("inspect", err)
	}
	return lockfileExists, nil
}

// unretryableTempError marks a Temp operation failure that waiting cannot clear, so
// retryWhileTempFileIsHeld returns its cause at once.
type unretryableTempError struct {
	cause error
}

func (err unretryableTempError) Error() string {
	return err.cause.Error()
}

func (err unretryableTempError) Unwrap() error {
	return err.cause
}

// retryWhileTempFileIsHeld repeats operation until it succeeds, it fails with an
// unretryableTempError, or the retry budget runs out, so every Temp operation shares one retry policy.
func retryWhileTempFileIsHeld(ctx context.Context, deps launchDeps, operation func() error) error {
	deadline := deps.now().Add(launchTempLockfileRetryTimeout)
	for {
		operationError := operation()
		if operationError == nil {
			return nil
		}
		var unretryable unretryableTempError
		if errors.As(operationError, &unretryable) {
			return unretryable.cause
		}
		if ctx.Err() != nil {
			return ctx.Err()
		}
		if !deps.now().Before(deadline) {
			return operationError
		}
		deps.sleep(launchTempLockfileRetryPoll)
	}
}

// staleUnityLockfileError names the lockfile by its project-relative path so the message stays
// readable without echoing the absolute location of the project.
func staleUnityLockfileError(action string, cause error) error {
	relativeLockfilePath := filepath.Join(launchTempDirectoryName, unityLockfileName)
	return fmt.Errorf(
		"could not %s the stale %s within %s; another process is still holding it: %w",
		action,
		relativeLockfilePath,
		launchTempLockfileRetryTimeout,
		cause,
	)
}

// writeStaleUnityTempLeftoverWarning reports files that stayed behind under Temp. Unity recreates
// them on startup, so this must not stop the launch.
func writeStaleUnityTempLeftoverWarning(stderr io.Writer, leftoverError error) {
	clicore.WriteFormat(
		stderr,
		"Warning: some files under %s could not be removed (still in use); Unity will recreate them: %v\n",
		launchTempDirectoryName,
		leftoverError,
	)
}
