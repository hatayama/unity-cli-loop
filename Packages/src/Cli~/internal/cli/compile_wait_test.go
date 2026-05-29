package cli

import (
	"bufio"
	"bytes"
	"context"
	"fmt"
	"net"
	"os"
	"path/filepath"
	"runtime"
	"strings"
	"testing"
	"time"

	"github.com/hatayama/unity-cli-loop/Packages/src/Cli/internal/unityipc"
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

// Verifies that execute-dynamic-code waits for domain reload by default.
func TestShouldWaitForExecuteDynamicCodeDomainReloadDefaultsToExecuteDynamicCode(t *testing.T) {
	if !shouldWaitForExecuteDynamicCodeDomainReload(executeDynamicCodeCommandName, map[string]any{}) {
		t.Fatal("execute-dynamic-code should wait for domain reload by default")
	}

	if shouldWaitForExecuteDynamicCodeDomainReload("get-logs", map[string]any{}) {
		t.Fatal("non-execute-dynamic-code commands should not use dynamic-code wait")
	}
}

// Verifies that execute-dynamic-code can preserve the fast no-wait path.
func TestShouldWaitForExecuteDynamicCodeDomainReloadRespectsExplicitFalse(t *testing.T) {
	params := map[string]any{compileWaitParam: false}

	if shouldWaitForExecuteDynamicCodeDomainReload(executeDynamicCodeCommandName, params) {
		t.Fatal("execute-dynamic-code wait should be disabled by an explicit false flag")
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
	result := []byte(`{"Success":true,"DomainReloadWaitRequired":true}`)

	if !executeDynamicCodeDomainReloadWaitRequired(result) {
		t.Fatal("dynamic-code response should request a reload wait")
	}

	if executeDynamicCodeDomainReloadWaitRequired([]byte(`{"Success":true}`)) {
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
	result := stripExecuteDynamicCodeControlResult([]byte(`{"Success":true,"DomainReloadWaitRequired":true}`))

	if strings.Contains(string(result), "DomainReloadWaitRequired") {
		t.Fatalf("control field leaked into user output: %s", result)
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

// Verifies that compile wait ignores stale server-state files and returns the persisted result.
func TestWaitForCompileCompletionIgnoresStaleServerState(t *testing.T) {
	projectRoot := t.TempDir()
	requestID := "compile_test"
	resultDir := filepath.Join(projectRoot, compileResultRelativeDir)
	if err := os.MkdirAll(resultDir, 0o755); err != nil {
		t.Fatalf("failed to create result dir: %v", err)
	}
	staleStatePath := filepath.Join(projectRoot, "Temp", "UnityCliLoop", "server-state.json")
	if err := os.MkdirAll(filepath.Dir(staleStatePath), 0o755); err != nil {
		t.Fatalf("failed to create stale state dir: %v", err)
	}
	if err := os.WriteFile(staleStatePath, []byte(`{"phase":"failed","lastError":"stale"}`), 0o644); err != nil {
		t.Fatalf("failed to write stale state: %v", err)
	}
	if err := os.WriteFile(
		filepath.Join(resultDir, requestID+".json"),
		[]byte("\xef\xbb\xbf{\"Success\":true}"),
		0o644,
	); err != nil {
		t.Fatalf("failed to write result: %v", err)
	}

	result, completed, err := waitForCompileCompletion(context.Background(), compileCompletionOptions{
		projectRoot:  projectRoot,
		requestID:    requestID,
		timeout:      time.Second,
		pollInterval: 5 * time.Millisecond,
		lockGrace:    10 * time.Millisecond,
	})
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

func TestShouldWaitForCompileResultRequiresDispatchedTransportError(t *testing.T) {
	if shouldWaitForCompileResult(os.ErrNotExist, unityipc.UnitySendOutcome{}) {
		t.Fatal("undispatched error should not wait")
	}

	outcome := unityipc.UnitySendOutcome{RequestDispatched: true}
	if !shouldWaitForCompileResult(fmt.Errorf("EOF"), outcome) {
		t.Fatal("dispatched transport error should wait")
	}
}

// Verifies accepted compile requests leave RPC waiting and poll the result file after the compile response timeout.
func TestRunCompileWithDomainReloadWaitPollsResultAfterAcceptedResponseTimeout(t *testing.T) {
	if runtime.GOOS == "windows" {
		t.Skip("TCP endpoint injection is only used by this non-Windows client test")
	}

	originalResponseTimeout := compileFinalResponseTimeout
	compileFinalResponseTimeout = 20 * time.Millisecond
	t.Cleanup(func() {
		compileFinalResponseTimeout = originalResponseTimeout
	})

	listener, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatalf("failed to listen: %v", err)
	}
	defer func() {
		_ = listener.Close()
	}()

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

		time.Sleep(100 * time.Millisecond)
	}()

	projectRoot := t.TempDir()
	requestID := "compile_test_timeout"
	resultDir := filepath.Join(projectRoot, compileResultRelativeDir)
	if err := os.MkdirAll(resultDir, 0o755); err != nil {
		t.Fatalf("failed to create result dir: %v", err)
	}
	if err := os.WriteFile(
		filepath.Join(resultDir, requestID+".json"),
		[]byte(`{"Success":false}`),
		0o644,
	); err != nil {
		t.Fatalf("failed to write result: %v", err)
	}

	connection := unityipc.Connection{
		Endpoint: unityipc.Endpoint{
			Network: "tcp",
			Address: listener.Addr().String(),
		},
		ProjectRoot: projectRoot,
	}
	params := map[string]any{compileRequestIDParam: requestID}
	var stdout bytes.Buffer
	var stderr bytes.Buffer

	code := runCompileWithDomainReloadWait(context.Background(), connection, params, &stdout, &stderr)

	if code != 0 {
		t.Fatalf("compile wait failed with code %d\nstdout:\n%s\nstderr:\n%s", code, stdout.String(), stderr.String())
	}
	if !strings.Contains(stdout.String(), `"Success": false`) {
		t.Fatalf("stdout does not contain result: %s", stdout.String())
	}
	select {
	case err := <-serverErr:
		t.Fatalf("server failed: %v", err)
	default:
	}
}

// Verifies compile readiness wait decisions include indeterminate forced-compile results.
func TestCompileResultReadinessWaitMode(t *testing.T) {
	cases := map[string]compileReadinessWaitMode{
		`{"Success":true}`: compileReadinessWaitWarmup,
		`{"Success":false,"Errors":[{"Message":"boom"}]}`: compileReadinessWaitNone,
		`{"Success":null,"Message":"indeterminate"}`:      compileReadinessWaitRequired,
		`{"Message":"indeterminate"}`:                     compileReadinessWaitRequired,
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
