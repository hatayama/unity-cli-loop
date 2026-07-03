//go:build windows

package unityipc

import (
	"bufio"
	"context"
	"errors"
	"fmt"
	"io"
	"net"
	"os"
	"strings"
	"testing"
	"time"

	"github.com/Microsoft/go-winio"
)

// These tests exercise the real Windows named pipe transport (go-winio) so the
// platform assumptions behind heartbeat deadlines and typed disconnect
// classification are verified on CI's Windows runners, not just on TCP.

func testPipeConnection(t *testing.T) (Connection, net.Listener) {
	t.Helper()
	pipePath := fmt.Sprintf(`\\.\pipe\uloop-test-%d-%s`, os.Getpid(), strings.ReplaceAll(t.Name(), "/", "_"))
	listener, err := winio.ListenPipe(pipePath, nil)
	if err != nil {
		t.Fatalf("failed to listen on named pipe: %v", err)
	}
	t.Cleanup(func() {
		_ = listener.Close()
	})

	connection := Connection{
		Endpoint: Endpoint{
			Network: "pipe",
			Address: pipePath,
		},
		ProjectRoot: t.TempDir(),
	}
	return connection, listener
}

func serveOnePipeRequest(t *testing.T, listener net.Listener, serve func(conn net.Conn)) {
	t.Helper()
	go func() {
		conn, acceptErr := listener.Accept()
		if acceptErr != nil {
			return
		}
		defer func() {
			_ = conn.Close()
		}()
		if _, readErr := Read(bufio.NewReader(conn)); readErr != nil {
			return
		}
		serve(conn)
	}()
}

// Verifies that heartbeat frames slide the silence deadline over a real named pipe,
// proving go-winio honors repeated SetDeadline calls the way the client relies on.
func TestPipeSendKeepsWaitingWhileHeartbeatsArrive(t *testing.T) {
	heartbeat := `{"jsonrpc":"2.0","id":1,"result":{"alive":true},"uloop":{"phase":"heartbeat","mainThreadStallSeconds":0}}`
	connection, listener := testPipeConnection(t)
	serveOnePipeRequest(t, listener, func(conn net.Conn) {
		writeFrame(t, conn, heartbeatAck)
		for i := 0; i < 6; i++ {
			time.Sleep(50 * time.Millisecond)
			writeFrame(t, conn, heartbeat)
		}
		writeFrame(t, conn, `{"jsonrpc":"2.0","id":1,"result":{"ok":true}}`)
	})

	client := NewClient(connection, "9.9.9")
	client.heartbeatSilenceOverride = 200 * time.Millisecond

	outcome, err := client.SendWithProgressOutcome(context.Background(), "run-tests", map[string]any{}, nil)
	if err != nil {
		t.Fatalf("expected success with heartbeats sliding the deadline, got %v", err)
	}
	if len(outcome.Result) == 0 {
		t.Fatal("expected final result")
	}
}

// Verifies that heartbeat silence over a named pipe fails with the diagnosis and a
// timeout-classified cause, proving the winio deadline error is recognized.
func TestPipeSendFailsWithDiagnosisWhenHeartbeatsStop(t *testing.T) {
	done := make(chan struct{})
	connection, listener := testPipeConnection(t)
	serveOnePipeRequest(t, listener, func(conn net.Conn) {
		writeFrame(t, conn, heartbeatAck)
		<-done
	})
	defer close(done)

	client := NewClient(connection, "9.9.9")
	client.heartbeatSilenceOverride = 150 * time.Millisecond

	_, err := client.SendWithProgressOutcome(context.Background(), "run-tests", map[string]any{}, nil)
	if err == nil {
		t.Fatal("expected heartbeat silence error")
	}
	if !strings.Contains(err.Error(), "heartbeat") {
		t.Fatalf("error misses heartbeat diagnosis: %v", err)
	}
	if !isDeadlineExpiry(err) {
		t.Fatalf("winio deadline expiry was not classified as timeout: %v", err)
	}
}

// Verifies that closing the pipe after the ack surfaces as typed io.EOF, the premise
// the transport disconnect classification relies on for Windows domain reloads.
func TestPipeServerCloseAfterAckYieldsTypedEOF(t *testing.T) {
	connection, listener := testPipeConnection(t)
	serveOnePipeRequest(t, listener, func(conn net.Conn) {
		writeFrame(t, conn, heartbeatAck)
	})

	client := NewClient(connection, "9.9.9")

	_, err := client.SendWithProgressOutcome(context.Background(), "run-tests", map[string]any{}, nil)
	if err == nil {
		t.Fatal("expected disconnect error after server closed the pipe")
	}
	if !errors.Is(err, io.EOF) && !errors.Is(err, io.ErrUnexpectedEOF) {
		t.Fatalf("pipe close did not surface as typed EOF: %v", err)
	}
}

// Verifies that dialing a missing pipe fails immediately with a dial error, the
// premise behind treating it as a retryable undispatched connection attempt.
func TestPipeDialMissingPipeFailsFast(t *testing.T) {
	connection := Connection{
		Endpoint: Endpoint{
			Network: "pipe",
			Address: fmt.Sprintf(`\\.\pipe\uloop-test-missing-%d`, os.Getpid()),
		},
		ProjectRoot: t.TempDir(),
	}

	client := NewClient(connection, "9.9.9")
	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()

	_, err := client.SendWithProgressOutcome(ctx, "get-logs", map[string]any{}, nil)
	var connectionErr *ConnectionAttemptError
	if !errors.As(err, &connectionErr) {
		t.Fatalf("expected ConnectionAttemptError for missing pipe, got %v", err)
	}
}
