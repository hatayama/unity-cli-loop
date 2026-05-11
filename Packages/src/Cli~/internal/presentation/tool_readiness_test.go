package presentation

import (
	"context"
	"errors"
	"testing"
)

// Verifies that parent cancellation is preserved instead of being reported as a timeout.
func TestToolReadinessDoneErrorPropagatesParentCancellation(t *testing.T) {
	ctx, cancel := context.WithCancel(context.Background())
	cancel()

	err := toolReadinessDoneError(ctx)

	if !errors.Is(err, context.Canceled) {
		t.Fatalf("expected context cancellation, got %v", err)
	}
}

// Verifies that readiness timeout still uses the user-facing timeout message.
func TestToolReadinessDoneErrorReportsTimeoutWhenParentIsActive(t *testing.T) {
	err := toolReadinessDoneError(context.Background())

	if err == nil || err.Error() != "timed out waiting for Unity tool readiness" {
		t.Fatalf("timeout error mismatch: %v", err)
	}
}
