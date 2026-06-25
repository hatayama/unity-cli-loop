package unityipc

import (
	"bufio"
	"context"
	"encoding/json"
	"errors"
	"net"
	"runtime"
	"strings"
	"testing"
	"time"

	clicontract "github.com/hatayama/unity-cli-loop/cli"
)

func TestFormatConnectionAttemptErrorExplainsDialFailureWithoutDisconnectClaim(t *testing.T) {
	// Verifies that dial failures report connection attempts without implying a lost active connection.
	connection := Connection{
		Endpoint: Endpoint{
			Network: "unix",
			Address: "/tmp/uloop/UnityCliLoop-sample.sock",
		},
		ProjectRoot: "/tmp/MyProject",
	}

	err := formatConnectionAttemptError(connection, errors.New("dial unix /tmp/uloop/UnityCliLoop-sample.sock: connect: no such file or directory"))
	connectionErr, ok := err.(*ConnectionAttemptError)
	if !ok {
		t.Fatalf("expected ConnectionAttemptError, got %T", err)
	}
	if connectionErr.ProjectRoot != "/tmp/MyProject" {
		t.Fatalf("project root mismatch: %s", connectionErr.ProjectRoot)
	}
	if connectionErr.Endpoint != "/tmp/uloop/UnityCliLoop-sample.sock" {
		t.Fatalf("endpoint mismatch: %s", connectionErr.Endpoint)
	}
	if connectionErr.Unwrap().Error() != "dial unix /tmp/uloop/UnityCliLoop-sample.sock: connect: no such file or directory" {
		t.Fatalf("cause mismatch: %v", connectionErr.Unwrap())
	}
}

func TestSendIncludesCliVersionWithoutProjectIdentityMetadata(t *testing.T) {
	// Verifies that requests carry CLI compatibility metadata without reviving legacy project identity metadata.
	if runtime.GOOS == "windows" {
		t.Skip("TCP endpoint injection is only used by this non-Windows client test")
	}

	listener, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatalf("failed to listen: %v", err)
	}
	defer func() {
		_ = listener.Close()
	}()

	captured := make(chan map[string]any, 1)
	serverErr := make(chan error, 1)
	go captureClientMetadataRequest(listener, captured, serverErr)

	connection := Connection{
		Endpoint: Endpoint{
			Network: "tcp",
			Address: listener.Addr().String(),
		},
		ProjectRoot: "/tmp/MyProject",
	}
	client := NewClient(connection, "3.0.0-beta.6")
	if _, err := client.Send(context.Background(), "get-version", map[string]any{}); err != nil {
		t.Fatalf("Send failed: %v", err)
	}

	select {
	case err := <-serverErr:
		t.Fatalf("server failed: %v", err)
	case request := <-captured:
		assertClientMetadataRequest(t, request)
	}
}

func captureClientMetadataRequest(
	listener net.Listener,
	captured chan<- map[string]any,
	serverErr chan<- error,
) {
	conn, err := listener.Accept()
	if err != nil {
		serverErr <- err
		return
	}
	defer func() {
		_ = conn.Close()
	}()

	payload, err := Read(bufio.NewReader(conn))
	if err != nil {
		serverErr <- err
		return
	}

	var request map[string]any
	if err := json.Unmarshal(payload, &request); err != nil {
		serverErr <- err
		return
	}
	captured <- request

	response := []byte(`{"jsonrpc":"2.0","result":{"ok":true},"id":1}`)
	if err := Write(conn, response); err != nil {
		serverErr <- err
		return
	}
}

func assertClientMetadataRequest(t *testing.T, request map[string]any) {
	t.Helper()

	if _, ok := request["x-uloop"]; ok {
		t.Fatalf("request should not include x-uloop metadata: %#v", request["x-uloop"])
	}
	metadata, ok := request["uloop"].(map[string]any)
	if !ok {
		t.Fatalf("request should include uloop metadata: %#v", request)
	}
	if metadata["cliVersion"] != "3.0.0-beta.6" {
		t.Fatalf("cli version metadata mismatch: %#v", metadata)
	}
	if metadata["protocolVersion"] != float64(clicontract.Current.ProtocolVersion) {
		t.Fatalf("protocol version metadata mismatch: %#v", metadata)
	}
	if metadata["acceptsDispatchAck"] != true {
		t.Fatalf("dispatch ack metadata mismatch: %#v", metadata)
	}
	if metadata["acceptsHeartbeat"] != true {
		t.Fatalf("heartbeat metadata mismatch: %#v", metadata)
	}
}

