package projectrunner

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"io"
	"os"
	"strings"
	"testing"
	"time"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"

	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

const (
	// Stops a wait that never ends instead of letting it poll through the whole default wait.
	compileRecoveryQueryLimit = 200
	// A compile error: definitive.
	compileRecoveryDefinitiveResult        = `{"Success":false,"ErrorCount":1,"WarningCount":0,"ErrorCode":null}`
	compileRecoveryAlreadyInProgressResult = `{"Success":false,"ErrorCode":"COMPILE_ALREADY_IN_PROGRESS","ErrorCount":1}`
)

// compileRecoverySend is one scripted end of a compile send.
type compileRecoverySend struct {
	outcome unityipc.UnitySendOutcome
	err     error
}

// compileRecoveryAnswer is one scripted answer to a compile status query.
type compileRecoveryAnswer struct {
	status compileStatusResponse
	err    error
}

// compileRecoveryScenario scripts the compile sends in order and, for the request each send
// carried, the status answers in order. A request's last answer repeats once its script runs out.
type compileRecoveryScenario struct {
	t             *testing.T
	sends         []compileRecoverySend
	answers       [][]compileRecoveryAnswer
	sentIDs       []string
	attemptOf     map[string]int
	nextAnswer    map[string]int
	queries       map[string]int
	totalQueries  int
	cancel        context.CancelFunc
	cancelAttempt int
	cancelAtQuery int
}

func newCompileRecoveryScenario(
	t *testing.T,
	sends []compileRecoverySend,
	answers ...[]compileRecoveryAnswer,
) *compileRecoveryScenario {
	t.Helper()
	return &compileRecoveryScenario{
		t:          t,
		sends:      sends,
		answers:    answers,
		attemptOf:  map[string]int{},
		nextAnswer: map[string]int{},
		queries:    map[string]int{},
	}
}

// cancelWhen cancels the command once the request of the given send (zero-based) has been queried
// queryCount times.
func (scenario *compileRecoveryScenario) cancelWhen(cancel context.CancelFunc, attempt int, queryCount int) {
	scenario.cancel = cancel
	scenario.cancelAttempt = attempt
	scenario.cancelAtQuery = queryCount
}

func (scenario *compileRecoveryScenario) deps() compileWaitDeps {
	deps := compileWaitTestDeps(scenario.query)
	deps.sendCompile = scenario.send
	deps.freshWaitPollInterval = time.Millisecond
	return deps
}

func (scenario *compileRecoveryScenario) send(
	_ context.Context,
	_ unityipc.Connection,
	_ string,
	params map[string]any,
	_ unityipc.ProgressFunc,
	_ time.Duration,
) (unityipc.UnitySendOutcome, error) {
	attempt := len(scenario.sentIDs)
	if attempt >= len(scenario.sends) {
		scenario.t.Fatalf("unexpected compile send #%d: the scenario allows %d", attempt+1, len(scenario.sends))
	}
	requestID, _ := params[compileRequestIDParam].(string)
	scenario.sentIDs = append(scenario.sentIDs, requestID)
	scenario.attemptOf[requestID] = attempt
	scenario.nextAnswer[requestID] = 0
	step := scenario.sends[attempt]
	return step.outcome, step.err
}

func (scenario *compileRecoveryScenario) query(
	ctx context.Context,
	_ unityipc.Connection,
	requestID string,
) (compileStatusResponse, error) {
	scenario.totalQueries++
	if scenario.totalQueries > compileRecoveryQueryLimit {
		scenario.t.Fatalf("compile status was queried more than %d times: the wait never ended", compileRecoveryQueryLimit)
	}
	attempt, ok := scenario.attemptOf[requestID]
	if !ok || attempt >= len(scenario.answers) {
		scenario.t.Fatalf("compile status was queried for a request with no scripted answers: %q", requestID)
	}
	answers := scenario.answers[attempt]
	index := scenario.nextAnswer[requestID]
	if index < len(answers)-1 {
		scenario.nextAnswer[requestID] = index + 1
	}
	// Why count only before the cancellation: the wait may poll once more after it, because its
	// select picks at random when the poll tick and the cancellation are both ready.
	if ctx.Err() == nil {
		scenario.queries[requestID]++
		if scenario.cancel != nil && attempt == scenario.cancelAttempt && scenario.queries[requestID] == scenario.cancelAtQuery {
			scenario.cancel()
		}
	}
	answer := answers[index]
	return answer.status, answer.err
}

