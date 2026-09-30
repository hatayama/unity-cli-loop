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

const (
	attachProbeTestPendingErrorCount    = 2
	attachProbeTestNewCompileErrorCount = 5
	// Why 10: long enough for a loaded machine to finish the waits these tests expect, short enough
	// that a regression which waits where it must not fails in seconds instead of the default 10 minutes.
	attachProbeTestWaitTimeoutSeconds = 10
	// Why 20: compileWaitTestDeps gives the probe a 40ms deadline at a 5ms interval, so it queries at
	// most 10 times, and timers never fire early, so a slow machine only lowers that count. The probe
	// therefore always ends on one of these answers, and the wait sees the rest.
	attachProbeTestUnansweredCalls = 20
	// Why 200ms: tests whose answers change after the first query need the probe to query more than
	// once even when the machine stalls on that first query.
	attachProbeTestLongProbeTimeout = 200 * time.Millisecond
	// Why 100: a 200ms probe at a 5ms interval queries at most 42 times.
	attachProbeTestLongProbeUnansweredCalls = 100

	vibeLogAttachStart       = `"operation":"cli_compile_attach_start"`
	vibeLogProbeFailed       = `"operation":"cli_compile_attach_probe_failed"`
	vibeLogNewCompileRequest = `"operation":"cli_compile_request_prepared"`
	vibeLogNextWaiting       = `"next":"waiting"`
	vibeLogNextNewCompile    = `"next":"new_compile"`
)

// Verifies a probe that Unity acknowledged but never answered waits for the in-flight compile instead
// of sending a new compile, which the Editor would reject as busy while that compile runs.
func TestRunCompileAttachUnansweredProbeWaitsForInFlightCompile(t *testing.T) {
	test := newAttachProbeTest(t, "compile_attach_unanswered_wait")
	deps := compileWaitTestDeps(pendingCompileStatusQuery(test.requestID, func(call int) (compileStatusResponse, error) {
		if call <= attachProbeTestUnansweredCalls {
			return compileStatusResponse{}, unansweredStatusQueryError()
		}
		return finishedCompileStatus(attachProbeTestPendingErrorCount), nil
	}))
	params := map[string]any{compileWaitTimeoutParam: attachProbeTestWaitTimeoutSeconds}

	code, stdout, stderr := test.runCompile(context.Background(), params, deps)
	if code != 1 {
		t.Fatalf("expected failed compile envelope: code=%d stderr=%s", code, stderr)
	}
	if got := compileResultErrorCount(t, stdout); got != attachProbeTestPendingErrorCount {
		t.Fatalf("expected the in-flight compile's result, got ErrorCount %d", got)
	}
	test.assertRecordCleared(t)
	test.assertVibeLog(t,
		[]string{vibeLogProbeFailed, vibeLogNextWaiting, vibeLogAttachStart, `"attach_outcome":"completed"`},
		[]string{vibeLogNewCompileRequest})
	test.assertServerHealthy(t)
}

// Verifies a probe and a wait that Unity never answers end in COMPILE_WAIT_TIMEOUT without a new
// compile, and leave the pending record as the first timeout wrote it so a later rerun can reattach.
func TestRunCompileAttachUnansweredProbeRetimesOutKeepsRecord(t *testing.T) {
	test := newAttachProbeTest(t, "compile_attach_unanswered_retimeout")
	deps := compileWaitTestDeps(pendingCompileStatusQuery(test.requestID, func(int) (compileStatusResponse, error) {
		return compileStatusResponse{}, unansweredStatusQueryError()
	}))
	params := map[string]any{compileWaitTimeoutParam: 1}

	code, _, stderr := test.runCompile(context.Background(), params, deps)
	if code != 1 {
		t.Fatalf("expected timeout exit 1: code=%d stderr=%s", code, stderr)
	}
	if !strings.Contains(stderr, "Compile status wait timed out after 1000ms") {
		t.Fatalf("expected COMPILE_WAIT_TIMEOUT from the reattach wait: %s", stderr)
	}
	test.assertRecordUnchanged(t)
	test.assertVibeLog(t,
		[]string{vibeLogNextWaiting, `"attach_outcome":"timeout"`},
		[]string{vibeLogNewCompileRequest})
	test.assertServerHealthy(t)
}

