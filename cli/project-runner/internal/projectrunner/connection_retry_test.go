package projectrunner

import (
	"bufio"
	"context"
	"errors"
	"fmt"
	"net"
	"os"
	"path/filepath"
	"runtime"
	"strings"
	"syscall"
	"testing"
	"time"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
	"github.com/hatayama/unity-cli-loop/common/vibelog"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
	"github.com/hatayama/unity-cli-loop/common/unityprocess"
)

// Verifies the default busy-stall focus threshold fires before the bounded busy retry window ends.
func TestDefaultBusyFocusStallThresholdFitsWithinBusyRetryWindow(t *testing.T) {
	deps := defaultConnectionRetryDeps()
	threshold := busyFocusStallThresholdFor(deps)
	if threshold >= deps.retryTimeout {
		t.Fatalf(
			"busy focus stall threshold must stay below the busy retry window: threshold=%s window=%s",
			threshold,
			deps.retryTimeout,
		)
	}
}

// Verifies connection-retry focus rescue bounds the focus external command with a deadline.
func TestConnectionRetryFocusControllerBoundsFocusContext(t *testing.T) {
	var receivedContext context.Context
	deps := defaultConnectionRetryDeps()
	deps.focusUnityProcess = func(ctx context.Context, pid int) (unityprocess.RestoreFocusFunc, error) {
		receivedContext = ctx
		return nil, nil
	}
	controller := newConnectionRetryFocusController(
		unityipc.Connection{ProjectRoot: t.TempDir()},
		"get-logs",
		deps,
	)
	controller.tryFocusProcess(context.Background(), 123, focusReasonBusyStall, errors.New("busy"))

	if receivedContext == nil {
		t.Fatal("expected focus attempt context")
	}
	deadline, hasDeadline := receivedContext.Deadline()
	if !hasDeadline {
		t.Fatal("focus attempt context should have a deadline")
	}
	remaining := time.Until(deadline)
	if remaining < unityprocess.FocusCommandTimeout-time.Second || remaining > unityprocess.FocusCommandTimeout {
		t.Fatalf("focus timeout mismatch: %s", remaining)
	}
}

// Verifies transient IPC connection failures focus Unity once and restore focus before reporting server-not-responding.
func TestSendWithTransientConnectionRetryReportsUnityServerNotResponding(t *testing.T) {
	deps := defaultConnectionRetryDeps()
	focusCallCount := 0
	restoreCallCount := 0
	deps.findRunningUnityProcess = func(context.Context, string) (*clicore.UnityProcess, error) {
		return &clicore.UnityProcess{Pid: 123}, nil
	}
	deps.focusUnityProcess = func(context.Context, int) (clicore.RestoreFocusFunc, error) {
		focusCallCount++
		return func(context.Context) error {
			restoreCallCount++
			return nil
		}, nil
	}
	deps.retryTimeout = time.Nanosecond
	deps.retryPoll = time.Nanosecond

	connection := unityipc.Connection{
		Endpoint: unityipc.Endpoint{
			Network: "unix",
			Address: t.TempDir() + "/missing.sock",
		},
		ProjectRoot: t.TempDir(),
	}

	_, err := sendWithTransientConnectionRetryWithDeps(
		context.Background(),
		connection,
		"get-logs",
		map[string]any{},
		nil,
		0,
		deps)

	var notRespondingErr clierrors.UnityServerNotRespondingError
	if !errors.As(err, &notRespondingErr) {
		t.Fatalf("expected unityServerNotRespondingError, got %v", err)
	}
	if focusCallCount != 1 {
		t.Fatalf("expected one focus attempt, got %d", focusCallCount)
	}
	if restoreCallCount != 1 {
		t.Fatalf("expected one focus restore, got %d", restoreCallCount)
	}
}

// Verifies successful retry focus attempts are persisted to CLI Vibe logs.
func TestSendWithTransientConnectionRetryWritesFocusSuccessVibeLog(t *testing.T) {
	enableCliVibeLog(t)

	deps := defaultConnectionRetryDeps()
	deps.findRunningUnityProcess = func(context.Context, string) (*clicore.UnityProcess, error) {
		return &clicore.UnityProcess{Pid: 456}, nil
	}
	deps.focusUnityProcess = func(context.Context, int) (clicore.RestoreFocusFunc, error) {
		return nil, nil
	}
	deps.retryTimeout = time.Nanosecond
	deps.retryPoll = time.Nanosecond

	projectRoot := t.TempDir()
	connection := unityipc.Connection{
		Endpoint: unityipc.Endpoint{
			Network: "unix",
			Address: filepath.Join(t.TempDir(), "missing.sock"),
		},
		ProjectRoot: projectRoot,
	}

	_, _ = sendWithTransientConnectionRetryWithDeps(
		context.Background(),
		connection,
		"get-logs",
		map[string]any{},
		nil,
		0,
		deps)

	logContent := readOnlyCliVibeLog(t, projectRoot)
	for _, expected := range []string{
		`"operation":"cli_connection_retry_focus_attempt"`,
		`"operation":"cli_connection_retry_focus_success"`,
		`"command":"get-logs"`,
		`"pid":456`,
		`"reason":"undispatched_connection_failure"`,
	} {
		if !strings.Contains(logContent, expected) {
			t.Fatalf("CLI Vibe log missing %q:\n%s", expected, logContent)
		}
	}
}