func (scenario *compileRecoveryScenario) sendCount() int {
	return len(scenario.sentIDs)
}

// queriesOf returns how often the request of the given send was queried before any cancellation.
func (scenario *compileRecoveryScenario) queriesOf(attempt int) int {
	return scenario.queries[scenario.sentIDs[attempt]]
}

func compileRecoveryAcceptedOutcome() unityipc.UnitySendOutcome {
	return unityipc.UnitySendOutcome{RequestDispatched: true, RequestAccepted: true}
}

// recoverySendDisconnected is a send that Unity accepted before the connection dropped.
func recoverySendDisconnected() compileRecoverySend {
	return compileRecoverySend{outcome: compileRecoveryAcceptedOutcome(), err: io.EOF}
}

// recoverySendTimedOut is a send that Unity accepted without a final response in time.
func recoverySendTimedOut(t *testing.T) compileRecoverySend {
	t.Helper()
	if !clierrors.IsFinalResponseTimeoutError(os.ErrDeadlineExceeded) {
		t.Fatal("os.ErrDeadlineExceeded must classify as a final response timeout")
	}
	return compileRecoverySend{outcome: compileRecoveryAcceptedOutcome(), err: os.ErrDeadlineExceeded}
}

// recoverySendAnswered is a send that received Unity's final response.
func recoverySendAnswered() compileRecoverySend {
	return compileRecoverySend{outcome: compileRecoveryAcceptedOutcome()}
}

// recoveryMissing is Unity Ready with no result for the request.
func recoveryMissing() compileRecoveryAnswer {
	return compileRecoveryAnswer{status: compileStatusResponse{Ready: true}}
}

func recoveryMissingTimes(count int) []compileRecoveryAnswer {
	answers := make([]compileRecoveryAnswer, 0, count)
	for range count {
		answers = append(answers, recoveryMissing())
	}
	return answers
}

func recoveryDone(result string) compileRecoveryAnswer {
	return compileRecoveryAnswer{status: compileStatusResponse{Ready: true, HasResult: true, Result: json.RawMessage(result)}}
}

func recoveryCompiling() compileRecoveryAnswer {
	return compileRecoveryAnswer{status: compileStatusResponse{IsCompiling: true}}
}

func recoveryQueryFailure(err error) compileRecoveryAnswer {
	return compileRecoveryAnswer{err: err}
}

func compileRecoveryRejection(errorCode string) string {
	return `{"Success":false,"ErrorCode":"` + errorCode + `","ErrorCount":1}`
}

// compileRecoveryBusyRejectionAnswers is Unity storing a busy rejection while it still compiles,
// then turning Ready.
func compileRecoveryBusyRejectionAnswers(errorCode string) []compileRecoveryAnswer {
	return []compileRecoveryAnswer{
		{status: compileStatusResponse{HasResult: true, IsCompiling: true, Result: json.RawMessage(compileRecoveryRejection(errorCode))}},
		recoveryDone(compileRecoveryRejection(errorCode)),
	}
}

// runCompileRecovery runs the resending entry against the scenario.
func runCompileRecovery(
	t *testing.T,
	ctx context.Context,
	scenario *compileRecoveryScenario,
	params map[string]any,
) (compileExecutionResult, string) {
	t.Helper()
	var stderr bytes.Buffer
	result := runFreshCompileRecoveringWithDeps(ctx, unreachableConnection(t.TempDir()), params, &stderr, scenario.deps())
	return result, stderr.String()
}