// Verifies a wait entered after an unanswered probe still gives up on a compile Unity no longer knows
// and starts a new one, as it does after a probe that Unity answered.
func TestRunCompileAttachUnansweredProbeThenDisappearedStartsNewCompile(t *testing.T) {
	test := newAttachProbeTest(t, "compile_attach_unanswered_disappeared")
	deps := compileWaitTestDeps(pendingCompileStatusQuery(test.requestID, func(call int) (compileStatusResponse, error) {
		if call <= attachProbeTestUnansweredCalls {
			return compileStatusResponse{}, unansweredStatusQueryError()
		}
		return compileStatusResponse{Ready: true, HasResult: false}, nil
	}))
	params := map[string]any{compileWaitTimeoutParam: attachProbeTestWaitTimeoutSeconds}

	code, stdout, stderr := test.runCompile(context.Background(), params, deps)
	if code != 1 {
		t.Fatalf("expected failed compile envelope: code=%d stderr=%s", code, stderr)
	}
	if got := compileResultErrorCount(t, stdout); got != attachProbeTestNewCompileErrorCount {
		t.Fatalf("expected the new compile's result, got ErrorCount %d", got)
	}
	test.assertRecordCleared(t)
	test.assertVibeLog(t,
		[]string{vibeLogNextWaiting, vibeLogAttachStart, `"attach_outcome":"disappeared"`, vibeLogNewCompileRequest},
		nil)
	test.assertServerHealthy(t)
}

// Verifies a probe that timed out before Unity acknowledged it keeps the pending record and starts a
// new compile: without the ack nothing shows the Editor is still working on the pending compile.
func TestRunCompileAttachUnacknowledgedProbeTimeoutStartsNewCompile(t *testing.T) {
	test := newAttachProbeTest(t, "compile_attach_unacknowledged_probe")
	deps := compileWaitTestDeps(pendingCompileStatusQuery(test.requestID, func(int) (compileStatusResponse, error) {
		return compileStatusResponse{}, unacknowledgedStatusQueryError()
	}))
	params := map[string]any{compileWaitTimeoutParam: attachProbeTestWaitTimeoutSeconds}

	code, stdout, stderr := test.runCompile(context.Background(), params, deps)
	if code != 1 {
		t.Fatalf("expected failed compile envelope: code=%d stderr=%s", code, stderr)
	}
	if got := compileResultErrorCount(t, stdout); got != attachProbeTestNewCompileErrorCount {
		t.Fatalf("expected the new compile's result, got ErrorCount %d", got)
	}
	test.assertRecordUnchanged(t)
	test.assertVibeLog(t,
		[]string{vibeLogProbeFailed, vibeLogNextNewCompile, vibeLogNewCompileRequest},
		[]string{vibeLogAttachStart})
	test.assertServerHealthy(t)
}

// Verifies --force-recompile does not send a new compile while an unanswered probe shows a compile
// still holds the Editor; the wait warns that the flag was not applied, as it does after an answered probe.
func TestRunCompileAttachUnansweredProbeWithForceRecompileWarnsAndWaits(t *testing.T) {
	test := newAttachProbeTest(t, "compile_attach_unanswered_force")
	deps := compileWaitTestDeps(pendingCompileStatusQuery(test.requestID, func(call int) (compileStatusResponse, error) {
		if call <= attachProbeTestUnansweredCalls {
			return compileStatusResponse{}, unansweredStatusQueryError()
		}
		return finishedCompileStatus(attachProbeTestPendingErrorCount), nil
	}))
	params := map[string]any{
		compileForceParam:       true,
		compileWaitTimeoutParam: attachProbeTestWaitTimeoutSeconds,
	}

	code, stdout, stderr := test.runCompile(context.Background(), params, deps)
	if code != 1 {
		t.Fatalf("expected failed compile envelope: code=%d stderr=%s", code, stderr)
	}
	if !strings.Contains(stderr, "--force-recompile is not applied") {
		t.Fatalf("expected force-recompile ignored warning: %s", stderr)
	}
	if got := compileResultErrorCount(t, stdout); got != attachProbeTestPendingErrorCount {
		t.Fatalf("expected the in-flight compile's result, got ErrorCount %d", got)
	}
	test.assertVibeLog(t, []string{vibeLogNextWaiting}, []string{vibeLogNewCompileRequest})
	test.assertServerHealthy(t)
}

