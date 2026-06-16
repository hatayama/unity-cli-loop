package cli

import (
	"bufio"
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	"net"
	"runtime"
	"strings"
	"testing"
	"time"

	"github.com/hatayama/unity-cli-loop/cli/internal/unityipc"
)

func TestEnsureCompileRequestIDPreservesSafeValue(t *testing.T) {
	params := map[string]any{compileRequestIDParam: "compile_safe-123"}

	requestID, err := ensureCompileRequestID(params)
	if err != nil {
		t.Fatalf("ensureCompileRequestID failed: %v", err)
	}

	if requestID != "compile_safe-123" {
		t.Fatalf("request id mismatch: %s", requestID)
	}
	if params[compileRequestIDParam] != "compile_safe-123" {
		t.Fatalf("params request id mismatch: %#v", params[compileRequestIDParam])
	}
}

func TestEnsureCompileRequestIDReplacesUnsafeValue(t *testing.T) {
	params := map[string]any{compileRequestIDParam: "../unsafe"}

	requestID, err := ensureCompileRequestID(params)
	if err != nil {
		t.Fatalf("ensureCompileRequestID failed: %v", err)
	}

	if requestID == "../unsafe" {
		t.Fatal("unsafe request id was preserved")
	}
	if !isSafeCompileRequestID(requestID) {
		t.Fatalf("generated request id is unsafe: %s", requestID)
	}
}

// Verifies that compile commands wait for domain reload even without an explicit flag.
func TestShouldWaitForCompileDomainReloadDefaultsToCompileCommands(t *testing.T) {
	if !shouldWaitForCompileDomainReload(compileCommandName, map[string]any{}) {
		t.Fatal("compile should wait for domain reload by default")
	}

	if shouldWaitForCompileDomainReload("get-logs", map[string]any{}) {
		t.Fatal("non-compile commands should not use compile wait")
	}
}

// Verifies that the explicit compile no-wait flag preserves the fast fire-and-forget path.
func TestShouldWaitForCompileDomainReloadRespectsExplicitFalse(t *testing.T) {
	params := map[string]any{compileWaitParam: false}

	if shouldWaitForCompileDomainReload(compileCommandName, params) {
		t.Fatal("compile wait should be disabled by an explicit false flag")
	}
}

// Verifies that execute-dynamic-code keeps the default hot path free from reload waiting.
func TestShouldWaitForExecuteDynamicCodeDomainReloadDefaultsToHotPath(t *testing.T) {
	if shouldWaitForExecuteDynamicCodeDomainReload(executeDynamicCodeCommandName, map[string]any{}) {
		t.Fatal("execute-dynamic-code should not wait for domain reload by default")
	}

	if shouldWaitForExecuteDynamicCodeDomainReload("get-logs", map[string]any{}) {
		t.Fatal("non-execute-dynamic-code commands should not use dynamic-code wait")
	}
}

// Verifies that execute-dynamic-code can opt into post-reload safety when needed.
func TestShouldWaitForExecuteDynamicCodeDomainReloadRespectsExplicitTrue(t *testing.T) {
	params := map[string]any{compileWaitParam: true}

	if !shouldWaitForExecuteDynamicCodeDomainReload(executeDynamicCodeCommandName, params) {
		t.Fatal("execute-dynamic-code wait should be enabled by an explicit true flag")
	}
}

// Verifies that compile-only dynamic-code requests keep the diagnostic path fast.
func TestShouldWaitForExecuteDynamicCodeDomainReloadSkipsCompileOnly(t *testing.T) {
	params := map[string]any{"CompileOnly": true}

	if shouldWaitForExecuteDynamicCodeDomainReload(executeDynamicCodeCommandName, params) {
		t.Fatal("compile-only execute-dynamic-code should not wait for domain reload")
	}
}

// Verifies that execute-dynamic-code waits only when Unity explicitly reports a reload signal.
func TestExecuteDynamicCodeDomainReloadWaitRequiredReadsResponseSignal(t *testing.T) {
	result := []byte(`{"success":true,"domainReloadWaitRequired":true}`)

	if !executeDynamicCodeDomainReloadWaitRequired(result) {
		t.Fatal("dynamic-code response should request a reload wait")
	}

	if executeDynamicCodeDomainReloadWaitRequired([]byte(`{"success":true}`)) {
		t.Fatal("dynamic-code response without a reload signal should not request a wait")
	}
}

