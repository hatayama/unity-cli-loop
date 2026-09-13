package dispatcher

import (
	"context"
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
	// has exited, and the removal fails with a sharing violation until it lets go.
	launchTempLockfileRemoveTimeout = 5 * time.Second
	launchTempLockfileRemovePoll    = 250 * time.Millisecond
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
// because the next Editor would then refuse to open the project as already opened.
func cleanStaleUnityTemp(ctx context.Context, projectRoot string, deps launchDeps) (staleUnityTempCleanupResult, error) {
	lockfilePath := unityLockfilePath(projectRoot)
	if _, err := os.Stat(lockfilePath); err != nil {
		if os.IsNotExist(err) {
			return staleUnityTempCleanupResult{}, nil
		}
		return staleUnityTempCleanupResult{}, err
	}

	if err := removeStaleUnityLockfile(ctx, lockfilePath, deps); err != nil {
		return staleUnityTempCleanupResult{}, err
	}

	result := staleUnityTempCleanupResult{lockfileRemoved: true}
	if err := deps.removePath(filepath.Join(projectRoot, launchTempDirectoryName)); err != nil {
		result.leftoverError = err
	}
	return result, nil
}

// removeStaleUnityLockfile deletes the lockfile, retrying while another process still holds it.
func removeStaleUnityLockfile(ctx context.Context, lockfilePath string, deps launchDeps) error {
	deadline := deps.now().Add(launchTempLockfileRemoveTimeout)
	for {
		removeError := deps.removePath(lockfilePath)
		if removeError == nil {
			return nil
		}
		if ctx.Err() != nil {
			return ctx.Err()
		}
		if !deps.now().Before(deadline) {
			return staleUnityLockfileRemovalError(removeError)
		}
		deps.sleep(launchTempLockfileRemovePoll)
	}
}

// staleUnityLockfileRemovalError names the lockfile by its project-relative path so the message
// stays readable without echoing the absolute location of the project.
func staleUnityLockfileRemovalError(cause error) error {
	relativeLockfilePath := filepath.Join(launchTempDirectoryName, unityLockfileName)
	return fmt.Errorf(
		"could not delete the stale %s within %s; another process is still holding it: %w",
		relativeLockfilePath,
		launchTempLockfileRemoveTimeout,
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
