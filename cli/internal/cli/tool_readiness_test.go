package cli

import (
	"context"
	"errors"
	"testing"
)

// Verifies that parent cancellation is preserved instead of being reported as a timeout.
func TestToolReadinessDoneErrorPropagatesParentCancellation(t *testing.T) {
	ctx, cancel := context.WithCancel(context.Background())
	cancel()

	err := toolReadinessDoneError(ctx, t.TempDir(), errors.New("probe failed"))

	if !errors.Is(err, context.Canceled) {
		t.Fatalf("expected context cancellation, got %v", err)
	}
}

// Verifies that readiness timeout still uses the user-facing timeout message.
func TestToolReadinessDoneErrorReportsTimeoutWhenParentIsActive(t *testing.T) {
	originalFinder := findRunningUnityProcessForReadiness
	findRunningUnityProcessForReadiness = func(context.Context, string) (*unityProcess, error) {
		return nil, nil
	}
	t.Cleanup(func() {
		findRunningUnityProcessForReadiness = originalFinder
	})

	err := toolReadinessDoneError(context.Background(), t.TempDir(), nil)

	if err == nil || err.Error() != "timed out waiting for Unity tool readiness" {
		t.Fatalf("timeout error mismatch: %v", err)
	}
}

// Verifies that readiness timeout reports a live Unity process whose IPC server does not respond.
func TestToolReadinessDoneErrorReportsServerNotRespondingWhenUnityRuns(t *testing.T) {
	originalFinder := findRunningUnityProcessForReadiness
	findRunningUnityProcessForReadiness = func(context.Context, string) (*unityProcess, error) {
		return &unityProcess{pid: 123}, nil
	}
	t.Cleanup(func() {
		findRunningUnityProcessForReadiness = originalFinder
	})

	err := toolReadinessDoneError(context.Background(), t.TempDir(), errors.New("probe failed"))

	var notRespondingErr unityServerNotRespondingError
	if !errors.As(err, &notRespondingErr) {
		t.Fatalf("expected Unity server not responding error, got %v", err)
	}
}
