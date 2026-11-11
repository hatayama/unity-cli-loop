//go:build !windows

package dispatcher

import (
	"errors"
	"os"

	"golang.org/x/sys/unix"
)

// isUnityLockfileHeld reports whether a process holds a lock on the project's lockfile, which a
// running Editor does for as long as it has the project open.
func isUnityLockfileHeld(lockfilePath string) (bool, error) {
	lockfile, err := os.Open(lockfilePath)
	if err != nil {
		return false, err
	}
	defer func() { _ = lockfile.Close() }()

	// Why both kinds of lock are checked: flock and fcntl record locks do not see each other on
	// Linux, and which one the Editor takes there is not something this code can rely on.
	recordLocked, err := hasConflictingRecordLock(lockfile)
	if err != nil || recordLocked {
		return recordLocked, err
	}
	return hasConflictingFlock(lockfile)
}

// hasConflictingRecordLock asks which lock would block a write lock, without taking one.
func hasConflictingRecordLock(lockfile *os.File) (bool, error) {
	query := unix.Flock_t{Type: unix.F_WRLCK, Whence: 0, Start: 0, Len: 0}
	if err := retryOnInterrupt(func() error {
		return unix.FcntlFlock(lockfile.Fd(), unix.F_GETLK, &query)
	}); err != nil {
		return false, err
	}
	return query.Type != unix.F_UNLCK, nil
}

// hasConflictingFlock takes the exclusive lock without blocking and gives it straight back.
func hasConflictingFlock(lockfile *os.File) (bool, error) {
	err := retryOnInterrupt(func() error {
		return unix.Flock(int(lockfile.Fd()), unix.LOCK_EX|unix.LOCK_NB)
	})
	if errors.Is(err, unix.EWOULDBLOCK) {
		return true, nil
	}
	if err != nil {
		return false, err
	}
	return false, unix.Flock(int(lockfile.Fd()), unix.LOCK_UN)
}

// retryOnInterrupt repeats a call a signal interrupted, since the interruption says nothing about
// the lock.
func retryOnInterrupt(call func() error) error {
	for {
		err := call()
		if !errors.Is(err, unix.EINTR) {
			return err
		}
	}
}
