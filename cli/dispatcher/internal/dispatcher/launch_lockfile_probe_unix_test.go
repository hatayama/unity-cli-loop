//go:build !windows

package dispatcher

import (
	"bufio"
	"io"
	"os"
	"os/exec"
	"path/filepath"
	"testing"

	"golang.org/x/sys/unix"
)

func TestIsUnityLockfileHeldReportsALockAnotherHolderKeeps(t *testing.T) {
	// Verifies the probe sees an exclusive lock kept through another open of the lockfile, which is how
	// a running Editor holds it.
	lockfilePath := filepath.Join(t.TempDir(), unityLockfileName)
	holder, err := os.OpenFile(lockfilePath, os.O_CREATE|os.O_RDWR, 0o644)
	if err != nil {
		t.Fatalf("failed to create the lockfile: %v", err)
	}
	t.Cleanup(func() { _ = holder.Close() })
	if err := unix.Flock(int(holder.Fd()), unix.LOCK_EX|unix.LOCK_NB); err != nil {
		t.Fatalf("failed to lock the lockfile: %v", err)
	}

	held, err := isUnityLockfileHeld(lockfilePath)
	if err != nil {
		t.Fatalf("probe failed: %v", err)
	}
	if !held {
		t.Fatal("expected the locked lockfile to be reported as held")
	}
}

func TestIsUnityLockfileHeldLeavesAnUnlockedLockfileAvailable(t *testing.T) {
	// Verifies a lockfile nobody locks is reported as free, and that the probe keeps no lock of its own.
	lockfilePath := filepath.Join(t.TempDir(), unityLockfileName)
	if err := os.WriteFile(lockfilePath, []byte{}, 0o644); err != nil {
		t.Fatalf("failed to create the lockfile: %v", err)
	}

	held, err := isUnityLockfileHeld(lockfilePath)
	if err != nil {
		t.Fatalf("probe failed: %v", err)
	}
	if held {
		t.Fatal("expected the unlocked lockfile to be reported as free")
	}
	next, err := os.Open(lockfilePath)
	if err != nil {
		t.Fatalf("failed to reopen the lockfile: %v", err)
	}
	t.Cleanup(func() { _ = next.Close() })
	if err := unix.Flock(int(next.Fd()), unix.LOCK_EX|unix.LOCK_NB); err != nil {
		t.Fatalf("the probe must release its lock, but the next lock failed: %v", err)
	}
}

const recordLockHolderLockfileEnv = "ULOOP_TEST_RECORD_LOCK_HOLDER_LOCKFILE"

func TestIsUnityLockfileHeldReportsARecordLockAnotherProcessKeeps(t *testing.T) {
	// Verifies the probe sees an fcntl record lock, which flock does not conflict with on Linux.
	// Why a second process holds it: fcntl never reports a lock the asking process owns itself.
	lockfilePath := filepath.Join(t.TempDir(), unityLockfileName)
	if err := os.WriteFile(lockfilePath, []byte{}, 0o644); err != nil {
		t.Fatalf("failed to create the lockfile: %v", err)
	}
	holder := exec.Command(os.Args[0], "-test.run=^TestRecordLockHolderProcess$")
	holder.Env = append(os.Environ(), recordLockHolderLockfileEnv+"="+lockfilePath)
	holderInput, err := holder.StdinPipe()
	if err != nil {
		t.Fatalf("failed to open the holder's stdin: %v", err)
	}
	holderOutput, err := holder.StdoutPipe()
	if err != nil {
		t.Fatalf("failed to open the holder's stdout: %v", err)
	}
	if err := holder.Start(); err != nil {
		t.Fatalf("failed to start the holder process: %v", err)
	}
	t.Cleanup(func() {
		_ = holderInput.Close()
		_ = holder.Wait()
	})
	lockedLine, err := bufio.NewReader(holderOutput).ReadString('\n')
	if err != nil || lockedLine != recordLockHolderReadyLine {
		t.Fatalf("the holder process did not take the lock: line=%q err=%v", lockedLine, err)
	}

	held, err := isUnityLockfileHeld(lockfilePath)
	if err != nil {
		t.Fatalf("probe failed: %v", err)
	}
	if !held {
		t.Fatal("expected the record-locked lockfile to be reported as held")
	}
}

const recordLockHolderReadyLine = "locked\n"

func TestRecordLockHolderProcess(t *testing.T) {
	// Not a test of its own: the body of the process the record-lock test starts. It takes an fcntl
	// write lock on the given lockfile and keeps it until its stdin closes.
	lockfilePath := os.Getenv(recordLockHolderLockfileEnv)
	if lockfilePath == "" {
		return
	}
	lockfile, err := os.OpenFile(lockfilePath, os.O_RDWR, 0)
	if err != nil {
		t.Fatalf("failed to open the lockfile: %v", err)
	}
	t.Cleanup(func() { _ = lockfile.Close() })
	lock := unix.Flock_t{Type: unix.F_WRLCK, Whence: 0, Start: 0, Len: 0}
	if err := unix.FcntlFlock(lockfile.Fd(), unix.F_SETLK, &lock); err != nil {
		t.Fatalf("failed to take the record lock: %v", err)
	}
	if _, err := os.Stdout.WriteString(recordLockHolderReadyLine); err != nil {
		t.Fatalf("failed to report the lock: %v", err)
	}
	_, _ = io.Copy(io.Discard, os.Stdin)
}