// Verifies that dispatched dynamic-code disconnects still wait for reload recovery.
func TestShouldWaitForExecuteDynamicCodeDisconnectWaitsAfterDispatchedTransportLoss(t *testing.T) {
	outcome := unityipc.UnitySendOutcome{RequestDispatched: true}

	if !shouldWaitForExecuteDynamicCodeDisconnect(fmt.Errorf("EOF"), outcome) {
		t.Fatal("dispatched transport loss should wait for domain reload recovery")
	}

	if shouldWaitForExecuteDynamicCodeDisconnect(fmt.Errorf("EOF"), unityipc.UnitySendOutcome{}) {
		t.Fatal("undispatched transport loss should not use dynamic-code reload wait")
	}
}

// Verifies that the CLI does not expose its internal reload-wait response field to users.
func TestStripExecuteDynamicCodeControlResultRemovesReloadSignal(t *testing.T) {
	result := stripExecuteDynamicCodeControlResult([]byte(`{"success":true,"domainReloadWaitRequired":true}`))

	if strings.Contains(string(result), "domainReloadWaitRequired") {
		t.Fatalf("control field leaked into user output: %s", result)
	}
}

// Verifies that the CLI hides legacy-cased reload-wait response fields from users.
func TestStripExecuteDynamicCodeControlResultRemovesLegacyReloadSignal(t *testing.T) {
	result := stripExecuteDynamicCodeControlResult([]byte(`{"success":true,"DomainReloadWaitRequired":true}`))

	if strings.Contains(string(result), "DomainReloadWaitRequired") {
		t.Fatalf("legacy control field leaked into user output: %s", result)
	}
}

// Verifies that compile wait preparation creates a request id and enables reload waiting.
func TestPrepareCompileWaitParamsForcesDomainReloadWait(t *testing.T) {
	params := map[string]any{}

	requestID, err := prepareCompileWaitParams(params)
	if err != nil {
		t.Fatalf("prepareCompileWaitParams failed: %v", err)
	}

	if requestID == "" {
		t.Fatal("request id was not generated")
	}
	if params[compileWaitParam] != true {
		t.Fatalf("compile wait flag was not forced: %#v", params[compileWaitParam])
	}
}

// Verifies that compile wait returns the result after Unity reports idle status.
func TestWaitForCompileCompletionReturnsReadyStatusResult(t *testing.T) {
	connection := compileWaitTestConnection(t)
	requestID := "compile_test"
	callCount := 0
	replaceQueryCompileStatus(t, func(context.Context, unityipc.Connection, string) (compileStatusResponse, error) {
		callCount++
		if callCount == 1 {
			return compileStatusResponse{Ready: false, HasResult: true, Result: json.RawMessage(`{"success":true}`)}, nil
		}
		return compileStatusResponse{Ready: true, HasResult: true, Result: json.RawMessage(`{"success":true}`)}, nil
	})

	result, completed, err := waitForCompileCompletion(context.Background(), compileCompletionOptions{
		connection:   connection,
		requestID:    requestID,
		timeout:      time.Second,
		pollInterval: 5 * time.Millisecond,
	})
	if err != nil {
		t.Fatalf("waitForCompileCompletion failed: %v", err)
	}
	if !completed {
		t.Fatal("compile wait did not complete")
	}
	if string(result) != "{\"success\":true}" {
		t.Fatalf("result mismatch: %s", result)
	}
}