// Verifies failed retry focus attempts are persisted to CLI Vibe logs.
func TestSendWithTransientConnectionRetryWritesFocusFailureVibeLog(t *testing.T) {
	enableCliVibeLog(t)

	deps := defaultConnectionRetryDeps()
	deps.findRunningUnityProcess = func(context.Context, string) (*clicore.UnityProcess, error) {
		return &clicore.UnityProcess{Pid: 789}, nil
	}
	deps.focusUnityProcess = func(context.Context, int) (clicore.RestoreFocusFunc, error) {
		return nil, fmt.Errorf("focus denied")
	}
	deps.retryTimeout = time.Nanosecond
	deps.retryPoll = time.Nanosecond

	projectRoot := t.TempDir()
	connection := unityipc.Connection{
		Endpoint: unityipc.Endpoint{
			Network: "unix",
			Address: filepath.Join(t.TempDir(), "missing.sock"),
		},
		ProjectRoot: projectRoot,
	}

	_, _ = sendWithTransientConnectionRetryWithDeps(
		context.Background(),
		connection,
		"compile",
		map[string]any{},
		nil,
		0,
		deps)

	logContent := readOnlyCliVibeLog(t, projectRoot)
	for _, expected := range []string{
		`"operation":"cli_connection_retry_focus_attempt"`,
		`"operation":"cli_connection_retry_focus_failed"`,
		`"command":"compile"`,
		`"pid":789`,
		`"reason":"undispatched_connection_failure"`,
		`"focusError":"focus denied"`,
	} {
		if !strings.Contains(logContent, expected) {
			t.Fatalf("CLI Vibe log missing %q:\n%s", expected, logContent)
		}
	}
}

// Verifies a process probe that timed out reports the dial error instead of the
// server-not-responding error: a probe that never read the process table cannot be the evidence
// for claiming Unity is running.
func TestSendWithTransientConnectionRetryReportsTheDialErrorWhenTheProcessProbeTimesOut(t *testing.T) {
	deps := defaultConnectionRetryDeps()
	deps.findRunningUnityProcess = func(ctx context.Context, projectRoot string) (*clicore.UnityProcess, error) {
		<-ctx.Done()
		return nil, ctx.Err()
	}
	deps.retryTimeout = time.Nanosecond
	deps.retryPoll = time.Nanosecond

	connection := unityipc.Connection{
		Endpoint: unityipc.Endpoint{
			Network: "unix",
			Address: endpointDirectoryWithRequiredMode(t) + "/missing.sock",
		},
		ProjectRoot: t.TempDir(),
	}

	_, err := sendWithTransientConnectionRetryWithDeps(
		context.Background(),
		connection,
		"get-logs",
		map[string]any{},
		nil,
		0,
		deps)

	var notRespondingErr clierrors.UnityServerNotRespondingError
	if errors.As(err, &notRespondingErr) {
		t.Fatalf("a failed process probe must not report Unity as running: %v", err)
	}
	var connectionErr *unityipc.ConnectionAttemptError
	if !errors.As(err, &connectionErr) {
		t.Fatalf("expected the connection attempt error, got %v", err)
	}
}

// Endpoint validation rejects any directory that is not 0700, which a plain t.TempDir() is not.
// Tests that need the dial itself to fail must get past that check first.
func endpointDirectoryWithRequiredMode(t *testing.T) string {
	t.Helper()
	directory := t.TempDir()
	if err := os.Chmod(directory, 0o700); err != nil {
		t.Fatalf("failed to set the endpoint directory mode: %v", err)
	}
	return directory
}

// Verifies a cancelled command reports the cancellation rather than an unreachable Unity: the
// process probe inherits the cancellation and fails, and that failure must not be turned into
// "Unity may be closed, run uloop launch" guidance for a user who pressed Ctrl-C.
func TestSendWithTransientConnectionRetryPreservesParentCancellation(t *testing.T) {
	projectRoot := t.TempDir()
	t.Setenv(vibelog.CLIVibeLogEnvName, "1")

	deps := defaultConnectionRetryDeps()
	deps.findRunningUnityProcess = func(ctx context.Context, projectRoot string) (*clicore.UnityProcess, error) {
		return nil, ctx.Err()
	}
	deps.retryPoll = time.Nanosecond

	cancelledContext, cancel := context.WithCancel(context.Background())
	cancel()

	_, err := sendWithTransientConnectionRetryWithDeps(
		cancelledContext,
		unityipc.Connection{
			Endpoint: unityipc.Endpoint{
				Network: "unix",
				Address: endpointDirectoryWithRequiredMode(t) + "/missing.sock",
			},
			ProjectRoot: projectRoot,
		},
		"get-logs",
		map[string]any{},
		nil,
		0,
		deps)

	if !errors.Is(err, context.Canceled) {
		t.Fatalf("expected the cancellation to be preserved, got %v", err)
	}
	logFiles, globErr := filepath.Glob(filepath.Join(projectRoot, vibelog.CLIVibeLogDirectory, "*.json"))
	if globErr != nil {
		t.Fatalf("reading the vibe log directory failed: %v", globErr)
	}
	for _, logFile := range logFiles {
		contents, readErr := os.ReadFile(logFile)
		if readErr != nil {
			t.Fatalf("reading the vibe log failed: %v", readErr)
		}
		if strings.Contains(string(contents), "cli_unity_process_probe_failed") {
			t.Fatalf("a cancelled command must not record a process probe failure: %s", contents)
		}
	}
}

