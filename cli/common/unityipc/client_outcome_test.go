package unityipc

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"net"
	"os"
	"runtime"
	"strings"
	"testing"
	"time"

	"github.com/hatayama/unity-cli-loop/common/progress"
)

// Test support server that accepts one connection and holds it open without reading,
// so the client's request write fills the socket buffers and blocks.
func startNonReadingTestServer(t *testing.T) Connection {
	t.Helper()
	if runtime.GOOS == "windows" {
		t.Skip("TCP endpoint injection is only used by this non-Windows client test")
	}

	listener, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatalf("failed to listen: %v", err)
	}
	released := make(chan struct{})
	t.Cleanup(func() {
		close(released)
		_ = listener.Close()
	})

	go func() {
		conn, acceptErr := listener.Accept()
		if acceptErr != nil {
			return
		}
		defer func() {
			_ = conn.Close()
		}()
		<-released
	}()

	return Connection{
		Endpoint: Endpoint{
			Network: "tcp",
			Address: listener.Addr().String(),
		},
		ProjectRoot: t.TempDir(),
	}
}

func encodeFrame(t *testing.T, payload string) []byte {
	t.Helper()
	var buffer strings.Builder
	if err := Write(&buffer, []byte(payload)); err != nil {
		t.Fatalf("failed to encode frame: %v", err)
	}
	return []byte(buffer.String())
}

// Verifies that a dial failure is wrapped in ConnectionAttemptError carrying the project
// root and endpoint, and that the request is never reported as dispatched.
func TestSendWrapsDialFailureInConnectionAttemptError(t *testing.T) {
	endpoint := unreachableTestEndpoint()
	connection := Connection{Endpoint: endpoint, ProjectRoot: "<PROJECT_ROOT>"}
	client := NewClient(connection, "9.9.9")
	_, expectedDialErr := dialEndpoint(context.Background(), endpoint)
	if expectedDialErr == nil {
		t.Fatalf("the test endpoint %q should not be dialable", endpoint.Address)
	}

	outcome, err := client.SendWithProgressOutcome(context.Background(), "get-version", map[string]any{}, nil)

	var attemptErr *ConnectionAttemptError
	if !errors.As(err, &attemptErr) {
		t.Fatalf("expected ConnectionAttemptError, got %T: %v", err, err)
	}
	if attemptErr.ProjectRoot != "<PROJECT_ROOT>" || attemptErr.Endpoint != endpoint.Address {
		t.Fatalf("connection attempt fields mismatch: %#v", attemptErr)
	}
	expectedMessage := "the Unity CLI Loop server is not reachable for this project: " + attemptErr.Cause.Error()
	if err.Error() != expectedMessage {
		t.Fatalf("message mismatch: got %q want %q", err.Error(), expectedMessage)
	}
	if attemptErr.Cause == nil || attemptErr.Cause.Error() != expectedDialErr.Error() {
		t.Fatalf("cause should be the dial failure %q, got %v", expectedDialErr, attemptErr.Cause)
	}
	if outcome.RequestDispatched {
		t.Fatalf("dial failure must not report a dispatched request: %#v", outcome)
	}
}

// Verifies that params that cannot be JSON-encoded fail with the marshal error before
// anything is written, so the request is not reported as dispatched.
func TestSendFailsWithMarshalErrorForUnencodableParams(t *testing.T) {
	connection := startHeartbeatTestServer(t, func(net.Conn) {})
	client := NewClient(connection, "9.9.9")

	outcome, err := client.SendWithProgressOutcome(
		context.Background(),
		"execute-dynamic-code",
		map[string]any{"callback": make(chan int)},
		nil,
	)

	var unsupportedErr *json.UnsupportedTypeError
	if !errors.As(err, &unsupportedErr) {
		t.Fatalf("expected json.UnsupportedTypeError, got %T: %v", err, err)
	}
	if outcome.RequestDispatched {
		t.Fatalf("marshal failure must not report a dispatched request: %#v", outcome)
	}
}

// Verifies that a request write that cannot complete before the accept deadline fails with a
// timeout and is not reported as dispatched, so callers know Unity never saw the request.
func TestSendReportsUndispatchedRequestWhenWriteTimesOut(t *testing.T) {
	connection := startNonReadingTestServer(t)
	client := NewClient(connection, "9.9.9", withAcceptTimeoutForTest(200*time.Millisecond))
	// Why 64 MiB: far beyond loopback socket buffers, so the write blocks until the deadline.
	oversizedParam := strings.Repeat("x", 64<<20)

	outcome, err := client.SendWithProgressOutcome(
		context.Background(),
		"execute-dynamic-code",
		map[string]any{"code": oversizedParam},
		nil,
	)

	if !errors.Is(err, os.ErrDeadlineExceeded) {
		t.Fatalf("expected write deadline error, got %v", err)
	}
	if outcome.RequestDispatched {
		t.Fatalf("write failure must not report a dispatched request: %#v", outcome)
	}
	if outcome.Timing.Write <= 0 {
		t.Fatalf("write timing should be recorded on write failure: %#v", outcome.Timing)
	}
}

