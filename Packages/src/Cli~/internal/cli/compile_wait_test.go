package cli

import (
	"bytes"
	"context"
	"fmt"
	"os"
	"path/filepath"
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

// Verifies that compile wait returns the persisted result only after the server state becomes ready.
func TestWaitForCompileCompletionReadsResultAfterRecoveryStateBecomesReady(t *testing.T) {
	projectRoot := t.TempDir()
	requestID := "compile_test"
	resultDir := filepath.Join(projectRoot, compileResultRelativeDir)
	if err := os.MkdirAll(resultDir, 0o755); err != nil {
		t.Fatalf("failed to create result dir: %v", err)
	}
	if err := writeServerStateForTest(projectRoot, "reloading", ""); err != nil {
		t.Fatalf("failed to write server state: %v", err)
	}
	if err := os.WriteFile(
		filepath.Join(resultDir, requestID+".json"),
		[]byte("\xef\xbb\xbf{\"Success\":true}"),
		0o644,
	); err != nil {
		t.Fatalf("failed to write result: %v", err)
	}

	stateWriteErrCh := make(chan error, 1)
	go func() {
		time.Sleep(20 * time.Millisecond)
		stateWriteErrCh <- writeServerStateForTest(projectRoot, "ready", "")
	}()

	result, completed, err := waitForCompileCompletion(context.Background(), compileCompletionOptions{
		projectRoot:  projectRoot,
		requestID:    requestID,
		timeout:      time.Second,
		pollInterval: 5 * time.Millisecond,
		lockGrace:    10 * time.Millisecond,
	})
	if stateWriteErr := <-stateWriteErrCh; stateWriteErr != nil {
		t.Fatalf("failed to publish ready state: %v", stateWriteErr)
	}
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

// Verifies that compile wait stops early when recovery publishes a failed server state.
func TestWaitForCompileCompletionStopsWhenServerStateFailed(t *testing.T) {
	projectRoot := t.TempDir()
	requestID := "compile_test"
	resultDir := filepath.Join(projectRoot, compileResultRelativeDir)
	if err := os.MkdirAll(resultDir, 0o755); err != nil {
		t.Fatalf("failed to create result dir: %v", err)
	}
	if err := os.WriteFile(
		filepath.Join(resultDir, requestID+".json"),
		[]byte("{\"Success\":true}"),
		0o644,
	); err != nil {
		t.Fatalf("failed to write result: %v", err)
	}
	if err := writeServerStateForTest(projectRoot, "failed", "readiness probe failed"); err != nil {
		t.Fatalf("failed to write server state: %v", err)
	}

	_, _, err := waitForCompileCompletion(context.Background(), compileCompletionOptions{
		projectRoot:  projectRoot,
		requestID:    requestID,
		timeout:      time.Second,
		pollInterval: 5 * time.Millisecond,
		lockGrace:    10 * time.Millisecond,
	})
	if err == nil || !strings.Contains(err.Error(), "readiness probe failed") {
		t.Fatalf("expected failed server state error, got %v", err)
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

// Verifies that post-compile readiness runs only after successful compile results.
func TestCompileResultSucceededRequiresTrueSuccess(t *testing.T) {
	if !compileResultSucceeded([]byte(`{"Success":true}`)) {
		t.Fatal("successful compile result was not accepted")
	}

	if compileResultSucceeded([]byte(`{"Success":false,"Errors":[{"Message":"boom"}]}`)) {
		t.Fatal("failed compile result should not trigger readiness")
	}

	if compileResultSucceeded([]byte(`{"Message":"indeterminate"}`)) {
		t.Fatal("indeterminate compile result should not trigger readiness")
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

func writeServerStateForTest(projectRoot string, phase string, lastError string) error {
	statePath := filepath.Join(projectRoot, serverStateRelativePath)
	if err := os.MkdirAll(filepath.Dir(statePath), 0o755); err != nil {
		return err
	}
	content := fmt.Sprintf(
		`{"phase":%q,"generationId":"test","updatedAt":"2026-05-16T00:00:00Z","reason":"test","endpoint":"test","lastError":%q}`,
		phase,
		lastError)
	return os.WriteFile(statePath, []byte(content), 0o644)
}