func readOnlyCliVibeLog(t *testing.T, projectRoot string) string {
	t.Helper()
	logFiles, err := filepath.Glob(filepath.Join(projectRoot, vibelog.CLIVibeLogDirectory, vibelog.CLIVibeLogPrefix+"_*.json"))
	if err != nil {
		t.Fatalf("failed to glob CLI Vibe logs: %v", err)
	}
	if len(logFiles) != 1 {
		t.Fatalf("expected one CLI Vibe log, got %d: %#v", len(logFiles), logFiles)
	}
	content, err := os.ReadFile(logFiles[0])
	if err != nil {
		t.Fatalf("failed to read CLI Vibe log: %v", err)
	}
	return string(content)
}

func enableCliVibeLog(t *testing.T) {
	t.Helper()
	t.Setenv(vibelog.CLIVibeLogEnvName, "1")
}

// timeoutOnlyError is duplicated from internal/clicore's test helper of the
// same name: test helpers cannot be shared across packages, and both
// packages exercise timeout-only error classification.
type timeoutOnlyError struct{}

func (timeoutOnlyError) Error() string {
	return "i/o timeout"
}

func (timeoutOnlyError) Timeout() bool {
	return true
}

// Verifies dispatched pre-accept timeouts are treated as focus-worthy slow Unity responses.
func TestConnectionRetryFocusReasonClassifiesPreAcceptTimeout(t *testing.T) {
	reason, ok := connectionRetryFocusReasonForError(
		timeoutOnlyError{},
		unityipc.UnitySendOutcome{RequestDispatched: true},
		0)

	if !ok {
		t.Fatal("expected pre-accept timeout to request focus")
	}
	if reason != focusReasonPreAcceptTimeout {
		t.Fatalf("focus reason mismatch: %s", reason)
	}
}

// Verifies heartbeat silence after acceptance is treated as an abnormal timeout.
func TestConnectionRetryFocusReasonClassifiesHeartbeatSilenceTimeout(t *testing.T) {
	err := fmt.Errorf("no response or heartbeat from Unity for 6s: %w", timeoutOnlyError{})
	reason, ok := connectionRetryFocusReasonForError(
		err,
		unityipc.UnitySendOutcome{RequestDispatched: true, RequestAccepted: true},
		0)

	if !ok {
		t.Fatal("expected heartbeat silence timeout to request focus")
	}
	if reason != focusReasonHeartbeatSilenceTimeout {
		t.Fatalf("focus reason mismatch: %s", reason)
	}
}

// Verifies abnormal final response timeouts after acceptance request focus.
func TestConnectionRetryFocusReasonClassifiesFinalResponseTimeout(t *testing.T) {
	reason, ok := connectionRetryFocusReasonForError(
		timeoutOnlyError{},
		unityipc.UnitySendOutcome{RequestDispatched: true, RequestAccepted: true},
		0)

	if !ok {
		t.Fatal("expected final response timeout to request focus")
	}
	if reason != focusReasonFinalResponseTimeout {
		t.Fatalf("focus reason mismatch: %s", reason)
	}
}

// Verifies explicit response timeouts such as compile status polling do not request focus.
func TestConnectionRetryFocusReasonSkipsIntentionalResponseTimeout(t *testing.T) {
	_, ok := connectionRetryFocusReasonForError(
		timeoutOnlyError{},
		unityipc.UnitySendOutcome{RequestDispatched: true, RequestAccepted: true},
		compileResponseTimeout)

	if ok {
		t.Fatal("intentional response timeout should not request focus")
	}
}

// Verifies server_busy responses do not request focus because Unity is already answering.
func TestConnectionRetryFocusReasonSkipsServerBusy(t *testing.T) {
	err := &unityipc.RPCError{
		Code:    -32603,
		Message: "Unity is busy running 'compile'.",
		Data:    []byte(`{"type":"server_busy"}`),
	}
	_, ok := connectionRetryFocusReasonForError(
		err,
		unityipc.UnitySendOutcome{RequestDispatched: true},
		0)

	if ok {
		t.Fatal("server_busy should not request focus")
	}
}

// Verifies editor-unresponsive heartbeat failures are treated as main-thread stalls.
func TestConnectionRetryFocusReasonClassifiesEditorUnresponsive(t *testing.T) {
	reason, ok := connectionRetryFocusReasonForError(
		&unityipc.EditorUnresponsiveError{StallSeconds: 300},
		unityipc.UnitySendOutcome{RequestDispatched: true, RequestAccepted: true},
		0)

	if !ok {
		t.Fatal("expected editor-unresponsive error to request focus")
	}
	if reason != focusReasonMainThreadStall {
		t.Fatalf("focus reason mismatch: %s", reason)
	}
}

