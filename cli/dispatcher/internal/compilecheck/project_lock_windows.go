//go:build windows

package compilecheck

import (
	"errors"
	"os"

	"golang.org/x/sys/windows"
)

// lockedRangeLength covers one byte: the lock only has to be exclusive, not to guard any content.
const lockedRangeLength = 1

// tryLockFile takes the exclusive lock without blocking and reports whether another holder has it.
func tryLockFile(file *os.File) (bool, error) {
	err := windows.LockFileEx(
		windows.Handle(file.Fd()),
		windows.LOCKFILE_EXCLUSIVE_LOCK|windows.LOCKFILE_FAIL_IMMEDIATELY,
		0, lockedRangeLength, 0, &windows.Overlapped{})
	if err == nil {
		return true, nil
	}
	if errors.Is(err, windows.ERROR_LOCK_VIOLATION) {
		return false, nil
	}

	return false, err
}

// unlockFile releases the lock tryLockFile took.
// Why it is not left to Close: Windows releases a closed handle's locks only eventually, and the
// next run polling for the lock should not have to wait for that.
func unlockFile(file *os.File) error {
	return windows.UnlockFileEx(windows.Handle(file.Fd()), 0, lockedRangeLength, 0, &windows.Overlapped{})
}