// Verifies that a connection closed before any response surfaces the transport EOF while
// still reporting the request as dispatched but not accepted.
func TestSendReportsDispatchedRequestWhenServerClosesBeforeResponding(t *testing.T) {
	connection := startHeartbeatTestServer(t, func(net.Conn) {})
	client := NewClient(connection, "9.9.9")

	outcome, err := client.SendWithProgressOutcome(context.Background(), "get-version", map[string]any{}, nil)

	if !errors.Is(err, io.EOF) {
		t.Fatalf("expected EOF from closed connection, got %T: %v", err, err)
	}
	if !outcome.RequestDispatched || outcome.RequestAccepted {
		t.Fatalf("expected dispatched but not accepted: %#v", outcome)
	}
}

// Verifies that a response frame that is not valid JSON fails with the decode error.
func TestSendFailsWithDecodeErrorForMalformedResponse(t *testing.T) {
	connection := startHeartbeatTestServer(t, func(conn net.Conn) {
		writeFrame(t, conn, "not json")
	})
	client := NewClient(connection, "9.9.9")

	outcome, err := client.SendWithProgressOutcome(context.Background(), "get-version", map[string]any{}, nil)

	var syntaxErr *json.SyntaxError
	if !errors.As(err, &syntaxErr) {
		t.Fatalf("expected json.SyntaxError, got %T: %v", err, err)
	}
	if !outcome.RequestDispatched || outcome.Result != nil {
		t.Fatalf("expected dispatched request without result: %#v", outcome)
	}
}

// Verifies that a JSON-RPC error response becomes an RPCError carrying the code, message,
// and data from Unity, with the Unity message in its error text.
func TestSendReturnsRPCErrorFromErrorResponse(t *testing.T) {
	connection := startHeartbeatTestServer(t, func(conn net.Conn) {
		writeFrame(t, conn, `{"jsonrpc":"2.0","id":1,"error":{"code":-32601,"message":"tool not found","data":{"tool":"missing"}}}`)
	})
	client := NewClient(connection, "9.9.9")

	outcome, err := client.SendWithProgressOutcome(context.Background(), "missing", map[string]any{}, nil)

	var rpcErr *RPCError
	if !errors.As(err, &rpcErr) {
		t.Fatalf("expected RPCError, got %T: %v", err, err)
	}
	if rpcErr.Code != -32601 || rpcErr.Message != "tool not found" || string(rpcErr.Data) != `{"tool":"missing"}` {
		t.Fatalf("rpc error fields mismatch: %#v", rpcErr)
	}
	if err.Error() != "unity error: tool not found" {
		t.Fatalf("rpc error message mismatch: %q", err.Error())
	}
	if outcome.Result != nil {
		t.Fatalf("error response must not carry a result: %#v", outcome)
	}
}

// Verifies that a response without result or error becomes NoResponseError so domain-reload
// recovery can treat it as a disconnect.
func TestSendReturnsNoResponseErrorForEmptyResult(t *testing.T) {
	connection := startHeartbeatTestServer(t, func(conn net.Conn) {
		writeFrame(t, conn, `{"jsonrpc":"2.0","id":1}`)
	})
	client := NewClient(connection, "9.9.9")

	_, err := client.SendWithProgressOutcome(context.Background(), "get-version", map[string]any{}, nil)

	var noResponseErr *NoResponseError
	if !errors.As(err, &noResponseErr) {
		t.Fatalf("expected NoResponseError, got %T: %v", err, err)
	}
	if err.Error() != "unity returned no RPC result" {
		t.Fatalf("no-response message mismatch: %q", err.Error())
	}
}

