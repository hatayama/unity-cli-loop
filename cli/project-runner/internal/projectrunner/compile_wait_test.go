package projectrunner

import (
	"bufio"
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	"net"
	"path/filepath"
	"runtime"
	"strings"
	"testing"
	"time"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
	"github.com/hatayama/unity-cli-loop/common/tooldocs"
	"github.com/hatayama/unity-cli-loop/common/vibelog"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
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
	if !shouldWaitForCompileDomainReload(clicore.CompileCommandName, map[string]any{}) {
		t.Fatal("compile should wait for domain reload by default")
	}

	if shouldWaitForCompileDomainReload("get-logs", map[string]any{}) {
		t.Fatal("non-compile commands should not use compile wait")
	}
}

// Verifies that the explicit compile no-wait flag preserves the fast fire-and-forget path.
func TestShouldWaitForCompileDomainReloadRespectsExplicitFalse(t *testing.T) {
	params := map[string]any{compileWaitParam: false}

	if shouldWaitForCompileDomainReload(clicore.CompileCommandName, params) {
		t.Fatal("compile wait should be disabled by an explicit false flag")
	}
}

// Verifies that execute-dynamic-code keeps the default hot path free from reload waiting.
func TestShouldWaitForExecuteDynamicCodeDomainReloadDefaultsToHotPath(t *testing.T) {
	if shouldWaitForExecuteDynamicCodeDomainReload(clicore.ExecuteDynamicCodeCommandName, map[string]any{}) {
		t.Fatal("execute-dynamic-code should not wait for domain reload by default")
	}

	if shouldWaitForExecuteDynamicCodeDomainReload("get-logs", map[string]any{}) {
		t.Fatal("non-execute-dynamic-code commands should not use dynamic-code wait")
	}
}

// Verifies that execute-dynamic-code can opt into post-reload safety when needed.
func TestShouldWaitForExecuteDynamicCodeDomainReloadRespectsExplicitTrue(t *testing.T) {
	params := map[string]any{compileWaitParam: true}

	if !shouldWaitForExecuteDynamicCodeDomainReload(clicore.ExecuteDynamicCodeCommandName, params) {
		t.Fatal("execute-dynamic-code wait should be enabled by an explicit true flag")
	}
}

// Verifies that compile-only dynamic-code requests keep the diagnostic path fast.
func TestShouldWaitForExecuteDynamicCodeDomainReloadSkipsCompileOnly(t *testing.T) {
	params := map[string]any{"CompileOnly": true}

	if shouldWaitForExecuteDynamicCodeDomainReload(clicore.ExecuteDynamicCodeCommandName, params) {
		t.Fatal("compile-only execute-dynamic-code should not wait for domain reload")
	}
}

// Verifies that execute-dynamic-code waits only when Unity explicitly reports a reload signal.
func TestExecuteDynamicCodeDomainReloadWaitRequiredReadsResponseSignal(t *testing.T) {
	result := []byte(`{"Success":true,"DomainReloadWaitRequired":true}`)

	if !executeDynamicCodeDomainReloadWaitRequired(result) {
		t.Fatal("dynamic-code response should request a reload wait")
	}

	if executeDynamicCodeDomainReloadWaitRequired([]byte(`{"Success":true}`)) {
		t.Fatal("dynamic-code response without a reload signal should not request a wait")
	}
}

