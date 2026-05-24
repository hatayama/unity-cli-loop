package cli

import (
	"bufio"
	"context"
	"errors"
	"net"
	"runtime"
	"testing"
	"time"

	"github.com/hatayama/unity-cli-loop/Packages/src/Cli/internal/unityipc"
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

// Verifies accepted RPCs can outlive the pre-dispatch connection retry timeout.
func TestSendWithTransientConnectionRetryDoesNotCancelAcceptedRequestAtRetryTimeout(t *testing.T) {
	if runtime.GOOS == "windows" {
		t.Skip("TCP endpoint injection is only used by this non-Windows client test")
	}

	originalTimeout := serverConnectionRetryTimeout
	serverConnectionRetryTimeout = 20 * time.Millisecond
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

		time.Sleep(60 * time.Millisecond)

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
	serverConnectionRetryTimeout = 20 * time.Millisecond
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

		time.Sleep(60 * time.Millisecond)

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