func vibeLogContextNumber(t *testing.T, entry map[string]any, key string) float64 {
	t.Helper()
	contextMap, ok := entry["context"].(map[string]any)
	if !ok {
		t.Fatalf("vibe log context missing: %#v", entry)
	}
	value, ok := contextMap[key].(float64)
	if !ok {
		t.Fatalf("vibe log context %s is not a number: %#v", key, contextMap[key])
	}
	return value
}

// compileResendLogEntry returns the only resend entry in the project's CLI vibe log.
func compileResendLogEntry(t *testing.T, projectRoot string) map[string]any {
	t.Helper()
	entries := cliVibeEntriesForOperation(t, readOnlyCliVibeLog(t, projectRoot), "cli_compile_request_resend")
	if len(entries) != 1 {
		t.Fatalf("compile resend log entries = %d, want 1", len(entries))
	}
	return entries[0]
}

// Verifies a request Unity lost across a server restart is sent again with a new request ID after
// three Ready answers without a result, instead of waiting out the whole timeout, and that the
// resend is logged as a lost request under the first request's ID.
func TestFreshCompileRecoveryResendsWhenUnityLostTheRequest(t *testing.T) {
	enableCliVibeLog(t)
	scenario := newCompileRecoveryScenario(t,
		[]compileRecoverySend{recoverySendDisconnected(), recoverySendAnswered()},
		[]compileRecoveryAnswer{recoveryMissing()},
		[]compileRecoveryAnswer{recoveryDone(compileRecoveryDefinitiveResult)},
	)
	projectRoot := t.TempDir()
	var stderrBuffer bytes.Buffer

	result := runFreshCompileRecoveringWithDeps(context.Background(), unreachableConnection(projectRoot), map[string]any{}, &stderrBuffer, scenario.deps())
	stderr := stderrBuffer.String()

	if scenario.sendCount() != 2 {
		t.Fatalf("compile sends = %d, want 2", scenario.sendCount())
	}
	if scenario.sentIDs[0] == scenario.sentIDs[1] {
		t.Fatalf("the resent compile must carry a new request ID: %q", scenario.sentIDs[0])
	}
	if string(result.result) != compileRecoveryDefinitiveResult {
		t.Fatalf("result = %s, want the resent compile's result", result.result)
	}
	if strings.Contains(stderr, "COMPILE_WAIT_TIMEOUT") {
		t.Fatalf("a resent compile must not report a wait timeout:\n%s", stderr)
	}
	if queries := scenario.queriesOf(0); queries != 3 {
		t.Fatalf("queries for the lost request = %d, want 3", queries)
	}
	resend := compileResendLogEntry(t, projectRoot)
	if reason := vibeLogContextString(t, resend, "reason"); reason != "request_missing" {
		t.Fatalf("resend reason = %q, want request_missing", reason)
	}
	if attempt := vibeLogContextNumber(t, resend, "attempt"); attempt != 1 {
		t.Fatalf("resend attempt = %v, want 1", attempt)
	}
	if requestID := vibeLogContextString(t, resend, "request_id"); requestID != scenario.sentIDs[0] {
		t.Fatalf("resend request_id = %q, want the lost request's ID %q", requestID, scenario.sentIDs[0])
	}
}

// Verifies a status query that finds the server gone, dropped mid-query or with nobody listening,
// counts as a server restart, so a request still missing afterwards is sent again.
func TestFreshCompileRecoveryResendsWhenAStatusQueryLosesTheServer(t *testing.T) {
	cases := []struct {
		name     string
		queryErr error
	}{
		{name: "dropped mid-query", queryErr: io.EOF},
		{name: "nobody listening", queryErr: &unityipc.ConnectionAttemptError{Cause: errors.New("connect: connection refused")}},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			scenario := newCompileRecoveryScenario(t,
				[]compileRecoverySend{recoverySendTimedOut(t), recoverySendAnswered()},
				append([]compileRecoveryAnswer{recoveryQueryFailure(testCase.queryErr)}, recoveryMissingTimes(3)...),
				[]compileRecoveryAnswer{recoveryDone(compileRecoveryDefinitiveResult)},
			)

			result, _ := runCompileRecovery(t, context.Background(), scenario, map[string]any{})

			if scenario.sendCount() != 2 {
				t.Fatalf("compile sends = %d, want 2", scenario.sendCount())
			}
			if string(result.result) != compileRecoveryDefinitiveResult {
				t.Fatalf("result = %s, want the resent compile's result", result.result)
			}
		})
	}
}

