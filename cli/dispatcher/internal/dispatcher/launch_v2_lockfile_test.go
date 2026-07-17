package dispatcher

import (
	"context"
	"errors"
	"os"
	"path/filepath"
	"testing"
	"time"
)

// Verifies V2 launch succeeds only after Unity creates or updates its lockfile after the new process starts.
func TestWaitForFreshUnityLockfileWaitsForPostLaunchWrite(t *testing.T) {
	lockfilePath := filepath.Join(t.TempDir(), unityLockfileName)
	startedAt := time.Now()
	go func() {
		time.Sleep(20 * time.Millisecond)
		if err := os.WriteFile(lockfilePath, []byte("lock"), 0o644); err != nil {
			panic(err)
		}
	}()

	err := waitForFreshUnityLockfile(context.Background(), lockfilePath, startedAt, 5*time.Millisecond, time.Second)
	if err != nil {
		t.Fatalf("wait for fresh lockfile: %v", err)
	}
}

// Verifies a stale UnityLockfile left by an earlier Editor session cannot satisfy V2 launch readiness.
func TestWaitForFreshUnityLockfileRejectsStaleLockfile(t *testing.T) {
	lockfilePath := filepath.Join(t.TempDir(), unityLockfileName)
	if err := os.WriteFile(lockfilePath, []byte("stale"), 0o644); err != nil {
		t.Fatalf("write stale lockfile: %v", err)
	}
	staleTime := time.Now().Add(-time.Second)
	if err := os.Chtimes(lockfilePath, staleTime, staleTime); err != nil {
		t.Fatalf("set stale lockfile time: %v", err)
	}

	err := waitForFreshUnityLockfile(context.Background(), lockfilePath, time.Now(), 5*time.Millisecond, 30*time.Millisecond)
	var timeoutErr v2LaunchLockfileTimeoutError
	if !errors.As(err, &timeoutErr) {
		t.Fatalf("expected V2 lockfile timeout, got %v", err)
	}
}
