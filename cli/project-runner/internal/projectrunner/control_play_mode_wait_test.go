package projectrunner

import (
	"bufio"
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"net"
	"testing"
	"time"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"

	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

// Verifies that control-play-mode polls a status-only request before returning stale PlayMode state.
func TestRunControlPlayModeWithStateWaitPollsStatusAfterStaleInitialResponse(t *testing.T) {
	originalPoll := controlPlayModeStatePoll
	controlPlayModeStatePoll = time.Millisecond
	t.Cleanup(func() {
		controlPlayModeStatePoll = originalPoll
	})

	listener := newLoopbackIpcListener(t)

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
			Network: listener.Addr().Network(),
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

// Verifies that state polling preserves the action result fields from the initial command response.
func TestRunControlPlayModeWithStateWaitPreservesStopChangeFields(t *testing.T) {
	originalPoll := controlPlayModeStatePoll
	controlPlayModeStatePoll = time.Millisecond
	t.Cleanup(func() {
		controlPlayModeStatePoll = originalPoll
	})

	listener := newLoopbackIpcListener(t)

	serverErr := make(chan error, 1)
	go serveControlPlayModeResponses(
		listener,
		make(chan map[string]any, 2),
		serverErr,
		[]string{
			`{"IsPlaying":true,"IsPaused":false,"Changed":true,"WasAlreadyStopped":false,"Message":"Play mode stopped"}`,
			`{"IsPlaying":false,"IsPaused":false,"Message":"Play mode status"}`,
		})

	connection := unityipc.Connection{
		Endpoint: unityipc.Endpoint{
			Network: listener.Addr().Network(),
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
			controlPlayModeActionParam:  "Stop",
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
	if !response.Changed {
		t.Fatalf("response should preserve Changed=true: %#v", response)
	}
	if response.WasAlreadyStopped {
		t.Fatalf("response should preserve WasAlreadyStopped=false: %#v", response)
	}
	if response.Message != "Play mode stopped" {
		t.Fatalf("response message mismatch: %s", response.Message)
	}

	select {
	case err := <-serverErr:
		t.Fatalf("server failed: %v", err)
	default:
	}
}

// Verifies that ResumedFromPause and Warning from the initial Play response survive the
// wait-and-remarshal path instead of being dropped by the Go-side response struct.
func TestRunControlPlayModeWithStateWaitPreservesResumeAndWarningFields(t *testing.T) {
	originalPoll := controlPlayModeStatePoll
	controlPlayModeStatePoll = time.Millisecond
	t.Cleanup(func() {
		controlPlayModeStatePoll = originalPoll
	})

	listener := newLoopbackIpcListener(t)

	serverErr := make(chan error, 1)
	// ResumedFromPause=true paired with a non-empty Warning never happens in production
	// (Warning is only set on a fresh Play start); the sentinel text here only exists to
	// prove the JSON round trip preserves a non-zero-value Warning string, not to assert
	// a real response shape.
	go serveControlPlayModeResponses(
		listener,
		make(chan map[string]any, 2),
		serverErr,
		[]string{
			`{"IsPlaying":true,"IsPaused":true,"Changed":true,"ResumedFromPause":true,"Warning":"warning sentinel from initial response","Message":"Play mode resumed"}`,
			`{"IsPlaying":true,"IsPaused":false,"Message":"Play mode status"}`,
		})

	connection := unityipc.Connection{
		Endpoint: unityipc.Endpoint{
			Network: listener.Addr().Network(),
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
	if !response.ResumedFromPause {
		t.Fatalf("response should preserve ResumedFromPause=true: %#v", response)
	}
	if response.Warning != "warning sentinel from initial response" {
		t.Fatalf("response should preserve non-empty Warning: %#v", response)
	}
	if response.Message != "Play mode resumed" {
		t.Fatalf("response message mismatch: %s", response.Message)
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

	listener := newLoopbackIpcListener(t)

	serverErr := make(chan error, 1)
	go serveRepeatedControlPlayModeResponse(
		listener,
		serverErr,
		`{"IsPlaying":false,"IsPaused":false,"Message":"Play mode status"}`)

	connection := unityipc.Connection{
		Endpoint: unityipc.Endpoint{
			Network: listener.Addr().Network(),
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
	if !bytes.Contains(stderr.Bytes(), []byte(clierrors.ErrorCodeControlPlayModeWaitTimeout)) {
		t.Fatalf("timeout error missing from stderr: %s", stderr.String())
	}

	select {
	case err := <-serverErr:
		t.Fatalf("server failed: %v", err)
	default:
	}
}

// Verifies that compiler-error PlayMode blocks fail immediately instead of waiting for state polling.
func TestRunControlPlayModeWithStateWaitFailsImmediatelyWhenCompileErrorsBlockPlay(t *testing.T) {
	originalPoll := controlPlayModeStatePoll
	controlPlayModeStatePoll = time.Millisecond
	t.Cleanup(func() {
		controlPlayModeStatePoll = originalPoll
	})

	listener := newLoopbackIpcListener(t)

	requests := make(chan map[string]any, 2)
	serverErr := make(chan error, 1)
	go serveControlPlayModeResponses(
		listener,
		requests,
		serverErr,
		[]string{
			`{"IsPlaying":false,"IsPaused":false,"BlockedByCompileErrors":true,"CompileErrorCount":1,"CompileErrors":[{"Message":"CS1002: ; expected","File":"Assets/Scripts/Sample.cs","Line":12}],"Message":"Play mode could not start because Unity has compiler errors."}`,
		})

	connection := unityipc.Connection{
		Endpoint: unityipc.Endpoint{
			Network: listener.Addr().Network(),
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
		t.Fatalf("expected compile error failure, got %d with stdout %s stderr %s", code, stdout.String(), stderr.String())
	}

	var envelope clierrors.CLIErrorEnvelope
	if err := json.Unmarshal(stderr.Bytes(), &envelope); err != nil {
		t.Fatalf("stderr is not valid JSON: %v\n%s", err, stderr.String())
	}
	if envelope.Error.ErrorCode != clierrors.ErrorCodeControlPlayModeCompileErrors {
		t.Fatalf("error code mismatch: %#v", envelope.Error)
	}
	if envelope.Error.Details["CompileErrorCount"] != float64(1) {
		t.Fatalf("compile error count mismatch: %#v", envelope.Error.Details)
	}
	if !bytes.Contains(stderr.Bytes(), []byte("CS1002")) {
		t.Fatalf("compiler diagnostic missing from stderr: %s", stderr.String())
	}

	firstRequest := readControlPlayModeRequest(t, requests)
	if _, ok := firstRequest[controlPlayModeStatusOnlyParam]; ok {
		t.Fatalf("initial request should not be status-only: %#v", firstRequest)
	}
	select {
	case secondRequest := <-requests:
		t.Fatalf("blocked compile errors should not trigger status polling: %#v", secondRequest)
	case <-time.After(100 * time.Millisecond):
	}

	select {
	case err := <-serverErr:
		t.Fatalf("server failed: %v", err)
	default:
	}
}

// Verifies that compiler-error status polling fails immediately instead of waiting for the PlayMode timeout.
func TestRunControlPlayModeWithStateWaitFailsWhenCompileErrorsAppearDuringPolling(t *testing.T) {
	originalPoll := controlPlayModeStatePoll
	controlPlayModeStatePoll = time.Millisecond
	t.Cleanup(func() {
		controlPlayModeStatePoll = originalPoll
	})

	listener := newLoopbackIpcListener(t)

	requests := make(chan map[string]any, 3)
	serverErr := make(chan error, 1)
	go serveControlPlayModeResponses(
		listener,
		requests,
		serverErr,
		[]string{
			`{"IsPlaying":false,"IsPaused":false,"Message":"Play mode started"}`,
			`{"IsPlaying":false,"IsPaused":false,"BlockedByCompileErrors":true,"CompileErrorCount":1,"CompileErrors":[{"Message":"CS1525: invalid expression","File":"Assets/Scripts/Sample.cs","Line":3}],"Message":"Play mode could not start because Unity has compiler errors."}`,
		})

	connection := unityipc.Connection{
		Endpoint: unityipc.Endpoint{
			Network: listener.Addr().Network(),
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
		t.Fatalf("expected compile error failure, got %d with stdout %s stderr %s", code, stdout.String(), stderr.String())
	}

	var envelope clierrors.CLIErrorEnvelope
	if err := json.Unmarshal(stderr.Bytes(), &envelope); err != nil {
		t.Fatalf("stderr is not valid JSON: %v\n%s", err, stderr.String())
	}
	if envelope.Error.ErrorCode != clierrors.ErrorCodeControlPlayModeCompileErrors {
		t.Fatalf("error code mismatch: %#v", envelope.Error)
	}
	if !bytes.Contains(stderr.Bytes(), []byte("CS1525")) {
		t.Fatalf("compiler diagnostic missing from stderr: %s", stderr.String())
	}

	firstRequest := readControlPlayModeRequest(t, requests)
	if _, ok := firstRequest[controlPlayModeStatusOnlyParam]; ok {
		t.Fatalf("initial request should not be status-only: %#v", firstRequest)
	}
	secondRequest := readControlPlayModeRequest(t, requests)
	if secondRequest[controlPlayModeStatusOnlyParam] != true {
		t.Fatalf("status request mismatch: %#v", secondRequest)
	}
	if secondRequest[controlPlayModeActionParam] != "Play" {
		t.Fatalf("status request action mismatch: %#v", secondRequest)
	}
	select {
	case thirdRequest := <-requests:
		t.Fatalf("blocked compile errors should stop status polling: %#v", thirdRequest)
	case <-time.After(100 * time.Millisecond):
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
			// Why tolerated: when the client's wait deadline expires it closes a
			// freshly dialed poll connection without sending a request. TCP hides
			// this because the request is already buffered in the socket, but a
			// named pipe surfaces it as EOF here; either way it is client-side
			// cancellation, not a server failure.
			if isClientAbandonedConnectionError(err) {
				continue
			}
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

// isClientAbandonedConnectionError reports whether a fixture-server read
// failed only because the client hung up before sending a request.
func isClientAbandonedConnectionError(err error) bool {
	return errors.Is(err, io.EOF) || errors.Is(err, io.ErrUnexpectedEOF) || errors.Is(err, net.ErrClosed)
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