// Verifies a probe that cannot connect keeps the pending record and starts a new compile, even when
// the connection error wraps a timeout.
func TestRunCompileAttachUnreachableProbeKeepsRecordAndStartsNewCompile(t *testing.T) {
	test := newAttachProbeTest(t, "compile_attach_unreachable_probe")
	deps := compileWaitTestDeps(pendingCompileStatusQuery(test.requestID, func(int) (compileStatusResponse, error) {
		return compileStatusResponse{}, unreachableStatusQueryError(test.connection.ProjectRoot)
	}))
	params := map[string]any{compileWaitTimeoutParam: attachProbeTestWaitTimeoutSeconds}

	code, stdout, stderr := test.runCompile(context.Background(), params, deps)
	if code != 1 {
		t.Fatalf("expected failed compile envelope: code=%d stderr=%s", code, stderr)
	}
	if got := compileResultErrorCount(t, stdout); got != attachProbeTestNewCompileErrorCount {
		t.Fatalf("expected the new compile's result, got ErrorCount %d", got)
	}
	test.assertRecordUnchanged(t)
	test.assertVibeLog(t,
		[]string{vibeLogProbeFailed, vibeLogNextNewCompile, vibeLogNewCompileRequest},
		[]string{vibeLogAttachStart})
	test.assertServerHealthy(t)
}

// Verifies the last probe answer decides: an Editor that was unreachable at first and then
// acknowledged the query without answering it is waited on.
func TestRunCompileAttachProbeLastAnswerDecidesAfterReconnect(t *testing.T) {
	test := newAttachProbeTest(t, "compile_attach_probe_reconnect")
	deps := compileWaitTestDeps(pendingCompileStatusQuery(test.requestID, func(call int) (compileStatusResponse, error) {
		switch {
		case call == 1:
			return compileStatusResponse{}, unreachableStatusQueryError(test.connection.ProjectRoot)
		case call <= attachProbeTestLongProbeUnansweredCalls:
			return compileStatusResponse{}, unansweredStatusQueryError()
		default:
			return finishedCompileStatus(attachProbeTestPendingErrorCount), nil
		}
	}))
	deps.attachProbeTimeout = attachProbeTestLongProbeTimeout
	params := map[string]any{compileWaitTimeoutParam: attachProbeTestWaitTimeoutSeconds}

	code, stdout, stderr := test.runCompile(context.Background(), params, deps)
	if code != 1 {
		t.Fatalf("expected failed compile envelope: code=%d stderr=%s", code, stderr)
	}
	if got := compileResultErrorCount(t, stdout); got != attachProbeTestPendingErrorCount {
		t.Fatalf("expected the in-flight compile's result, got ErrorCount %d", got)
	}
	test.assertVibeLog(t,
		[]string{vibeLogNextWaiting, vibeLogAttachStart},
		[]string{vibeLogNewCompileRequest})
	test.assertServerHealthy(t)
}

// Verifies the last probe answer decides: an Editor that acknowledged the query without answering and
// then stopped accepting connections is left to a new compile, which retries the connection.
func TestRunCompileAttachProbeLastAnswerDecidesAfterDisconnect(t *testing.T) {
	test := newAttachProbeTest(t, "compile_attach_probe_disconnect")
	deps := compileWaitTestDeps(pendingCompileStatusQuery(test.requestID, func(call int) (compileStatusResponse, error) {
		if call == 1 {
			return compileStatusResponse{}, unansweredStatusQueryError()
		}
		return compileStatusResponse{}, unreachableStatusQueryError(test.connection.ProjectRoot)
	}))
	deps.attachProbeTimeout = attachProbeTestLongProbeTimeout
	params := map[string]any{compileWaitTimeoutParam: attachProbeTestWaitTimeoutSeconds}

	code, stdout, stderr := test.runCompile(context.Background(), params, deps)
	if code != 1 {
		t.Fatalf("expected failed compile envelope: code=%d stderr=%s", code, stderr)
	}
	if got := compileResultErrorCount(t, stdout); got != attachProbeTestNewCompileErrorCount {
		t.Fatalf("expected the new compile's result, got ErrorCount %d", got)
	}
	test.assertRecordUnchanged(t)
	test.assertVibeLog(t,
		[]string{vibeLogProbeFailed, vibeLogNextNewCompile, vibeLogNewCompileRequest},
		[]string{vibeLogAttachStart})
	test.assertServerHealthy(t)
}