// Verifies an answer that reports a domain reload in progress counts as a server restart, so a
// request still missing afterwards is sent again.
func TestFreshCompileRecoveryResendsAfterSeeingTheDomainReloadFlag(t *testing.T) {
	scenario := newCompileRecoveryScenario(t,
		[]compileRecoverySend{recoverySendTimedOut(t), recoverySendAnswered()},
		append(
			[]compileRecoveryAnswer{{status: compileStatusResponse{IsDomainReloadInProgress: true}}},
			recoveryMissingTimes(3)...,
		),
		[]compileRecoveryAnswer{recoveryDone(compileRecoveryDefinitiveResult)},
	)

	result, _ := runCompileRecovery(t, context.Background(), scenario, map[string]any{})

	if scenario.sendCount() != 2 {
		t.Fatalf("compile sends = %d, want 2", scenario.sendCount())
	}
	if string(result.result) != compileRecoveryDefinitiveResult {
		t.Fatalf("result = %s, want the resent compile's result", result.result)
	}
}

// Verifies Ready answers without a result never lead to a resend when nothing showed the server
// went away: a live request answers that way until its compile starts, and a resend then would be
// rejected as busy.
func TestFreshCompileRecoveryKeepsWaitingWhenTheServerWasNeverLost(t *testing.T) {
	cases := []struct {
		name         string
		firstAnswers []compileRecoveryAnswer
	}{
		{name: "no failed query"},
		{
			name:         "query acknowledged but unanswered",
			firstAnswers: []compileRecoveryAnswer{recoveryQueryFailure(&compileStatusUnansweredError{cause: os.ErrDeadlineExceeded})},
		},
		{
			name:         "other query error",
			firstAnswers: []compileRecoveryAnswer{recoveryQueryFailure(errors.New("unity error: boom"))},
		},
		{
			name:         "connect timed out",
			firstAnswers: []compileRecoveryAnswer{recoveryQueryFailure(&unityipc.ConnectionAttemptError{Cause: context.DeadlineExceeded})},
		},
		{
			name:         "connect denied",
			firstAnswers: []compileRecoveryAnswer{recoveryQueryFailure(&unityipc.ConnectionAttemptError{Cause: os.ErrPermission})},
		},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			script := append([]compileRecoveryAnswer{}, testCase.firstAnswers...)
			script = append(script, recoveryMissingTimes(6)...)
			script = append(script, recoveryDone(compileRecoveryDefinitiveResult))
			scenario := newCompileRecoveryScenario(t, []compileRecoverySend{recoverySendTimedOut(t)}, script)

			result, _ := runCompileRecovery(t, context.Background(), scenario, map[string]any{})

			if scenario.sendCount() != 1 {
				t.Fatalf("compile sends = %d, want 1", scenario.sendCount())
			}
			if string(result.result) != compileRecoveryDefinitiveResult {
				t.Fatalf("result = %s, want the compile's result", result.result)
			}
		})
	}
}

// Verifies two Ready answers without a result are not enough for a resend: a single such answer
// can race Unity storing the result.
func TestFreshCompileRecoveryDoesNotResendBeforeThreeMissingAnswers(t *testing.T) {
	scenario := newCompileRecoveryScenario(t,
		[]compileRecoverySend{recoverySendDisconnected()},
		append(recoveryMissingTimes(2), recoveryDone(compileRecoveryDefinitiveResult)),
	)

	result, _ := runCompileRecovery(t, context.Background(), scenario, map[string]any{})

	if scenario.sendCount() != 1 {
		t.Fatalf("compile sends = %d, want 1", scenario.sendCount())
	}
	if string(result.result) != compileRecoveryDefinitiveResult {
		t.Fatalf("result = %s, want the compile's result", result.result)
	}
}

