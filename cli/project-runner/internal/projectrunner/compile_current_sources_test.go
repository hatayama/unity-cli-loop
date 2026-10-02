package projectrunner

import (
	"bytes"
	"context"
	"encoding/json"
	"os"
	"runtime"
	"strings"
	"testing"
	"time"

	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

const (
	earlierCompileResult = `{"Success":false,"ErrorCount":9}`
	currentCompileResult = `{"Success":true,"ErrorCount":0,"WarningCount":0}`
)

// Verifies that a caller needing the current sources compiled does not take an earlier timed-out
// compile's stored result as its own: it sends a new compile and reports that compile's result.
func TestRunCompileOfCurrentSourcesIgnoresStoredResultOfEarlierCompile(t *testing.T) {
	if runtime.GOOS == "windows" {
		t.Skip("TCP endpoint injection is only used by this non-Windows client test")
	}

	enableCliVibeLog(t)
	endpoint, serverErr := startCompileAcceptOnceServer(t)
	projectRoot := t.TempDir()
	earlierRequestID := "compile_earlier_timed_out"
	writeTestCompilePendingRecord(t, projectRoot, earlierRequestID)

	deps := compileWaitTestDeps(func(_ context.Context, _ unityipc.Connection, requestID string) (compileStatusResponse, error) {
		if requestID == earlierRequestID {
			return compileStatusResponse{Ready: true, HasResult: true, Result: json.RawMessage(earlierCompileResult)}, nil
		}
		return compileStatusResponse{Ready: true, HasResult: true, Result: json.RawMessage(currentCompileResult)}, nil
	})
	connection := unityipc.Connection{Endpoint: endpoint, ProjectRoot: projectRoot}
	var stderr bytes.Buffer

	result := runCompileOfCurrentSourcesResultWithDeps(context.Background(), connection, &stderr, deps)

	assertCurrentCompileResult(t, result, stderr.String())
	assertPendingRecordCleared(t, projectRoot)
	logContent := readOnlyCliVibeLog(t, projectRoot)
	if !strings.Contains(logContent, `"operation":"cli_compile_request_prepared"`) {
		t.Fatalf("a compile of the current sources must be sent:\n%s", logContent)
	}
	assertNoServerError(t, serverErr)
}

// Verifies that when the earlier timed-out compile is still running, a caller needing the current
// sources waits for it to finish and then sends its own compile instead of returning the earlier one.
func TestRunCompileOfCurrentSourcesWaitsForInFlightCompileThenCompilesAgain(t *testing.T) {
	if runtime.GOOS == "windows" {
		t.Skip("TCP endpoint injection is only used by this non-Windows client test")
	}

	enableCliVibeLog(t)
	endpoint, serverErr := startCompileAcceptOnceServer(t)
	projectRoot := t.TempDir()
	earlierRequestID := "compile_earlier_in_flight"
	writeTestCompilePendingRecord(t, projectRoot, earlierRequestID)

	earlierQueries := 0
	deps := compileWaitTestDeps(func(_ context.Context, _ unityipc.Connection, requestID string) (compileStatusResponse, error) {
		if requestID != earlierRequestID {
			return compileStatusResponse{Ready: true, HasResult: true, Result: json.RawMessage(currentCompileResult)}, nil
		}
		earlierQueries++
		if earlierQueries == 1 {
			return compileStatusResponse{Ready: false, IsCompiling: true}, nil
		}
		return compileStatusResponse{Ready: true, HasResult: true, Result: json.RawMessage(earlierCompileResult)}, nil
	})
	connection := unityipc.Connection{Endpoint: endpoint, ProjectRoot: projectRoot}
	var stderr bytes.Buffer

	result := runCompileOfCurrentSourcesResultWithDeps(context.Background(), connection, &stderr, deps)

	assertCurrentCompileResult(t, result, stderr.String())
	assertPendingRecordCleared(t, projectRoot)
	if earlierQueries < 2 {
		t.Fatalf("the in-flight compile must be waited for first, queried %d times", earlierQueries)
	}
	logContent := readOnlyCliVibeLog(t, projectRoot)
	if !strings.Contains(logContent, `"operation":"cli_compile_request_prepared"`) {
		t.Fatalf("a compile of the current sources must be sent after the wait:\n%s", logContent)
	}
	assertNoServerError(t, serverErr)
}

// Verifies that when the earlier compile is still running past the wait, the caller reports the
// timeout and keeps the pending record, since Unity would reject a new compile as busy.
func TestRunCompileOfCurrentSourcesReportsTimeoutWhileEarlierCompileRuns(t *testing.T) {
	enableCliVibeLog(t)
	projectRoot := t.TempDir()
	earlierRequestID := "compile_earlier_still_running"
	writeTestCompilePendingRecord(t, projectRoot, earlierRequestID)

	deps := compileWaitTestDeps(func(context.Context, unityipc.Connection, string) (compileStatusResponse, error) {
		return compileStatusResponse{Ready: false, IsCompiling: true}, nil
	})
	connection := unityipc.Connection{
		Endpoint:    unityipc.Endpoint{Network: "tcp", Address: "127.0.0.1:1"},
		ProjectRoot: projectRoot,
	}
	var stderr bytes.Buffer

	result := runCompileOfCurrentSourcesWithTimeoutForTest(context.Background(), connection, &stderr, deps, 1)

	if result.exitCode != 1 {
		t.Fatalf("expected the wait timeout to fail: code=%d stderr=%s", result.exitCode, stderr.String())
	}
	if !strings.Contains(stderr.String(), "COMPILE_WAIT_TIMEOUT") {
		t.Fatalf("expected COMPILE_WAIT_TIMEOUT on stderr: %s", stderr.String())
	}
	if _, err := os.Stat(compilePendingRecordPath(projectRoot)); err != nil {
		t.Fatalf("pending record must be kept while the earlier compile runs: %v", err)
	}
	if strings.Contains(readOnlyCliVibeLog(t, projectRoot), `"operation":"cli_compile_request_prepared"`) {
		t.Fatalf("no compile may be sent while the earlier one still runs")
	}
}

func runCompileOfCurrentSourcesWithTimeoutForTest(
	ctx context.Context,
	connection unityipc.Connection,
	stderr *bytes.Buffer,
	deps compileWaitDeps,
	timeoutSeconds int,
) compileExecutionResult {
	return runCompileWithReattachPolicy(
		ctx,
		connection,
		map[string]any{compileWaitTimeoutParam: timeoutSeconds},
		stderr,
		deps,
		compileReattachRequiresCurrentSources)
}

func writeTestCompilePendingRecord(t *testing.T, projectRoot string, requestID string) {
	t.Helper()
	if err := writeCompilePendingRecord(projectRoot, compilePendingRecord{
		RequestID:     requestID,
		TimedOutAtUtc: time.Now().UTC().Add(-time.Minute),
	}); err != nil {
		t.Fatalf("write pending record failed: %v", err)
	}
}

func assertCurrentCompileResult(t *testing.T, result compileExecutionResult, stderr string) {
	t.Helper()
	if result.exitCode != 0 {
		t.Fatalf("expected the current compile to succeed: code=%d stderr=%s", result.exitCode, stderr)
	}
	if strings.Contains(string(result.result), `"ErrorCount":9`) {
		t.Fatalf("the earlier compile's result must not stand in for the current one: %s", result.result)
	}
	if !strings.Contains(string(result.result), `"Success":true`) {
		t.Fatalf("expected the current compile's result: %s", result.result)
	}
}

func assertPendingRecordCleared(t *testing.T, projectRoot string) {
	t.Helper()
	if _, err := os.Stat(compilePendingRecordPath(projectRoot)); !os.IsNotExist(err) {
		t.Fatalf("pending record should be cleared: %v", err)
	}
}

func assertNoServerError(t *testing.T, serverErr <-chan error) {
	t.Helper()
	select {
	case err := <-serverErr:
		t.Fatalf("server failed: %v", err)
	default:
	}
}