// Verifies a probe cut off by the caller's own deadline does not start the wait even though Unity had
// acknowledged the query, because the command has no time left to wait.
func TestRunCompileAttachProbeContextDeadlineDoesNotWait(t *testing.T) {
	test := newAttachProbeTest(t, "compile_attach_probe_context_deadline")
	deps := compileWaitTestDeps(nil)
	deps.queryCompileStatus = func(ctx context.Context, _ unityipc.Connection, requestID string) (compileStatusResponse, error) {
		if requestID != test.requestID {
			return finishedCompileStatus(attachProbeTestNewCompileErrorCount), nil
		}
		calledAt := time.Now()
		<-ctx.Done()
		// Why also wait out the probe deadline: the probe set it before this call, so the probe then
		// reports this error. Returning before it lets the probe stop on its own check of the finished
		// context, and that bare ctx.Err() starts a new compile even without the caller-deadline check.
		time.Sleep(time.Until(calledAt.Add(deps.attachProbeTimeout)))
		// This is how a status query ends when the caller's deadline passes after Unity's ack.
		return compileStatusResponse{}, classifyCompileStatusQueryError(
			unityipc.UnitySendOutcome{RequestDispatched: true, RequestAccepted: true},
			ctx.Err(),
		)
	}
	params := map[string]any{compileWaitTimeoutParam: attachProbeTestWaitTimeoutSeconds}
	// Why a deadline, not a cancel: only a deadline that passes after the ack marks the query as
	// unanswered, which is the case the caller-deadline check exists for.
	ctx, cancel := context.WithTimeout(context.Background(), 100*time.Millisecond)
	defer cancel()

	_, _, _ = test.runCompile(ctx, params, deps)
	test.assertRecordUnchanged(t)
	test.assertVibeLog(t,
		[]string{vibeLogProbeFailed, vibeLogNextNewCompile},
		[]string{vibeLogNextWaiting, vibeLogAttachStart})
}

// attachProbeTest holds what the probe-failure reattach tests share: a pending record for a compile
// that timed out a minute ago, and a server that accepts the one new compile a test may send.
type attachProbeTest struct {
	connection unityipc.Connection
	requestID  string
	timedOutAt time.Time
	serverErr  <-chan error
}

func newAttachProbeTest(t *testing.T, requestID string) attachProbeTest {
	t.Helper()
	if runtime.GOOS == "windows" {
		t.Skip("TCP endpoint injection is only used by this non-Windows client test")
	}

	enableCliVibeLog(t)
	endpoint, serverErr := startCompileAcceptOnceServer(t)
	projectRoot := t.TempDir()
	timedOutAt := time.Now().UTC().Add(-time.Minute)
	if err := writeCompilePendingRecord(projectRoot, compilePendingRecord{
		RequestID:     requestID,
		TimedOutAtUtc: timedOutAt,
	}); err != nil {
		t.Fatalf("write pending record failed: %v", err)
	}
	return attachProbeTest{
		connection: unityipc.Connection{Endpoint: endpoint, ProjectRoot: projectRoot},
		requestID:  requestID,
		timedOutAt: timedOutAt,
		serverErr:  serverErr,
	}
}

// runCompile runs compile with domain reload wait and returns the exit code, stdout, and stderr.
func (test attachProbeTest) runCompile(
	ctx context.Context,
	params map[string]any,
	deps compileWaitDeps,
) (int, string, string) {
	var stdout, stderr bytes.Buffer
	code := runCompileWithDomainReloadWaitWithDeps(ctx, test.connection, params, &stdout, &stderr, deps)
	return code, stdout.String(), stderr.String()
}

