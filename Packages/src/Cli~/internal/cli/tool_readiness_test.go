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

// Verifies that recovery waits exit immediately when Unity reports an intentional stop.
func TestWaitForRecoveringToolReadinessStopsWhenServerStateStopped(t *testing.T) {
	projectRoot := t.TempDir()
	writeReadinessServerStateForTest(t, projectRoot, `{"phase":"stopped","reason":"manual-stop"}`)

	err := waitForRecoveringToolReadiness(context.Background(), projectRoot)

	var stoppedErr serverStoppedError
	if !errors.As(err, &stoppedErr) {
		t.Fatalf("expected stopped error, got %v", err)
	}
	if stoppedErr.state.Reason != "manual-stop" {
		t.Fatalf("stopped reason mismatch: %#v", stoppedErr.state)
	}
}

// Verifies that stale busy state returns immediately when the Unity process is gone.
func TestWaitForRecoveringToolReadinessReportsStaleBusyStateWhenUnityIsGone(t *testing.T) {
	originalFinder := findRunningUnityProcessForReadiness
	findRunningUnityProcessForReadiness = func(context.Context, string) (*unityProcess, error) {
		return nil, nil
	}
	defer func() {
		findRunningUnityProcessForReadiness = originalFinder
	}()

	projectRoot := t.TempDir()
	writeReadinessServerStateForTest(t, projectRoot, `{"phase":"recovering","reason":"domain-reload-after"}`)

	err := waitForRecoveringToolReadiness(context.Background(), projectRoot)

	var staleErr staleServerStateError
	if !errors.As(err, &staleErr) {
		t.Fatalf("expected stale state error, got %v", err)
	}
	if staleErr.state.Phase != "recovering" {
		t.Fatalf("stale phase mismatch: %#v", staleErr.state)
	}
}

// Verifies that stale-state process lookup is bounded by the readiness timeout context.
func TestWaitForRecoveringToolReadinessPassesTimeoutContextToStaleCheck(t *testing.T) {
	originalFinder := findRunningUnityProcessForReadiness
	receivedDeadline := false
	findRunningUnityProcessForReadiness = func(ctx context.Context, projectRoot string) (*unityProcess, error) {
		_, receivedDeadline = ctx.Deadline()
		return nil, nil
	}
	defer func() {
		findRunningUnityProcessForReadiness = originalFinder
	}()

	projectRoot := t.TempDir()
	writeReadinessServerStateForTest(t, projectRoot, `{"phase":"recovering","reason":"domain-reload-after"}`)

	err := waitForRecoveringToolReadiness(context.Background(), projectRoot)

	var staleErr staleServerStateError
	if !errors.As(err, &staleErr) {
		t.Fatalf("expected stale state error, got %v", err)
	}
	if !receivedDeadline {
		t.Fatal("stale-state process lookup did not receive a timeout context")
	}
}