// Verifies compile wait writes the status polling lifecycle to CLI Vibe logs.
func TestWaitForCompileCompletionWritesPollLifecycleVibeLogs(t *testing.T) {
	enableCliVibeLog(t)
	connection := compileWaitTestConnection(t)
	requestID := "compile_poll_lifecycle_test"
	callCount := 0
	replaceQueryCompileStatus(t, func(context.Context, unityipc.Connection, string) (compileStatusResponse, error) {
		callCount++
		if callCount == 1 {
			return compileStatusResponse{
				Ready:       false,
				HasResult:   false,
				IsCompiling: true,
				Message:     "Compiling",
			}, nil
		}
		return compileStatusResponse{
			Ready:     true,
			HasResult: true,
			Result:    json.RawMessage(`{"success":false,"errorCount":2,"warningCount":1}`),
			Message:   "Compile result is available.",
		}, nil
	})

	result, completed, err := waitForCompileCompletion(context.Background(), compileCompletionOptions{
		connection:   connection,
		requestID:    requestID,
		timeout:      time.Second,
		pollInterval: 5 * time.Millisecond,
	})
	if err != nil {
		t.Fatalf("waitForCompileCompletion failed: %v", err)
	}
	if !completed {
		t.Fatal("compile wait did not complete")
	}
	if string(result) != `{"success":false,"errorCount":2,"warningCount":1}` {
		t.Fatalf("result mismatch: %s", result)
	}

	logContent := readOnlyCliVibeLog(t, connection.ProjectRoot)
	for _, expected := range []string{
		`"operation":"cli_compile_status_poll_start"`,
		`"operation":"cli_compile_status_poll_observed"`,
		`"operation":"cli_compile_status_poll_complete"`,
		`"request_id":"compile_poll_lifecycle_test"`,
		`"poll_attempts":2`,
		`"success":false`,
		`"error_count":2`,
		`"warning_count":1`,
	} {
		if !strings.Contains(logContent, expected) {
			t.Fatalf("CLI Vibe log missing %q:\n%s", expected, logContent)
		}
	}
}

// Verifies force compile waits for Unity's stored result instead of fabricating one from idle status.
func TestWaitForCompileCompletionForceCompileWaitsForStoredResult(t *testing.T) {
	connection := compileWaitTestConnection(t)
	requestID := "compile_force_unknown"
	callCount := 0
	replaceQueryCompileStatus(t, func(context.Context, unityipc.Connection, string) (compileStatusResponse, error) {
		callCount++
		if callCount == 1 {
			return compileStatusResponse{Ready: true, HasResult: false}, nil
		}
		return compileStatusResponse{
			Ready:     true,
			HasResult: true,
			Result:    json.RawMessage(`{"success":null,"errorCount":null,"message":"Force compilation completed"}`),
		}, nil
	})

	result, completed, err := waitForCompileCompletion(context.Background(), compileCompletionOptions{
		connection:     connection,
		requestID:      requestID,
		forceRecompile: true,
		timeout:        time.Second,
		pollInterval:   5 * time.Millisecond,
	})
	if err != nil {
		t.Fatalf("waitForCompileCompletion failed: %v", err)
	}
	if !completed {
		t.Fatal("force compile wait did not complete")
	}
	if callCount != 2 {
		t.Fatalf("force compile should wait for stored result, got %d status calls", callCount)
	}

	var payload map[string]any
	if err := json.Unmarshal(result, &payload); err != nil {
		t.Fatalf("force compile result is not JSON: %v", err)
	}
	if payload["success"] != nil {
		t.Fatalf("force compile success should be unknown: %#v", payload["success"])
	}
	if payload["errorCount"] != nil || payload["warningCount"] != nil {
		t.Fatalf("force compile counts should be unknown: %#v", payload)
	}
	message, ok := payload["message"].(string)
	if !ok || !strings.Contains(message, "Force compilation completed") {
		t.Fatalf("force compile message mismatch: %#v", payload["message"])
	}
}

// Verifies force compile does not finish only because Unity reports an idle status without a result.
func TestWaitForCompileCompletionForceCompileTimesOutWithoutStoredResult(t *testing.T) {
	connection := compileWaitTestConnection(t)
	requestID := "compile_force_no_result"
	replaceQueryCompileStatus(t, func(context.Context, unityipc.Connection, string) (compileStatusResponse, error) {
		return compileStatusResponse{Ready: true, HasResult: false}, nil
	})

	_, completed, err := waitForCompileCompletion(context.Background(), compileCompletionOptions{
		connection:     connection,
		requestID:      requestID,
		forceRecompile: true,
		timeout:        20 * time.Millisecond,
		pollInterval:   5 * time.Millisecond,
	})
	if err != nil {
		t.Fatalf("waitForCompileCompletion failed: %v", err)
	}
	if completed {
		t.Fatal("force compile wait should not complete without a stored result")
	}
}

