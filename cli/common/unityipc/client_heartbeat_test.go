package unityipc

import (
	"bufio"
	"context"
	"encoding/json"
	"errors"
	"net"
	"os"
	"runtime"
	"strings"
	"testing"
	"time"

	"github.com/hatayama/unity-cli-loop/common/progress"
)

// Test support server that acks with heartbeat negotiation and then runs the given
// frame script (heartbeats and/or a final response) over one accepted connection.
func startHeartbeatTestServer(t *testing.T, serve func(conn net.Conn)) Connection {
	t.Helper()
	if runtime.GOOS == "windows" {
		t.Skip("TCP endpoint injection is only used by this non-Windows client test")
	}

	listener, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatalf("failed to listen: %v", err)
	}
	t.Cleanup(func() {
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
		if _, readErr := Read(bufio.NewReader(conn)); readErr != nil {
			return
		}
		serve(conn)
	}()

	return Connection{
		Endpoint: Endpoint{
			Network: "tcp",
			Address: listener.Addr().String(),
		},
		ProjectRoot: t.TempDir(),
	}
}

func writeFrame(t *testing.T, conn net.Conn, payload string) {
	t.Helper()
	if err := Write(conn, []byte(payload)); err != nil {
		t.Errorf("failed to write frame: %v", err)
	}
}

const heartbeatAck = `{"jsonrpc":"2.0","id":1,"result":{"accepted":true},"uloop":{"phase":"accepted","heartbeatIntervalSeconds":1}}`

// Verifies that the request advertises heartbeat support so the server can negotiate.
func TestSendAdvertisesHeartbeatSupport(t *testing.T) {
	if runtime.GOOS == "windows" {
		t.Skip("TCP endpoint injection is only used by this non-Windows client test")
	}

	captured := make(chan map[string]any, 1)
	listener, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatalf("failed to listen: %v", err)
	}
	defer func() {
		_ = listener.Close()
	}()
	go func() {
		conn, acceptErr := listener.Accept()
		if acceptErr != nil {
			return
		}
		defer func() {
			_ = conn.Close()
		}()
		payload, readErr := Read(bufio.NewReader(conn))
		if readErr != nil {
			return
		}
		var request map[string]any
		if json.Unmarshal(payload, &request) == nil {
			captured <- request
		}
		_ = Write(conn, []byte(`{"jsonrpc":"2.0","id":1,"result":{"ok":true}}`))
	}()
	connection := Connection{
		Endpoint:    Endpoint{Network: "tcp", Address: listener.Addr().String()},
		ProjectRoot: t.TempDir(),
	}

	client := NewClient(connection, "9.9.9")
	if _, err := client.Send(context.Background(), "get-version", map[string]any{}); err != nil {
		t.Fatalf("send failed: %v", err)
	}

	request := <-captured
	metadata, ok := request["uloop"].(map[string]any)
	if !ok {
		t.Fatalf("request misses uloop metadata: %#v", request)
	}
	if metadata["acceptsHeartbeat"] != true {
		t.Fatalf("acceptsHeartbeat not advertised: %#v", metadata)
	}
}

// Verifies that heartbeat frames slide the silence deadline so a slow final response
// arrives even though the initial silence window alone would have expired.
func TestSendKeepsWaitingWhileHeartbeatsArrive(t *testing.T) {
	heartbeat := `{"jsonrpc":"2.0","id":1,"result":{"alive":true},"uloop":{"phase":"heartbeat","mainThreadStallSeconds":0}}`
	connection := startHeartbeatTestServer(t, func(conn net.Conn) {
		writeFrame(t, conn, heartbeatAck)
		for i := 0; i < 6; i++ {
			time.Sleep(50 * time.Millisecond)
			writeFrame(t, conn, heartbeat)
		}
		writeFrame(t, conn, `{"jsonrpc":"2.0","id":1,"result":{"ok":true}}`)
	})

	client := NewClient(connection, "9.9.9", withHeartbeatSilenceOverrideForTest(200*time.Millisecond))

	outcome, err := client.SendWithProgressOutcome(context.Background(), "run-tests", map[string]any{}, nil)
	if err != nil {
		t.Fatalf("expected success with heartbeats sliding the deadline, got %v", err)
	}
	if !outcome.RequestAccepted {
		t.Fatal("expected accepted outcome")
	}
	if len(outcome.Result) == 0 {
		t.Fatal("expected final result")
	}
}

