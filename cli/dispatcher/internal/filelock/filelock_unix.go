//go:build unix

package filelock

import (
	"errors"
	"os"

	"golang.org/x/sys/unix"
)

// TryLock takes the exclusive lock without blocking and reports whether another holder has it.
func TryLock(file *os.File) (bool, error) {
	for {
		err := unix.Flock(int(file.Fd()), unix.LOCK_EX|unix.LOCK_NB)
		if err == nil {
			return true, nil
		}
		// Why retry EINTR here: a signal interrupted the attempt, which says nothing about another
		// holder, and reporting it as contention would make callers that do not poll give up.
		if errors.Is(err, unix.EINTR) {
			continue
		}
		if errors.Is(err, unix.EWOULDBLOCK) {
			return false, nil
		}
		return false, err
	}
}

// Unlock releases the lock TryLock took.
func Unlock(file *os.File) error {
	return unix.Flock(int(file.Fd()), unix.LOCK_UN)
}