// Verifies that compile status wait timeouts are visible in CLI Vibe logs.
func TestWaitForCompileCompletionWritesTimeoutVibeLog(t *testing.T) {
	enableCliVibeLog(t)
	connection := compileWaitTestConnection(t)
	requestID := "compile_timeout_log_test"
	replaceQueryCompileStatus(t, func(context.Context, unityipc.Connection, string) (compileStatusResponse, error) {
		return compileStatusResponse{Ready: false, IsCompiling: true, Message: "Compiling"}, nil
	})

	_, completed, err := waitForCompileCompletion(context.Background(), compileCompletionOptions{
		connection:   connection,
		requestID:    requestID,
		timeout:      20 * time.Millisecond,
		pollInterval: 5 * time.Millisecond,
	})
	if err != nil {
		t.Fatalf("waitForCompileCompletion failed: %v", err)
	}
	if completed {
		t.Fatal("compile wait should time out without a ready result")
	}

	logContent := readOnlyCliVibeLog(t, connection.ProjectRoot)
	for _, expected := range []string{
		`"operation":"cli_compile_status_poll_start"`,
		`"operation":"cli_compile_status_poll_observed"`,
		`"operation":"cli_compile_status_poll_timeout"`,
		`"request_id":"compile_timeout_log_test"`,
		`"last_status"`,
		`"poll_attempts"`,
	} {
		if !strings.Contains(logContent, expected) {
			t.Fatalf("CLI Vibe log missing %q:\n%s", expected, logContent)
		}
	}
}

// Verifies that compile status wait cancellations are visible in CLI Vibe logs.
func TestWaitForCompileCompletionWritesCancellationVibeLog(t *testing.T) {
	enableCliVibeLog(t)
	connection := compileWaitTestConnection(t)
	requestID := "compile_cancel_log_test"
	ctx, cancel := context.WithCancel(context.Background())
	replaceQueryCompileStatus(t, func(context.Context, unityipc.Connection, string) (compileStatusResponse, error) {
		cancel()
		return compileStatusResponse{Ready: false, IsDomainReloadInProgress: true}, nil
	})

	_, completed, err := waitForCompileCompletion(ctx, compileCompletionOptions{
		connection:   connection,
		requestID:    requestID,
		timeout:      time.Second,
		pollInterval: time.Second,
	})
	if err == nil {
		t.Fatal("waitForCompileCompletion should return the cancellation error")
	}
	if completed {
		t.Fatal("compile wait should not complete after cancellation")
	}

	logContent := readOnlyCliVibeLog(t, connection.ProjectRoot)
	for _, expected := range []string{
		`"operation":"cli_compile_status_poll_cancelled"`,
		`"request_id":"compile_cancel_log_test"`,
		`"last_status"`,
		`"poll_attempts":1`,
	} {
		if !strings.Contains(logContent, expected) {
			t.Fatalf("CLI Vibe log missing %q:\n%s", expected, logContent)
		}
	}
}

// Verifies the compile command records request preparation and send outcome diagnostics.
func TestRunCompileWithDomainReloadWaitWritesRequestLifecycleVibeLogs(t *testing.T) {
	if runtime.GOOS == "windows" {
		t.Skip("TCP endpoint injection is only used by this non-Windows client test")
	}

	enableCliVibeLog(t)
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

		final := []byte(`{"jsonrpc":"2.0","result":{"Accepted":true},"id":1}`)
		if err := unityipc.Write(conn, final); err != nil {
			serverErr <- err
			return
		}
	}()

	replaceQueryCompileStatus(t, func(context.Context, unityipc.Connection, string) (compileStatusResponse, error) {
		return compileStatusResponse{
			Ready:     true,
			HasResult: true,
			Result:    json.RawMessage(`{"success":false,"errorCount":1,"warningCount":0}`),
		}, nil
	})

	projectRoot := t.TempDir()
	connection := unityipc.Connection{
		Endpoint: unityipc.Endpoint{
			Network: "tcp",
			Address: listener.Addr().String(),
		},
		ProjectRoot: projectRoot,
	}
	params := map[string]any{
		compileForceParam:                      true,
		reloadExternalSceneChangesPropertyName: false,
	}
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := runCompileWithDomainReloadWait(context.Background(), connection, params, &stdout, &stderr)
	if code != 0 {
		t.Fatalf("runCompileWithDomainReloadWait failed: code=%d stdout=%s stderr=%s", code, stdout.String(), stderr.String())
	}

	logContent := readOnlyCliVibeLog(t, projectRoot)
	for _, expected := range []string{
		`"operation":"cli_debug_mode_resolved"`,
		`"operation":"cli_compile_request_prepared"`,
		`"operation":"cli_compile_request_send_result"`,
		`"debug_source":"env"`,
		`"request_dispatched":true`,
		`"request_accepted":true`,
		`"response_received":true`,
		`"force_recompile":true`,
		`"reload_external_scene_changes":false`,
	} {
		if !strings.Contains(logContent, expected) {
			t.Fatalf("CLI Vibe log missing %q:\n%s", expected, logContent)
		}
	}

	select {
	case err := <-serverErr:
		t.Fatalf("server failed: %v", err)
	default:
	}
}

