package dispatcher

import (
	"errors"
	"io/fs"
	"os"
	"path/filepath"
	"runtime"
	"time"
)

const (
	dispatcherUpdateLockFileName = "dispatcher-update.lock"
	// A crashed update leaves its lock behind; past this age the lock is treated as abandoned.
	// It is far longer than a healthy installer run so a slow download is never interrupted.
	dispatcherUpdateLockStaleAfter = 15 * time.Minute
)

type dispatcherUpdateLockResult string

const (
	dispatcherUpdateLockAcquired    dispatcherUpdateLockResult = "acquired"
	dispatcherUpdateLockHeld        dispatcherUpdateLockResult = "held"
	dispatcherUpdateLockUnavailable dispatcherUpdateLockResult = "unavailable"
)

func releaseNothing() {}

// tryAcquireDispatcherUpdateLock keeps two uloop processes from running the dispatcher installer
// at the same time. The returned release function is a no-op unless the lock was acquired.
func tryAcquireDispatcherUpdateLock(cacheRoot string, now time.Time) (func(), dispatcherUpdateLockResult) {
	if err := os.MkdirAll(cacheRoot, 0o755); err != nil {
		return releaseNothing, dispatcherUpdateLockUnavailable
	}
	lockPath := filepath.Join(cacheRoot, dispatcherUpdateLockFileName)
	result := createDispatcherUpdateLockFile(lockPath)
	if result != dispatcherUpdateLockHeld {
		return dispatcherUpdateLockRelease(lockPath, result), result
	}
	if !isStaleDispatcherUpdateLock(lockPath, now) {
		return releaseNothing, dispatcherUpdateLockHeld
	}
	_ = os.Remove(lockPath)
	// Why only one retry: a second ErrExist means another process won the race for the
	// abandoned lock, which is exactly the "someone else is updating" case.
	result = createDispatcherUpdateLockFile(lockPath)
	return dispatcherUpdateLockRelease(lockPath, result), result
}

// acquireDispatcherUpdateLockInCache resolves the cache root and takes the update lock there.
func acquireDispatcherUpdateLockInCache(deps dispatcherRunDeps) (func(), dispatcherUpdateLockResult) {
	cacheRoot, err := dispatcherCacheRoot(runtime.GOOS)
	if err != nil {
		return releaseNothing, dispatcherUpdateLockUnavailable
	}
	return deps.acquireUpdateLock(cacheRoot, deps.now())
}

func createDispatcherUpdateLockFile(lockPath string) dispatcherUpdateLockResult {
	file, err := os.OpenFile(lockPath, os.O_CREATE|os.O_EXCL|os.O_WRONLY, 0o644)
	if err == nil {
		// Why close at once: an open handle would block os.Remove on Windows at release time.
		_ = file.Close()
		return dispatcherUpdateLockAcquired
	}
	if errors.Is(err, fs.ErrExist) {
		return dispatcherUpdateLockHeld
	}
	return dispatcherUpdateLockUnavailable
}

func isStaleDispatcherUpdateLock(lockPath string, now time.Time) bool {
	info, err := os.Stat(lockPath)
	if err != nil {
		// The holder released the lock between our create and stat; retrying is safe.
		return errors.Is(err, fs.ErrNotExist)
	}
	return now.Sub(info.ModTime()) >= dispatcherUpdateLockStaleAfter
}

func dispatcherUpdateLockRelease(lockPath string, result dispatcherUpdateLockResult) func() {
	if result != dispatcherUpdateLockAcquired {
		return releaseNothing
	}
	return func() {
		_ = os.Remove(lockPath)
	}
}
