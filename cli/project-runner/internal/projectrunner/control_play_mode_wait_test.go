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
	"strings"
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

// Verifies that an unsaved-changes save failure on the initial Play response fails immediately
// instead of falling through to the state-wait poll loop (Round-8: Untitled dirty scenes made
// this timeout after 180s even though Unity had already given up synchronously).
func TestRunControlPlayModeWithStateWaitFailsImmediatelyWhenUnsavedChangesBlockPlay(t *testing.T) {
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
			`{"IsPlaying":false,"IsPaused":false,"BlockedByUnsavedChanges":true,"Message":"Play mode could not start because unsaved scene or prefab changes could not be saved. Unsaved changes: Untitled scene"}`,
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
		t.Fatalf("expected unsaved-changes failure, got %d with stdout %s stderr %s", code, stdout.String(), stderr.String())
	}

	var envelope clierrors.CLIErrorEnvelope
	if err := json.Unmarshal(stderr.Bytes(), &envelope); err != nil {
		t.Fatalf("stderr is not valid JSON: %v\n%s", err, stderr.String())
	}
	if envelope.Error.ErrorCode != clierrors.ErrorCodeControlPlayModeUnsavedChanges {
		t.Fatalf("error code mismatch: %#v", envelope.Error)
	}
	if !bytes.Contains(stderr.Bytes(), []byte("Untitled scene")) {
		t.Fatalf("save-failure message missing from stderr: %s", stderr.String())
	}

	firstRequest := readControlPlayModeRequest(t, requests)
	if _, ok := firstRequest[controlPlayModeStatusOnlyParam]; ok {
		t.Fatalf("initial request should not be status-only: %#v", firstRequest)
	}
	select {
	case secondRequest := <-requests:
		t.Fatalf("blocked unsaved changes should not trigger status polling: %#v", secondRequest)
	case <-time.After(100 * time.Millisecond):
	}

	select {
	case err := <-serverErr:
		t.Fatalf("server failed: %v", err)
	default:
	}
}

// Verifies that Status is a pure read: it must never enter the PlayMode state-wait poll loop,
// unlike Play/Stop/Pause which wait for a state transition to complete.
func TestShouldWaitForControlPlayModeStateSkipsStatusAction(t *testing.T) {
	params := map[string]any{controlPlayModeActionParam: "Status"}

	if shouldWaitForControlPlayModeState(controlPlayModeCommandName, params) {
		t.Fatal("Status action should not enter the state-wait poll loop")
	}
}

// Verifies Resume is treated as a waitable Play alias for state polling.
func TestShouldWaitForControlPlayModeStateIncludesResumeAction(t *testing.T) {
	params := map[string]any{controlPlayModeActionParam: "Resume"}

	if !shouldWaitForControlPlayModeState(controlPlayModeCommandName, params) {
		t.Fatal("Resume action should enter the state-wait poll loop like Play")
	}
}