// Verifies a compiling answer between Ready answers without a result restarts their count.
func TestFreshCompileRecoveryMissingStreakRestartsAfterABusyAnswer(t *testing.T) {
	script := recoveryMissingTimes(2)
	script = append(script, recoveryCompiling())
	script = append(script, recoveryMissingTimes(2)...)
	script = append(script, recoveryDone(compileRecoveryDefinitiveResult))
	scenario := newCompileRecoveryScenario(t, []compileRecoverySend{recoverySendDisconnected()}, script)

	result, _ := runCompileRecovery(t, context.Background(), scenario, map[string]any{})

	if scenario.sendCount() != 1 {
		t.Fatalf("compile sends = %d, want 1", scenario.sendCount())
	}
	if string(result.result) != compileRecoveryDefinitiveResult {
		t.Fatalf("result = %s, want the compile's result", result.result)
	}
}

// Verifies a compile Unity rejected because it was compiling or updating is sent again with a new
// request ID once the wait has seen the Editor Ready.
func TestFreshCompileRecoveryResendsAfterUnityRejectedTheCompileAsBusy(t *testing.T) {
	for _, errorCode := range []string{"COMPILE_ALREADY_IN_PROGRESS", "COMPILE_EDITOR_UPDATING"} {
		t.Run(errorCode, func(t *testing.T) {
			scenario := newCompileRecoveryScenario(t,
				[]compileRecoverySend{recoverySendAnswered(), recoverySendAnswered()},
				compileRecoveryBusyRejectionAnswers(errorCode),
				[]compileRecoveryAnswer{recoveryDone(compileRecoveryDefinitiveResult)},
			)

			result, _ := runCompileRecovery(t, context.Background(), scenario, map[string]any{})

			if scenario.sendCount() != 2 {
				t.Fatalf("compile sends = %d, want 2", scenario.sendCount())
			}
			if scenario.sentIDs[0] == scenario.sentIDs[1] {
				t.Fatalf("the resent compile must carry a new request ID: %q", scenario.sentIDs[0])
			}
			if string(result.result) != compileRecoveryDefinitiveResult {
				t.Fatalf("result = %s, want the resent compile's result", result.result)
			}
		})
	}
}

// Verifies a definitive compile failure is returned as it is, without a resend.
func TestFreshCompileRecoveryReturnsADefinitiveFailureWithoutResending(t *testing.T) {
	scenario := newCompileRecoveryScenario(t,
		[]compileRecoverySend{recoverySendAnswered()},
		[]compileRecoveryAnswer{recoveryDone(compileRecoveryDefinitiveResult)},
	)

	result, _ := runCompileRecovery(t, context.Background(), scenario, map[string]any{})

	if scenario.sendCount() != 1 {
		t.Fatalf("compile sends = %d, want 1", scenario.sendCount())
	}
	if string(result.result) != compileRecoveryDefinitiveResult {
		t.Fatalf("result = %s, want the compile's result", result.result)
	}
}

// Verifies a compile rejected as busy on every attempt is sent three times in all, and the last
// rejection is returned the way a rejection is returned without resending.
func TestFreshCompileRecoveryReturnsTheRejectionAfterTheAttemptLimit(t *testing.T) {
	rejected := []compileRecoveryAnswer{recoveryDone(compileRecoveryAlreadyInProgressResult)}
	scenario := newCompileRecoveryScenario(t,
		[]compileRecoverySend{recoverySendAnswered(), recoverySendAnswered(), recoverySendAnswered()},
		rejected, rejected, rejected,
	)

	result, _ := runCompileRecovery(t, context.Background(), scenario, map[string]any{})

	if scenario.sendCount() != 3 {
		t.Fatalf("compile sends = %d, want 3", scenario.sendCount())
	}
	if string(result.result) != compileRecoveryAlreadyInProgressResult {
		t.Fatalf("result = %s, want the last rejection", result.result)
	}
	if result.exitCode != 1 {
		t.Fatalf("exit code = %d, want 1", result.exitCode)
	}
}