// Verifies that cancelling the request context stops processing at the next heartbeat even
// when the final response is already buffered, instead of returning that result.
func TestSendStopsAtHeartbeatWhenRequestContextIsCancelled(t *testing.T) {
	heartbeat := `{"jsonrpc":"2.0","id":1,"result":{"alive":true},"uloop":{"phase":"heartbeat"}}`
	connection := startHeartbeatTestServer(t, func(conn net.Conn) {
		// Why one write: the ack, heartbeat, and final response land in the client's read
		// buffer together, so later frames are read without touching the cancelled socket.
		frames := append(encodeFrame(t, heartbeatAck), encodeFrame(t, heartbeat)...)
		frames = append(frames, encodeFrame(t, `{"jsonrpc":"2.0","id":1,"result":{"ok":true}}`)...)
		if _, err := conn.Write(frames); err != nil {
			t.Errorf("failed to write frames: %v", err)
		}
	})
	client := NewClient(connection, "9.9.9", withHeartbeatSilenceOverrideForTest(5*time.Second))
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()

	outcome, err := client.SendWithProgressOutcomeAcceptContext(
		ctx,
		context.Background(),
		"run-tests",
		map[string]any{},
		func(event progress.Event) {
			if event.Stage == progress.StageAccepted {
				cancel()
			}
		},
	)

	if !errors.Is(err, context.Canceled) {
		t.Fatalf("expected context cancellation, got %v (outcome %#v)", err, outcome)
	}
	if !outcome.RequestAccepted || outcome.Result != nil {
		t.Fatalf("expected accepted request without result: %#v", outcome)
	}
}

// Verifies that a stall that began after this request's accept is reported to the stall
// handler and as "busy executing this command" progress on a self-induced-tolerant client.
func TestSendReportsSelfInducedStallAsBusyProgress(t *testing.T) {
	// Why 30: the lowest reported stall, and within the self-induced margin right after accept.
	stalledHeartbeat := `{"jsonrpc":"2.0","id":1,"result":{"alive":true},"uloop":{"phase":"heartbeat","mainThreadStallSeconds":30}}`
	connection := startHeartbeatTestServer(t, func(conn net.Conn) {
		writeFrame(t, conn, heartbeatAck)
		writeFrame(t, conn, stalledHeartbeat)
		writeFrame(t, conn, `{"jsonrpc":"2.0","id":1,"result":{"ok":true}}`)
	})
	stallReports := []float64{}
	client := NewClient(connection, "9.9.9", withHeartbeatSilenceOverrideForTest(5*time.Second)).
		WithSelfInducedMainThreadStallTolerance().
		WithMainThreadStallHandler(func(stallSeconds float64) {
			stallReports = append(stallReports, stallSeconds)
		})
	progressMessages := []string{}

	outcome, err := client.SendWithProgressOutcome(
		context.Background(),
		"execute-dynamic-code",
		map[string]any{},
		func(event progress.Event) {
			if event.Stage == progress.StageMessage {
				progressMessages = append(progressMessages, event.Message)
			}
		},
	)

	if err != nil || string(outcome.Result) != `{"ok":true}` {
		t.Fatalf("expected final result, got %q / %v", string(outcome.Result), err)
	}
	if len(stallReports) != 1 || stallReports[0] != 30 {
		t.Fatalf("stall reports mismatch: %#v", stallReports)
	}
	expected := "Unity main thread busy executing this command for 30s; still waiting..."
	if len(progressMessages) != 1 || progressMessages[0] != expected {
		t.Fatalf("progress messages mismatch: %#v", progressMessages)
	}
}

type timeoutProbeError struct {
	timeout bool
}

func (err timeoutProbeError) Error() string {
	return fmt.Sprintf("timeout probe (timeout=%t)", err.timeout)
}

func (err timeoutProbeError) Timeout() bool {
	return err.timeout
}

// Verifies that deadline expiry is detected through os.ErrDeadlineExceeded and through a
// wrapped Timeout() probe (the Windows named-pipe case), and nothing else.
func TestIsDeadlineExpiryClassifiesTimeoutErrors(t *testing.T) {
	cases := []struct {
		name     string
		err      error
		expected bool
	}{
		{name: "deadline exceeded", err: fmt.Errorf("read: %w", os.ErrDeadlineExceeded), expected: true},
		{name: "wrapped timeout probe", err: fmt.Errorf("pipe read: %w", timeoutProbeError{timeout: true}), expected: true},
		{name: "wrapped non-timeout probe", err: fmt.Errorf("pipe read: %w", timeoutProbeError{timeout: false}), expected: false},
		{name: "plain error", err: errors.New("connection reset"), expected: false},
	}

	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			if actual := isDeadlineExpiry(testCase.err); actual != testCase.expected {
				t.Fatalf("isDeadlineExpiry(%v) = %t, want %t", testCase.err, actual, testCase.expected)
			}
		})
	}
}

// unreachableTestEndpoint returns an endpoint whose dial fails without opening a socket: an unknown
// network on Unix, and a pipe name nothing listens on for Windows, whose dialer always opens a pipe.
func unreachableTestEndpoint() Endpoint {
	if runtime.GOOS == "windows" {
		return Endpoint{Network: "pipe", Address: `\\.\pipe\uloop-test-unreachable-endpoint`}
	}
	return Endpoint{Network: "bogus-network", Address: "<ENDPOINT_ADDRESS>"}
}