// Verifies Resume blocked by compile errors keeps the raw RequestedAction in error details.
func TestRunControlPlayModeWithStateWaitResumeCompileErrorsPreserveRequestedAction(t *testing.T) {
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
			controlPlayModeActionParam:  "Resume",
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
	if envelope.Error.Details["RequestedAction"] != "Resume" {
		t.Fatalf("RequestedAction must remain the raw user action Resume, got %#v", envelope.Error.Details["RequestedAction"])
	}

	firstRequest := readControlPlayModeRequest(t, requests)
	if firstRequest[controlPlayModeActionParam] != "Resume" {
		t.Fatalf("Unity-bound initial request must keep Resume: %#v", firstRequest)
	}
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

// Verifies Resume waits for the playing state the same way Play does after a stale initial response.
func TestRunControlPlayModeWithStateWaitPollsStatusForResumeLikePlay(t *testing.T) {
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
			`{"IsPlaying":false,"IsPaused":false,"Message":"Play mode resumed","ResumedFromPause":true,"Changed":true}`,
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
			controlPlayModeActionParam:  "Resume",
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
	if !response.ResumedFromPause {
		t.Fatalf("ResumedFromPause should be preserved from the initial Resume response: %#v", response)
	}

	firstRequest := readControlPlayModeRequest(t, requests)
	if firstRequest[controlPlayModeActionParam] != "Resume" {
		t.Fatalf("Unity-bound initial request must keep Resume: %#v", firstRequest)
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

// Verifies that the re-marshaled control-play-mode response keeps ActiveScenario and omits it when Unity sent none.
func TestDecodeControlPlayModeResponsePreservesActiveScenario(t *testing.T) {
	withScenario, err := decodeControlPlayModeResponse([]byte(`{"IsPlaying":true,"IsPaused":false,"Message":"Play mode status","Warning":"","ActiveScenario":"SampleScenario"}`))
	if err != nil {
		t.Fatalf("decode with scenario failed: %v", err)
	}
	withScenarioJSON, err := json.Marshal(withScenario)
	if err != nil {
		t.Fatalf("marshal with scenario failed: %v", err)
	}
	withScenarioFields := map[string]any{}
	if err := json.Unmarshal(withScenarioJSON, &withScenarioFields); err != nil {
		t.Fatalf("unmarshal with scenario failed: %v", err)
	}
	if withScenarioFields["ActiveScenario"] != "SampleScenario" {
		t.Fatalf("ActiveScenario mismatch: %s", withScenarioJSON)
	}

	withoutScenario, err := decodeControlPlayModeResponse([]byte(`{"IsPlaying":true,"IsPaused":false,"Message":"Play mode status","Warning":""}`))
	if err != nil {
		t.Fatalf("decode without scenario failed: %v", err)
	}
	withoutScenarioJSON, err := json.Marshal(withoutScenario)
	if err != nil {
		t.Fatalf("marshal without scenario failed: %v", err)
	}
	withoutScenarioFields := map[string]any{}
	if err := json.Unmarshal(withoutScenarioJSON, &withoutScenarioFields); err != nil {
		t.Fatalf("unmarshal without scenario failed: %v", err)
	}
	if _, exists := withoutScenarioFields["ActiveScenario"]; exists {
		t.Fatalf("ActiveScenario should be omitted: %s", withoutScenarioJSON)
	}
}

// Verifies the initial control-play-mode response short-circuits the wait: an undecodable result
// fails, an already-reached state returns at once, and a Unity error is reported without polling.
func TestRunControlPlayModeWithStateWaitHandlesInitialResponse(t *testing.T) {
	cases := []struct {
		name       string
		response   string
		wantCode   int
		wantStdout string
		wantStderr string
	}{
		{name: "undecodable result", response: `{"jsonrpc":"2.0","result":"text","id":1}`, wantCode: 1, wantStderr: "cannot unmarshal"},
		{name: "state already reached", response: `{"jsonrpc":"2.0","result":{"IsPlaying":true,"IsPaused":false,"Message":"already playing"},"id":1}`, wantStdout: "already playing"},
		{name: "Unity error", response: testUnityRPCFailureResponse, wantCode: 1, wantStderr: "tool exploded in Unity"},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			server := startFakeUnityServer(t, t.TempDir(), controlPlayModeCommandName, testCase.response)
			var stdout, stderr bytes.Buffer

			code := runControlPlayModeWithStateWait(
				context.Background(),
				server.connection,
				map[string]any{controlPlayModeActionParam: "Play", controlPlayModeTimeoutParam: 1},
				&stdout,
				&stderr,
			)

			if code != testCase.wantCode {
				t.Fatalf("exit code = %d, want %d; stderr=%s", code, testCase.wantCode, stderr.String())
			}
			server.receivedRequest(t)
			if !strings.Contains(stdout.String(), testCase.wantStdout) {
				t.Fatalf("stdout must contain %q:\n%s", testCase.wantStdout, stdout.String())
			}
			if !strings.Contains(stderr.String(), testCase.wantStderr) {
				t.Fatalf("stderr must contain %q:\n%s", testCase.wantStderr, stderr.String())
			}
		})
	}
}

// serveDroppedConnections reads each request and closes the connection without answering, so
// the client sees a transport disconnect after dispatch.
func serveDroppedConnections(listener net.Listener) {
	for {
		conn, err := listener.Accept()
		if err != nil {
			return
		}
		_, _ = unityipc.Read(bufio.NewReader(conn))
		_ = conn.Close()
	}
}

// Verifies a disconnect during the action request moves on to the state wait, and the caller's
// context ending during that wait is reported as a failure.
func TestRunControlPlayModeWithStateWaitReportsContextEndDuringWait(t *testing.T) {
	originalPoll := controlPlayModeStatePoll
	controlPlayModeStatePoll = time.Millisecond
	t.Cleanup(func() { controlPlayModeStatePoll = originalPoll })
	listener := newLoopbackIpcListener(t)
	go serveDroppedConnections(listener)
	connection := unityipc.Connection{
		Endpoint:    unityipc.Endpoint{Network: listener.Addr().Network(), Address: listener.Addr().String()},
		ProjectRoot: t.TempDir(),
	}
	ctx, cancel := context.WithTimeout(context.Background(), 200*time.Millisecond)
	defer cancel()
	var stdout, stderr bytes.Buffer

	code := runControlPlayModeWithStateWait(
		ctx,
		connection,
		map[string]any{controlPlayModeActionParam: "Stop", controlPlayModeTimeoutParam: 30},
		&stdout,
		&stderr,
	)

	if code != 1 || stdout.Len() != 0 {
		t.Fatalf("code=%d stdout=%q stderr=%s", code, stdout.String(), stderr.String())
	}
	if !strings.Contains(stderr.String(), "deadline") {
		t.Fatalf("stderr must report the ended wait as a deadline:\n%s", stderr.String())
	}
}

// Verifies a wait that never got a status response times out with the last transport error attached.
func TestWaitForControlPlayModeStateTimesOutWithLastError(t *testing.T) {
	originalPoll := controlPlayModeStatePoll
	controlPlayModeStatePoll = time.Millisecond
	t.Cleanup(func() { controlPlayModeStatePoll = originalPoll })

	_, completed, err := waitForControlPlayModeState(
		context.Background(),
		unreachableConnection(t.TempDir()),
		"Play",
		20*time.Millisecond,
	)

	if completed || err == nil {
		t.Fatalf("expected an error, got completed=%v err=%v", completed, err)
	}
	if !strings.Contains(err.Error(), "timed out waiting for play mode state:") {
		t.Fatalf("error must wrap the last transport error: %v", err)
	}
}

// Verifies only a dispatched request that lost its transport is treated as a reload disconnect.
func TestShouldWaitForControlPlayModeDisconnect(t *testing.T) {
	if shouldWaitForControlPlayModeDisconnect(io.EOF, unityipc.UnitySendOutcome{}) {
		t.Fatal("an undispatched request must not wait")
	}
	if !shouldWaitForControlPlayModeDisconnect(io.EOF, unityipc.UnitySendOutcome{RequestDispatched: true}) {
		t.Fatal("a dispatched request that lost its transport must wait")
	}
}

// Verifies the action defaults to Play and the timeout accepts every JSON number form, falling
// back to the default for missing, non-positive, or overflowing values.
func TestControlPlayModeParamDefaults(t *testing.T) {
	if action := controlPlayModeAction(map[string]any{}); action != "Play" {
		t.Fatalf("default action = %q, want Play", action)
	}
	cases := []struct {
		name  string
		value any
		want  int
	}{
		{name: "int", value: 5, want: 5},
		{name: "int64", value: int64(6), want: 6},
		{name: "float64", value: float64(7), want: 7},
		{name: "negative", value: -1, want: controlPlayModeDefaultTimeout},
		{name: "string", value: "8", want: controlPlayModeDefaultTimeout},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			if got := controlPlayModeTimeoutSeconds(map[string]any{controlPlayModeTimeoutParam: testCase.value}); got != testCase.want {
				t.Fatalf("timeout = %d, want %d", got, testCase.want)
			}
		})
	}
	if got := controlPlayModeTimeoutSeconds(map[string]any{}); got != controlPlayModeDefaultTimeout {
		t.Fatalf("missing timeout = %d, want default", got)
	}
}