// Verifies the last attempt neither detects a lost request nor sends it again: it keeps waiting the
// way a compile that is never resent does, until the command ends.
func TestFreshCompileRecoveryStopsDetectingOnTheLastAttempt(t *testing.T) {
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	missing := []compileRecoveryAnswer{recoveryMissing()}
	scenario := newCompileRecoveryScenario(t,
		[]compileRecoverySend{recoverySendDisconnected(), recoverySendDisconnected(), recoverySendDisconnected()},
		missing, missing, missing,
	)
	scenario.cancelWhen(cancel, 2, 6)

	result, stderr := runCompileRecovery(t, ctx, scenario, map[string]any{})

	if scenario.sendCount() != 3 {
		t.Fatalf("compile sends = %d, want 3", scenario.sendCount())
	}
	if result.exitCode != 1 || len(result.result) != 0 {
		t.Fatalf("unexpected result: %#v", result)
	}
	if !strings.Contains(stderr, context.Canceled.Error()) {
		t.Fatalf("stderr must report the cancellation:\n%s", stderr)
	}
}

// Verifies the single-attempt entry that never resends keeps waiting for a lost request until the
// command ends instead of resending it or returning without a result.
func TestFreshCompileWithoutRecoveryDoesNotResend(t *testing.T) {
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	scenario := newCompileRecoveryScenario(t,
		[]compileRecoverySend{recoverySendDisconnected()},
		[]compileRecoveryAnswer{recoveryMissing()},
	)
	scenario.cancelWhen(cancel, 0, 6)
	var stderr bytes.Buffer

	result := runFreshCompileWithDomainReloadWaitResultWithDeps(
		ctx, unreachableConnection(t.TempDir()), map[string]any{}, &stderr, scenario.deps())

	if scenario.sendCount() != 1 {
		t.Fatalf("compile sends = %d, want 1", scenario.sendCount())
	}
	if result.exitCode != 1 || len(result.result) != 0 {
		t.Fatalf("unexpected result: %#v", result)
	}
	if !strings.Contains(stderr.String(), context.Canceled.Error()) {
		t.Fatalf("stderr must report the cancellation:\n%s", stderr.String())
	}
	if queries := scenario.queriesOf(0); queries != 6 {
		t.Fatalf("queries for the lost request = %d, want 6", queries)
	}
}

// Verifies the hot-reload compile fallback also sends a compile rejected as busy again.
func TestHotReloadFallbackCompileResendsAfterABusyRejection(t *testing.T) {
	scenario := newCompileRecoveryScenario(t,
		[]compileRecoverySend{recoverySendAnswered(), recoverySendAnswered()},
		compileRecoveryBusyRejectionAnswers("COMPILE_ALREADY_IN_PROGRESS"),
		[]compileRecoveryAnswer{recoveryDone(compileRecoveryDefinitiveResult)},
	)
	var stderr bytes.Buffer

	result := hotReloadFallbackCompileWithDeps(context.Background(), unreachableConnection(t.TempDir()), &stderr, scenario.deps())

	if scenario.sendCount() != 2 {
		t.Fatalf("compile sends = %d, want 2", scenario.sendCount())
	}
	if string(result.result) != compileRecoveryDefinitiveResult {
		t.Fatalf("result = %s, want the resent compile's result", result.result)
	}
}

// Verifies a busy rejection is returned as it is when too little wait time is left for another
// attempt: a resent compile would start in Unity just as the command times out.
func TestFreshCompileRecoveryDoesNotResendARejectionWhenLittleWaitTimeIsLeft(t *testing.T) {
	scenario := newCompileRecoveryScenario(t,
		[]compileRecoverySend{recoverySendAnswered()},
		[]compileRecoveryAnswer{recoveryDone(compileRecoveryAlreadyInProgressResult)},
	)

	result, _ := runCompileRecovery(t, context.Background(), scenario, map[string]any{compileWaitTimeoutParam: 5})

	if scenario.sendCount() != 1 {
		t.Fatalf("compile sends = %d, want 1", scenario.sendCount())
	}
	if string(result.result) != compileRecoveryAlreadyInProgressResult {
		t.Fatalf("result = %s, want the rejection", result.result)
	}
	if result.exitCode != 1 {
		t.Fatalf("exit code = %d, want 1", result.exitCode)
	}
}