// Verifies a transient process discovery miss does not suppress a later focus attempt.
func TestConnectionRetryFocusControllerRetriesAfterProcessDiscoveryMiss(t *testing.T) {
	deps := defaultConnectionRetryDeps()
	findCallCount := 0
	focusCallCount := 0
	deps.findRunningUnityProcess = func(context.Context, string) (*clicore.UnityProcess, error) {
		findCallCount++
		if findCallCount == 1 {
			return nil, nil
		}
		return &clicore.UnityProcess{Pid: 123}, nil
	}
	deps.focusUnityProcess = func(context.Context, int) (clicore.RestoreFocusFunc, error) {
		focusCallCount++
		return nil, nil
	}

	connection := unityipc.Connection{ProjectRoot: t.TempDir()}
	controller := newConnectionRetryFocusController(connection, "get-logs", deps)

	controller.tryFocus(context.Background(), focusReasonMainThreadStall, errors.New("first probe missed"))
	controller.tryFocus(context.Background(), focusReasonFinalResponseTimeout, errors.New("terminal timeout"))

	if findCallCount != 2 {
		t.Fatalf("expected process discovery to retry, got %d calls", findCallCount)
	}
	if focusCallCount != 1 {
		t.Fatalf("expected later focus attempt to run once, got %d calls", focusCallCount)
	}
}

// Verifies a connect() the operating system refused permanently is not retried. Retrying it
// burned the whole 60-second window and then reported the window's own deadline error, hiding
// the real syscall error the first attempt already had.
func TestShouldNotRetryPermanentlyRefusedConnection(t *testing.T) {
	err := &unityipc.ConnectionAttemptError{
		Endpoint: "/tmp/uloop-501/UnityCliLoop-sample.sock",
		Cause: &net.OpError{
			Op:   "dial",
			Net:  "unix",
			Addr: &net.UnixAddr{Name: "/tmp/uloop-501/UnityCliLoop-sample.sock", Net: "unix"},
			Err:  os.NewSyscallError("connect", syscall.EPERM),
		},
	}

	if shouldRetryUndispatchedConnection(err, unityipc.UnitySendOutcome{}) {
		t.Fatal("a permanently refused connect must not enter the retry loop")
	}
}

// Verifies the whole send path fails at the first attempt when the operating system refuses the
// connect, and reports that syscall error itself. Retrying it consumed the extended
// unity-alive window and then reported the window's own deadline as `i/o timeout`.
func TestSendWithTransientConnectionRetryAbortsOnRefusedConnect(t *testing.T) {
	if runtime.GOOS == "windows" {
		t.Skip("unix socket file permissions do not apply to named pipes")
	}
	if os.Geteuid() == 0 {
		t.Skip("root bypasses socket file permissions")
	}

	// Why a real listening socket with mode 000: the refusal has to come from the kernel's
	// connect, which is the only thing that produces the errno this path classifies. The
	// directory comes from MkdirTemp rather than t.TempDir because a socket path built from this
	// test's name exceeds the sockaddr_un limit.
	endpointDirectory, err := os.MkdirTemp("", "uloop-refused")
	if err != nil {
		t.Fatalf("failed to create the endpoint directory: %v", err)
	}
	t.Cleanup(func() {
		_ = os.RemoveAll(endpointDirectory)
	})
	socketPath := filepath.Join(endpointDirectory, "refused.sock")
	listener, listenErr := net.Listen("unix", socketPath)
	if listenErr != nil {
		t.Fatalf("failed to listen on the endpoint: %v", listenErr)
	}
	defer func() {
		_ = listener.Close()
	}()
	if err := os.Chmod(socketPath, 0o000); err != nil {
		t.Fatalf("failed to remove socket permissions: %v", err)
	}

	deps := defaultConnectionRetryDeps()
	deps.findRunningUnityProcess = func(context.Context, string) (*clicore.UnityProcess, error) {
		return &clicore.UnityProcess{Pid: 123}, nil
	}
	connection := unityipc.Connection{
		Endpoint:    unityipc.Endpoint{Network: "unix", Address: socketPath},
		ProjectRoot: t.TempDir(),
	}

	startedAt := time.Now()
	_, sendErr := sendWithTransientConnectionRetryWithDeps(
		context.Background(),
		connection,
		"get-logs",
		map[string]any{},
		nil,
		0,
		deps)
	elapsed := time.Since(startedAt)

	var connectionErr *unityipc.ConnectionAttemptError
	if !errors.As(sendErr, &connectionErr) {
		t.Fatalf("expected the connection attempt error, got %v", sendErr)
	}
	var notRespondingErr clierrors.UnityServerNotRespondingError
	if errors.As(sendErr, &notRespondingErr) {
		t.Fatalf("a refused connect must not be reported as a non-responding server: %v", sendErr)
	}
	if !clierrors.IsPermanentConnectError(sendErr) {
		t.Fatalf("the syscall error was replaced on the way out: %v", sendErr)
	}
	if elapsed >= deps.retryPoll {
		t.Fatalf("expected the send to abort before the first retry wait, took %s", elapsed)
	}
}

// Verifies the dial failures the retry window exists for — the socket not created yet, nobody
// listening yet — keep being retried.
func TestShouldRetryTransientlyFailedConnection(t *testing.T) {
	transientCauses := []error{
		os.NewSyscallError("connect", syscall.ENOENT),
		os.NewSyscallError("connect", syscall.ECONNREFUSED),
	}

	for _, cause := range transientCauses {
		err := &unityipc.ConnectionAttemptError{
			Endpoint: "/tmp/uloop-501/UnityCliLoop-sample.sock",
			Cause:    cause,
		}
		if !shouldRetryUndispatchedConnection(err, unityipc.UnitySendOutcome{}) {
			t.Fatalf("a transient dial failure must stay retryable: %v", cause)
		}
	}
}