// Verifies state matching and the user-facing messages for every action, including unknown ones.
func TestControlPlayModeStateAndMessages(t *testing.T) {
	if !controlPlayModeStateMatches("Pause", controlPlayModeResponse{IsPlaying: true, IsPaused: true}) {
		t.Fatal("Pause must match a paused editor")
	}
	if controlPlayModeStateMatches("Status", controlPlayModeResponse{IsPlaying: true}) {
		t.Fatal("an action without a target state must never match")
	}
	for action, want := range map[string][2]string{
		"Stop":  {"Play mode stopped", "Play mode stop"},
		"Pause": {"Play mode paused", "Play mode pause"},
		"Play":  {"Play mode started", "Play mode start"},
	} {
		if got := completedControlPlayModeMessage(action, controlPlayModeResponse{}, false); got != want[0] {
			t.Fatalf("completed message for %s = %q, want %q", action, got, want[0])
		}
		if got := requestedControlPlayModeMessage(action); got != want[1] {
			t.Fatalf("requested message for %s = %q, want %q", action, got, want[1])
		}
	}
}

// Verifies the compile-errors envelope counts the listed errors when Unity omitted the count.
func TestControlPlayModeCompileErrorsErrorCountsListedErrors(t *testing.T) {
	cliError := controlPlayModeCompileErrorsError("<PROJECT_ROOT>", "Play", controlPlayModeResponse{
		CompileErrors: []controlPlayModeCompileError{{Message: "a"}, {Message: "b"}},
	})

	if cliError.Details["CompileErrorCount"] != 2 {
		t.Fatalf("CompileErrorCount = %#v, want 2", cliError.Details["CompileErrorCount"])
	}
}
