package cli

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
	"testing"
	"time"

	"github.com/hatayama/unity-cli-loop/cli/internal/unityipc"
)

// Verifies transient IPC connection failures focus Unity once and restore focus before reporting server-not-responding.
func TestSendWithTransientConnectionRetryReportsUnityServerNotResponding(t *testing.T) {
	originalFinder := findRunningUnityProcessForConnectionRetry
	originalFocus := focusUnityProcessForConnectionRetry
	originalTimeout := serverConnectionRetryTimeout
	originalPoll := serverConnectionRetryPoll
	focusCallCount := 0
	restoreCallCount := 0
	findRunningUnityProcessForConnectionRetry = func(context.Context, string) (*unityProcess, error) {
		return &unityProcess{pid: 123}, nil
	}
	focusUnityProcessForConnectionRetry = func(context.Context, int) (restoreFocusFunc, error) {
		focusCallCount++
		return func(context.Context) error {
			restoreCallCount++
			return nil
		}, nil
	}
	serverConnectionRetryTimeout = time.Nanosecond
	serverConnectionRetryPoll = time.Nanosecond
	t.Cleanup(func() {
		findRunningUnityProcessForConnectionRetry = originalFinder
		focusUnityProcessForConnectionRetry = originalFocus
		serverConnectionRetryTimeout = originalTimeout
		serverConnectionRetryPoll = originalPoll
	})

	connection := unityipc.Connection{
		Endpoint: unityipc.Endpoint{
			Network: "unix",
			Address: t.TempDir() + "/missing.sock",
		},
		ProjectRoot: t.TempDir(),
	}

	_, err := sendWithTransientConnectionRetry(
		context.Background(),
		connection,
		"get-logs",
		map[string]any{},
		nil)

	var notRespondingErr unityServerNotRespondingError
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

	originalFinder := findRunningUnityProcessForConnectionRetry
	originalFocus := focusUnityProcessForConnectionRetry
	originalTimeout := serverConnectionRetryTimeout
	originalPoll := serverConnectionRetryPoll
	findRunningUnityProcessForConnectionRetry = func(context.Context, string) (*unityProcess, error) {
		return &unityProcess{pid: 456}, nil
	}
	focusUnityProcessForConnectionRetry = func(context.Context, int) (restoreFocusFunc, error) {
		return nil, nil
	}
	serverConnectionRetryTimeout = time.Nanosecond
	serverConnectionRetryPoll = time.Nanosecond
	t.Cleanup(func() {
		findRunningUnityProcessForConnectionRetry = originalFinder
		focusUnityProcessForConnectionRetry = originalFocus
		serverConnectionRetryTimeout = originalTimeout
		serverConnectionRetryPoll = originalPoll
	})

	projectRoot := t.TempDir()
	connection := unityipc.Connection{
		Endpoint: unityipc.Endpoint{
			Network: "unix",
			Address: filepath.Join(t.TempDir(), "missing.sock"),
		},
		ProjectRoot: projectRoot,
	}

	_, _ = sendWithTransientConnectionRetry(
		context.Background(),
		connection,
		"get-logs",
		map[string]any{},
		nil)

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

	originalFinder := findRunningUnityProcessForConnectionRetry
	originalFocus := focusUnityProcessForConnectionRetry
	originalTimeout := serverConnectionRetryTimeout
	originalPoll := serverConnectionRetryPoll
	findRunningUnityProcessForConnectionRetry = func(context.Context, string) (*unityProcess, error) {
		return &unityProcess{pid: 789}, nil
	}
	focusUnityProcessForConnectionRetry = func(context.Context, int) (restoreFocusFunc, error) {
		return nil, fmt.Errorf("focus denied")
	}
	serverConnectionRetryTimeout = time.Nanosecond
	serverConnectionRetryPoll = time.Nanosecond
	t.Cleanup(func() {
		findRunningUnityProcessForConnectionRetry = originalFinder
		focusUnityProcessForConnectionRetry = originalFocus
		serverConnectionRetryTimeout = originalTimeout
		serverConnectionRetryPoll = originalPoll
	})

	projectRoot := t.TempDir()
	connection := unityipc.Connection{
		Endpoint: unityipc.Endpoint{
			Network: "unix",
			Address: filepath.Join(t.TempDir(), "missing.sock"),
		},
		ProjectRoot: projectRoot,
	}

	_, _ = sendWithTransientConnectionRetry(
		context.Background(),
		connection,
		"compile",
		map[string]any{},
		nil)

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

// Verifies process probe timeouts keep the structured server-not-responding error.
func TestSendWithTransientConnectionRetryClassifiesProcessProbeTimeout(t *testing.T) {
	originalFinder := findRunningUnityProcessForConnectionRetry
	originalTimeout := serverConnectionRetryTimeout
	originalPoll := serverConnectionRetryPoll
	findRunningUnityProcessForConnectionRetry = func(ctx context.Context, projectRoot string) (*unityProcess, error) {
		<-ctx.Done()
		return nil, ctx.Err()
	}
	serverConnectionRetryTimeout = time.Nanosecond
	serverConnectionRetryPoll = time.Nanosecond
	t.Cleanup(func() {
		findRunningUnityProcessForConnectionRetry = originalFinder
		serverConnectionRetryTimeout = originalTimeout
		serverConnectionRetryPoll = originalPoll
	})

	connection := unityipc.Connection{
		Endpoint: unityipc.Endpoint{
			Network: "unix",
			Address: t.TempDir() + "/missing.sock",
		},
		ProjectRoot: t.TempDir(),
	}

	_, err := sendWithTransientConnectionRetry(
		context.Background(),
		connection,
		"get-logs",
		map[string]any{},
		nil)

	var notRespondingErr unityServerNotRespondingError
	if !errors.As(err, &notRespondingErr) {
		t.Fatalf("expected unityServerNotRespondingError, got %v", err)
	}
}

func readOnlyCliVibeLog(t *testing.T, projectRoot string) string {
	t.Helper()
	logFiles, err := filepath.Glob(filepath.Join(projectRoot, cliVibeLogDirectory, cliVibeLogPrefix+"_*.json"))
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
	originalFinder := findRunningUnityProcessForConnectionRetry
	originalFocus := focusUnityProcessForConnectionRetry
	findCallCount := 0
	focusCallCount := 0
	findRunningUnityProcessForConnectionRetry = func(context.Context, string) (*unityProcess, error) {
		findCallCount++
		if findCallCount == 1 {
			return nil, nil
		}
		return &unityProcess{pid: 123}, nil
	}
	focusUnityProcessForConnectionRetry = func(context.Context, int) (restoreFocusFunc, error) {
		focusCallCount++
		return nil, nil
	}
	t.Cleanup(func() {
		findRunningUnityProcessForConnectionRetry = originalFinder
		focusUnityProcessForConnectionRetry = originalFocus
	})

	connection := unityipc.Connection{ProjectRoot: t.TempDir()}
	controller := newConnectionRetryFocusController(connection, "get-logs")

	controller.tryFocus(context.Background(), focusReasonMainThreadStall, errors.New("first probe missed"))
	controller.tryFocus(context.Background(), focusReasonFinalResponseTimeout, errors.New("terminal timeout"))

	if findCallCount != 2 {
		t.Fatalf("expected process discovery to retry, got %d calls", findCallCount)
	}
	if focusCallCount != 1 {
		t.Fatalf("expected later focus attempt to run once, got %d calls", focusCallCount)
	}
}

// Verifies accepted RPCs can outlive the pre-dispatch connection retry timeout.
func TestSendWithTransientConnectionRetryDoesNotCancelAcceptedRequestAtRetryTimeout(t *testing.T) {
	if runtime.GOOS == "windows" {
		t.Skip("TCP endpoint injection is only used by this non-Windows client test")
	}

	originalTimeout := serverConnectionRetryTimeout
	// The retry window must be wide enough that the dial plus accepted ack always
	// completes inside it even on a loaded CI machine, while the server delay stays
	// well past the window so the timeout reliably fires mid-request.
	serverConnectionRetryTimeout = 200 * time.Millisecond
	t.Cleanup(func() {
		serverConnectionRetryTimeout = originalTimeout
	})

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

	outcome, err := sendWithTransientConnectionRetry(
		context.Background(),
		connection,
		"compile",
		map[string]any{},
		nil)
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

	originalTimeout := serverConnectionRetryTimeout
	// The retry window must be wide enough that the dial and request write always
	// complete inside it even on a loaded CI machine, while the server delay stays
	// well past the window so the pre-accept timeout reliably fires first.
	serverConnectionRetryTimeout = 200 * time.Millisecond
	t.Cleanup(func() {
		serverConnectionRetryTimeout = originalTimeout
	})

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

	outcome, err := sendWithTransientConnectionRetry(
		context.Background(),
		connection,
		"compile",
		map[string]any{},
		nil)
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

	originalFinder := findRunningUnityProcessForConnectionRetry
	originalFocus := focusUnityProcessForConnectionRetry
	originalTimeout := serverConnectionRetryTimeout
	focusCallCount := 0
	restoreCallCount := 0
	findRunningUnityProcessForConnectionRetry = func(context.Context, string) (*unityProcess, error) {
		return &unityProcess{pid: 123}, nil
	}
	focusUnityProcessForConnectionRetry = func(context.Context, int) (restoreFocusFunc, error) {
		focusCallCount++
		return func(context.Context) error {
			restoreCallCount++
			return nil
		}, nil
	}
	serverConnectionRetryTimeout = 100 * time.Millisecond
	t.Cleanup(func() {
		findRunningUnityProcessForConnectionRetry = originalFinder
		focusUnityProcessForConnectionRetry = originalFocus
		serverConnectionRetryTimeout = originalTimeout
	})

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

	_, err = sendWithTransientConnectionRetry(
		context.Background(),
		connection,
		"get-logs",
		map[string]any{},
		nil)

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

	originalPoll := serverConnectionRetryPoll
	serverConnectionRetryPoll = 5 * time.Millisecond
	t.Cleanup(func() {
		serverConnectionRetryPoll = originalPoll
	})

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

	outcome, err := sendWithTransientConnectionRetry(
		context.Background(),
		connection,
		"get-logs",
		map[string]any{},
		nil)
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

	originalFinder := findRunningUnityProcessForConnectionRetry
	originalTimeout := serverConnectionRetryTimeout
	originalPoll := serverConnectionRetryPoll
	// The busy assertion only holds once at least one busy response lands inside the
	// retry window. A narrow window can expire before the first dial completes on a
	// loaded CI machine, surfacing a dial timeout instead of the busy RPC error.
	serverConnectionRetryTimeout = 500 * time.Millisecond
	serverConnectionRetryPoll = 5 * time.Millisecond
	// A dial cut short by the expiring window probes for a running Unity process.
	// The dial deadline is a separate timer that can fire microseconds before
	// retryContext reports expiry, so an instant probe would reach the busy-masking
	// guard while retryContext.Err() is still nil and surface the dial error instead.
	// Block until the context is done, like a real OS process scan that always
	// outlasts those microseconds, so the busy guard sees the expired context.
	findRunningUnityProcessForConnectionRetry = func(ctx context.Context, projectRoot string) (*unityProcess, error) {
		<-ctx.Done()
		return nil, ctx.Err()
	}
	t.Cleanup(func() {
		findRunningUnityProcessForConnectionRetry = originalFinder
		serverConnectionRetryTimeout = originalTimeout
		serverConnectionRetryPoll = originalPoll
	})

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

	_, err = sendWithTransientConnectionRetry(
		context.Background(),
		connection,
		"get-logs",
		map[string]any{},
		nil)
	if err == nil {
		t.Fatal("expected busy error after retry window")
	}
	var rpcErr *unityipc.RPCError
	if !errors.As(err, &rpcErr) {
		t.Fatalf("busy must surface as the original RPC error, got: %v", err)
	}
}

// Verifies that undispatched dial failures keep retrying past the base window while a
// running Unity process is confirmed, so a domain reload longer than the base window
// (e.g. on large projects) does not fail the command spuriously.
func TestSendWithTransientConnectionRetryExtendsWindowWhileUnityProcessRuns(t *testing.T) {
	if runtime.GOOS == "windows" {
		t.Skip("TCP endpoint injection is only used by this non-Windows client test")
	}

	originalFinder := findRunningUnityProcessForConnectionRetry
	originalFocus := focusUnityProcessForConnectionRetry
	originalTimeout := serverConnectionRetryTimeout
	originalPoll := serverConnectionRetryPoll
	findRunningUnityProcessForConnectionRetry = func(context.Context, string) (*unityProcess, error) {
		return &unityProcess{pid: 123}, nil
	}
	focusUnityProcessForConnectionRetry = func(context.Context, int) (restoreFocusFunc, error) {
		return func(context.Context) error { return nil }, nil
	}
	// Base window expires well before the server comes up; only the extended
	// unity-alive window (base * factor) allows the late dial to succeed.
	serverConnectionRetryTimeout = 100 * time.Millisecond
	serverConnectionRetryPoll = 10 * time.Millisecond
	t.Cleanup(func() {
		findRunningUnityProcessForConnectionRetry = originalFinder
		focusUnityProcessForConnectionRetry = originalFocus
		serverConnectionRetryTimeout = originalTimeout
		serverConnectionRetryPoll = originalPoll
	})

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

	outcome, err := sendWithTransientConnectionRetry(
		context.Background(),
		connection,
		"get-logs",
		map[string]any{},
		nil)
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

	originalTimeout := serverConnectionRetryTimeout
	originalPoll := serverConnectionRetryPoll
	serverConnectionRetryTimeout = 200 * time.Millisecond
	serverConnectionRetryPoll = 10 * time.Millisecond
	t.Cleanup(func() {
		serverConnectionRetryTimeout = originalTimeout
		serverConnectionRetryPoll = originalPoll
	})

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
	_, err = sendWithTransientConnectionRetry(
		context.Background(),
		connection,
		"get-logs",
		map[string]any{},
		nil)
	elapsed := time.Since(startedAt)

	var rpcErr *unityipc.RPCError
	if !errors.As(err, &rpcErr) {
		t.Fatalf("busy must surface as the original RPC error, got: %v", err)
	}
	// Generous CI margin: the assertion only needs to prove busy did not run for the
	// full extended window (base * factor = 1200ms here).
	if elapsed >= unityAliveRetryWindow() {
		t.Fatalf("busy retries ran into the extended window: %v", elapsed)
	}
}

// Verifies a dispatched RPC failure arriving after the retry window expires is not
// masked by a busy response seen earlier in the window.
func TestSendWithTransientConnectionRetrySurfacesDispatchedFailureAfterBusy(t *testing.T) {
	if runtime.GOOS == "windows" {
		t.Skip("TCP endpoint injection is only used by this non-Windows client test")
	}

	originalTimeout := serverConnectionRetryTimeout
	originalPoll := serverConnectionRetryPoll
	retryWindow := 150 * time.Millisecond
	serverConnectionRetryTimeout = retryWindow
	serverConnectionRetryPoll = 5 * time.Millisecond
	t.Cleanup(func() {
		serverConnectionRetryTimeout = originalTimeout
		serverConnectionRetryPoll = originalPoll
	})

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

	_, err = sendWithTransientConnectionRetry(
		context.Background(),
		connection,
		"get-logs",
		map[string]any{},
		nil)
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