// Verifies accepted RPCs can outlive the pre-dispatch connection retry timeout.
func TestSendWithTransientConnectionRetryDoesNotCancelAcceptedRequestAtRetryTimeout(t *testing.T) {
	if runtime.GOOS == "windows" {
		t.Skip("TCP endpoint injection is only used by this non-Windows client test")
	}

	deps := defaultConnectionRetryDeps()
	// The retry window must be wide enough that the dial plus accepted ack always
	// completes inside it even on a loaded CI machine, while the server delay stays
	// well past the window so the timeout reliably fires mid-request.
	deps.retryTimeout = 200 * time.Millisecond

	listener, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatalf("failed to listen: %v", err)
	}
	defer func() {
		_ = listener.Close()
	}()

	serverErr := make(chan error, 1)
	go func() {
		conn, err := listener.Accept()
		if err != nil {
			serverErr <- err
			return
		}
		defer func() {
			_ = conn.Close()
		}()

		if _, err := unityipc.Read(bufio.NewReader(conn)); err != nil {
			serverErr <- err
			return
		}

		accepted := []byte(`{"jsonrpc":"2.0","result":{"accepted":true},"uloop":{"phase":"accepted"},"id":1}`)
		if err := unityipc.Write(conn, accepted); err != nil {
			serverErr <- err
			return
		}

		time.Sleep(600 * time.Millisecond)

		final := []byte(`{"jsonrpc":"2.0","result":{"ok":true},"id":1}`)
		if err := unityipc.Write(conn, final); err != nil {
			serverErr <- err
			return
		}
	}()

	connection := unityipc.Connection{
		Endpoint: unityipc.Endpoint{
			Network: "tcp",
			Address: listener.Addr().String(),
		},
		ProjectRoot: t.TempDir(),
	}

	outcome, err := sendWithTransientConnectionRetryWithDeps(
		context.Background(),
		connection,
		"compile",
		map[string]any{},
		nil,
		0,
		deps)
	if err != nil {
		t.Fatalf("accepted request should not be canceled by retry timeout: %v", err)
	}
	if string(outcome.Result) != `{"ok":true}` {
		t.Fatalf("final result mismatch: %s", outcome.Result)
	}

	select {
	case err := <-serverErr:
		t.Fatalf("server failed: %v", err)
	default:
	}
}

// Verifies the retry timeout still bounds pre-accepted server silence.
func TestSendWithTransientConnectionRetryKeepsRetryTimeoutBeforeAcceptedAck(t *testing.T) {
	if runtime.GOOS == "windows" {
		t.Skip("TCP endpoint injection is only used by this non-Windows client test")
	}

	deps := defaultConnectionRetryDeps()
	// The retry window must be wide enough that the dial and request write always
	// complete inside it even on a loaded CI machine, while the server delay stays
	// well past the window so the pre-accept timeout reliably fires first.
	deps.retryTimeout = 200 * time.Millisecond

	listener, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatalf("failed to listen: %v", err)
	}
	defer func() {
		_ = listener.Close()
	}()

	serverErr := make(chan error, 1)
	go func() {
		conn, err := listener.Accept()
		if err != nil {
			serverErr <- err
			return
		}
		defer func() {
			_ = conn.Close()
		}()

		if _, err := unityipc.Read(bufio.NewReader(conn)); err != nil {
			serverErr <- err
			return
		}

		time.Sleep(600 * time.Millisecond)

		final := []byte(`{"jsonrpc":"2.0","result":{"ok":true},"id":1}`)
		if err := unityipc.Write(conn, final); err != nil {
			return
		}
	}()

	connection := unityipc.Connection{
		Endpoint: unityipc.Endpoint{
			Network: "tcp",
			Address: listener.Addr().String(),
		},
		ProjectRoot: t.TempDir(),
	}

	outcome, err := sendWithTransientConnectionRetryWithDeps(
		context.Background(),
		connection,
		"compile",
		map[string]any{},
		nil,
		0,
		deps)
	if err == nil {
		t.Fatalf("expected retry timeout before accepted ack, got result %s", outcome.Result)
	}
	if !outcome.RequestDispatched {
		t.Fatalf("request dispatch flag mismatch: %#v", outcome)
	}

	select {
	case err := <-serverErr:
		t.Fatalf("server failed: %v", err)
	default:
	}
}

