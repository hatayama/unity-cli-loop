package projectrunner

import (
	"bytes"
	"context"
	"encoding/json"
	"io"
	"os"
	"runtime"
	"strings"
	"testing"
	"time"

	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

// The error counts tell the two results apart.
const (
	earlierCompileResult = `{"Success":false,"ErrorCount":9}`
	currentCompileResult = `{"Success":false,"ErrorCount":1}`
)

// Verifies that the hot-reload compile fallback and the run-tests implicit compile do not take an
// earlier timed-out compile's stored result as their own: each sends a new compile and reports it.
func TestCompilesOfCurrentSourcesIgnoreStoredResultOfEarlierCompile(t *testing.T) {
	if runtime.GOOS == "windows" {
		t.Skip("TCP endpoint injection is only used by this non-Windows client test")
	}

	callers := map[string]func(context.Context, unityipc.Connection, io.Writer, compileWaitDeps) compileExecutionResult{
		"hot-reload fallback": hotReloadFallbackCompileWithDeps,
		"run-tests implicit":  runTestsImplicitCompileWithDeps,
	}
	for name, compile := range callers {
		t.Run(name, func(t *testing.T) {
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

			result := compile(context.Background(), connection, &stderr, deps)

			assertCurrentCompileResult(t, result)
			assertPendingRecordCleared(t, projectRoot)
			logContent := readOnlyCliVibeLog(t, projectRoot)
			if !strings.Contains(logContent, `"operation":"cli_compile_request_prepared"`) {
				t.Fatalf("a compile of the current sources must be sent:\n%s", logContent)
			}
			assertNoServerError(t, serverErr)
		})
	}
}

// Verifies that when the earlier timed-out compile is still running, a caller needing the current
// sources waits until the Editor is Ready again before sending its own compile: Unity stores the
// earlier result while it is still compiling and would reject a compile sent in between.
func TestRunCompileOfCurrentSourcesWaitsForEditorReadyBeforeCompilingAgain(t *testing.T) {
	if runtime.GOOS == "windows" {
		t.Skip("TCP endpoint injection is only used by this non-Windows client test")
	}

	earlierSequences := map[string][]compileStatusResponse{
		"still compiling at the probe": {
			{Ready: false, IsCompiling: true},
			{Ready: false, IsCompiling: true, HasResult: true, Result: json.RawMessage(earlierCompileResult)},
			{Ready: true, HasResult: true, Result: json.RawMessage(earlierCompileResult)},
		},
		"result stored but not Ready at the probe": {
			{Ready: false, IsCompiling: true, HasResult: true, Result: json.RawMessage(earlierCompileResult)},
			{Ready: true, HasResult: true, Result: json.RawMessage(earlierCompileResult)},
		},
	}
	for name, sequence := range earlierSequences {
		t.Run(name, func(t *testing.T) {
			enableCliVibeLog(t)
			endpoint, serverErr := startCompileAcceptOnceServer(t)
			projectRoot := t.TempDir()
			earlierRequestID := "compile_earlier_in_flight"
			writeTestCompilePendingRecord(t, projectRoot, earlierRequestID)

			earlierQueries := 0
			earlierQueriesBeforeNewCompile := -1
			deps := compileWaitTestDeps(func(_ context.Context, _ unityipc.Connection, requestID string) (compileStatusResponse, error) {
				if requestID != earlierRequestID {
					if earlierQueriesBeforeNewCompile < 0 {
						earlierQueriesBeforeNewCompile = earlierQueries
					}
					return compileStatusResponse{Ready: true, HasResult: true, Result: json.RawMessage(currentCompileResult)}, nil
				}
				status := sequence[len(sequence)-1]
				if earlierQueries < len(sequence) {
					status = sequence[earlierQueries]
				}
				earlierQueries++
				return status, nil
			})
			connection := unityipc.Connection{Endpoint: endpoint, ProjectRoot: projectRoot}
			var stderr bytes.Buffer

			result := runCompileOfCurrentSourcesResultWithDeps(context.Background(), connection, &stderr, deps)

			assertCurrentCompileResult(t, result)
			assertPendingRecordCleared(t, projectRoot)
			if earlierQueriesBeforeNewCompile != len(sequence) {
				t.Fatalf("the new compile must follow the first Ready status (query %d), followed query %d",
					len(sequence), earlierQueriesBeforeNewCompile)
			}
			assertNoServerError(t, serverErr)
		})
	}
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

func assertCurrentCompileResult(t *testing.T, result compileExecutionResult) {
	t.Helper()
	if string(result.result) != currentCompileResult {
		t.Fatalf("expected the current compile's result, got: %s", result.result)
	}
	// Why 1: the current compile's result is a failed compile, and a caller must not go on as if
	// it had succeeded.
	if result.exitCode != 1 {
		t.Fatalf("expected the failed current compile to exit 1, got: %d", result.exitCode)
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