// Verifies that legacy lower-camel dynamic-code reload signals remain parseable by the CLI.
func TestExecuteDynamicCodeDomainReloadWaitRequiredReadsLegacyLowerCamelResponseSignal(t *testing.T) {
	result := []byte(`{"success":true,"domainReloadWaitRequired":true}`)

	if !executeDynamicCodeDomainReloadWaitRequired(result) {
		t.Fatal("legacy dynamic-code response should request a reload wait")
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
	result := stripExecuteDynamicCodeControlResult([]byte(`{"Success":true,"DomainReloadWaitRequired":true}`))

	if strings.Contains(string(result), "DomainReloadWaitRequired") {
		t.Fatalf("control field leaked into user output: %s", result)
	}
}

// Verifies that the CLI hides legacy lower-camel reload-wait response fields from users.
func TestStripExecuteDynamicCodeControlResultRemovesLegacyLowerCamelReloadSignal(t *testing.T) {
	result := stripExecuteDynamicCodeControlResult([]byte(`{"Success":true,"domainReloadWaitRequired":true}`))

	if strings.Contains(string(result), "domainReloadWaitRequired") {
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
	deps := compileWaitTestDeps(func(context.Context, unityipc.Connection, string) (compileStatusResponse, error) {
		callCount++
		if callCount == 1 {
			return compileStatusResponse{Ready: false, HasResult: true, Result: json.RawMessage(`{"Success":true}`)}, nil
		}
		return compileStatusResponse{Ready: true, HasResult: true, Result: json.RawMessage(`{"Success":true}`)}, nil
	})

	result, completed, _, err := waitForCompileCompletionWithDeps(context.Background(), compileCompletionOptions{
		connection:   connection,
		requestID:    requestID,
		timeout:      time.Second,
		pollInterval: 5 * time.Millisecond,
	}, deps)
	if err != nil {
		t.Fatalf("waitForCompileCompletion failed: %v", err)
	}
	if !completed {
		t.Fatal("compile wait did not complete")
	}
	if string(result) != "{\"Success\":true}" {
		t.Fatalf("result mismatch: %s", result)
	}
}

// Verifies compile wait writes the status polling lifecycle to CLI Vibe logs.
func TestWaitForCompileCompletionWritesPollLifecycleVibeLogs(t *testing.T) {
	enableCliVibeLog(t)
	connection := compileWaitTestConnection(t)
	requestID := "compile_poll_lifecycle_test"
	callCount := 0
	deps := compileWaitTestDeps(func(context.Context, unityipc.Connection, string) (compileStatusResponse, error) {
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
			Result:    json.RawMessage(`{"Success":false,"ErrorCount":2,"WarningCount":1}`),
			Message:   "Compile result is available.",
		}, nil
	})

	result, completed, _, err := waitForCompileCompletionWithDeps(context.Background(), compileCompletionOptions{
		connection:   connection,
		requestID:    requestID,
		timeout:      time.Second,
		pollInterval: 5 * time.Millisecond,
	}, deps)
	if err != nil {
		t.Fatalf("waitForCompileCompletion failed: %v", err)
	}
	if !completed {
		t.Fatal("compile wait did not complete")
	}
	if string(result) != `{"Success":false,"ErrorCount":2,"WarningCount":1}` {
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
	deps := compileWaitTestDeps(func(context.Context, unityipc.Connection, string) (compileStatusResponse, error) {
		callCount++
		if callCount == 1 {
			return compileStatusResponse{Ready: true, HasResult: false}, nil
		}
		return compileStatusResponse{
			Ready:     true,
			HasResult: true,
			Result:    json.RawMessage(`{"Success":false,"ErrorCount":null,"Message":"Force compilation completed"}`),
		}, nil
	})

	result, completed, _, err := waitForCompileCompletionWithDeps(context.Background(), compileCompletionOptions{
		connection:     connection,
		requestID:      requestID,
		forceRecompile: true,
		timeout:        time.Second,
		pollInterval:   5 * time.Millisecond,
	}, deps)
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
	if payload["Success"] != false {
		t.Fatalf("force compile must fail closed when the result is unknown: %#v", payload["Success"])
	}
	if payload["ErrorCount"] != nil || payload["WarningCount"] != nil {
		t.Fatalf("force compile counts should be unknown: %#v", payload)
	}
	message, ok := payload["Message"].(string)
	if !ok || !strings.Contains(message, "Force compilation completed") {
		t.Fatalf("force compile message mismatch: %#v", payload["Message"])
	}
}

// Verifies force compile does not finish only because Unity reports an idle status without a result.
func TestWaitForCompileCompletionForceCompileTimesOutWithoutStoredResult(t *testing.T) {
	connection := compileWaitTestConnection(t)
	requestID := "compile_force_no_result"
	deps := compileWaitTestDeps(func(context.Context, unityipc.Connection, string) (compileStatusResponse, error) {
		return compileStatusResponse{Ready: true, HasResult: false}, nil
	})

	_, completed, _, err := waitForCompileCompletionWithDeps(context.Background(), compileCompletionOptions{
		connection:     connection,
		requestID:      requestID,
		forceRecompile: true,
		timeout:        20 * time.Millisecond,
		pollInterval:   5 * time.Millisecond,
	}, deps)
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
	deps := compileWaitTestDeps(func(context.Context, unityipc.Connection, string) (compileStatusResponse, error) {
		return compileStatusResponse{Ready: false, IsCompiling: true, Message: "Compiling"}, nil
	})

	_, completed, _, err := waitForCompileCompletionWithDeps(context.Background(), compileCompletionOptions{
		connection:   connection,
		requestID:    requestID,
		timeout:      20 * time.Millisecond,
		pollInterval: 5 * time.Millisecond,
	}, deps)
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
	deps := compileWaitTestDeps(func(context.Context, unityipc.Connection, string) (compileStatusResponse, error) {
		cancel()
		return compileStatusResponse{Ready: false, IsDomainReloadInProgress: true}, nil
	})

	_, completed, _, err := waitForCompileCompletionWithDeps(ctx, compileCompletionOptions{
		connection:   connection,
		requestID:    requestID,
		timeout:      time.Second,
		pollInterval: time.Second,
	}, deps)
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
	endpoint, serverErr := startCompileAcceptOnceServer(t)

	deps := compileWaitTestDeps(func(context.Context, unityipc.Connection, string) (compileStatusResponse, error) {
		return compileStatusResponse{
			Ready:     true,
			HasResult: true,
			Result:    json.RawMessage(`{"Success":false,"ErrorCount":1,"WarningCount":0}`),
		}, nil
	})

	projectRoot := t.TempDir()
	connection := unityipc.Connection{
		Endpoint:    endpoint,
		ProjectRoot: projectRoot,
	}
	params := map[string]any{
		compileForceParam: true,
		tooldocs.ReloadExternalSceneChangesPropertyName: false,
	}
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := runCompileWithDomainReloadWaitWithDeps(context.Background(), connection, params, &stdout, &stderr, deps)
	if code != 1 {
		t.Fatalf("expected failed compile envelope to exit 1: code=%d stdout=%s stderr=%s", code, stdout.String(), stderr.String())
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
		`"timeout_ms":600000`,
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

// Verifies runCompileWithDomainReloadWaitWithDeps wires CompileWaitTimeoutSeconds into
// the wait deadline and COMPILE_WAIT_TIMEOUT message (not only the wait helper itself).
func TestRunCompileWithDomainReloadWaitUsesConfiguredTimeout(t *testing.T) {
	if runtime.GOOS == "windows" {
		t.Skip("TCP endpoint injection is only used by this non-Windows client test")
	}

	enableCliVibeLog(t)
	endpoint, serverErr := startCompileAcceptOnceServer(t)
	deps := compileWaitTestDeps(func(context.Context, unityipc.Connection, string) (compileStatusResponse, error) {
		return compileStatusResponse{Ready: false, IsCompiling: true, Message: "Compiling"}, nil
	})

	projectRoot := t.TempDir()
	connection := unityipc.Connection{
		Endpoint:    endpoint,
		ProjectRoot: projectRoot,
	}
	params := map[string]any{
		compileWaitTimeoutParam: 1,
	}
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := runCompileWithDomainReloadWaitWithDeps(context.Background(), connection, params, &stdout, &stderr, deps)
	if code != 1 {
		t.Fatalf("expected timeout exit 1: code=%d stdout=%s stderr=%s", code, stdout.String(), stderr.String())
	}
	if !strings.Contains(stderr.String(), "Compile status wait timed out after 1000ms") {
		t.Fatalf("timeout message missing configured duration: %s", stderr.String())
	}
	if !strings.Contains(stderr.String(), `"ErrorCode": "COMPILE_WAIT_TIMEOUT"`) &&
		!strings.Contains(stderr.String(), `"ErrorCode":"COMPILE_WAIT_TIMEOUT"`) {
		t.Fatalf("expected COMPILE_WAIT_TIMEOUT envelope: %s", stderr.String())
	}
	assertCompileWaitTimeoutEnvelopeDetails(t, stderr.Bytes(), true)

	logContent := readOnlyCliVibeLog(t, projectRoot)
	if !strings.Contains(logContent, `"timeout_ms":1000`) {
		t.Fatalf("prepared vibe log should record configured timeout_ms=1000:\n%s", logContent)
	}

	select {
	case err := <-serverErr:
		t.Fatalf("server failed: %v", err)
	default:
	}
}

// Verifies invalid CompileWaitTimeoutSeconds fails before a compile request is dispatched.
func TestRunCompileWithDomainReloadWaitRejectsNonPositiveTimeout(t *testing.T) {
	enableCliVibeLog(t)
	projectRoot := t.TempDir()
	connection := unityipc.Connection{
		Endpoint: unityipc.Endpoint{
			Network: "tcp",
			Address: "127.0.0.1:1",
		},
		ProjectRoot: projectRoot,
	}
	params := map[string]any{
		compileWaitTimeoutParam: 0,
	}
	var stdout bytes.Buffer
	var stderr bytes.Buffer
	queryCalls := 0
	deps := compileWaitTestDeps(func(context.Context, unityipc.Connection, string) (compileStatusResponse, error) {
		queryCalls++
		return compileStatusResponse{}, nil
	})

	code := runCompileWithDomainReloadWaitWithDeps(context.Background(), connection, params, &stdout, &stderr, deps)
	if code != 1 {
		t.Fatalf("expected invalid timeout exit 1: code=%d stderr=%s", code, stderr.String())
	}
	if queryCalls != 0 {
		t.Fatalf("compile status must not be queried for invalid timeout: calls=%d", queryCalls)
	}
	logFiles, err := filepath.Glob(filepath.Join(projectRoot, vibelog.CLIVibeLogDirectory, vibelog.CLIVibeLogPrefix+"_*.json"))
	if err != nil {
		t.Fatalf("failed to glob CLI Vibe logs: %v", err)
	}
	if len(logFiles) != 0 {
		t.Fatalf("compile request must not write vibe logs for invalid timeout: %#v", logFiles)
	}
	if !strings.Contains(stderr.String(), "positive integer") {
		t.Fatalf("expected positive integer validation error: %s", stderr.String())
	}
}

// Verifies timeouts above the Unity result retention window warn on stderr before compile proceeds.
func TestRunCompileWithDomainReloadWaitWarnsWhenTimeoutExceedsRetention(t *testing.T) {
	if runtime.GOOS == "windows" {
		t.Skip("TCP endpoint injection is only used by this non-Windows client test")
	}

	endpoint, serverErr := startCompileAcceptOnceServer(t)
	// Why Success:false: Success:true triggers post-compile warmup against a live Editor
	// and would hang this unit test. The warning is emitted before send/wait.
	deps := compileWaitTestDeps(func(context.Context, unityipc.Connection, string) (compileStatusResponse, error) {
		return compileStatusResponse{
			Ready:     true,
			HasResult: true,
			Result:    json.RawMessage(`{"Success":false,"ErrorCount":1,"WarningCount":0}`),
		}, nil
	})

	connection := unityipc.Connection{
		Endpoint:    endpoint,
		ProjectRoot: t.TempDir(),
	}
	params := map[string]any{
		compileWaitTimeoutParam: compileWaitTimeoutRetentionWarningSeconds + 1,
	}
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := runCompileWithDomainReloadWaitWithDeps(context.Background(), connection, params, &stdout, &stderr, deps)
	if code != 1 {
		t.Fatalf("expected failed compile envelope exit 1: code=%d stdout=%s stderr=%s", code, stdout.String(), stderr.String())
	}
	if !strings.Contains(stderr.String(), "exceeds the Unity-side compile result retention window (20 minutes)") {
		t.Fatalf("expected retention warning on stderr: %s", stderr.String())
	}

	select {
	case err := <-serverErr:
		t.Fatalf("server failed: %v", err)
	default:
	}
}

// startCompileAcceptOnceServer accepts one compile IPC session and returns accepted + final responses.
func startCompileAcceptOnceServer(t *testing.T) (unityipc.Endpoint, <-chan error) {
	t.Helper()
	listener, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatalf("failed to listen: %v", err)
	}
	t.Cleanup(func() {
		_ = listener.Close()
	})

	serverErr := make(chan error, 1)
	go func() {
		conn, acceptErr := listener.Accept()
		if acceptErr != nil {
			serverErr <- acceptErr
			return
		}
		defer func() {
			_ = conn.Close()
		}()

		if _, readErr := unityipc.Read(bufio.NewReader(conn)); readErr != nil {
			serverErr <- readErr
			return
		}

		accepted := []byte(`{"jsonrpc":"2.0","result":{"accepted":true},"uloop":{"phase":"accepted"},"id":1}`)
		if writeErr := unityipc.Write(conn, accepted); writeErr != nil {
			serverErr <- writeErr
			return
		}

		final := []byte(`{"jsonrpc":"2.0","result":{"Accepted":true},"id":1}`)
		if writeErr := unityipc.Write(conn, final); writeErr != nil {
			serverErr <- writeErr
			return
		}
	}()

	return unityipc.Endpoint{
		Network: "tcp",
		Address: listener.Addr().String(),
	}, serverErr
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
		`{"Success":true}`: compileReadinessWaitWarmup,
		`{"Success":false,"Errors":[{"Message":"boom"}]}`: compileReadinessWaitNone,
		`{"Success":false,"Message":"indeterminate"}`:     compileReadinessWaitNone,
		`{"Message":"indeterminate"}`:                     compileReadinessWaitNone,
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

// Verifies CompileWaitTimeoutSeconds is parsed from tool params with the default
// kept when absent and non-positive or non-integer values rejected.
func TestCompileWaitTimeoutFromParams(t *testing.T) {
	cases := []struct {
		name    string
		params  map[string]any
		want    time.Duration
		wantErr bool
	}{
		{
			name:   "missing uses default",
			params: map[string]any{},
			want:   compileWaitTimeout,
		},
		{
			name:   "nil value uses default",
			params: map[string]any{compileWaitTimeoutParam: nil},
			want:   compileWaitTimeout,
		},
		{
			name:   "int value",
			params: map[string]any{compileWaitTimeoutParam: 90},
			want:   90 * time.Second,
		},
		{
			name:   "float64 whole number",
			params: map[string]any{compileWaitTimeoutParam: float64(120)},
			want:   120 * time.Second,
		},
		{
			name:   "json.Number",
			params: map[string]any{compileWaitTimeoutParam: json.Number("45")},
			want:   45 * time.Second,
		},
		{
			name:    "zero is rejected",
			params:  map[string]any{compileWaitTimeoutParam: 0},
			wantErr: true,
		},
		{
			name:    "negative is rejected",
			params:  map[string]any{compileWaitTimeoutParam: -1},
			wantErr: true,
		},
		{
			name:    "non-integer float is rejected",
			params:  map[string]any{compileWaitTimeoutParam: 1.5},
			wantErr: true,
		},
		{
			name:   "max representable duration is accepted",
			params: map[string]any{compileWaitTimeoutParam: compileWaitTimeoutMaxSeconds},
			want:   time.Duration(compileWaitTimeoutMaxSeconds) * time.Second,
		},
		{
			name:    "overflowing duration is rejected",
			params:  map[string]any{compileWaitTimeoutParam: compileWaitTimeoutMaxSeconds + 1},
			wantErr: true,
		},
	}

	for _, tc := range cases {
		t.Run(tc.name, func(t *testing.T) {
			got, err := compileWaitTimeoutFromParams(tc.params)
			if tc.wantErr {
				if err == nil {
					t.Fatal("expected error")
				}
				return
			}
			if err != nil {
				t.Fatalf("unexpected error: %v", err)
			}
			if got != tc.want {
				t.Fatalf("timeout mismatch: got %v want %v", got, tc.want)
			}
		})
	}
}

// Verifies waitForCompileCompletionWithDeps stops when the configured timeout elapses
// instead of waiting for the default compileWaitTimeout.
func TestWaitForCompileCompletionRespectsConfiguredTimeout(t *testing.T) {
	connection := compileWaitTestConnection(t)
	deps := compileWaitTestDeps(func(context.Context, unityipc.Connection, string) (compileStatusResponse, error) {
		return compileStatusResponse{Ready: false, IsCompiling: true}, nil
	})

	startedAt := time.Now()
	_, completed, _, err := waitForCompileCompletionWithDeps(context.Background(), compileCompletionOptions{
		connection:   connection,
		requestID:    "compile_configured_timeout",
		timeout:      40 * time.Millisecond,
		pollInterval: 5 * time.Millisecond,
	}, deps)
	elapsed := time.Since(startedAt)
	if err != nil {
		t.Fatalf("waitForCompileCompletion failed: %v", err)
	}
	if completed {
		t.Fatal("compile wait should time out while still compiling")
	}
	if elapsed < 40*time.Millisecond {
		t.Fatalf("timed out too early: %v", elapsed)
	}
	if elapsed > 500*time.Millisecond {
		t.Fatalf("timed out too late for a 40ms deadline: %v", elapsed)
	}
}

// Tests that compile wait timeout guidance teaches the caller to verify Editor
// responsiveness instead of assuming a freeze, because agents have terminated
// whole sessions after misreading this timeout as a frozen Editor.
func TestCompileWaitTimeoutError(t *testing.T) {
	cliErr := compileWaitTimeoutError(
		"/tmp/MyProject",
		90*time.Second,
		nil,
		90*time.Second,
		compilePendingRecordLifetime-90*time.Second,
	)

	if cliErr.ErrorCode != clierrors.ErrorCodeCompileWaitTimeout {
		t.Fatalf("error code mismatch: %#v", cliErr)
	}
	if !cliErr.Retryable || !cliErr.SafeToRetry {
		t.Fatalf("retry flags mismatch: %#v", cliErr)
	}
	if cliErr.ProjectRoot != "/tmp/MyProject" {
		t.Fatalf("project root mismatch: %#v", cliErr)
	}
	expectedMessage := "Compile status wait timed out after 90000ms. This does not mean the Unity Editor is frozen; the compile may simply still be running."
	if cliErr.Message != expectedMessage {
		t.Fatalf("message mismatch: %#v", cliErr.Message)
	}
	expectedActions := []string{
		"Run a light command such as `uloop get-logs --max-count 1` to check whether Unity is responsive before treating this as a freeze.",
		"Unity keeps compiling and refuses other commands with UNITY_SERVER_BUSY until it finishes. Re-run `uloop compile`: it will reattach to the in-flight compile and wait for its result instead of starting a new one. The result stays retrievable for roughly 18 more minutes.",
		clierrors.ApiUpdateConsentModalNextAction,
		"Only if Unity does not respond to any command, restart it with `uloop launch -r`.",
		"If repeated waits keep showing is_compiling=true with no progress, the Editor's compile pipeline may be stalled (for example by a modal dialog); restart Unity with 'uloop launch -r' and rerun 'uloop compile'.",
	}
	if len(cliErr.NextActions) != len(expectedActions) {
		t.Fatalf("next actions mismatch: %#v", cliErr.NextActions)
	}
	for i, expected := range expectedActions {
		if cliErr.NextActions[i] != expected {
			t.Fatalf("next action %d mismatch:\n got: %#v\nwant: %#v", i, cliErr.NextActions[i], expected)
		}
	}
}

// Verifies a wait that consumes the full retention window omits the remaining-minutes sentence.
func TestCompileWaitTimeoutErrorOmitsRemainingMinutesWhenNoneLeft(t *testing.T) {
	cliErr := compileWaitTimeoutError(
		"/tmp/MyProject",
		compilePendingRecordLifetime,
		nil,
		compilePendingRecordLifetime,
		0,
	)
	reattach := cliErr.NextActions[1]
	if strings.Contains(reattach, "retrievable for roughly") {
		t.Fatalf("remaining-minutes sentence should be omitted: %#v", reattach)
	}
	if !strings.Contains(reattach, "reattach to the in-flight compile") {
		t.Fatalf("reattach guidance missing: %#v", reattach)
	}
}

// Verifies NextActions use the caller-supplied retentionRemaining instead of (TTL - timeout).
func TestCompileWaitTimeoutErrorUsesCallerRetentionRemaining(t *testing.T) {
	cliErr := compileWaitTimeoutError(
		"/tmp/MyProject",
		10*time.Minute,
		nil,
		10*time.Minute,
		time.Minute,
	)
	reattach := cliErr.NextActions[1]
	if strings.Contains(reattach, "roughly 10 more minutes") {
		t.Fatalf("must not derive remaining from wait timeout: %#v", reattach)
	}
	if !strings.Contains(reattach, "roughly 1 more minutes") {
		t.Fatalf("must use caller retentionRemaining: %#v", reattach)
	}
}

// Verifies COMPILE_WAIT_TIMEOUT Details include the last observed status flags and WaitedMs.
func TestCompileWaitTimeoutErrorIncludesLastObservedStatusDetails(t *testing.T) {
	lastStatus := &compileStatusResponse{
		IsCompiling:              true,
		IsUpdating:               false,
		IsDomainReloadInProgress: true,
	}
	cliErr := compileWaitTimeoutError(
		"/tmp/MyProject",
		time.Second,
		lastStatus,
		1234*time.Millisecond,
		compilePendingRecordLifetime-time.Second,
	)
	if cliErr.Details["WaitedMs"] != int64(1234) {
		t.Fatalf("WaitedMs mismatch: %#v", cliErr.Details["WaitedMs"])
	}
	if cliErr.Details["IsCompiling"] != true {
		t.Fatalf("IsCompiling mismatch: %#v", cliErr.Details)
	}
	if cliErr.Details["IsUpdating"] != false {
		t.Fatalf("IsUpdating mismatch: %#v", cliErr.Details)
	}
	if cliErr.Details["IsDomainReloadInProgress"] != true {
		t.Fatalf("IsDomainReloadInProgress mismatch: %#v", cliErr.Details)
	}
}

// Verifies timeout Details omit status flags when no compile status was observed.
func TestCompileWaitTimeoutErrorOmitsStatusDetailsWhenUnobserved(t *testing.T) {
	cliErr := compileWaitTimeoutError(
		"/tmp/MyProject",
		time.Second,
		nil,
		time.Second,
		compilePendingRecordLifetime-time.Second,
	)
	if _, ok := cliErr.Details["IsCompiling"]; ok {
		t.Fatalf("IsCompiling should be omitted without an observation: %#v", cliErr.Details)
	}
	if cliErr.Details["WaitedMs"] != int64(1000) {
		t.Fatalf("WaitedMs mismatch: %#v", cliErr.Details["WaitedMs"])
	}
}

// Verifies the wait helper returns the last successful status observation on timeout.
func TestWaitForCompileCompletionReturnsLastStatusOnTimeout(t *testing.T) {
	connection := compileWaitTestConnection(t)
	deps := compileWaitTestDeps(func(context.Context, unityipc.Connection, string) (compileStatusResponse, error) {
		return compileStatusResponse{
			Ready:                    false,
			IsCompiling:              true,
			IsDomainReloadInProgress: true,
		}, nil
	})

	_, completed, lastStatus, err := waitForCompileCompletionWithDeps(context.Background(), compileCompletionOptions{
		connection:   connection,
		requestID:    "compile_last_status",
		timeout:      30 * time.Millisecond,
		pollInterval: 5 * time.Millisecond,
	}, deps)
	if err != nil {
		t.Fatalf("waitForCompileCompletion failed: %v", err)
	}
	if completed {
		t.Fatal("expected timeout")
	}
	if lastStatus == nil {
		t.Fatal("expected last observed status")
	}
	if !lastStatus.IsCompiling || !lastStatus.IsDomainReloadInProgress {
		t.Fatalf("last status mismatch: %#v", lastStatus)
	}
}

// Verifies all-idle compile status past the start-stall threshold focuses Unity once
// with reason compile_start_stall.
func TestWaitForCompileCompletionFocusesOnceWhenStatusStaysIdle(t *testing.T) {
	enableCliVibeLog(t)
	connection := compileWaitTestConnection(t)
	deps := compileWaitTestDeps(func(context.Context, unityipc.Connection, string) (compileStatusResponse, error) {
		return compileStatusResponse{}, nil
	})
	probe := attachCompileWaitFocusProbe(&deps)

	_, completed, _, err := waitForCompileCompletionWithDeps(context.Background(), compileCompletionOptions{
		connection:   connection,
		requestID:    "compile_start_stall_idle",
		timeout:      200 * time.Millisecond,
		pollInterval: 5 * time.Millisecond,
	}, deps)
	if err != nil {
		t.Fatalf("waitForCompileCompletion failed: %v", err)
	}
	if completed {
		t.Fatal("idle compile wait should time out")
	}
	if probe.focusCount != 1 {
		t.Fatalf("focus attempts mismatch: got %d want 1", probe.focusCount)
	}

	logContent := readOnlyCliVibeLog(t, connection.ProjectRoot)
	attemptEntries := cliVibeEntriesForOperation(t, logContent, "cli_connection_retry_focus_attempt")
	if len(attemptEntries) != 1 {
		t.Fatalf("expected exactly 1 focus attempt log, got %d:\n%s", len(attemptEntries), logContent)
	}
	reason := vibeLogContextString(t, attemptEntries[0], "reason")
	expectedReason := "compile_start_stall"
	if reason != expectedReason {
		t.Fatalf("focus reason mismatch: got %q want %q", reason, expectedReason)
	}
}

// Verifies observing IsCompiling before the threshold suppresses focus even when later polls fail.
func TestWaitForCompileCompletionDoesNotFocusAfterActivityStarted(t *testing.T) {
	connection := compileWaitTestConnection(t)
	callCount := 0
	deps := compileWaitTestDeps(func(context.Context, unityipc.Connection, string) (compileStatusResponse, error) {
		callCount++
		if callCount == 1 {
			return compileStatusResponse{IsCompiling: true}, nil
		}
		return compileStatusResponse{}, fmt.Errorf("status poll failed")
	})
	probe := attachCompileWaitFocusProbe(&deps)

	_, completed, _, err := waitForCompileCompletionWithDeps(context.Background(), compileCompletionOptions{
		connection:   connection,
		requestID:    "compile_start_stall_activity",
		timeout:      200 * time.Millisecond,
		pollInterval: 5 * time.Millisecond,
	}, deps)
	if err != nil {
		t.Fatalf("waitForCompileCompletion failed: %v", err)
	}
	if completed {
		t.Fatal("compile wait should time out after activity then silence")
	}
	if probe.focusCount != 0 {
		t.Fatalf("focus attempts mismatch: got %d want 0", probe.focusCount)
	}
}

// Verifies probe errors alone past the threshold still focus Unity once.
func TestWaitForCompileCompletionFocusesOnceWhenProbesKeepFailing(t *testing.T) {
	connection := compileWaitTestConnection(t)
	deps := compileWaitTestDeps(func(context.Context, unityipc.Connection, string) (compileStatusResponse, error) {
		return compileStatusResponse{}, fmt.Errorf("status probe timeout")
	})
	probe := attachCompileWaitFocusProbe(&deps)

	_, completed, _, err := waitForCompileCompletionWithDeps(context.Background(), compileCompletionOptions{
		connection:   connection,
		requestID:    "compile_start_stall_probe_error",
		timeout:      200 * time.Millisecond,
		pollInterval: 5 * time.Millisecond,
	}, deps)
	if err != nil {
		t.Fatalf("waitForCompileCompletion failed: %v", err)
	}
	if completed {
		t.Fatal("probe-error compile wait should time out")
	}
	if probe.focusCount != 1 {
		t.Fatalf("focus attempts mismatch: got %d want 1", probe.focusCount)
	}
}

// Verifies a compile wait that focused Unity restores the previous front window on completion.
func TestWaitForCompileCompletionRestoresFocusOnCompletion(t *testing.T) {
	connection := compileWaitTestConnection(t)
	callCount := 0
	deps := compileWaitTestDeps(func(context.Context, unityipc.Connection, string) (compileStatusResponse, error) {
		callCount++
		if callCount < 8 {
			return compileStatusResponse{}, nil
		}
		return compileStatusResponse{
			Ready:     true,
			HasResult: true,
			Result:    json.RawMessage(`{"Success":true}`),
		}, nil
	})
	probe := attachCompileWaitFocusProbe(&deps)

	result, completed, _, err := waitForCompileCompletionWithDeps(context.Background(), compileCompletionOptions{
		connection:   connection,
		requestID:    "compile_start_stall_restore",
		timeout:      200 * time.Millisecond,
		pollInterval: 5 * time.Millisecond,
	}, deps)
	if err != nil {
		t.Fatalf("waitForCompileCompletion failed: %v", err)
	}
	if !completed {
		t.Fatal("compile wait should complete after the stall")
	}
	if string(result) != `{"Success":true}` {
		t.Fatalf("result mismatch: %s", result)
	}
	if probe.focusCount != 1 {
		t.Fatalf("focus attempts mismatch: got %d want 1", probe.focusCount)
	}
	if probe.restoreCount != 1 {
		t.Fatalf("restore calls mismatch: got %d want 1", probe.restoreCount)
	}
}

// Verifies compile activity is any of IsCompiling, IsUpdating, domain reload, or HasResult,
// and that Ready alone does not count.
func TestCompileActivityHasStarted(t *testing.T) {
	cases := []struct {
		name   string
		status compileStatusResponse
		want   bool
	}{
		{name: "all false", status: compileStatusResponse{}, want: false},
		{name: "IsCompiling", status: compileStatusResponse{IsCompiling: true}, want: true},
		{name: "IsUpdating", status: compileStatusResponse{IsUpdating: true}, want: true},
		{name: "IsDomainReloadInProgress", status: compileStatusResponse{IsDomainReloadInProgress: true}, want: true},
		{name: "HasResult", status: compileStatusResponse{HasResult: true}, want: true},
		{name: "Ready alone is not activity", status: compileStatusResponse{Ready: true}, want: false},
	}
	for _, tc := range cases {
		t.Run(tc.name, func(t *testing.T) {
			got := compileActivityHasStarted(tc.status)
			if got != tc.want {
				t.Fatalf("compileActivityHasStarted mismatch: got %v want %v status=%#v", got, tc.want, tc.status)
			}
		})
	}
}

// assertCompileWaitTimeoutEnvelopeDetails checks the run/attach wiring that passes
// lastStatus and WaitedMs into the COMPILE_WAIT_TIMEOUT stderr envelope.
func assertCompileWaitTimeoutEnvelopeDetails(t *testing.T, stderr []byte, wantCompiling bool) {
	t.Helper()
	jsonStart := bytes.IndexByte(stderr, '{')
	if jsonStart < 0 {
		t.Fatalf("stderr has no JSON envelope: %s", string(stderr))
	}
	var envelope clierrors.CLIErrorEnvelope
	if err := json.Unmarshal(stderr[jsonStart:], &envelope); err != nil {
		t.Fatalf("stderr is not a JSON error envelope: %v\n%s", err, string(stderr))
	}
	if envelope.Error.ErrorCode != clierrors.ErrorCodeCompileWaitTimeout {
		t.Fatalf("error code mismatch: %#v", envelope.Error.ErrorCode)
	}
	details := envelope.Error.Details
	if details == nil {
		t.Fatal("COMPILE_WAIT_TIMEOUT Details missing")
	}
	if details["IsCompiling"] != wantCompiling {
		t.Fatalf("IsCompiling mismatch: %#v", details["IsCompiling"])
	}
	waitedMs, ok := details["WaitedMs"].(float64)
	if !ok {
		t.Fatalf("WaitedMs missing or wrong type: %#v", details["WaitedMs"])
	}
	if waitedMs < 1000 {
		t.Fatalf("WaitedMs should cover the configured 1s wait: %#v", waitedMs)
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

func compileWaitTestDeps(
	replacement func(context.Context, unityipc.Connection, string) (compileStatusResponse, error),
) compileWaitDeps {
	return compileWaitDeps{
		queryCompileStatus:     replacement,
		attachProbeTimeout:     40 * time.Millisecond,
		attachProbeInterval:    5 * time.Millisecond,
		attachWaitPollInterval: 5 * time.Millisecond,
	}
}

type compileWaitFocusProbe struct {
	focusCount   int
	restoreCount int
}

func attachCompileWaitFocusProbe(deps *compileWaitDeps) *compileWaitFocusProbe {
	probe := &compileWaitFocusProbe{}
	deps.startStallFocusThreshold = 20 * time.Millisecond
	deps.focus = defaultConnectionRetryDeps()
	deps.focus.findRunningUnityProcess = func(context.Context, string) (*clicore.UnityProcess, error) {
		return &clicore.UnityProcess{Pid: 4242}, nil
	}
	deps.focus.focusUnityProcess = func(context.Context, int) (clicore.RestoreFocusFunc, error) {
		probe.focusCount++
		return func(context.Context) error {
			probe.restoreCount++
			return nil
		}, nil
	}
	return probe
}

func vibeLogContextString(t *testing.T, entry map[string]any, key string) string {
	t.Helper()
	contextMap, ok := entry["context"].(map[string]any)
	if !ok {
		t.Fatalf("vibe log context missing: %#v", entry)
	}
	value, ok := contextMap[key].(string)
	if !ok {
		t.Fatalf("vibe log context %s mismatch: %#v", key, contextMap[key])
	}
	return value
}
