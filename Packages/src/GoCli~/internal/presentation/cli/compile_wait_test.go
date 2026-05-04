package cli

import (
	"context"
	"fmt"
	"os"
	"path/filepath"
	"testing"
	"time"

	"github.com/hatayama/unity-cli-loop/Packages/src/GoCli/internal/domain"
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

func TestWaitForCompileCompletionReadsResultAfterLocksClear(t *testing.T) {
	projectRoot := t.TempDir()
	requestID := "compile_test"
	resultDir := filepath.Join(projectRoot, compileResultRelativeDir)
	if err := os.MkdirAll(resultDir, 0o755); err != nil {
		t.Fatalf("failed to create result dir: %v", err)
	}
	if err := os.MkdirAll(filepath.Join(projectRoot, "Temp"), 0o755); err != nil {
		t.Fatalf("failed to create temp dir: %v", err)
	}
	lockPath := filepath.Join(projectRoot, "Temp", "domainreload.lock")
	if err := os.WriteFile(lockPath, []byte("busy"), 0o644); err != nil {
		t.Fatalf("failed to write lock: %v", err)
	}
	if err := os.WriteFile(
		filepath.Join(resultDir, requestID+".json"),
		[]byte("\xef\xbb\xbf{\"Success\":true}"),
		0o644,
	); err != nil {
		t.Fatalf("failed to write result: %v", err)
	}

	go func() {
		time.Sleep(20 * time.Millisecond)
		_ = os.Remove(lockPath)
	}()

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

func TestWaitForCompileCompletionWaitsForServerStartingLock(t *testing.T) {
	projectRoot := t.TempDir()
	requestID := "compile_test"
	resultDir := filepath.Join(projectRoot, compileResultRelativeDir)
	if err := os.MkdirAll(resultDir, 0o755); err != nil {
		t.Fatalf("failed to create result dir: %v", err)
	}
	tempPath := filepath.Join(projectRoot, "Temp")
	if err := os.MkdirAll(tempPath, 0o755); err != nil {
		t.Fatalf("failed to create temp dir: %v", err)
	}
	lockPath := filepath.Join(tempPath, "serverstarting.lock")
	if err := os.WriteFile(lockPath, []byte("busy"), 0o644); err != nil {
		t.Fatalf("failed to write lock: %v", err)
	}
	if err := os.WriteFile(
		filepath.Join(resultDir, requestID+".json"),
		[]byte("{\"Success\":true}"),
		0o644,
	); err != nil {
		t.Fatalf("failed to write result: %v", err)
	}

	go func() {
		time.Sleep(20 * time.Millisecond)
		_ = os.Remove(lockPath)
	}()

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
	if shouldWaitForCompileResult(os.ErrNotExist, domain.UnitySendOutcome{}) {
		t.Fatal("undispatched error should not wait")
	}

	outcome := domain.UnitySendOutcome{RequestDispatched: true}
	if !shouldWaitForCompileResult(fmt.Errorf("EOF"), outcome) {
		t.Fatal("dispatched transport error should wait")
	}
}
