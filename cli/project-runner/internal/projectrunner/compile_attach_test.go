package projectrunner

import (
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	"os"
	"runtime"
	"strings"
	"testing"
	"time"

	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

// Verifies that with no pending record, compile follows the normal request path.
func TestRunCompileAttachWithoutPendingRecordUsesNormalPath(t *testing.T) {
	if runtime.GOOS == "windows" {
		t.Skip("TCP endpoint injection is only used by this non-Windows client test")
	}

	enableCliVibeLog(t)
	endpoint, serverErr := startCompileAcceptOnceServer(t)
	projectRoot := t.TempDir()
	connection := unityipc.Connection{Endpoint: endpoint, ProjectRoot: projectRoot}
	deps := compileWaitTestDeps(func(context.Context, unityipc.Connection, string) (compileStatusResponse, error) {
		return compileStatusResponse{
			Ready:     true,
			HasResult: true,
			Result:    json.RawMessage(`{"Success":false,"ErrorCount":1}`),
		}, nil
	})
	var stdout, stderr bytes.Buffer

	code := runCompileWithDomainReloadWaitWithDeps(context.Background(), connection, map[string]any{}, &stdout, &stderr, deps)
	if code != 1 {
		t.Fatalf("expected failed compile envelope: code=%d stderr=%s", code, stderr.String())
	}
	logContent := readOnlyCliVibeLog(t, projectRoot)
	if !strings.Contains(logContent, `"operation":"cli_compile_request_prepared"`) {
		t.Fatalf("normal path should prepare a compile request:\n%s", logContent)
	}
	if strings.Contains(logContent, `"operation":"cli_compile_attach_start"`) {
		t.Fatalf("attach must not run without a pending record:\n%s", logContent)
	}
	select {
	case err := <-serverErr:
		t.Fatalf("server failed: %v", err)
	default:
	}
}

// Verifies HasResult is returned even when Ready is false (editor-wide busy is unrelated).
func TestRunCompileAttachReturnsStoredResultWhenEditorNotReady(t *testing.T) {
	enableCliVibeLog(t)
	projectRoot := t.TempDir()
	if err := writeCompilePendingRecord(projectRoot, compilePendingRecord{
		RequestID:     "compile_attach_busy_stored",
		TimedOutAtUtc: time.Now().UTC(),
	}); err != nil {
		t.Fatalf("write pending record failed: %v", err)
	}

	deps := compileWaitTestDeps(func(context.Context, unityipc.Connection, string) (compileStatusResponse, error) {
		return compileStatusResponse{
			Ready:       false,
			IsCompiling: true,
			HasResult:   true,
			Result:      json.RawMessage(`{"Success":false,"ErrorCount":7}`),
		}, nil
	})
	connection := unityipc.Connection{
		Endpoint:    unityipc.Endpoint{Network: "tcp", Address: "127.0.0.1:1"},
		ProjectRoot: projectRoot,
	}
	var stdout, stderr bytes.Buffer

	code := runCompileWithDomainReloadWaitWithDeps(context.Background(), connection, map[string]any{}, &stdout, &stderr, deps)
	if code != 1 {
		t.Fatalf("expected failed compile envelope: code=%d stderr=%s", code, stderr.String())
	}
	if !strings.Contains(stdout.String(), `"ErrorCount"`) || !strings.Contains(stdout.String(), "7") {
		t.Fatalf("stored result missing from stdout: %s", stdout.String())
	}
	if _, err := os.Stat(compilePendingRecordPath(projectRoot)); !os.IsNotExist(err) {
		t.Fatalf("pending record should be cleared: %v", err)
	}
	logContent := readOnlyCliVibeLog(t, projectRoot)
	if !strings.Contains(logContent, `"attach_mode":"stored_result"`) {
		t.Fatalf("stored-result attach mode missing:\n%s", logContent)
	}
	if strings.Contains(logContent, `"operation":"cli_compile_request_prepared"`) {
		t.Fatalf("stored result must not send a new compile request:\n%s", logContent)
	}
}

// Verifies an in-flight pending compile is waited on without sending a new compile request.
func TestRunCompileAttachWaitsForInFlightCompileAndClearsRecord(t *testing.T) {
	enableCliVibeLog(t)
	projectRoot := t.TempDir()
	requestID := "compile_attach_waiting"
	timedOutAt := time.Now().UTC().Add(-time.Minute)
	if err := writeCompilePendingRecord(projectRoot, compilePendingRecord{
		RequestID:     requestID,
		TimedOutAtUtc: timedOutAt,
	}); err != nil {
		t.Fatalf("write pending record failed: %v", err)
	}

	callCount := 0
	deps := compileWaitTestDeps(func(context.Context, unityipc.Connection, string) (compileStatusResponse, error) {
		callCount++
		if callCount == 1 {
			return compileStatusResponse{Ready: false, IsCompiling: true}, nil
		}
		return compileStatusResponse{
			Ready:     true,
			HasResult: true,
			Result:    json.RawMessage(`{"Success":false,"ErrorCount":2,"WarningCount":0}`),
		}, nil
	})
	connection := unityipc.Connection{
		Endpoint:    unityipc.Endpoint{Network: "tcp", Address: "127.0.0.1:1"},
		ProjectRoot: projectRoot,
	}
	var stdout, stderr bytes.Buffer

	code := runCompileWithDomainReloadWaitWithDeps(context.Background(), connection, map[string]any{}, &stdout, &stderr, deps)
	if code != 1 {
		t.Fatalf("expected failed compile envelope: code=%d stderr=%s", code, stderr.String())
	}
	if !strings.Contains(stdout.String(), `"ErrorCount"`) || !strings.Contains(stdout.String(), "2") {
		t.Fatalf("attached result missing from stdout: %s", stdout.String())
	}
	if _, err := os.Stat(compilePendingRecordPath(projectRoot)); !os.IsNotExist(err) {
		t.Fatalf("pending record should be cleared after attach success: %v", err)
	}
	logContent := readOnlyCliVibeLog(t, projectRoot)
	for _, expected := range []string{
		`"operation":"cli_compile_attach_start"`,
		`"operation":"cli_compile_attach_result"`,
		`"attach_mode":"waiting"`,
		`"attach_outcome":"completed"`,
	} {
		if !strings.Contains(logContent, expected) {
			t.Fatalf("attach vibe log missing %q:\n%s", expected, logContent)
		}
	}
	if strings.Contains(logContent, `"operation":"cli_compile_request_prepared"`) {
		t.Fatalf("attach wait must not send a new compile request:\n%s", logContent)
	}
}

// Verifies a completed pending compile returns the stored result without a new request.
func TestRunCompileAttachReturnsStoredResultAndClearsRecord(t *testing.T) {
	enableCliVibeLog(t)
	projectRoot := t.TempDir()
	requestID := "compile_attach_stored"
	if err := writeCompilePendingRecord(projectRoot, compilePendingRecord{
		RequestID:     requestID,
		TimedOutAtUtc: time.Now().UTC(),
	}); err != nil {
		t.Fatalf("write pending record failed: %v", err)
	}

	deps := compileWaitTestDeps(func(context.Context, unityipc.Connection, string) (compileStatusResponse, error) {
		return compileStatusResponse{
			Ready:     true,
			HasResult: true,
			Result:    json.RawMessage(`{"Success":false,"ErrorCount":3}`),
		}, nil
	})
	connection := unityipc.Connection{
		Endpoint:    unityipc.Endpoint{Network: "tcp", Address: "127.0.0.1:1"},
		ProjectRoot: projectRoot,
	}
	var stdout, stderr bytes.Buffer

	code := runCompileWithDomainReloadWaitWithDeps(context.Background(), connection, map[string]any{}, &stdout, &stderr, deps)
	if code != 1 {
		t.Fatalf("expected failed compile envelope: code=%d stderr=%s", code, stderr.String())
	}
	if !strings.Contains(stdout.String(), `"ErrorCount"`) || !strings.Contains(stdout.String(), "3") {
		t.Fatalf("stored result missing from stdout: %s", stdout.String())
	}
	if _, err := os.Stat(compilePendingRecordPath(projectRoot)); !os.IsNotExist(err) {
		t.Fatalf("pending record should be cleared: %v", err)
	}
	logContent := readOnlyCliVibeLog(t, projectRoot)
	if !strings.Contains(logContent, `"attach_mode":"stored_result"`) {
		t.Fatalf("stored-result attach mode missing:\n%s", logContent)
	}
	if strings.Contains(logContent, `"operation":"cli_compile_request_prepared"`) {
		t.Fatalf("stored-result attach must not send a compile request:\n%s", logContent)
	}
}

// Verifies ForceRecompile clears the pending record and starts a new compile.
func TestRunCompileAttachForceRecompileClearsRecordAndStartsNewCompile(t *testing.T) {
	if runtime.GOOS == "windows" {
		t.Skip("TCP endpoint injection is only used by this non-Windows client test")
	}

	enableCliVibeLog(t)
	endpoint, serverErr := startCompileAcceptOnceServer(t)
	projectRoot := t.TempDir()
	if err := writeCompilePendingRecord(projectRoot, compilePendingRecord{
		RequestID:     "compile_attach_force",
		TimedOutAtUtc: time.Now().UTC(),
	}); err != nil {
		t.Fatalf("write pending record failed: %v", err)
	}

	deps := compileWaitTestDeps(func(context.Context, unityipc.Connection, string) (compileStatusResponse, error) {
		return compileStatusResponse{
			Ready:     true,
			HasResult: true,
			Result:    json.RawMessage(`{"Success":false,"ErrorCount":9}`),
		}, nil
	})
	connection := unityipc.Connection{Endpoint: endpoint, ProjectRoot: projectRoot}
	params := map[string]any{compileForceParam: true}
	var stdout, stderr bytes.Buffer

	code := runCompileWithDomainReloadWaitWithDeps(context.Background(), connection, params, &stdout, &stderr, deps)
	if code != 1 {
		t.Fatalf("expected failed compile envelope: code=%d stderr=%s", code, stderr.String())
	}
	if _, err := os.Stat(compilePendingRecordPath(projectRoot)); !os.IsNotExist(err) {
		t.Fatalf("pending record should be cleared before forced recompile: %v", err)
	}
	logContent := readOnlyCliVibeLog(t, projectRoot)
	if !strings.Contains(logContent, `"operation":"cli_compile_request_prepared"`) {
		t.Fatalf("force recompile should send a new compile request:\n%s", logContent)
	}
	if strings.Contains(logContent, `"operation":"cli_compile_attach_start"`) {
		t.Fatalf("force recompile should not return via attach:\n%s", logContent)
	}
	select {
	case err := <-serverErr:
		t.Fatalf("server failed: %v", err)
	default:
	}
}

// Verifies Ready-without-result clears the pending record and starts a new compile.
func TestRunCompileAttachReadyWithoutResultClearsRecordAndStartsNewCompile(t *testing.T) {
	if runtime.GOOS == "windows" {
		t.Skip("TCP endpoint injection is only used by this non-Windows client test")
	}

	enableCliVibeLog(t)
	endpoint, serverErr := startCompileAcceptOnceServer(t)
	projectRoot := t.TempDir()
	if err := writeCompilePendingRecord(projectRoot, compilePendingRecord{
		RequestID:     "compile_attach_missing_result",
		TimedOutAtUtc: time.Now().UTC(),
	}); err != nil {
		t.Fatalf("write pending record failed: %v", err)
	}

	callCount := 0
	deps := compileWaitTestDeps(func(context.Context, unityipc.Connection, string) (compileStatusResponse, error) {
		callCount++
		if callCount == 1 {
			return compileStatusResponse{Ready: true, HasResult: false}, nil
		}
		return compileStatusResponse{
			Ready:     true,
			HasResult: true,
			Result:    json.RawMessage(`{"Success":false,"ErrorCount":4}`),
		}, nil
	})
	connection := unityipc.Connection{Endpoint: endpoint, ProjectRoot: projectRoot}
	var stdout, stderr bytes.Buffer

	code := runCompileWithDomainReloadWaitWithDeps(context.Background(), connection, map[string]any{}, &stdout, &stderr, deps)
	if code != 1 {
		t.Fatalf("expected failed compile envelope: code=%d stderr=%s", code, stderr.String())
	}
	if _, err := os.Stat(compilePendingRecordPath(projectRoot)); !os.IsNotExist(err) {
		t.Fatalf("pending record should be cleared when stored result is gone: %v", err)
	}
	logContent := readOnlyCliVibeLog(t, projectRoot)
	if !strings.Contains(logContent, `"operation":"cli_compile_request_prepared"`) {
		t.Fatalf("missing stored result should fall through to a new compile:\n%s", logContent)
	}
	if strings.Contains(logContent, `"operation":"cli_compile_attach_start"`) {
		t.Fatalf("missing stored result should not attach:\n%s", logContent)
	}
	select {
	case err := <-serverErr:
		t.Fatalf("server failed: %v", err)
	default:
	}
}

// Verifies an attach wait that times out again keeps the original pending record untouched.
func TestRunCompileAttachRetimesOutPreservesPendingRecord(t *testing.T) {
	enableCliVibeLog(t)
	projectRoot := t.TempDir()
	requestID := "compile_attach_retimeout"
	timedOutAt := time.Now().UTC().Add(-time.Minute)
	if err := writeCompilePendingRecord(projectRoot, compilePendingRecord{
		RequestID:     requestID,
		TimedOutAtUtc: timedOutAt,
	}); err != nil {
		t.Fatalf("write pending record failed: %v", err)
	}

	deps := compileWaitTestDeps(func(context.Context, unityipc.Connection, string) (compileStatusResponse, error) {
		return compileStatusResponse{Ready: false, IsCompiling: true}, nil
	})
	connection := unityipc.Connection{
		Endpoint:    unityipc.Endpoint{Network: "tcp", Address: "127.0.0.1:1"},
		ProjectRoot: projectRoot,
	}
	params := map[string]any{compileWaitTimeoutParam: 1}
	var stdout, stderr bytes.Buffer

	code := runCompileWithDomainReloadWaitWithDeps(context.Background(), connection, params, &stdout, &stderr, deps)
	if code != 1 {
		t.Fatalf("expected timeout exit 1: code=%d stderr=%s", code, stderr.String())
	}
	if !strings.Contains(stderr.String(), "Compile status wait timed out after 1000ms") {
		t.Fatalf("reattach timeout message mismatch: %s", stderr.String())
	}

	got, ok := readCompilePendingRecord(projectRoot)
	if !ok {
		t.Fatal("pending record must be preserved after attach timeout")
	}
	if got.RequestID != requestID || !got.TimedOutAtUtc.Equal(timedOutAt) {
		t.Fatalf("pending record mutated on re-timeout: %#v", got)
	}
	logContent := readOnlyCliVibeLog(t, projectRoot)
	if !strings.Contains(logContent, `"attach_outcome":"timeout"`) {
		t.Fatalf("attach timeout outcome missing:\n%s", logContent)
	}
}

// Verifies probe failures keep the pending record and fall through to a new compile.
func TestRunCompileAttachProbeFailureKeepsRecordAndStartsNewCompile(t *testing.T) {
	if runtime.GOOS == "windows" {
		t.Skip("TCP endpoint injection is only used by this non-Windows client test")
	}

	enableCliVibeLog(t)
	endpoint, serverErr := startCompileAcceptOnceServer(t)
	projectRoot := t.TempDir()
	requestID := "compile_attach_probe_fail"
	timedOutAt := time.Now().UTC().Add(-time.Minute)
	if err := writeCompilePendingRecord(projectRoot, compilePendingRecord{
		RequestID:     requestID,
		TimedOutAtUtc: timedOutAt,
	}); err != nil {
		t.Fatalf("write pending record failed: %v", err)
	}

	deps := compileWaitTestDeps(func(context.Context, unityipc.Connection, string) (compileStatusResponse, error) {
		return compileStatusResponse{}, fmt.Errorf("probe unavailable")
	})
	connection := unityipc.Connection{Endpoint: endpoint, ProjectRoot: projectRoot}
	var stdout, stderr bytes.Buffer

	// After probe failure, normal path queries status again with the new request id.
	// Replace deps so probe fails, then normal wait succeeds via a second deps wrapper.
	queryCalls := 0
	deps.queryCompileStatus = func(ctx context.Context, connection unityipc.Connection, id string) (compileStatusResponse, error) {
		queryCalls++
		if id == requestID {
			return compileStatusResponse{}, fmt.Errorf("probe unavailable")
		}
		return compileStatusResponse{
			Ready:     true,
			HasResult: true,
			Result:    json.RawMessage(`{"Success":false,"ErrorCount":5}`),
		}, nil
	}

	code := runCompileWithDomainReloadWaitWithDeps(context.Background(), connection, map[string]any{}, &stdout, &stderr, deps)
	if code != 1 {
		t.Fatalf("expected failed compile envelope: code=%d stderr=%s", code, stderr.String())
	}
	got, ok := readCompilePendingRecord(projectRoot)
	if !ok {
		t.Fatal("pending record must remain after probe failure")
	}
	if got.RequestID != requestID || !got.TimedOutAtUtc.Equal(timedOutAt) {
		t.Fatalf("pending record mutated after probe failure: %#v", got)
	}
	if queryCalls < 2 {
		t.Fatalf("expected probe retries then normal status query, got %d calls", queryCalls)
	}
	logContent := readOnlyCliVibeLog(t, projectRoot)
	if !strings.Contains(logContent, `"operation":"cli_compile_request_prepared"`) {
		t.Fatalf("probe failure should fall through to a new compile:\n%s", logContent)
	}
	if strings.Contains(logContent, `"operation":"cli_compile_attach_start"`) {
		t.Fatalf("failed probe must not attach:\n%s", logContent)
	}
	select {
	case err := <-serverErr:
		t.Fatalf("server failed: %v", err)
	default:
	}
}

// Verifies three consecutive Ready&&!HasResult observations abandon attach without waiting out the deadline.
func TestRunCompileAttachDisappearedResultFallsThroughQuickly(t *testing.T) {
	if runtime.GOOS == "windows" {
		t.Skip("TCP endpoint injection is only used by this non-Windows client test")
	}

	enableCliVibeLog(t)
	endpoint, serverErr := startCompileAcceptOnceServer(t)
	projectRoot := t.TempDir()
	if err := writeCompilePendingRecord(projectRoot, compilePendingRecord{
		RequestID:     "compile_attach_disappeared",
		TimedOutAtUtc: time.Now().UTC(),
	}); err != nil {
		t.Fatalf("write pending record failed: %v", err)
	}

	callCount := 0
	deps := compileWaitTestDeps(func(context.Context, unityipc.Connection, string) (compileStatusResponse, error) {
		callCount++
		if callCount == 1 {
			// Probe: editor busy with unrelated work, no stored result for this RequestId.
			return compileStatusResponse{Ready: false, IsCompiling: true, HasResult: false}, nil
		}
		if callCount <= 1+compileAttachMissingResultStreak {
			return compileStatusResponse{Ready: true, HasResult: false}, nil
		}
		return compileStatusResponse{
			Ready:     true,
			HasResult: true,
			Result:    json.RawMessage(`{"Success":false,"ErrorCount":8}`),
		}, nil
	})
	connection := unityipc.Connection{Endpoint: endpoint, ProjectRoot: projectRoot}
	params := map[string]any{compileWaitTimeoutParam: 600}
	var stdout, stderr bytes.Buffer

	startedAt := time.Now()
	code := runCompileWithDomainReloadWaitWithDeps(context.Background(), connection, params, &stdout, &stderr, deps)
	elapsed := time.Since(startedAt)
	if code != 1 {
		t.Fatalf("expected failed compile envelope: code=%d stderr=%s", code, stderr.String())
	}
	if elapsed > 2*time.Second {
		t.Fatalf("disappearance should not wait for the configured timeout: elapsed=%v", elapsed)
	}
	if _, err := os.Stat(compilePendingRecordPath(projectRoot)); !os.IsNotExist(err) {
		t.Fatalf("disappeared attach should clear the pending record: %v", err)
	}
	logContent := readOnlyCliVibeLog(t, projectRoot)
	if !strings.Contains(logContent, `"attach_outcome":"disappeared"`) {
		t.Fatalf("disappeared attach outcome missing:\n%s", logContent)
	}
	if !strings.Contains(logContent, `"operation":"cli_compile_request_prepared"`) {
		t.Fatalf("disappearance should fall through to a new compile:\n%s", logContent)
	}
	select {
	case err := <-serverErr:
		t.Fatalf("server failed: %v", err)
	default:
	}
}

// Verifies a single Ready&&!HasResult sample does not abort attach (completion/store race).
func TestRunCompileAttachToleratesTransientReadyWithoutResult(t *testing.T) {
	enableCliVibeLog(t)
	projectRoot := t.TempDir()
	if err := writeCompilePendingRecord(projectRoot, compilePendingRecord{
		RequestID:     "compile_attach_transient_ready",
		TimedOutAtUtc: time.Now().UTC(),
	}); err != nil {
		t.Fatalf("write pending record failed: %v", err)
	}

	callCount := 0
	deps := compileWaitTestDeps(func(context.Context, unityipc.Connection, string) (compileStatusResponse, error) {
		callCount++
		switch callCount {
		case 1:
			return compileStatusResponse{Ready: false, IsCompiling: true}, nil
		case 2:
			return compileStatusResponse{Ready: true, HasResult: false}, nil
		default:
			return compileStatusResponse{
				Ready:     true,
				HasResult: true,
				Result:    json.RawMessage(`{"Success":false,"ErrorCount":6}`),
			}, nil
		}
	})
	connection := unityipc.Connection{
		Endpoint:    unityipc.Endpoint{Network: "tcp", Address: "127.0.0.1:1"},
		ProjectRoot: projectRoot,
	}
	var stdout, stderr bytes.Buffer

	code := runCompileWithDomainReloadWaitWithDeps(context.Background(), connection, map[string]any{}, &stdout, &stderr, deps)
	if code != 1 {
		t.Fatalf("expected failed compile envelope: code=%d stderr=%s", code, stderr.String())
	}
	if !strings.Contains(stdout.String(), `"ErrorCount"`) || !strings.Contains(stdout.String(), "6") {
		t.Fatalf("attach should continue after one Ready&&!HasResult sample: %s", stdout.String())
	}
	logContent := readOnlyCliVibeLog(t, projectRoot)
	if strings.Contains(logContent, `"attach_outcome":"disappeared"`) {
		t.Fatalf("one Ready&&!HasResult sample must not count as disappearance:\n%s", logContent)
	}
	if !strings.Contains(logContent, `"attach_outcome":"completed"`) {
		t.Fatalf("attach should complete after the stored result appears:\n%s", logContent)
	}
}

// Verifies ForceRecompile during attach wait warns and does not start a forced recompile.
func TestRunCompileAttachWarnsWhenForceRecompileIgnoredDuringWait(t *testing.T) {
	enableCliVibeLog(t)
	projectRoot := t.TempDir()
	if err := writeCompilePendingRecord(projectRoot, compilePendingRecord{
		RequestID:     "compile_attach_force_warn",
		TimedOutAtUtc: time.Now().UTC(),
	}); err != nil {
		t.Fatalf("write pending record failed: %v", err)
	}

	callCount := 0
	deps := compileWaitTestDeps(func(context.Context, unityipc.Connection, string) (compileStatusResponse, error) {
		callCount++
		if callCount == 1 {
			return compileStatusResponse{Ready: false, IsCompiling: true}, nil
		}
		return compileStatusResponse{
			Ready:     true,
			HasResult: true,
			Result:    json.RawMessage(`{"Success":false,"ErrorCount":11}`),
		}, nil
	})
	connection := unityipc.Connection{
		Endpoint:    unityipc.Endpoint{Network: "tcp", Address: "127.0.0.1:1"},
		ProjectRoot: projectRoot,
	}
	params := map[string]any{compileForceParam: true}
	var stdout, stderr bytes.Buffer

	code := runCompileWithDomainReloadWaitWithDeps(context.Background(), connection, params, &stdout, &stderr, deps)
	if code != 1 {
		t.Fatalf("expected failed compile envelope: code=%d stderr=%s", code, stderr.String())
	}
	if !strings.Contains(stderr.String(), "--force-recompile is not applied") {
		t.Fatalf("expected force-recompile ignored warning: %s", stderr.String())
	}
	if !strings.Contains(stdout.String(), `"ErrorCount"`) || !strings.Contains(stdout.String(), "11") {
		t.Fatalf("attach should still return the in-flight result: %s", stdout.String())
	}
	logContent := readOnlyCliVibeLog(t, projectRoot)
	if strings.Contains(logContent, `"operation":"cli_compile_request_prepared"`) {
		t.Fatalf("force during attach wait must not send a new compile request:\n%s", logContent)
	}
}

// Verifies COMPILE_WAIT_TIMEOUT persistence writes a pending record for later attach.
func TestRunCompileTimeoutWritesPendingRecord(t *testing.T) {
	if runtime.GOOS == "windows" {
		t.Skip("TCP endpoint injection is only used by this non-Windows client test")
	}

	endpoint, serverErr := startCompileAcceptOnceServer(t)
	projectRoot := t.TempDir()
	deps := compileWaitTestDeps(func(context.Context, unityipc.Connection, string) (compileStatusResponse, error) {
		return compileStatusResponse{Ready: false, IsCompiling: true}, nil
	})
	connection := unityipc.Connection{Endpoint: endpoint, ProjectRoot: projectRoot}
	params := map[string]any{compileWaitTimeoutParam: 1}
	var stdout, stderr bytes.Buffer

	code := runCompileWithDomainReloadWaitWithDeps(context.Background(), connection, params, &stdout, &stderr, deps)
	if code != 1 {
		t.Fatalf("expected timeout exit 1: code=%d stderr=%s", code, stderr.String())
	}
	got, ok := readCompilePendingRecord(projectRoot)
	if !ok {
		t.Fatal("timeout should persist a pending compile record")
	}
	if got.RequestID == "" || got.TimedOutAtUtc.IsZero() {
		t.Fatalf("pending record incomplete: %#v", got)
	}
	select {
	case err := <-serverErr:
		t.Fatalf("server failed: %v", err)
	default:
	}
}