func (test attachProbeTest) assertRecordUnchanged(t *testing.T) {
	t.Helper()
	got, ok := readCompilePendingRecord(test.connection.ProjectRoot)
	if !ok {
		t.Fatal("pending record must remain")
	}
	if got.RequestID != test.requestID || !got.TimedOutAtUtc.Equal(test.timedOutAt) {
		t.Fatalf("pending record mutated: %#v", got)
	}
}

func (test attachProbeTest) assertRecordCleared(t *testing.T) {
	t.Helper()
	if _, err := os.Stat(compilePendingRecordPath(test.connection.ProjectRoot)); !os.IsNotExist(err) {
		t.Fatalf("pending record should be cleared: %v", err)
	}
}

// assertVibeLog checks that the CLI Vibe log holds every entry of logged and none of notLogged.
func (test attachProbeTest) assertVibeLog(t *testing.T, logged []string, notLogged []string) {
	t.Helper()
	logContent := readOnlyCliVibeLog(t, test.connection.ProjectRoot)
	for _, expected := range logged {
		if !strings.Contains(logContent, expected) {
			t.Fatalf("vibe log missing %q:\n%s", expected, logContent)
		}
	}
	for _, unexpected := range notLogged {
		if strings.Contains(logContent, unexpected) {
			t.Fatalf("vibe log must not contain %q:\n%s", unexpected, logContent)
		}
	}
}

func (test attachProbeTest) assertServerHealthy(t *testing.T) {
	t.Helper()
	select {
	case err := <-test.serverErr:
		t.Fatalf("server failed: %v", err)
	default:
	}
}

// pendingCompileStatusQuery answers status queries for the pending request with answer(call), where
// call counts those queries from 1, and answers any other request, which is a new compile, with a
// finished result whose ErrorCount tells the two compiles apart.
func pendingCompileStatusQuery(
	pendingRequestID string,
	answer func(call int) (compileStatusResponse, error),
) func(context.Context, unityipc.Connection, string) (compileStatusResponse, error) {
	calls := 0
	return func(_ context.Context, _ unityipc.Connection, requestID string) (compileStatusResponse, error) {
		if requestID != pendingRequestID {
			return finishedCompileStatus(attachProbeTestNewCompileErrorCount), nil
		}
		calls++
		return answer(calls)
	}
}

func finishedCompileStatus(errorCount int) compileStatusResponse {
	return compileStatusResponse{
		Ready:     true,
		HasResult: true,
		// Why Success false: a successful result makes the command wait for tool readiness, which a
		// temporary directory never reaches.
		Result: json.RawMessage(fmt.Sprintf(`{"Success":false,"ErrorCount":%d}`, errorCount)),
	}
}

// unansweredStatusQueryError is how a status query ends when Unity acknowledged it and the query
// deadline passed before the answer, which is what a blocked main thread produces.
func unansweredStatusQueryError() error {
	return classifyCompileStatusQueryError(
		unityipc.UnitySendOutcome{RequestDispatched: true, RequestAccepted: true},
		context.DeadlineExceeded,
	)
}

// unacknowledgedStatusQueryError is how a status query ends when its read deadline passes before
// any ack arrives.
func unacknowledgedStatusQueryError() error {
	return classifyCompileStatusQueryError(
		unityipc.UnitySendOutcome{RequestDispatched: true},
		fmt.Errorf("read unix: %w", os.ErrDeadlineExceeded),
	)
}

// unreachableStatusQueryError is how a status query ends when its dial times out.
func unreachableStatusQueryError(projectRoot string) error {
	return classifyCompileStatusQueryError(
		unityipc.UnitySendOutcome{},
		&unityipc.ConnectionAttemptError{ProjectRoot: projectRoot, Endpoint: "test", Cause: os.ErrDeadlineExceeded},
	)
}

// compileResultErrorCount reads ErrorCount from the compile result printed on stdout.
func compileResultErrorCount(t *testing.T, stdout string) int {
	t.Helper()
	var result struct {
		ErrorCount int
	}
	if err := json.Unmarshal([]byte(stdout), &result); err != nil {
		t.Fatalf("stdout is not a compile result: %v\n%s", err, stdout)
	}
	return result.ErrorCount
}
