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

// Verifies a probe that answered without a process ends the retry loop with UnityNotRunningError
// instead of the dial error, whose guidance tells the caller to wait for an Editor that is not there.
// Why a busy answer earlier in the window does not win: the server that answered it is gone now.
func TestFinishUndispatchedRetryProbeReportsUnityNotRunningWhenNoProcessIsFound(t *testing.T) {
	busyAttempt := sendAttempt{
		err: &unityipc.RPCError{
			Code:    -32603,
			Message: "Unity is busy running 'compile'.",
			Data:    []byte(`{"type":"server_busy"}`),
		},
	}
	connection := unityipc.Connection{
		ProjectRoot: t.TempDir(),
		Endpoint:    unityipc.Endpoint{Address: "/tmp/uloop/sample.sock"},
	}
	currentAttempt := refusedDialAttempt()

	finished, _, err := finishUndispatchedRetryProbe(
		expiredRetryContext(),
		connection,
		currentAttempt,
		nil,
		nil,
		busyAttempt,
	)

	if !finished {
		t.Fatal("expected the retry loop to finish when no Unity process is running")
	}
	var notRunning clierrors.UnityNotRunningError
	if !errors.As(err, &notRunning) {
		t.Fatalf("expected UnityNotRunningError, got %v", err)
	}
	if notRunning.ExitedDuringCommand {
		t.Fatal("an undispatched request never reached Unity, so it cannot have exited during it")
	}
	if notRunning.Cause != currentAttempt.err || notRunning.Endpoint != connection.Endpoint.Address {
		t.Fatalf("expected the dial error and endpoint to be kept, got %#v", notRunning)
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

	_, err := finishNonRetryableConnectionAttempt(context.Background(), unityipc.Connection{}, current, last, 0, nil)
	if !errors.Is(err, busy) {
		t.Fatalf("err = %v, want the earlier busy answer", err)
	}

	_, err = finishNonRetryableConnectionAttempt(cancelledContext(), unityipc.Connection{}, current, last, 0, nil)
	if !errors.Is(err, context.Canceled) {
		t.Fatalf("err = %v, want context.Canceled", err)
	}
}

// Verifies a dropped connection, a timeout, or any other non-RPC error from an attempt that reached
// Unity after a busy answer comes back as that attempt's own error and outcome, so the caller can
// recover from what really happened, and that a caller's cancellation still wins over it.
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
		{
			// Stands for an error that is neither a disconnect nor a timeout, such as a final response
			// that fails to decode: the rule does not depend on the kind of error.
			name:    "failed another way after the accept",
			current: sendAttempt{outcome: unityipc.UnitySendOutcome{RequestDispatched: true, RequestAccepted: true}, err: errors.New("final response could not be decoded")},
		},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			t.Run("reports the dispatched failure", func(t *testing.T) {
				// A positive response timeout keeps the accepted timeout out of the focus handling,
				// which needs a focus controller this test does not build.
				outcome, err := finishNonRetryableConnectionAttempt(context.Background(), unityipc.Connection{}, testCase.current, last, time.Second, nil)
				if !errors.Is(err, testCase.current.err) || isUnityServerBusyRPCError(err) {
					t.Fatalf("err = %v, want the dispatched attempt's own error %v", err, testCase.current.err)
				}
				if !reflect.DeepEqual(outcome, testCase.current.outcome) {
					t.Fatalf("outcome = %+v, want the dispatched attempt's outcome %+v", outcome, testCase.current.outcome)
				}
			})
			t.Run("reports the cancellation", func(t *testing.T) {
				_, err := finishNonRetryableConnectionAttempt(cancelledContext(), unityipc.Connection{}, testCase.current, last, time.Second, nil)
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

	outcome, err := finishNonRetryableConnectionAttempt(context.Background(), unityipc.Connection{}, current, last, 0, focusController)
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

// Verifies a request that reached Unity but timed out before Unity accepted it comes back as
// UnityServerNotRespondingError carrying the timeout and the connection it was sent on, whether or
// not a Unity process was found and whether or not the caller has cancelled since.
func TestFinishNonRetryableConnectionAttemptWrapsAPreAcceptTimeoutAsUnityNotResponding(t *testing.T) {
	cases := []struct {
		name string
		ctx  context.Context
	}{
		{name: "live context", ctx: context.Background()},
		{name: "cancelled context", ctx: cancelledContext()},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			connection := unityipc.Connection{
				Endpoint:    unityipc.Endpoint{Network: "unix", Address: "/tmp/uloop/sample.sock"},
				ProjectRoot: t.TempDir(),
			}
			current := sendAttempt{outcome: unityipc.UnitySendOutcome{RequestDispatched: true}, err: os.ErrDeadlineExceeded}

			outcome, err := finishNonRetryableConnectionAttempt(testCase.ctx, connection, current, sendAttempt{}, 0, nilProbeFocusController(t))

			var notResponding clierrors.UnityServerNotRespondingError
			if !errors.As(err, &notResponding) {
				t.Fatalf("err = %v, want UnityServerNotRespondingError", err)
			}
			if notResponding.ProjectRoot != connection.ProjectRoot || notResponding.Endpoint != connection.Endpoint.Address {
				t.Errorf("error = %+v, want project root %q and endpoint %q", notResponding, connection.ProjectRoot, connection.Endpoint.Address)
			}
			if !errors.Is(err, os.ErrDeadlineExceeded) {
				t.Errorf("err = %v, want it to carry the pre-accept timeout", err)
			}
			if !clierrors.IsFinalResponseTimeoutError(err) {
				t.Errorf("err = %v, want it to still read as a timeout", err)
			}
			if !reflect.DeepEqual(outcome, current.outcome) {
				t.Errorf("outcome = %+v, want the attempt's outcome %+v", outcome, current.outcome)
			}
		})
	}
}

// Verifies every attempt other than a pre-accept timeout comes back unwrapped, as its own error (or
// the cancellation, when a busy answer came before and the caller cancelled).
func TestFinishNonRetryableConnectionAttemptLeavesOtherFailuresUnwrapped(t *testing.T) {
	permanentConnectFailure := &unityipc.ConnectionAttemptError{Cause: os.ErrPermission}
	cases := []struct {
		name            string
		ctx             context.Context
		current         sendAttempt
		last            sendAttempt
		responseTimeout time.Duration
		// needsFocus marks an attempt the focus handling sees, which needs a real focus controller.
		needsFocus bool
		wantErr    error
	}{
		{
			name:    "accepted then timed out",
			current: sendAttempt{outcome: unityipc.UnitySendOutcome{RequestDispatched: true, RequestAccepted: true}, err: os.ErrDeadlineExceeded},
		},
		{
			name:    "dropped before the accept",
			current: sendAttempt{outcome: unityipc.UnitySendOutcome{RequestDispatched: true}, err: io.EOF},
		},
		{
			name:    "dropped after the accept",
			current: sendAttempt{outcome: unityipc.UnitySendOutcome{RequestDispatched: true, RequestAccepted: true}, err: io.ErrUnexpectedEOF},
		},
		{
			name:       "editor unresponsive after the accept",
			current:    sendAttempt{outcome: unityipc.UnitySendOutcome{RequestDispatched: true, RequestAccepted: true}, err: &unityipc.EditorUnresponsiveError{StallSeconds: 30}},
			needsFocus: true,
		},
		{
			// A write that timed out; a dial that timed out is a ConnectionAttemptError and is retried
			// before it gets here.
			name:    "undispatched timeout",
			current: sendAttempt{outcome: unityipc.UnitySendOutcome{}, err: os.ErrDeadlineExceeded},
		},
		{
			name:            "accepted then timed out, no response timeout",
			current:         sendAttempt{outcome: unityipc.UnitySendOutcome{RequestDispatched: true, RequestAccepted: true}, err: os.ErrDeadlineExceeded},
			responseTimeout: 0,
			needsFocus:      true,
		},
		{
			name:    "cancelled after busy",
			ctx:     cancelledContext(),
			current: sendAttempt{outcome: unityipc.UnitySendOutcome{RequestDispatched: true}, err: os.ErrDeadlineExceeded},
			last:    busyAttemptAfterAccept(serverBusyRPCError(t)),
			wantErr: context.Canceled,
		},
		{
			name:    "succeeded",
			current: sendAttempt{outcome: unityipc.UnitySendOutcome{RequestDispatched: true, RequestAccepted: true}},
		},
		{
			name:    "malformed frame before the accept",
			current: sendAttempt{outcome: unityipc.UnitySendOutcome{RequestDispatched: true}, err: errors.New("invalid frame")},
		},
		{
			name:    "undispatched disconnect",
			current: sendAttempt{outcome: unityipc.UnitySendOutcome{}, err: io.EOF},
		},
		{
			name:    "cancelled after the accept",
			current: sendAttempt{outcome: unityipc.UnitySendOutcome{RequestDispatched: true, RequestAccepted: true}, err: context.Canceled},
		},
		{
			name:    "permanent connect failure",
			current: sendAttempt{outcome: unityipc.UnitySendOutcome{}, err: permanentConnectFailure},
		},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			ctx := testCase.ctx
			if ctx == nil {
				ctx = context.Background()
			}
			responseTimeout := testCase.responseTimeout
			if !testCase.needsFocus {
				// A positive response timeout keeps the accepted timeout out of the focus handling.
				responseTimeout = time.Second
			}
			var focusController *connectionRetryFocusController
			if testCase.needsFocus {
				focusController = nilProbeFocusController(t)
			}
			wantErr := testCase.wantErr
			if wantErr == nil {
				wantErr = testCase.current.err
			}

			_, err := finishNonRetryableConnectionAttempt(ctx, unityipc.Connection{ProjectRoot: t.TempDir()}, testCase.current, testCase.last, responseTimeout, focusController)

			var notResponding clierrors.UnityServerNotRespondingError
			if errors.As(err, &notResponding) {
				t.Fatalf("err = %v, want it left unwrapped", err)
			}
			if wantErr == nil {
				if err != nil {
					t.Fatalf("err = %v, want nil", err)
				}
				return
			}
			if !errors.Is(err, wantErr) {
				t.Fatalf("err = %v, want %v", err, wantErr)
			}
		})
	}
}

