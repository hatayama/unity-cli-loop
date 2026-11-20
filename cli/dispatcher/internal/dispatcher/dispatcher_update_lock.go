package dispatcher

import (
	"os"
	"path/filepath"
	"runtime"

	"github.com/hatayama/unity-cli-loop/dispatcher/internal/filelock"
)

// The lock is an OS lock on this file, not the file's existence: the OS releases it when the
// holding process exits, so a crashed or slow installer can neither leave a stale lock behind nor
// have its lock expire while it is still running. The file itself is never deleted, because a
// process that opened it before the deletion would lock a different file than later processes.
const dispatcherUpdateLockFileName = "dispatcher-update.lock"

type dispatcherUpdateLockResult string

const (
	dispatcherUpdateLockAcquired    dispatcherUpdateLockResult = "acquired"
	dispatcherUpdateLockHeld        dispatcherUpdateLockResult = "held"
	dispatcherUpdateLockUnavailable dispatcherUpdateLockResult = "unavailable"
)

func releaseNothing() {}

// tryAcquireDispatcherUpdateLock keeps two uloop processes from running the dispatcher installer
// at the same time. The returned release function is a no-op unless the lock was acquired.
func tryAcquireDispatcherUpdateLock(cacheRoot string) (func(), dispatcherUpdateLockResult) {
	if err := os.MkdirAll(cacheRoot, 0o755); err != nil {
		return releaseNothing, dispatcherUpdateLockUnavailable
	}
	file, err := os.OpenFile(filepath.Join(cacheRoot, dispatcherUpdateLockFileName), os.O_CREATE|os.O_RDWR, 0o644)
	if err != nil {
		return releaseNothing, dispatcherUpdateLockUnavailable
	}
	locked, err := filelock.TryLock(file)
	if err != nil {
		_ = file.Close()
		return releaseNothing, dispatcherUpdateLockUnavailable
	}
	if !locked {
		_ = file.Close()
		return releaseNothing, dispatcherUpdateLockHeld
	}
	return func() {
		_ = filelock.Unlock(file)
		_ = file.Close()
	}, dispatcherUpdateLockAcquired
}

// acquireDispatcherUpdateLockInCache resolves the cache root and takes the update lock there.
func acquireDispatcherUpdateLockInCache(deps dispatcherRunDeps) (func(), dispatcherUpdateLockResult) {
	cacheRoot, err := dispatcherCacheRoot(runtime.GOOS)
	if err != nil {
		return releaseNothing, dispatcherUpdateLockUnavailable
	}
	return deps.acquireUpdateLock(cacheRoot)
}