func TestSendWithProgressOutcomeReadsDispatchAckBeforeFinalResponse(t *testing.T) {
	if runtime.GOOS == "windows" {
		t.Skip("TCP endpoint injection is only used by this non-Windows client test")
	}

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

		if _, err := Read(bufio.NewReader(conn)); err != nil {
			serverErr <- err
			return
		}

		accepted := []byte(`{"jsonrpc":"2.0","result":{"accepted":true},"uloop":{"phase":"accepted"},"id":1}`)
		if err := Write(conn, accepted); err != nil {
			serverErr <- err
			return
		}

		final := []byte(`{"jsonrpc":"2.0","result":{"ok":true},"id":1}`)
		if err := Write(conn, final); err != nil {
			serverErr <- err
			return
		}
	}()

	connection := Connection{
		Endpoint: Endpoint{
			Network: "tcp",
			Address: listener.Addr().String(),
		},
		ProjectRoot: "/tmp/MyProject",
	}
	client := NewClient(connection, "3.0.0-beta.6")
	outcome, err := client.SendWithProgressOutcome(context.Background(), "get-logs", map[string]any{}, nil)
	if err != nil {
		t.Fatalf("Send failed: %v", err)
	}
	if outcome.RequestAccepted != true {
		t.Fatalf("request accepted flag mismatch: %#v", outcome)
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

func TestSendWithProgressOutcomeWaitsForFinalResponseAfterDispatchAckWithoutAcceptTimeout(t *testing.T) {
	if runtime.GOOS == "windows" {
		t.Skip("TCP endpoint injection is only used by this non-Windows client test")
	}

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

		if _, err := Read(bufio.NewReader(conn)); err != nil {
			serverErr <- err
			return
		}

		accepted := []byte(`{"jsonrpc":"2.0","result":{"accepted":true},"uloop":{"phase":"accepted"},"id":1}`)
		if err := Write(conn, accepted); err != nil {
			serverErr <- err
			return
		}

		time.Sleep(750 * time.Millisecond)

		final := []byte(`{"jsonrpc":"2.0","result":{"ok":true},"id":1}`)
		if err := Write(conn, final); err != nil {
			serverErr <- err
			return
		}
	}()

	connection := Connection{
		Endpoint: Endpoint{
			Network: "tcp",
			Address: listener.Addr().String(),
		},
		ProjectRoot: "/tmp/MyProject",
	}
	client := NewClient(connection, "3.0.0-beta.6")
	// The accept timeout must be wide enough that the accepted ack always arrives
	// inside it even on a loaded CI machine, while the server delays the final
	// response well past it to prove accepted requests outlive the accept timeout.
	client.acceptTimeout = 250 * time.Millisecond

	outcome, err := client.SendWithProgressOutcome(context.Background(), "execute-dynamic-code", map[string]any{}, nil)
	if err != nil {
		t.Fatalf("Send failed: %v", err)
	}
	if outcome.RequestAccepted != true {
		t.Fatalf("request accepted flag mismatch: %#v", outcome)
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

func TestSendWithProgressOutcomeTimesOutFinalResponseAfterDispatchAck(t *testing.T) {
	if runtime.GOOS == "windows" {
		t.Skip("TCP endpoint injection is only used by this non-Windows client test")
	}

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

		if _, err := Read(bufio.NewReader(conn)); err != nil {
			serverErr <- err
			return
		}

		accepted := []byte(`{"jsonrpc":"2.0","result":{"accepted":true},"uloop":{"phase":"accepted"},"id":1}`)
		if err := Write(conn, accepted); err != nil {
			serverErr <- err
			return
		}

		time.Sleep(250 * time.Millisecond)
	}()

	connection := Connection{
		Endpoint: Endpoint{
			Network: "tcp",
			Address: listener.Addr().String(),
		},
		ProjectRoot: "/tmp/MyProject",
	}
	client := NewClient(connection, "3.0.0-beta.6")
	client.acceptTimeout = time.Second
	client.responseTimeout = 50 * time.Millisecond

	outcome, err := client.SendWithProgressOutcome(context.Background(), "execute-dynamic-code", map[string]any{}, nil)
	if err == nil || !strings.Contains(err.Error(), "i/o timeout") {
		t.Fatalf("expected final response timeout, got %v", err)
	}
	if outcome.RequestAccepted != true {
		t.Fatalf("request accepted flag mismatch: %#v", outcome)
	}

	select {
	case err := <-serverErr:
		t.Fatalf("server failed: %v", err)
	default:
	}
}

func TestSendWithProgressOutcomeStillHonorsParentCancellationAfterDispatchAck(t *testing.T) {
	if runtime.GOOS == "windows" {
		t.Skip("TCP endpoint injection is only used by this non-Windows client test")
	}

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

		if _, err := Read(bufio.NewReader(conn)); err != nil {
			serverErr <- err
			return
		}

		accepted := []byte(`{"jsonrpc":"2.0","result":{"accepted":true},"uloop":{"phase":"accepted"},"id":1}`)
		if err := Write(conn, accepted); err != nil {
			serverErr <- err
			return
		}

		time.Sleep(250 * time.Millisecond)
	}()

	connection := Connection{
		Endpoint: Endpoint{
			Network: "tcp",
			Address: listener.Addr().String(),
		},
		ProjectRoot: "/tmp/MyProject",
	}
	client := NewClient(connection, "3.0.0-beta.6")
	client.acceptTimeout = time.Second

	ctx, cancel := context.WithTimeout(context.Background(), 50*time.Millisecond)
	defer cancel()

	outcome, err := client.SendWithProgressOutcome(ctx, "execute-dynamic-code", map[string]any{}, nil)
	if !errors.Is(err, context.DeadlineExceeded) {
		t.Fatalf("expected parent context deadline, got %v", err)
	}
	if outcome.RequestAccepted != true {
		t.Fatalf("request accepted flag mismatch: %#v", outcome)
	}

	select {
	case err := <-serverErr:
		t.Fatalf("server failed: %v", err)
	default:
	}
}
