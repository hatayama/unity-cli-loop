package cli

import (
	"context"
	"errors"
	"os"
	"path/filepath"
	"testing"
	"time"
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

// Verifies that launch readiness fails quickly when Unity leaves the server stopped.
func TestWaitForToolReadinessStopsWhenServerStateStaysStopped(t *testing.T) {
	originalGrace := stoppedServerStateGrace
	originalFinder := findRunningUnityProcessForReadiness
	stoppedServerStateGrace = 0
	findRunningUnityProcessForReadiness = func(context.Context, string) (*unityProcess, error) {
		return nil, nil
	}
	t.Cleanup(func() {
		stoppedServerStateGrace = originalGrace
		findRunningUnityProcessForReadiness = originalFinder
	})

	projectRoot := t.TempDir()
	createUnityProjectForReadinessTest(t, projectRoot)
	writeReadinessServerStateForTest(t, projectRoot, `{"phase":"stopped","reason":"domain-reload-after-no-server"}`)
	ctx, cancel := context.WithTimeout(context.Background(), 50*time.Millisecond)
	defer cancel()

	err := waitForToolReadiness(ctx, projectRoot)

	var stoppedErr serverStoppedError
	if !errors.As(err, &stoppedErr) {
		t.Fatalf("expected stopped error, got %v", err)
	}
	if stoppedErr.state.Reason != "domain-reload-after-no-server" {
		t.Fatalf("stopped reason mismatch: %#v", stoppedErr.state)
	}
}

// Verifies that launch readiness does not fail early while Unity is still starting.
func TestWaitForToolReadinessContinuesWhenStoppedStateIsStaleButUnityRuns(t *testing.T) {
	originalGrace := stoppedServerStateGrace
	originalFinder := findRunningUnityProcessForReadiness
	stoppedServerStateGrace = 0
	findRunningUnityProcessForReadiness = func(context.Context, string) (*unityProcess, error) {
		return &unityProcess{pid: 123}, nil
	}
	t.Cleanup(func() {
		stoppedServerStateGrace = originalGrace
		findRunningUnityProcessForReadiness = originalFinder
	})

	projectRoot := t.TempDir()
	createUnityProjectForReadinessTest(t, projectRoot)
	writeReadinessServerStateForTest(t, projectRoot, `{"phase":"stopped","reason":"editor-quitting"}`)
	ctx, cancel := context.WithTimeout(context.Background(), 50*time.Millisecond)
	defer cancel()

	err := waitForToolReadiness(ctx, projectRoot)

	if !errors.Is(err, context.DeadlineExceeded) {
		t.Fatalf("expected readiness wait to keep polling until context deadline, got %v", err)
	}
}

// Verifies that launch readiness gives startup recovery a chance to replace stopped state.
func TestWaitForStoppedServerStateChangeDetectsRecoveryState(t *testing.T) {
	originalGrace := stoppedServerStateGrace
	originalPoll := stoppedServerStatePoll
	stoppedServerStateGrace = 100 * time.Millisecond
	stoppedServerStatePoll = time.Millisecond
	t.Cleanup(func() {
		stoppedServerStateGrace = originalGrace
		stoppedServerStatePoll = originalPoll
	})

	projectRoot := t.TempDir()
	initialState := serverState{Phase: "stopped", Reason: "domain-reload-after-no-server"}
	writeReadinessServerStateForTest(t, projectRoot, `{"phase":"stopped","reason":"domain-reload-after-no-server"}`)
	go func() {
		time.Sleep(10 * time.Millisecond)
		writeReadinessServerStateForTest(t, projectRoot, `{"phase":"recovering","reason":"server-recovery"}`)
	}()

	changed, err := waitForStoppedServerStateChange(context.Background(), context.Background(), projectRoot, initialState)
	if err != nil {
		t.Fatalf("waitForStoppedServerStateChange failed: %v", err)
	}
	if !changed {
		t.Fatal("stopped server state change was not detected")
	}
}

// Verifies that stopped-state grace timeout uses the public readiness timeout message.
func TestWaitForStoppedServerStateChangeWhenInternalTimeoutExpiresReportsReadinessTimeout(t *testing.T) {
	originalGrace := stoppedServerStateGrace
	originalPoll := stoppedServerStatePoll
	stoppedServerStateGrace = 100 * time.Millisecond
	stoppedServerStatePoll = time.Millisecond
	t.Cleanup(func() {
		stoppedServerStateGrace = originalGrace
		stoppedServerStatePoll = originalPoll
	})

	projectRoot := t.TempDir()
	initialState := serverState{Phase: "stopped", Reason: "domain-reload-after-no-server"}
	writeReadinessServerStateForTest(t, projectRoot, `{"phase":"stopped","reason":"domain-reload-after-no-server"}`)
	timeoutCtx, cancel := context.WithTimeout(context.Background(), time.Nanosecond)
	defer cancel()
	<-timeoutCtx.Done()

	_, err := waitForStoppedServerStateChange(timeoutCtx, context.Background(), projectRoot, initialState)

	if err == nil || err.Error() != "timed out waiting for Unity tool readiness" {
		t.Fatalf("timeout error mismatch: %v", err)
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

func createUnityProjectForReadinessTest(t *testing.T, projectRoot string) {
	t.Helper()
	for _, relativePath := range []string{"Assets", "ProjectSettings"} {
		if err := os.MkdirAll(filepath.Join(projectRoot, relativePath), 0o755); err != nil {
			t.Fatalf("failed to create Unity project directory: %v", err)
		}
	}
}
