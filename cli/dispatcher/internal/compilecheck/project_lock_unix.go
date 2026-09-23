//go:build unix

package compilecheck

import (
	"errors"
	"os"

	"golang.org/x/sys/unix"
)

// tryLockFile takes the exclusive lock without blocking and reports whether another holder has it.
func tryLockFile(file *os.File) (bool, error) {
	err := unix.Flock(int(file.Fd()), unix.LOCK_EX|unix.LOCK_NB)
	if err == nil {
		return true, nil
	}
	// Why EINTR counts as "not yet": a signal interrupted the attempt, and the caller polls again.
	if errors.Is(err, unix.EWOULDBLOCK) || errors.Is(err, unix.EINTR) {
		return false, nil
	}

	return false, err
}

// unlockFile releases the lock tryLockFile took.
func unlockFile(file *os.File) error {
	return unix.Flock(int(file.Fd()), unix.LOCK_UN)
}