func TestShouldWaitForCompileStatusRequiresDispatchedTransportError(t *testing.T) {
	if shouldWaitForCompileStatus(fmt.Errorf("missing"), unityipc.UnitySendOutcome{}) {
		t.Fatal("undispatched error should not wait")
	}

	outcome := unityipc.UnitySendOutcome{RequestDispatched: true}
	if !shouldWaitForCompileStatus(fmt.Errorf("EOF"), outcome) {
		t.Fatal("dispatched transport error should wait")
	}
}

// Verifies accepted final-response timeouts can fall back to status polling.
func TestShouldWaitForCompileStatusAllowsAcceptedFinalResponseTimeout(t *testing.T) {
	outcome := unityipc.UnitySendOutcome{RequestDispatched: true, RequestAccepted: true}
	if !shouldWaitForCompileStatus(fmt.Errorf("read tcp 127.0.0.1:1: i/o timeout"), outcome) {
		t.Fatal("accepted final-response timeout should wait")
	}

	unacceptedOutcome := unityipc.UnitySendOutcome{RequestDispatched: true}
	if shouldWaitForCompileStatus(fmt.Errorf("read tcp 127.0.0.1:1: i/o timeout"), unacceptedOutcome) {
		t.Fatal("unaccepted timeout should not wait")
	}
}

// Verifies compile readiness warmup only runs after confirmed successful results.
func TestCompileResultReadinessWaitMode(t *testing.T) {
	cases := map[string]compileReadinessWaitMode{
		`{"success":true}`: compileReadinessWaitWarmup,
		`{"success":false,"errors":[{"message":"boom"}]}`: compileReadinessWaitNone,
		`{"success":null,"message":"indeterminate"}`:      compileReadinessWaitNone,
		`{"message":"indeterminate"}`:                     compileReadinessWaitNone,
	}

	for result, expected := range cases {
		actual := compileResultReadinessWaitMode([]byte(result))
		if actual != expected {
			t.Fatalf("readiness wait mode mismatch for %s: %v", result, actual)
		}
	}
}

// Verifies that failed best-effort warmup reports a warning without taking over compile output.
func TestWritePostCompileWarmupWarningReportsNonFatalFailure(t *testing.T) {
	var stderr bytes.Buffer

	writePostCompileWarmupWarning(&stderr, fmt.Errorf("probe failed"))

	if !strings.Contains(stderr.String(), "warning: post-compile warmup skipped: probe failed") {
		t.Fatalf("warning mismatch: %s", stderr.String())
	}
}

func compileWaitTestConnection(t *testing.T) unityipc.Connection {
	t.Helper()
	return unityipc.Connection{
		Endpoint: unityipc.Endpoint{
			Network: "tcp",
			Address: "127.0.0.1:1",
		},
		ProjectRoot: t.TempDir(),
	}
}

func replaceQueryCompileStatus(
	t *testing.T,
	replacement func(context.Context, unityipc.Connection, string) (compileStatusResponse, error),
) {
	t.Helper()
	original := queryCompileStatus
	queryCompileStatus = replacement
	t.Cleanup(func() {
		queryCompileStatus = original
	})
}