// Verifies a lost request is not sent again when too little wait time is left for another attempt;
// the wait goes on the way it does without resending.
func TestFreshCompileRecoveryKeepsWaitingForALostRequestWhenLittleWaitTimeIsLeft(t *testing.T) {
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	scenario := newCompileRecoveryScenario(t,
		[]compileRecoverySend{recoverySendDisconnected()},
		[]compileRecoveryAnswer{recoveryMissing()},
	)
	scenario.cancelWhen(cancel, 0, 6)

	result, stderr := runCompileRecovery(t, ctx, scenario, map[string]any{compileWaitTimeoutParam: 5})

	if scenario.sendCount() != 1 {
		t.Fatalf("compile sends = %d, want 1", scenario.sendCount())
	}
	if result.exitCode != 1 {
		t.Fatalf("exit code = %d, want 1", result.exitCode)
	}
	if !strings.Contains(stderr, context.Canceled.Error()) {
		t.Fatalf("stderr must report the cancellation:\n%s", stderr)
	}
}

// Verifies the first attempt waits exactly as long as --timeout-seconds says, while a resent
// compile waits only for the time that is left, and that the resend after a busy rejection is
// logged as one.
func TestFreshCompileRecoveryGivesAResendOnlyTheTimeThatIsLeft(t *testing.T) {
	enableCliVibeLog(t)
	scenario := newCompileRecoveryScenario(t,
		[]compileRecoverySend{recoverySendAnswered(), recoverySendAnswered()},
		compileRecoveryBusyRejectionAnswers("COMPILE_ALREADY_IN_PROGRESS"),
		[]compileRecoveryAnswer{recoveryDone(compileRecoveryDefinitiveResult)},
	)
	projectRoot := t.TempDir()
	var stderr bytes.Buffer

	runFreshCompileRecoveringWithDeps(context.Background(), unreachableConnection(projectRoot), map[string]any{}, &stderr, scenario.deps())

	prepared := cliVibeEntriesForOperation(t, readOnlyCliVibeLog(t, projectRoot), "cli_compile_request_prepared")
	if len(prepared) != 2 {
		t.Fatalf("prepared compile requests = %d, want 2", len(prepared))
	}
	if first := vibeLogContextNumber(t, prepared[0], "timeout_ms"); first != 600000 {
		t.Fatalf("first attempt timeout_ms = %v, want 600000", first)
	}
	if second := vibeLogContextNumber(t, prepared[1], "timeout_ms"); second >= 600000 {
		t.Fatalf("resent attempt timeout_ms = %v, want less than 600000", second)
	}
	resend := compileResendLogEntry(t, projectRoot)
	if reason := vibeLogContextString(t, resend, "reason"); reason != "editor_busy" {
		t.Fatalf("resend reason = %q, want editor_busy", reason)
	}
}

// Verifies a status query against an endpoint nobody listens on fails with an error that counts as
// the server being gone, so the scripted connection failure above matches what the real query
// returns.
func TestStatusQueryAgainstNoListenerCountsAsTheServerBeingGone(t *testing.T) {
	_, err := queryCompileStatusFromUnity(context.Background(), unreachableConnection(t.TempDir()), "request")

	if err == nil {
		t.Fatal("a status query against an endpoint nobody listens on must fail")
	}
	if errors.Is(err, os.ErrPermission) {
		t.Skipf("the environment denied the connection itself, so it cannot show a missing listener: %v", err)
	}
	if !isServerGoneError(err) {
		t.Fatalf("a status query with no listener must count as the server being gone: %v", err)
	}
}