// Verifies terminal timeout focus remains visible after the command returns.
func TestSendWithTransientConnectionRetryKeepsUnityFocusedAfterPreAcceptTimeout(t *testing.T) {
	if runtime.GOOS == "windows" {
		t.Skip("TCP endpoint injection is only used by this non-Windows client test")
	}

	deps := defaultConnectionRetryDeps()
	focusCallCount := 0
	restoreCallCount := 0
	deps.findRunningUnityProcess = func(context.Context, string) (*clicore.UnityProcess, error) {
		return &clicore.UnityProcess{Pid: 123}, nil
	}
	deps.focusUnityProcess = func(context.Context, int) (clicore.RestoreFocusFunc, error) {
		focusCallCount++
		return func(context.Context) error {
			restoreCallCount++
			return nil
		}, nil
	}
	deps.retryTimeout = 100 * time.Millisecond

	listener, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatalf("failed to listen: %v", err)
	}
	defer func() {
		_ = listener.Close()
	}()

	releaseServer := make(chan struct{})
	go func() {
		conn, acceptErr := listener.Accept()
		if acceptErr != nil {
			return
		}
		defer func() {
			_ = conn.Close()
		}()
		if _, readErr := unityipc.Read(bufio.NewReader(conn)); readErr != nil {
			return
		}
		<-releaseServer
	}()
	t.Cleanup(func() {
		close(releaseServer)
	})

	connection := unityipc.Connection{
		Endpoint: unityipc.Endpoint{
			Network: "tcp",
			Address: listener.Addr().String(),
		},
		ProjectRoot: t.TempDir(),
	}

	_, err = sendWithTransientConnectionRetryWithDeps(
		context.Background(),
		connection,
		"get-logs",
		map[string]any{},
		nil,
		0,
		deps)

	if err == nil {
		t.Fatal("expected pre-accept timeout")
	}
	if focusCallCount != 1 {
		t.Fatalf("expected one focus attempt, got %d", focusCallCount)
	}
	if restoreCallCount != 0 {
		t.Fatalf("terminal timeout focus should not be restored immediately, got %d restores", restoreCallCount)
	}
}

// Verifies a server_busy response is retried, because the request was never executed
// and Unity frees the execution slot when the running tool completes.
func TestSendWithTransientConnectionRetryRetriesBusyResponses(t *testing.T) {
	if runtime.GOOS == "windows" {
		t.Skip("TCP endpoint injection is only used by this non-Windows client test")
	}

	deps := defaultConnectionRetryDeps()
	deps.retryPoll = 5 * time.Millisecond

	listener, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatalf("failed to listen: %v", err)
	}
	defer func() {
		_ = listener.Close()
	}()

	busy := `{"jsonrpc":"2.0","id":1,"error":{"code":-32603,"message":"Unity is busy running 'compile'.","data":{"type":"server_busy","runningToolName":"compile","requestedToolName":"get-logs","message":"busy"}}}`
	ok := `{"jsonrpc":"2.0","result":{"ok":true},"id":1}`
	go func() {
		for _, payload := range []string{busy, ok} {
			conn, err := listener.Accept()
			if err != nil {
				return
			}
			if _, err := unityipc.Read(bufio.NewReader(conn)); err != nil {
				_ = conn.Close()
				return
			}
			_ = unityipc.Write(conn, []byte(payload))
			_ = conn.Close()
		}
	}()

	connection := unityipc.Connection{
		Endpoint: unityipc.Endpoint{
			Network: "tcp",
			Address: listener.Addr().String(),
		},
		ProjectRoot: t.TempDir(),
	}

	outcome, err := sendWithTransientConnectionRetryWithDeps(
		context.Background(),
		connection,
		"get-logs",
		map[string]any{},
		nil,
		0,
		deps)
	if err != nil {
		t.Fatalf("busy response should be retried to success: %v", err)
	}
	if string(outcome.Result) != `{"ok":true}` {
		t.Fatalf("final result mismatch: %s", outcome.Result)
	}
}

// Verifies a persistently busy Unity still surfaces the busy error after the retry window.
func TestSendWithTransientConnectionRetryReturnsBusyAfterRetryWindow(t *testing.T) {
	if runtime.GOOS == "windows" {
		t.Skip("TCP endpoint injection is only used by this non-Windows client test")
	}

	deps := defaultConnectionRetryDeps()
	// The busy assertion only holds once at least one busy response lands inside the
	// retry window. A narrow window can expire before the first dial completes on a
	// loaded CI machine, surfacing a dial timeout instead of the busy RPC error.
	deps.retryTimeout = 500 * time.Millisecond
	deps.retryPoll = 5 * time.Millisecond
	// A dial cut short by the expiring window probes for a running Unity process.
	// The dial deadline is a separate timer that can fire microseconds before
	// retryContext reports expiry, so an instant probe would reach the busy-masking
	// guard while retryContext.Err() is still nil and surface the dial error instead.
	// Block until the context is done, like a real OS process scan that always
	// outlasts those microseconds, so the busy guard sees the expired context.
	deps.findRunningUnityProcess = func(ctx context.Context, projectRoot string) (*clicore.UnityProcess, error) {
		<-ctx.Done()
		return nil, ctx.Err()
	}

	listener, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatalf("failed to listen: %v", err)
	}
	defer func() {
		_ = listener.Close()
	}()

	busy := `{"jsonrpc":"2.0","id":1,"error":{"code":-32603,"message":"Unity is busy running 'compile'.","data":{"type":"server_busy","runningToolName":"compile","requestedToolName":"get-logs","message":"busy"}}}`
	go func() {
		for {
			conn, err := listener.Accept()
			if err != nil {
				return
			}
			// Serve concurrently so rapid retry reconnects never queue behind a slow handler.
			go func() {
				defer func() {
					_ = conn.Close()
				}()
				if _, readErr := unityipc.Read(bufio.NewReader(conn)); readErr != nil {
					return
				}
				_ = unityipc.Write(conn, []byte(busy))
			}()
		}
	}()

	connection := unityipc.Connection{
		Endpoint: unityipc.Endpoint{
			Network: "tcp",
			Address: listener.Addr().String(),
		},
		ProjectRoot: t.TempDir(),
	}

	_, err = sendWithTransientConnectionRetryWithDeps(
		context.Background(),
		connection,
		"get-logs",
		map[string]any{},
		nil,
		0,
		deps)
	if err == nil {
		t.Fatal("expected busy error after retry window")
	}
	var rpcErr *unityipc.RPCError
	if !errors.As(err, &rpcErr) {
		t.Fatalf("busy must surface as the original RPC error, got: %v", err)
	}
}