// nilProbeFocusController builds a focus controller whose Unity process lookup finds nothing, so an
// attempt that enters the focus handling never focuses a real window.
func nilProbeFocusController(t *testing.T) *connectionRetryFocusController {
	t.Helper()
	deps := defaultConnectionRetryDeps()
	deps.findRunningUnityProcess = func(context.Context, string) (*clicore.UnityProcess, error) {
		return nil, nil
	}
	return newConnectionRetryFocusController(unityipc.Connection{ProjectRoot: t.TempDir()}, "get-logs", deps)
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

// Verifies a connection that dropped after the request reached Unity becomes UnityNotRunningError only
// when a lookup that answered finds no Editor, and stays the disconnect in every other case, so a domain
// reload still reaches the callers that wait it out.
func TestReportUnityExitDuringRequest(t *testing.T) {
	dispatched := unityipc.UnitySendOutcome{RequestDispatched: true}
	accepted := unityipc.UnitySendOutcome{RequestDispatched: true, RequestAccepted: true}
	failedLookup := func(context.Context, string) (*unityprocess.UnityProcess, error) {
		return nil, errors.New("sysctl kern.proc.all: operation not permitted")
	}
	cases := []struct {
		name        string
		ctx         context.Context
		outcome     unityipc.UnitySendOutcome
		err         error
		lookup      func(context.Context, string) (*unityprocess.UnityProcess, error)
		wantExitErr bool
	}{
		{name: "dropped before the accept, no Editor", outcome: dispatched, err: io.EOF, lookup: noUnityProcessLookup, wantExitErr: true},
		{name: "dropped after the accept, no Editor", outcome: accepted, err: io.ErrUnexpectedEOF, lookup: noUnityProcessLookup, wantExitErr: true},
		{name: "dropped, Editor running", outcome: accepted, err: io.EOF, lookup: runningUnityProcessLookup},
		{name: "dropped, lookup failed", outcome: accepted, err: io.EOF, lookup: failedLookup},
		{name: "dropped before dispatch", outcome: unityipc.UnitySendOutcome{}, err: io.EOF, lookup: noUnityProcessLookup},
		{name: "caller cancelled", ctx: cancelledContext(), outcome: accepted, err: io.EOF, lookup: noUnityProcessLookup},
		{name: "not a disconnect", outcome: accepted, err: os.ErrDeadlineExceeded, lookup: noUnityProcessLookup},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			ctx := testCase.ctx
			if ctx == nil {
				ctx = context.Background()
			}
			deps := defaultConnectionRetryDeps()
			deps.findRunningUnityProcess = testCase.lookup
			connection := unityipc.Connection{
				ProjectRoot: t.TempDir(),
				Endpoint:    unityipc.Endpoint{Address: "/tmp/uloop/sample.sock"},
			}

			err := reportUnityExitDuringRequest(ctx, connection, testCase.outcome, testCase.err, deps)

			var notRunning clierrors.UnityNotRunningError
			if !testCase.wantExitErr {
				if err != testCase.err {
					t.Fatalf("err = %v, want the original error %v", err, testCase.err)
				}
				return
			}
			if !errors.As(err, &notRunning) {
				t.Fatalf("err = %v, want UnityNotRunningError", err)
			}
			if !notRunning.ExitedDuringCommand || notRunning.Cause != testCase.err {
				t.Fatalf("expected an exit during the command caused by the disconnect, got %#v", notRunning)
			}
		})
	}
}

// Verifies a process lookup that failed after a dispatched disconnect leaves a trace in the CLI vibe log,
// as the same failure before dispatch does, since the command output keeps only the disconnect.
func TestReportUnityExitDuringRequestLogsAFailedLookup(t *testing.T) {
	enableCliVibeLog(t)
	deps := defaultConnectionRetryDeps()
	deps.findRunningUnityProcess = func(context.Context, string) (*unityprocess.UnityProcess, error) {
		return nil, errors.New("sysctl kern.proc.all: operation not permitted")
	}
	connection := unityipc.Connection{
		ProjectRoot: t.TempDir(),
		Endpoint:    unityipc.Endpoint{Address: "/tmp/uloop/sample.sock"},
	}

	_ = reportUnityExitDuringRequest(
		context.Background(),
		connection,
		unityipc.UnitySendOutcome{RequestDispatched: true, RequestAccepted: true},
		io.EOF,
		deps,
	)

	logContent := readOnlyCliVibeLog(t, connection.ProjectRoot)
	if entries := cliVibeEntriesForOperation(t, logContent, "cli_unity_process_probe_failed"); len(entries) != 1 {
		t.Fatalf("probe failure entries = %d, want 1\n%s", len(entries), logContent)
	}
}
