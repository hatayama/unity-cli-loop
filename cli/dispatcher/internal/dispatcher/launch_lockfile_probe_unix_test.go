//go:build !windows

package dispatcher

import (
	"os"
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
