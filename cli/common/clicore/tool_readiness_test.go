package clicore

import (
	"context"
	"encoding/json"
	"errors"
	"net"
	"os"
	"syscall"
	"testing"
	"time"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
	"github.com/hatayama/unity-cli-loop/common/unityprocess"
)

// Verifies shared readiness waits keep the shorter non-launch timeout.
func TestWaitForToolReadinessUsesDefaultTimeout(t *testing.T) {
	deps := toolReadinessDeps{
		probeToolReadinessSequence: func(ctx context.Context, projectRoot string) error {
			deadline, ok := ctx.Deadline()
			if !ok {
				t.Fatal("readiness probe context should have a deadline")
			}
			remaining := time.Until(deadline)
			if remaining < ToolReadinessTimeout-time.Second || remaining > ToolReadinessTimeout {
				t.Fatalf("readiness timeout mismatch: %s", remaining)
			}
			return nil
		},
	}

	if err := waitForToolReadinessWithDeps(context.Background(), t.TempDir(), ToolReadinessTimeout, deps); err != nil {
		t.Fatalf("waitForToolReadiness failed: %v", err)
	}
}

// Verifies protocol mismatch responses surface immediately instead of waiting for readiness timeout.
func TestWaitForToolReadinessReturnsCliUpdateRequiredImmediately(t *testing.T) {
	expectedErr := &unityipc.RPCError{
		Code:    -32603,
		Message: "The installed uloop CLI uses an IPC protocol that does not match this Unity package.",
		Data:    json.RawMessage(`{"type":"cli_update_required"}`),
	}
	deps := toolReadinessDeps{
		probeToolReadinessSequence: func(context.Context, string) error {
			return expectedErr
		},
	}

	err := waitForToolReadinessWithDeps(context.Background(), t.TempDir(), time.Hour, deps)

	if !errors.Is(err, expectedErr) {
		t.Fatalf("expected cli update error, got %v", err)
	}
}

// Verifies a connect() the operating system refused permanently ends the readiness wait at the
// first probe and reports that error, instead of polling out the whole timeout and replacing it
// with server-not-responding guidance the caller cannot act on.
func TestWaitForToolReadinessReturnsPermanentlyRefusedConnectImmediately(t *testing.T) {
	expectedErr := &unityipc.ConnectionAttemptError{
		Endpoint: "/tmp/uloop-501/UnityCliLoop-sample.sock",
		Cause: &net.OpError{
			Op:   "dial",
			Net:  "unix",
			Addr: &net.UnixAddr{Name: "/tmp/uloop-501/UnityCliLoop-sample.sock", Net: "unix"},
			Err:  os.NewSyscallError("connect", syscall.EPERM),
		},
	}
	probeCount := 0
	deps := toolReadinessDeps{
		probeToolReadinessSequence: func(context.Context, string) error {
			probeCount++
			return expectedErr
		},
	}

	err := waitForToolReadinessWithDeps(context.Background(), t.TempDir(), time.Hour, deps)

	if !errors.Is(err, expectedErr) {
		t.Fatalf("expected the refused connect error, got %v", err)
	}
	if probeCount != 1 {
		t.Fatalf("expected the wait to stop after the first probe, got %d probes", probeCount)
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
	deps := toolReadinessDeps{
		findRunningUnityProcess: func(context.Context, string) (*UnityProcess, error) {
			return nil, nil
		},
	}

	err := toolReadinessDoneErrorWithDeps(context.Background(), t.TempDir(), nil, deps)

	if err == nil || err.Error() != "timed out waiting for Unity tool readiness" {
		t.Fatalf("timeout error mismatch: %v", err)
	}
}

// Verifies that readiness timeout reports a live Unity process whose IPC server does not respond.
func TestToolReadinessDoneErrorReportsServerNotRespondingWhenUnityRuns(t *testing.T) {
	deps := toolReadinessDeps{
		findRunningUnityProcess: func(ctx context.Context, projectRoot string) (*UnityProcess, error) {
			deadline, hasDeadline := ctx.Deadline()
			if !hasDeadline {
				t.Fatal("process lookup context should have a deadline")
			}
			remaining := time.Until(deadline)
			if remaining < unityprocess.ProcessListCommandTimeout-time.Second || remaining > unityprocess.ProcessListCommandTimeout {
				t.Fatalf("process lookup timeout mismatch: %s", remaining)
			}
			return &UnityProcess{Pid: 123}, nil
		},
	}

	err := toolReadinessDoneErrorWithDeps(context.Background(), t.TempDir(), errors.New("probe failed"), deps)

	var notRespondingErr clierrors.UnityServerNotRespondingError
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
