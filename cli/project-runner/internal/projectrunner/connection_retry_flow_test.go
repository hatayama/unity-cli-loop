package projectrunner

import (
	"context"
	"errors"
	"io"
	"os"
	"path/filepath"
	"reflect"
	"strings"
	"testing"
	"time"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
	"github.com/hatayama/unity-cli-loop/common/unityprocess"
	"github.com/hatayama/unity-cli-loop/common/vibelog"
)

// Verifies only execute-dynamic-code gets main-thread stall tolerance: other commands'
// stalls must keep failing as a genuine freeze signal.
func TestCommandNeedsSelfInducedStallToleranceOnlyForExecuteDynamicCode(t *testing.T) {
	if !commandNeedsSelfInducedStallTolerance(clicore.ExecuteDynamicCodeCommandName) {
		t.Fatal("expected execute-dynamic-code to need self-induced stall tolerance")
	}
	if commandNeedsSelfInducedStallTolerance("compile") {
		t.Fatal("expected compile to not need self-induced stall tolerance")
	}
	if commandNeedsSelfInducedStallTolerance("run-tests") {
		t.Fatal("expected run-tests to not need self-induced stall tolerance")
	}
}

func refusedDialAttempt() sendAttempt {
	return sendAttempt{
		outcome: unityipc.UnitySendOutcome{},
		err: &unityipc.ConnectionAttemptError{
			ProjectRoot: "/projects/sample",
			Endpoint:    "/tmp/uloop/sample.sock",
			Cause:       errors.New("dial unix /tmp/uloop/sample.sock: connect: connection refused"),
		},
	}
}

func expiredRetryContext() context.Context {
	expired, cancel := context.WithCancel(context.Background())
	cancel()
	return expired
}

// Verifies a failed process probe never upgrades the diagnosis to "Unity is running": the probe
// observed nothing, so the dial error must be reported exactly as it is on the no-process path.
func TestFinishUndispatchedRetryProbeDoesNotClaimUnityIsRunningWhenTheProbeFailed(t *testing.T) {
	currentAttempt := refusedDialAttempt()

	finished, _, err := finishUndispatchedRetryProbe(
		expiredRetryContext(),
		unityipc.Connection{ProjectRoot: t.TempDir()},
		currentAttempt,
		errors.New("sysctl kern.proc.all: operation not permitted"),
		nil,
		sendAttempt{},
	)

	if !finished {
		t.Fatal("expected the retry loop to finish after a failed probe with an expired window")
	}
	var notResponding clierrors.UnityServerNotRespondingError
	if errors.As(err, &notResponding) {
		t.Fatalf("a failed probe must not report Unity as running: %v", err)
	}
	if err != currentAttempt.err {
		t.Fatalf("expected the dial error verbatim, got: %v", err)
	}
}

// Verifies the same fallback applies while the retry window is still alive: the probe failure
// alone would hide the dial error, which is the fact the caller acts on.
func TestFinishUndispatchedRetryProbeReportsTheDialErrorWhileTheWindowIsAlive(t *testing.T) {
	currentAttempt := refusedDialAttempt()
	probeErr := errors.New("listing Unity processes timed out")

	finished, _, err := finishUndispatchedRetryProbe(
		context.Background(),
		unityipc.Connection{ProjectRoot: t.TempDir()},
		currentAttempt,
		probeErr,
		nil,
		sendAttempt{},
	)

	if !finished {
		t.Fatal("expected the retry loop to finish after a failed probe")
	}
	if err != currentAttempt.err {
		t.Fatalf("expected the dial error verbatim, got: %v", err)
	}
}

// Verifies a busy response seen earlier in the window still wins over the final dial error when
// the probe failed, because a server that answered moments ago is the truer diagnosis.
func TestFinishUndispatchedRetryProbeKeepsABusyResponseWhenTheProbeFailed(t *testing.T) {
	busyAttempt := sendAttempt{
		err: &unityipc.RPCError{
			Code:    -32603,
			Message: "Unity is busy running 'compile'.",
			Data:    []byte(`{"type":"server_busy"}`),
		},
	}

	finished, _, err := finishUndispatchedRetryProbe(
		expiredRetryContext(),
		unityipc.Connection{ProjectRoot: t.TempDir()},
		refusedDialAttempt(),
		errors.New("sysctl kern.proc.all: operation not permitted"),
		nil,
		busyAttempt,
	)

	if !finished {
		t.Fatal("expected the retry loop to finish after a failed probe with an expired window")
	}
	if err != busyAttempt.err {
		t.Fatalf("expected the busy response to be preserved, got: %v", err)
	}
}