// Verifies heartbeat main-thread stall reports reach the typed handler before the final response.
func TestSendReportsMainThreadStallToHandler(t *testing.T) {
	stalledHeartbeat := `{"jsonrpc":"2.0","id":1,"result":{"alive":true},"uloop":{"phase":"heartbeat","mainThreadStallSeconds":31}}`
	connection := startHeartbeatTestServer(t, func(conn net.Conn) {
		writeFrame(t, conn, heartbeatAck)
		writeFrame(t, conn, stalledHeartbeat)
		writeFrame(t, conn, `{"jsonrpc":"2.0","id":1,"result":{"ok":true}}`)
	})

	stallReports := []float64{}
	client := NewClient(
		connection,
		"9.9.9",
		WithMainThreadStallHandler(func(stallSeconds float64) {
			stallReports = append(stallReports, stallSeconds)
		}),
		withHeartbeatSilenceOverrideForTest(5*time.Second),
	)

	outcome, err := client.SendWithProgressOutcome(context.Background(), "run-tests", map[string]any{}, nil)
	if err != nil {
		t.Fatalf("expected success after stall heartbeat, got %v", err)
	}
	if len(outcome.Result) == 0 {
		t.Fatal("expected final result")
	}
	if len(stallReports) != 1 || stallReports[0] != 31 {
		t.Fatalf("stall reports mismatch: %#v", stallReports)
	}
}

// Verifies heartbeat stall progress points users at modal dialogs or long editor work.
func TestSendReportsMainThreadStallProgressWithModalHint(t *testing.T) {
	stalledHeartbeat := `{"jsonrpc":"2.0","id":1,"result":{"alive":true},"uloop":{"phase":"heartbeat","mainThreadStallSeconds":31}}`
	connection := startHeartbeatTestServer(t, func(conn net.Conn) {
		writeFrame(t, conn, heartbeatAck)
		writeFrame(t, conn, stalledHeartbeat)
		writeFrame(t, conn, `{"jsonrpc":"2.0","id":1,"result":{"ok":true}}`)
	})

	progressMessages := []string{}
	client := NewClient(connection, "9.9.9", withHeartbeatSilenceOverrideForTest(5*time.Second))

	_, err := client.SendWithProgressOutcome(
		context.Background(),
		"run-tests",
		map[string]any{},
		func(event progress.Event) {
			progressMessages = append(progressMessages, event.Message)
		},
	)
	if err != nil {
		t.Fatalf("expected success after stall heartbeat, got %v", err)
	}

	joinedMessages := strings.Join(progressMessages, "\n")
	if !strings.Contains(joinedMessages, "modal") {
		t.Fatalf("progress should mention modal: %#v", progressMessages)
	}
	if !strings.Contains(joinedMessages, "long operation") {
		t.Fatalf("progress should mention long operation: %#v", progressMessages)
	}
}

// Verifies that a negotiated connection fails with a heartbeat-silence diagnosis when
// frames stop arriving, instead of waiting for the 30-minute absolute deadline.
func TestSendFailsWithDiagnosisWhenHeartbeatsStop(t *testing.T) {
	heartbeat := `{"jsonrpc":"2.0","id":1,"result":{"alive":true},"uloop":{"phase":"heartbeat","mainThreadStallSeconds":0}}`
	done := make(chan struct{})
	connection := startHeartbeatTestServer(t, func(conn net.Conn) {
		writeFrame(t, conn, heartbeatAck)
		writeFrame(t, conn, heartbeat)
		<-done
	})
	defer close(done)

	client := NewClient(connection, "9.9.9", withHeartbeatSilenceOverrideForTest(150*time.Millisecond))

	startedAt := time.Now()
	_, err := client.SendWithProgressOutcome(context.Background(), "run-tests", map[string]any{}, nil)
	if err == nil {
		t.Fatal("expected heartbeat silence error")
	}
	if !strings.Contains(err.Error(), "heartbeat") {
		t.Fatalf("error misses heartbeat diagnosis: %v", err)
	}
	if !errors.Is(err, os.ErrDeadlineExceeded) && !os.IsTimeout(err) {
		t.Fatalf("silence error must stay classified as a timeout: %v", err)
	}
	if elapsed := time.Since(startedAt); elapsed > 5*time.Second {
		t.Fatalf("silence detection took too long: %v", elapsed)
	}
}

// Verifies that a heartbeat reporting a main-thread stall beyond the limit fails with
// an editor-unresponsive diagnosis while the connection is still alive.
func TestSendFailsWhenMainThreadStallExceedsLimit(t *testing.T) {
	stalledHeartbeat := `{"jsonrpc":"2.0","id":1,"result":{"alive":true},"uloop":{"phase":"heartbeat","mainThreadStallSeconds":3}}`
	done := make(chan struct{})
	connection := startHeartbeatTestServer(t, func(conn net.Conn) {
		writeFrame(t, conn, heartbeatAck)
		writeFrame(t, conn, stalledHeartbeat)
		<-done
	})
	defer close(done)

	client := NewClient(
		connection,
		"9.9.9",
		withHeartbeatSilenceOverrideForTest(5*time.Second),
		withMainThreadStallLimitForTest(2*time.Second),
	)

	_, err := client.SendWithProgressOutcome(context.Background(), "run-tests", map[string]any{}, nil)
	var unresponsiveErr *EditorUnresponsiveError
	if !errors.As(err, &unresponsiveErr) {
		t.Fatalf("expected EditorUnresponsiveError, got %v", err)
	}
	if !strings.Contains(err.Error(), "uloop launch -r") {
		t.Fatalf("error misses restart hint: %v", err)
	}
}

