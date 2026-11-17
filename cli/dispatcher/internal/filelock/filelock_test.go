package filelock

import (
	"os"
	"path/filepath"
	"testing"
)

func openLockFile(t *testing.T, lockPath string) *os.File {
	t.Helper()
	file, err := os.OpenFile(lockPath, os.O_CREATE|os.O_RDWR, 0o644)
	if err != nil {
		t.Fatalf("open lock file: %v", err)
	}
	t.Cleanup(func() {
		_ = file.Close()
	})
	return file
}

func TestTryLockIsExclusiveAcrossOpensAndReleasedByUnlock(t *testing.T) {
	// Verifies a second open of the same file cannot take the lock while the first holds it, and can once it is unlocked.
	lockPath := filepath.Join(t.TempDir(), "test.lock")
	first := openLockFile(t, lockPath)
	second := openLockFile(t, lockPath)

	if locked, err := TryLock(first); err != nil || !locked {
		t.Fatalf("first lock: locked=%t err=%v", locked, err)
	}
	if locked, err := TryLock(second); err != nil || locked {
		t.Fatalf("second lock while held: locked=%t err=%v", locked, err)
	}
	if err := Unlock(first); err != nil {
		t.Fatalf("unlock: %v", err)
	}
	if locked, err := TryLock(second); err != nil || !locked {
		t.Fatalf("second lock after unlock: locked=%t err=%v", locked, err)
	}
}

func TestTryLockReportsAnUnusableFile(t *testing.T) {
	// Verifies locking a closed file returns an error instead of reporting the lock as held by someone else.
	file := openLockFile(t, filepath.Join(t.TempDir(), "closed.lock"))
	if err := file.Close(); err != nil {
		t.Fatalf("close: %v", err)
	}

	if locked, err := TryLock(file); err == nil || locked {
		t.Fatalf("expected an error for a closed file: locked=%t err=%v", locked, err)
	}
}
