package cli

import (
	"bufio"
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	"net"
	"strings"
	"testing"
	"time"

	"github.com/hatayama/unity-cli-loop/cli/internal/unityipc"
)

// Verifies wait-for-debug-break succeeds only after Unity transitions from running to paused.
func TestRunWaitForDebugBreakCompletesAfterPauseTransition(t *testing.T) {
	states := []playModeStateResponse{
		{IsPlaying: true, IsPaused: false, Message: "Unity Editor is playing and not paused."},
		{IsPlaying: true, IsPaused: true, Message: "Unity Editor is paused."},
	}
	replaceQueryPlayModeState(t, func(context.Context, unityipc.Connection) (playModeStateResponse, error) {
		if len(states) == 0 {
			t.Fatal("unexpected extra play mode state query")
		}
		state := states[0]
		states = states[1:]
		return state, nil
	})

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runWaitForDebugBreak(
		context.Background(),
		unityipc.Connection{ProjectRoot: t.TempDir()},
		[]string{},
		&stdout,
		&stderr)

	if code != 0 {
		t.Fatalf("wait-for-debug-break failed with %d: %s", code, stderr.String())
	}
	response := waitForDebugBreakResponse{}
	if err := json.Unmarshal(stdout.Bytes(), &response); err != nil {
		t.Fatalf("failed to decode stdout: %v\n%s", err, stdout.String())
	}
	if !response.Success || !response.IsPaused {
		t.Fatalf("response state mismatch: %#v", response)
	}
	if response.Message != "Debug break observed." {
		t.Fatalf("response message mismatch: %s", response.Message)
	}
}

// Verifies wait-for-debug-break rejects an already-paused Editor to avoid reporting a stale break.
func TestRunWaitForDebugBreakFailsWhenUnityAlreadyPaused(t *testing.T) {
	replaceQueryPlayModeState(t, func(context.Context, unityipc.Connection) (playModeStateResponse, error) {
		return playModeStateResponse{IsPlaying: true, IsPaused: true}, nil
	})

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runWaitForDebugBreak(
		context.Background(),
		unityipc.Connection{ProjectRoot: t.TempDir()},
		[]string{},
		&stdout,
		&stderr)

	if code != 1 {
		t.Fatalf("expected already-paused failure, got %d with stdout %s", code, stdout.String())
	}
	if !strings.Contains(stderr.String(), errorCodeDebugBreakAlreadyPaused) {
		t.Fatalf("already-paused error missing from stderr: %s", stderr.String())
	}
}

// Verifies Debug.Break polling keeps the last state when the caller cancels the wait.
func TestWaitForDebugBreakReturnsLastStateWhenCanceled(t *testing.T) {
	originalPollInterval := waitForDebugBreakPollInterval
	waitForDebugBreakPollInterval = time.Millisecond
	t.Cleanup(func() {
		waitForDebugBreakPollInterval = originalPollInterval
	})
	replaceQueryPlayModeState(t, func(context.Context, unityipc.Connection) (playModeStateResponse, error) {
		return playModeStateResponse{IsPlaying: true, IsPaused: false}, nil
	})
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Millisecond)
	defer cancel()

	state, err := waitForDebugBreak(
		ctx,
		unityipc.Connection{ProjectRoot: t.TempDir()},
		playModeStateResponse{IsPlaying: true, IsPaused: false})
	if err == nil {
		t.Fatalf("wait should be canceled: %#v", state)
	}
	if state.IsPaused {
		t.Fatalf("last state should remain unpaused: %#v", state)
	}
}

// Verifies the Unity probe uses the internal get-play-mode-state bridge command.
func TestQueryPlayModeStateFromUnityUsesInternalBridgeCommand(t *testing.T) {
	listener, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatalf("failed to listen: %v", err)
	}
	defer func() {
		_ = listener.Close()
	}()

	requests := make(chan string, 1)
	serverErr := make(chan error, 1)
	go servePlayModeStateResponse(
		listener,
		requests,
		serverErr,
		`{"IsPlaying":true,"IsPaused":true,"Message":"Unity Editor is paused."}`)

	connection := unityipc.Connection{
		Endpoint: unityipc.Endpoint{
			Network: "tcp",
			Address: listener.Addr().String(),
		},
		ProjectRoot: t.TempDir(),
	}

	response, err := queryPlayModeStateFromUnity(context.Background(), connection)
	if err != nil {
		t.Fatalf("queryPlayModeStateFromUnity failed: %v", err)
	}
	if !response.IsPaused {
		t.Fatalf("response state mismatch: %#v", response)
	}
	if method := readPlayModeStateMethod(t, requests); method != playModeStateCommandName {
		t.Fatalf("method mismatch: %s", method)
	}

	select {
	case err := <-serverErr:
		t.Fatalf("server failed: %v", err)
	default:
	}
}

// Verifies wait-for-debug-break accepts no command-specific options.
func TestParseWaitForDebugBreakArgsAcceptsNoOptions(t *testing.T) {
	if err := parseWaitForDebugBreakArgs([]string{}); err != nil {
		t.Fatalf("parseWaitForDebugBreakArgs failed: %v", err)
	}
}

// Verifies wait-for-debug-break rejects extra options before contacting Unity.
func TestParseWaitForDebugBreakArgsRejectsOptions(t *testing.T) {
	if err := parseWaitForDebugBreakArgs([]string{"--unknown"}); err == nil {
		t.Fatal("expected option validation error")
	}
}

func replaceQueryPlayModeState(
	t *testing.T,
	replacement func(context.Context, unityipc.Connection) (playModeStateResponse, error),
) {
	t.Helper()
	original := queryPlayModeState
	queryPlayModeState = replacement
	t.Cleanup(func() {
		queryPlayModeState = original
	})
}

func servePlayModeStateResponse(
	listener net.Listener,
	requests chan<- string,
	serverErr chan<- error,
	result string,
) {
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
		Method string `json:"method"`
	}{}
	if err := json.Unmarshal(payload, &request); err != nil {
		_ = conn.Close()
		serverErr <- err
		return
	}
	requests <- request.Method

	response := []byte(fmt.Sprintf(`{"jsonrpc":"2.0","result":%s,"id":1}`, result))
	if err := unityipc.Write(conn, response); err != nil {
		_ = conn.Close()
		serverErr <- err
		return
	}
	_ = conn.Close()
}

func readPlayModeStateMethod(t *testing.T, requests <-chan string) string {
	t.Helper()
	select {
	case method := <-requests:
		return method
	case <-time.After(time.Second):
		t.Fatal("timed out waiting for request")
		return ""
	}
}
