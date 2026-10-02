//go:build !windows

package unityprocess

import (
	"context"
	"errors"
	"testing"
)

// Verifies a listing failure is returned to the caller instead of being reported as "no Editor running".
// The context is cancelled up front, and the macOS and Linux listers check it before reading any
// process information, so no OS process listing happens. Windows is excluded because its lister
// starts PowerShell instead.
func TestFindRunningUnityProcessReturnsListingError(t *testing.T) {
	cancelledContext, cancel := context.WithCancel(context.Background())
	cancel()

	process, err := FindRunningUnityProcess(cancelledContext, t.TempDir())

	if !errors.Is(err, context.Canceled) {
		t.Fatalf("expected the cancellation error from the listing, got %v", err)
	}
	if process != nil {
		t.Fatalf("expected no process on a listing failure, got %#v", process)
	}
}