// Verifies that a self-induced-stall-tolerant client keeps waiting through a main-thread
// stall reported shortly after this request's own accept, instead of failing with
// EditorUnresponsiveError: a command that itself blocks the main thread synchronously
// (e.g. a long execute-dynamic-code snippet) must not be treated as a frozen editor.
func TestSendKeepsWaitingThroughSelfInducedMainThreadStall(t *testing.T) {
	stalledHeartbeat := `{"jsonrpc":"2.0","id":1,"result":{"alive":true},"uloop":{"phase":"heartbeat","mainThreadStallSeconds":3}}`
	connection := startHeartbeatTestServer(t, func(conn net.Conn) {
		writeFrame(t, conn, heartbeatAck)
		writeFrame(t, conn, stalledHeartbeat)
		writeFrame(t, conn, `{"jsonrpc":"2.0","id":1,"result":{"ok":true}}`)
	})

	client := NewClient(
		connection,
		"9.9.9",
		WithSelfInducedMainThreadStallTolerance(),
		withHeartbeatSilenceOverrideForTest(5*time.Second),
		withMainThreadStallLimitForTest(2*time.Second),
	)

	outcome, err := client.SendWithProgressOutcome(context.Background(), "execute-dynamic-code", map[string]any{}, nil)
	if err != nil {
		t.Fatalf("expected success through a self-induced stall, got %v", err)
	}
	if len(outcome.Result) == 0 {
		t.Fatal("expected final result")
	}
}

// Verifies that self-induced-stall tolerance does not mask a genuine freeze: a stall that
// already exceeds the time elapsed since this request's own accept (plus margin) predates
// the request and must still fail with EditorUnresponsiveError.
func TestSendFailsWhenMainThreadStallPredatesAcceptDespiteTolerance(t *testing.T) {
	// Why 40s: exceeds the elapsed-since-accept time (a few ms in this test) plus the
	// self-induced-stall margin (30s), so this stall could not have started after accept.
	stalledHeartbeat := `{"jsonrpc":"2.0","id":1,"result":{"alive":true},"uloop":{"phase":"heartbeat","mainThreadStallSeconds":40}}`
	done := make(chan struct{})
	connection := startHeartbeatTestServer(t, func(conn net.Conn) {
		writeFrame(t, conn, heartbeatAck)
		writeFrame(t, conn, stalledHeartbeat)
		<-done
	})
	defer close(done)

	client := NewClient(
		connection,
		"9.9.9",
		WithSelfInducedMainThreadStallTolerance(),
		withHeartbeatSilenceOverrideForTest(5*time.Second),
		withMainThreadStallLimitForTest(2*time.Second),
	)

	_, err := client.SendWithProgressOutcome(context.Background(), "execute-dynamic-code", map[string]any{}, nil)
	var unresponsiveErr *EditorUnresponsiveError
	if !errors.As(err, &unresponsiveErr) {
		t.Fatalf("expected EditorUnresponsiveError for a stall predating accept, got %v", err)
	}
}

// Verifies that an explicit response timeout stays an absolute deadline: heartbeats
// are skipped but must not extend it (compile relies on this to fall back to polling).
func TestSendKeepsExplicitResponseTimeoutDespiteHeartbeats(t *testing.T) {
	heartbeat := `{"jsonrpc":"2.0","id":1,"result":{"alive":true},"uloop":{"phase":"heartbeat","mainThreadStallSeconds":0}}`
	done := make(chan struct{})
	connection := startHeartbeatTestServer(t, func(conn net.Conn) {
		writeFrame(t, conn, heartbeatAck)
		for {
			select {
			case <-done:
				return
			case <-time.After(20 * time.Millisecond):
				// The client is expected to time out and close the connection while
				// this loop is still writing, so write failures end the loop silently
				// instead of failing the test through writeFrame's t.Errorf.
				if err := Write(conn, []byte(heartbeat)); err != nil {
					return
				}
			}
		}
	})
	defer close(done)

	client := NewClient(connection, "9.9.9").WithResponseTimeout(150 * time.Millisecond)

	startedAt := time.Now()
	_, err := client.SendWithProgressOutcome(context.Background(), "compile", map[string]any{}, nil)
	if err == nil {
		t.Fatal("expected timeout despite heartbeats")
	}
	if !os.IsTimeout(err) && !errors.Is(err, os.ErrDeadlineExceeded) {
		t.Fatalf("expected deadline error, got %v", err)
	}
	if elapsed := time.Since(startedAt); elapsed > 5*time.Second {
		t.Fatalf("explicit response timeout was extended by heartbeats: %v", elapsed)
	}
}
