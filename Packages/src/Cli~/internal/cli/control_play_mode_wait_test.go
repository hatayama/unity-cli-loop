package cli

import (
	"bufio"
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	"net"
	"testing"
	"time"

	"github.com/hatayama/unity-cli-loop/Packages/src/Cli/internal/unityipc"
)

// Verifies that control-play-mode polls a status-only request before returning stale PlayMode state.
func TestRunControlPlayModeWithStateWaitPollsStatusAfterStaleInitialResponse(t *testing.T) {
	originalPoll := controlPlayModeStatePoll
	controlPlayModeStatePoll = time.Millisecond
	t.Cleanup(func() {
		controlPlayModeStatePoll = originalPoll
	})

	listener, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatalf("failed to listen: %v", err)
	}
	defer func() {
		_ = listener.Close()
	}()

	requests := make(chan map[string]any, 2)
	serverErr := make(chan error, 1)
	go serveControlPlayModeResponses(
		listener,
		requests,
		serverErr,
		[]string{
			`{"IsPlaying":false,"IsPaused":false,"Message":"Play mode started"}`,
			`{"IsPlaying":true,"IsPaused":false,"Message":"Play mode status"}`,
		})

	connection := unityipc.Connection{
		Endpoint: unityipc.Endpoint{
			Network: "tcp",
			Address: listener.Addr().String(),
		},
		ProjectRoot: t.TempDir(),
	}
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := runControlPlayModeWithStateWait(
		context.Background(),
		connection,
		map[string]any{
			controlPlayModeActionParam:  "Play",
			controlPlayModeTimeoutParam: 1,
		},
		&stdout,
		&stderr)

	if code != 0 {
		t.Fatalf("runControlPlayModeWithStateWait failed with %d: %s", code, stderr.String())
	}

	response := controlPlayModeResponse{}
	if err := json.Unmarshal(stdout.Bytes(), &response); err != nil {
		t.Fatalf("failed to decode stdout: %v\n%s", err, stdout.String())
	}
	if !response.IsPlaying || response.IsPaused {
		t.Fatalf("response state mismatch: %#v", response)
	}
	if response.Message != "Play mode started" {
		t.Fatalf("response message mismatch: %s", response.Message)
	}

	firstRequest := readControlPlayModeRequest(t, requests)
	if _, ok := firstRequest[controlPlayModeStatusOnlyParam]; ok {
		t.Fatalf("initial request should not be status-only: %#v", firstRequest)
	}
	secondRequest := readControlPlayModeRequest(t, requests)
	if secondRequest[controlPlayModeStatusOnlyParam] != true {
		t.Fatalf("status request mismatch: %#v", secondRequest)
	}

	select {
	case err := <-serverErr:
		t.Fatalf("server failed: %v", err)
	default:
	}
}

// Verifies that dispatched PlayMode disconnects are treated as post-reload waits.
func TestShouldWaitForControlPlayModeDisconnectWaitsAfterDispatchedTransportLoss(t *testing.T) {
	outcome := unityipc.UnitySendOutcome{RequestDispatched: true}

	if !shouldWaitForControlPlayModeDisconnect(fmt.Errorf("EOF"), outcome) {
		t.Fatal("dispatched transport loss should wait for play mode state")
	}
	if shouldWaitForControlPlayModeDisconnect(fmt.Errorf("EOF"), unityipc.UnitySendOutcome{}) {
		t.Fatal("undispatched transport loss should not wait")
	}
}

// Verifies that a timed-out PlayMode transition fails the command instead of reporting ready state.
func TestRunControlPlayModeWithStateWaitFailsWhenStateNeverMatches(t *testing.T) {
	originalPoll := controlPlayModeStatePoll
	controlPlayModeStatePoll = 50 * time.Millisecond
	t.Cleanup(func() {
		controlPlayModeStatePoll = originalPoll
	})

	listener, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatalf("failed to listen: %v", err)
	}
	defer func() {
		_ = listener.Close()
	}()

	serverErr := make(chan error, 1)
	go serveRepeatedControlPlayModeResponse(
		listener,
		serverErr,
		`{"IsPlaying":false,"IsPaused":false,"Message":"Play mode status"}`)

	connection := unityipc.Connection{
		Endpoint: unityipc.Endpoint{
			Network: "tcp",
			Address: listener.Addr().String(),
		},
		ProjectRoot: t.TempDir(),
	}
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := runControlPlayModeWithStateWait(
		context.Background(),
		connection,
		map[string]any{
			controlPlayModeActionParam:  "Play",
			controlPlayModeTimeoutParam: 1,
		},
		&stdout,
		&stderr)

	if code != 1 {
		t.Fatalf("expected timeout failure, got %d with stdout %s stderr %s", code, stdout.String(), stderr.String())
	}
	if !bytes.Contains(stderr.Bytes(), []byte(errorCodeControlPlayModeWaitTimeout)) {
		t.Fatalf("timeout error missing from stderr: %s", stderr.String())
	}

	select {
	case err := <-serverErr:
		t.Fatalf("server failed: %v", err)
	default:
	}
}

// Verifies that live Unity tool caches using number schemas still drive integer wait budgets.
func TestControlPlayModeTimeoutSecondsAcceptsFloatSchemaValue(t *testing.T) {
	params := map[string]any{controlPlayModeTimeoutParam: 12.0}

	if controlPlayModeTimeoutSeconds(params) != 12 {
		t.Fatalf("timeout mismatch: %d", controlPlayModeTimeoutSeconds(params))
	}
}

func serveRepeatedControlPlayModeResponse(
	listener net.Listener,
	serverErr chan<- error,
	result string,
) {
	for {
		conn, err := listener.Accept()
		if err != nil {
			return
		}

		if _, err := unityipc.Read(bufio.NewReader(conn)); err != nil {
			_ = conn.Close()
			serverErr <- err
			return
		}

		response := []byte(fmt.Sprintf(`{"jsonrpc":"2.0","result":%s,"id":1}`, result))
		if err := unityipc.Write(conn, response); err != nil {
			_ = conn.Close()
			serverErr <- err
			return
		}
		_ = conn.Close()
	}
}

func serveControlPlayModeResponses(
	listener net.Listener,
	requests chan<- map[string]any,
	serverErr chan<- error,
	results []string,
) {
	for _, result := range results {
		conn, err := listener.Accept()
		if err != nil {
			serverErr <- err
			return
		}

		payload, err := unityipc.Read(bufio.NewReader(conn))
		if err != nil {
			_ = conn.Close()
			serverErr <- err
			return
		}

		request := struct {
			Method string         `json:"method"`
			Params map[string]any `json:"params"`
		}{}
		if err := json.Unmarshal(payload, &request); err != nil {
			_ = conn.Close()
			serverErr <- err
			return
		}
		if request.Method != controlPlayModeCommandName {
			_ = conn.Close()
			serverErr <- fmt.Errorf("method mismatch: %s", request.Method)
			return
		}
		requests <- request.Params

		response := []byte(fmt.Sprintf(`{"jsonrpc":"2.0","result":%s,"id":1}`, result))
		if err := unityipc.Write(conn, response); err != nil {
			_ = conn.Close()
			serverErr <- err
			return
		}
		_ = conn.Close()
	}
}

func readControlPlayModeRequest(t *testing.T, requests <-chan map[string]any) map[string]any {
	t.Helper()
	select {
	case request := <-requests:
		return request
	case <-time.After(time.Second):
		t.Fatal("timed out waiting for request")
		return nil
	}
}