// Verifies a probe that found a running process still lets the retry loop continue.
func TestFinishUndispatchedRetryProbeContinuesWhenUnityIsRunning(t *testing.T) {
	finished, _, err := finishUndispatchedRetryProbe(
		context.Background(),
		unityipc.Connection{ProjectRoot: t.TempDir()},
		refusedDialAttempt(),
		nil,
		&unityprocess.UnityProcess{Pid: 4321},
		sendAttempt{},
	)

	if finished {
		t.Fatalf("expected the retry loop to continue while Unity is running, got: %v", err)
	}
	if err != nil {
		t.Fatalf("expected no error while continuing, got: %v", err)
	}
}

// Verifies the swallowed probe failure is still recorded: dropping the diagnosis from the error
// must not drop it from the diagnostics too.
func TestFinishUndispatchedRetryProbeRecordsTheProbeFailureInTheVibeLog(t *testing.T) {
	projectRoot := t.TempDir()
	t.Setenv(vibelog.CLIVibeLogEnvName, "1")

	_, _, _ = finishUndispatchedRetryProbe(
		expiredRetryContext(),
		unityipc.Connection{ProjectRoot: projectRoot},
		refusedDialAttempt(),
		errors.New("sysctl kern.proc.all: operation not permitted"),
		nil,
		sendAttempt{},
	)

	entries, globErr := filepath.Glob(filepath.Join(projectRoot, vibelog.CLIVibeLogDirectory, "*.json"))
	if globErr != nil {
		t.Fatalf("reading the vibe log directory failed: %v", globErr)
	}
	if len(entries) == 0 {
		t.Fatal("expected the failed process probe to be written to the CLI vibe log")
	}
	contents, readErr := os.ReadFile(entries[0])
	if readErr != nil {
		t.Fatalf("reading the vibe log failed: %v", readErr)
	}
	if !strings.Contains(string(contents), "operation not permitted") {
		t.Fatalf("the probe failure was not recorded: %s", contents)
	}
}

func cancelledContext() context.Context {
	ctx, cancel := context.WithCancel(context.Background())
	cancel()
	return ctx
}

// Verifies the busy retry stops with the caller's context error when the caller cancelled, and
// with the last busy error when only the retry window ran out.
func TestFinishBusyRetryStopsWithTheRightError(t *testing.T) {
	busy := errors.New("busy")
	ticker := time.NewTicker(time.Hour)
	defer ticker.Stop()
	deps := connectionRetryDeps{retryTimeout: time.Hour}

	cases := []struct {
		name         string
		ctx          context.Context
		retryContext context.Context
		startedAt    time.Time
		want         error
	}{
		{name: "window elapsed after caller cancelled", ctx: cancelledContext(), retryContext: context.Background(), startedAt: time.Now().Add(-2 * time.Hour), want: context.Canceled},
		{name: "retry context ended after caller cancelled", ctx: cancelledContext(), retryContext: cancelledContext(), startedAt: time.Now(), want: context.Canceled},
		{name: "retry context ended on its own", ctx: context.Background(), retryContext: cancelledContext(), startedAt: time.Now(), want: busy},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			finished, _, err := finishBusyRetry(testCase.ctx, testCase.retryContext, testCase.startedAt, ticker, unityipc.UnitySendOutcome{}, busy, deps)
			if !finished || !errors.Is(err, testCase.want) {
				t.Fatalf("finished=%v err=%v, want finished with %v", finished, err, testCase.want)
			}
		})
	}
}

// Verifies a transport error from an attempt that never reached Unity reports the earlier busy
// answer, unless the caller cancelled, in which case the cancellation wins.
func TestFinishNonRetryableConnectionAttemptPrefersBusyOverAnUndispatchedTransportError(t *testing.T) {
	busy := serverBusyRPCError(t)
	current := sendAttempt{err: io.ErrUnexpectedEOF}
	last := sendAttempt{err: busy}

	_, err := finishNonRetryableConnectionAttempt(context.Background(), current, last, 0, nil)
	if !errors.Is(err, busy) {
		t.Fatalf("err = %v, want the earlier busy answer", err)
	}

	_, err = finishNonRetryableConnectionAttempt(cancelledContext(), current, last, 0, nil)
	if !errors.Is(err, context.Canceled) {
		t.Fatalf("err = %v, want context.Canceled", err)
	}
}

