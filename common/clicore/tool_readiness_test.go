package clicore

import (
	"context"
	"encoding/json"
	"errors"
	"testing"
	"time"

	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

// Verifies shared readiness waits keep the shorter non-launch timeout.
func TestWaitForToolReadinessUsesDefaultTimeout(t *testing.T) {
	originalProbe := probeToolReadinessSequenceForReadiness
	probeToolReadinessSequenceForReadiness = func(ctx context.Context, projectRoot string) error {
		deadline, ok := ctx.Deadline()
		if !ok {
			t.Fatal("readiness probe context should have a deadline")
		}
		remaining := time.Until(deadline)
		if remaining < ToolReadinessTimeout-time.Second || remaining > ToolReadinessTimeout {
			t.Fatalf("readiness timeout mismatch: %s", remaining)
		}
		return nil
	}
	t.Cleanup(func() {
		probeToolReadinessSequenceForReadiness = originalProbe
	})

	if err := WaitForToolReadiness(context.Background(), t.TempDir()); err != nil {
		t.Fatalf("waitForToolReadiness failed: %v", err)
	}
}

// Verifies protocol mismatch responses surface immediately instead of waiting for readiness timeout.
func TestWaitForToolReadinessReturnsCliUpdateRequiredImmediately(t *testing.T) {
	originalProbe := probeToolReadinessSequenceForReadiness
	expectedErr := &unityipc.RPCError{
		Code:    -32603,
		Message: "The installed uloop CLI uses an IPC protocol that does not match this Unity package.",
		Data:    json.RawMessage(`{"type":"cli_update_required"}`),
	}
	probeToolReadinessSequenceForReadiness = func(context.Context, string) error {
		return expectedErr
	}
	t.Cleanup(func() {
		probeToolReadinessSequenceForReadiness = originalProbe
	})

	err := WaitForToolReadinessWithTimeout(context.Background(), t.TempDir(), time.Hour)

	if !errors.Is(err, expectedErr) {
		t.Fatalf("expected cli update error, got %v", err)
	}
}

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
	findRunningUnityProcessForReadiness = func(context.Context, string) (*UnityProcess, error) {
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
	findRunningUnityProcessForReadiness = func(context.Context, string) (*UnityProcess, error) {
		return &UnityProcess{Pid: 123}, nil
	}
	t.Cleanup(func() {
		findRunningUnityProcessForReadiness = originalFinder
	})

	err := toolReadinessDoneError(context.Background(), t.TempDir(), errors.New("probe failed"))

	var notRespondingErr UnityServerNotRespondingError
	if !errors.As(err, &notRespondingErr) {
		t.Fatalf("expected Unity server not responding error, got %v", err)
	}
}

// Verifies that readiness probes exercise the same foreground warmup path as user executions.
func TestExecuteDynamicCodeReadinessProbeParamsUseForegroundWarmup(t *testing.T) {
	params := executeDynamicCodeReadinessProbeParams()

	if params["YieldToForegroundRequests"] != false {
		t.Fatalf("readiness probe should use foreground warmup: %#v", params["YieldToForegroundRequests"])
	}
	if params[DomainReloadWaitParam] != false {
		t.Fatalf("readiness probe should not wait for its own reload check: %#v", params[DomainReloadWaitParam])
	}
}