// TDD repro for B-7a: before busy_stall focus rescue, persistent server_busy never called
// focusUnityProcess (focusCallCount stayed 0). This assertion was Red on pre-fix
// connection_retry.go and turns Green after the busy stall threshold hook.
func TestSendWithTransientConnectionRetryFocusesOnceAfterPersistentBusy(t *testing.T) {
	if runtime.GOOS == "windows" {
		t.Skip("TCP endpoint injection is only used by this non-Windows client test")
	}

	deps := defaultConnectionRetryDeps()
	deps.retryTimeout = 500 * time.Millisecond
	deps.retryPoll = 5 * time.Millisecond
	deps.busyFocusStallThreshold = 30 * time.Millisecond
	focusCallCount := 0
	restoreCallCount := 0
	deps.findRunningUnityProcess = func(context.Context, string) (*clicore.UnityProcess, error) {
		return &clicore.UnityProcess{Pid: 123}, nil
	}
	deps.focusUnityProcess = func(context.Context, int) (clicore.RestoreFocusFunc, error) {
		focusCallCount++
		return func(context.Context) error {
			restoreCallCount++
			return nil
		}, nil
	}

	listener, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatalf("failed to listen: %v", err)
	}
	defer func() {
		_ = listener.Close()
	}()

	busy := `{"jsonrpc":"2.0","id":1,"error":{"code":-32603,"message":"Unity is busy running 'compile'.","data":{"type":"server_busy","runningToolName":"compile","requestedToolName":"get-logs","message":"busy"}}}`
	go func() {
		for {
			conn, acceptErr := listener.Accept()
			if acceptErr != nil {
				return
			}
			go func() {
				defer func() {
					_ = conn.Close()
				}()
				if _, readErr := unityipc.Read(bufio.NewReader(conn)); readErr != nil {
					return
				}
				_ = unityipc.Write(conn, []byte(busy))
			}()
		}
	}()

	connection := unityipc.Connection{
		Endpoint: unityipc.Endpoint{
			Network: "tcp",
			Address: listener.Addr().String(),
		},
		ProjectRoot: t.TempDir(),
	}

	_, err = sendWithTransientConnectionRetryWithDeps(
		context.Background(),
		connection,
		"get-logs",
		map[string]any{},
		nil,
		0,
		deps)
	if err == nil {
		t.Fatal("expected busy error after retry window")
	}
	if focusCallCount != 1 {
		t.Fatalf("expected one busy-stall focus attempt, got %d", focusCallCount)
	}
	if restoreCallCount != 1 {
		t.Fatalf("expected focus restore after busy retry exit, got %d", restoreCallCount)
	}
}

// Verifies that undispatched dial failures keep retrying past the base window while a
// running Unity process is confirmed, so a domain reload longer than the base window
// (e.g. on large projects) does not fail the command spuriously.
func TestSendWithTransientConnectionRetryExtendsWindowWhileUnityProcessRuns(t *testing.T) {
	if runtime.GOOS == "windows" {
		t.Skip("TCP endpoint injection is only used by this non-Windows client test")
	}

	deps := defaultConnectionRetryDeps()
	deps.findRunningUnityProcess = func(context.Context, string) (*clicore.UnityProcess, error) {
		return &clicore.UnityProcess{Pid: 123}, nil
	}
	deps.focusUnityProcess = func(context.Context, int) (clicore.RestoreFocusFunc, error) {
		return func(context.Context) error { return nil }, nil
	}
	// Base window expires well before the server comes up; only the extended
	// unity-alive window (base * factor) allows the late dial to succeed.
	deps.retryTimeout = 100 * time.Millisecond
	deps.retryPoll = 10 * time.Millisecond

	listener, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatalf("failed to listen: %v", err)
	}
	address := listener.Addr().String()
	// Simulate the IPC endpoint being down during a domain reload: nothing accepts
	// until the "reload" finishes at 250ms, past the 100ms base window.
	_ = listener.Close()

	serverReady := make(chan net.Listener, 1)
	go func() {
		time.Sleep(250 * time.Millisecond)
		lateListener, listenErr := net.Listen("tcp", address)
		if listenErr != nil {
			serverReady <- nil
			return
		}
		serverReady <- lateListener
		success := `{"jsonrpc":"2.0","id":1,"result":{"ok":true}}`
		for {
			conn, acceptErr := lateListener.Accept()
			if acceptErr != nil {
				return
			}
			go func() {
				defer func() {
					_ = conn.Close()
				}()
				if _, readErr := unityipc.Read(bufio.NewReader(conn)); readErr != nil {
					return
				}
				_ = unityipc.Write(conn, []byte(success))
			}()
		}
	}()
	t.Cleanup(func() {
		if lateListener := <-serverReady; lateListener != nil {
			_ = lateListener.Close()
		}
	})

	connection := unityipc.Connection{
		Endpoint: unityipc.Endpoint{
			Network: "tcp",
			Address: address,
		},
		ProjectRoot: t.TempDir(),
	}

	outcome, err := sendWithTransientConnectionRetryWithDeps(
		context.Background(),
		connection,
		"get-logs",
		map[string]any{},
		nil,
		0,
		deps)
	if err != nil {
		t.Fatalf("expected success after server came back inside the extended window, got %v", err)
	}
	if len(outcome.Result) == 0 {
		t.Fatal("expected a result from the recovered server")
	}
}