// Verifies a dropped connection or a timeout from an attempt that reached Unity after a busy answer
// comes back as that attempt's own error and outcome, so the caller can recover from what really
// happened, and that a caller's cancellation still wins over it.
func TestFinishNonRetryableConnectionAttemptKeepsADispatchedFailureAfterBusy(t *testing.T) {
	busy := serverBusyRPCError(t)
	last := busyAttemptAfterAccept(busy)
	cases := []struct {
		name    string
		current sendAttempt
	}{
		{
			name:    "dropped after the accept",
			current: sendAttempt{outcome: unityipc.UnitySendOutcome{RequestDispatched: true, RequestAccepted: true}, err: io.ErrUnexpectedEOF},
		},
		{
			name:    "dropped before the accept",
			current: sendAttempt{outcome: unityipc.UnitySendOutcome{RequestDispatched: true}, err: io.EOF},
		},
		{
			name:    "final response timed out after the accept",
			current: sendAttempt{outcome: unityipc.UnitySendOutcome{RequestDispatched: true, RequestAccepted: true}, err: os.ErrDeadlineExceeded},
		},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			t.Run("reports the dispatched failure", func(t *testing.T) {
				// A positive response timeout keeps the accepted timeout out of the focus handling,
				// which needs a focus controller this test does not build.
				outcome, err := finishNonRetryableConnectionAttempt(context.Background(), testCase.current, last, time.Second, nil)
				if !errors.Is(err, testCase.current.err) || isUnityServerBusyRPCError(err) {
					t.Fatalf("err = %v, want the dispatched attempt's own error %v", err, testCase.current.err)
				}
				if !reflect.DeepEqual(outcome, testCase.current.outcome) {
					t.Fatalf("outcome = %+v, want the dispatched attempt's outcome %+v", outcome, testCase.current.outcome)
				}
			})
			t.Run("reports the cancellation", func(t *testing.T) {
				_, err := finishNonRetryableConnectionAttempt(cancelledContext(), testCase.current, last, time.Second, nil)
				if !errors.Is(err, context.Canceled) {
					t.Fatalf("err = %v, want context.Canceled", err)
				}
			})
		})
	}
}

// busyAttemptAfterAccept returns a busy answer to an accepted request. Its distinct timing tells its
// outcome apart from a current attempt whose flags are the same, so a test can see which outcome
// came back.
func busyAttemptAfterAccept(busy error) sendAttempt {
	return sendAttempt{
		outcome: unityipc.UnitySendOutcome{
			RequestDispatched: true,
			RequestAccepted:   true,
			Timing:            unityipc.UnitySendTiming{Total: time.Millisecond},
		},
		err: busy,
	}
}

// Verifies an editor-unresponsive error from an attempt that Unity accepted after a busy answer comes
// back as that attempt's own error and outcome, and goes through the main-thread-stall focus handling
// a first attempt would get, instead of being reported as the busy answer.
func TestFinishNonRetryableConnectionAttemptKeepsAnEditorUnresponsiveErrorAfterBusy(t *testing.T) {
	busy := serverBusyRPCError(t)
	last := busyAttemptAfterAccept(busy)
	current := sendAttempt{
		outcome: unityipc.UnitySendOutcome{RequestDispatched: true, RequestAccepted: true},
		err:     &unityipc.EditorUnresponsiveError{StallSeconds: 30},
	}
	processLookups := 0
	deps := defaultConnectionRetryDeps()
	deps.findRunningUnityProcess = func(context.Context, string) (*clicore.UnityProcess, error) {
		processLookups++
		return nil, nil
	}
	// This error enters the focus handling whatever the response timeout is, so it needs a real focus
	// controller. Finding no Unity process keeps the focus itself from running.
	focusController := newConnectionRetryFocusController(unityipc.Connection{ProjectRoot: t.TempDir()}, "get-logs", deps)

	outcome, err := finishNonRetryableConnectionAttempt(context.Background(), current, last, 0, focusController)
	if !errors.Is(err, current.err) || isUnityServerBusyRPCError(err) {
		t.Errorf("err = %v, want the accepted attempt's own error %v", err, current.err)
	}
	if !reflect.DeepEqual(outcome, current.outcome) {
		t.Errorf("outcome = %+v, want the accepted attempt's outcome %+v", outcome, current.outcome)
	}
	if processLookups != 1 {
		t.Errorf("Unity process lookups = %d, want 1 from the main-thread-stall focus handling", processLookups)
	}
}

// Verifies the unity-alive retry reports the caller's cancellation when its retry context ends
// because the caller cancelled, and Unity-not-responding otherwise.
func TestFinishUnityAliveRetryWaitWhenRetryContextEnds(t *testing.T) {
	ticker := time.NewTicker(time.Hour)
	defer ticker.Stop()
	deps := connectionRetryDeps{retryTimeout: time.Hour}
	connection := unityipc.Connection{ProjectRoot: t.TempDir()}
	dialErr := errors.New("dial refused")

	finished, _, err := finishUnityAliveRetryWait(cancelledContext(), cancelledContext(), time.Now(), ticker, connection, unityipc.UnitySendOutcome{}, dialErr, deps)
	if !finished || !errors.Is(err, context.Canceled) {
		t.Fatalf("finished=%v err=%v, want context.Canceled", finished, err)
	}

	finished, _, err = finishUnityAliveRetryWait(context.Background(), cancelledContext(), time.Now(), ticker, connection, unityipc.UnitySendOutcome{}, dialErr, deps)
	var notResponding clierrors.UnityServerNotRespondingError
	if !finished || !errors.As(err, &notResponding) {
		t.Fatalf("finished=%v err=%v, want UnityServerNotRespondingError", finished, err)
	}
}