// Verifies busy responses still stop retrying at the base window even though the
// unity-alive window for dial failures is longer.
func TestSendWithTransientConnectionRetryKeepsBusyBoundedByBaseWindow(t *testing.T) {
	if runtime.GOOS == "windows" {
		t.Skip("TCP endpoint injection is only used by this non-Windows client test")
	}

	deps := defaultConnectionRetryDeps()
	deps.retryTimeout = 200 * time.Millisecond
	deps.retryPoll = 10 * time.Millisecond

	listener, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatalf("failed to listen: %v", err)
	}
	defer func() {
		_ = listener.Close()
	}()

	busy := `{"jsonrpc":"2.0","id":1,"error":{"code":-32603,"message":"Unity is busy running 'compile'.","data":{"type":"server_busy","runningToolName":"compile","requestedToolName":"get-logs","message":"busy"}}}`
	go func() {
		for {
			conn, acceptErr := listener.Accept()
			if acceptErr != nil {
				return
			}
			go func() {
				defer func() {
					_ = conn.Close()
				}()
				if _, readErr := unityipc.Read(bufio.NewReader(conn)); readErr != nil {
					return
				}
				_ = unityipc.Write(conn, []byte(busy))
			}()
		}
	}()

	connection := unityipc.Connection{
		Endpoint: unityipc.Endpoint{
			Network: "tcp",
			Address: listener.Addr().String(),
		},
		ProjectRoot: t.TempDir(),
	}

	startedAt := time.Now()
	_, err = sendWithTransientConnectionRetryWithDeps(
		context.Background(),
		connection,
		"get-logs",
		map[string]any{},
		nil,
		0,
		deps)
	elapsed := time.Since(startedAt)

	var rpcErr *unityipc.RPCError
	if !errors.As(err, &rpcErr) {
		t.Fatalf("busy must surface as the original RPC error, got: %v", err)
	}
	// Generous CI margin: the assertion only needs to prove busy did not run for the
	// full extended window (base * factor = 1200ms here).
	if elapsed >= unityAliveRetryWindow(deps) {
		t.Fatalf("busy retries ran into the extended window: %v", elapsed)
	}
}

// Verifies a dispatched RPC failure arriving after the retry window expires is not
// masked by a busy response seen earlier in the window.
func TestSendWithTransientConnectionRetrySurfacesDispatchedFailureAfterBusy(t *testing.T) {
	if runtime.GOOS == "windows" {
		t.Skip("TCP endpoint injection is only used by this non-Windows client test")
	}

	deps := defaultConnectionRetryDeps()
	retryWindow := 150 * time.Millisecond
	deps.retryTimeout = retryWindow
	deps.retryPoll = 5 * time.Millisecond

	listener, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatalf("failed to listen: %v", err)
	}
	defer func() {
		_ = listener.Close()
	}()

	busy := `{"jsonrpc":"2.0","id":1,"error":{"code":-32603,"message":"Unity is busy running 'compile'.","data":{"type":"server_busy","runningToolName":"compile","requestedToolName":"get-logs","message":"busy"}}}`
	failure := `{"jsonrpc":"2.0","id":1,"error":{"code":-32603,"message":"tool execution failed","data":{"type":"tool_error","message":"tool execution failed"}}}`
	go func() {
		first := true
		for {
			conn, acceptErr := listener.Accept()
			if acceptErr != nil {
				return
			}
			sendBusy := first
			first = false
			go func(conn net.Conn, sendBusy bool) {
				defer func() {
					_ = conn.Close()
				}()
				if _, readErr := unityipc.Read(bufio.NewReader(conn)); readErr != nil {
					return
				}
				if sendBusy {
					_ = unityipc.Write(conn, []byte(busy))
					return
				}
				accepted := `{"jsonrpc":"2.0","result":{"accepted":true},"uloop":{"phase":"accepted"},"id":1}`
				if writeErr := unityipc.Write(conn, []byte(accepted)); writeErr != nil {
					return
				}
				// The delay starts only after the accepted ack is on the wire, and twice
				// the retry window guarantees the window has expired before the failure
				// response arrives, regardless of scheduler jitter.
				time.Sleep(retryWindow * 2)
				_ = unityipc.Write(conn, []byte(failure))
			}(conn, sendBusy)
		}
	}()

	connection := unityipc.Connection{
		Endpoint: unityipc.Endpoint{
			Network: "tcp",
			Address: listener.Addr().String(),
		},
		ProjectRoot: t.TempDir(),
	}

	_, err = sendWithTransientConnectionRetryWithDeps(
		context.Background(),
		connection,
		"get-logs",
		map[string]any{},
		nil,
		0,
		deps)
	if err == nil {
		t.Fatal("expected the dispatched failure to surface")
	}
	if isUnityServerBusyRPCError(err) {
		t.Fatalf("dispatched failure must not be masked by the earlier busy response, got: %v", err)
	}
	var rpcErr *unityipc.RPCError
	if !errors.As(err, &rpcErr) {
		t.Fatalf("dispatched failure must surface as the original RPC error, got: %v", err)
	}
}
