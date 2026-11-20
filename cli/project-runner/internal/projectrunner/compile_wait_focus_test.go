package projectrunner

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"os"
	"strings"
	"testing"
	"time"

	"github.com/hatayama/unity-cli-loop/common/unityipc"
	"github.com/hatayama/unity-cli-loop/common/unityprocess"
)

// compileStartPendingFocusRecorder stands in for the Editor focus so a test can see whether, how
// often, and for which process a compile wait brought Unity to the front and restored the window
// that was in front before.
type compileStartPendingFocusRecorder struct {
	focusCalls   []int
	restoreCalls int
	focusErr     error
}

func (recorder *compileStartPendingFocusRecorder) focus(_ context.Context, pid int) (unityprocess.RestoreFocusFunc, error) {
	recorder.focusCalls = append(recorder.focusCalls, pid)
	if recorder.focusErr != nil {
		return nil, recorder.focusErr
	}
	return func(context.Context) error {
		recorder.restoreCalls++
		return nil
	}, nil
}

// pendingStartThenResult answers status polls with the given pending-start seconds in order, then
// with the stored result on every later poll.
func pendingStartThenResult(pendingSeconds []float64, result string) func(context.Context, unityipc.Connection, string) (compileStatusResponse, error) {
	calls := 0
	return func(context.Context, unityipc.Connection, string) (compileStatusResponse, error) {
		calls++
		if calls <= len(pendingSeconds) {
			return compileStatusResponse{IsCompiling: true, CompileStartPendingSeconds: pendingSeconds[calls-1]}, nil
		}
		return compileStatusResponse{Ready: true, HasResult: true, Result: json.RawMessage(result)}, nil
	}
}

func compileStartPendingFocusDeps(
	recorder *compileStartPendingFocusRecorder,
	query func(context.Context, unityipc.Connection, string) (compileStatusResponse, error),
	lines *[]string,
) compileWaitDeps {
	deps := compileWaitTestDeps(query)
	deps.focusUnityProcess = recorder.focus
	deps.reportInterim = func(line string) { *lines = append(*lines, line) }
	return deps
}

func compileStartPendingOptions(t *testing.T, requestID string) compileCompletionOptions {
	t.Helper()
	return compileCompletionOptions{
		connection:   compileWaitTestConnection(t),
		requestID:    requestID,
		timeout:      time.Second,
		pollInterval: time.Millisecond,
	}
}

// Verifies a fresh compile wait brings Unity to the front once when Unity has reported the compile
// request as accepted but not started for the threshold, tells the user on stderr, and restores the
// previously frontmost window when the wait ends.
func TestWaitForCompileCompletionFocusesUnityOnceWhenCompileStartStaysPending(t *testing.T) {
	recorder := &compileStartPendingFocusRecorder{}
	var lines []string
	deps := compileStartPendingFocusDeps(recorder, pendingStartThenResult([]float64{0, 4, 10, 25, 40}, `{"Success":true}`), &lines)

	result, completed, _, err := waitForCompileCompletionWithDeps(context.Background(), compileStartPendingOptions(t, "compile_pending_focus"), deps)

	if err != nil || !completed {
		t.Fatalf("wait failed: completed=%v err=%v", completed, err)
	}
	if string(result) != `{"Success":true}` {
		t.Fatalf("result mismatch: %s", result)
	}
	if len(recorder.focusCalls) != 1 || recorder.focusCalls[0] != os.Getpid() {
		t.Fatalf("focus calls = %v, want one call for the running Editor pid %d", recorder.focusCalls, os.Getpid())
	}
	if recorder.restoreCalls != 1 {
		t.Fatalf("restore calls = %d, want 1 after the wait ended", recorder.restoreCalls)
	}
	if len(lines) != 1 || !strings.Contains(lines[0], "brought the Unity Editor to the front") {
		t.Fatalf("stderr lines = %#v, want one line saying the Editor was brought to the front", lines)
	}
	if !strings.HasPrefix(lines[0], "compile: Unity accepted the compile request 10s ago but has not started compiling") {
		t.Fatalf("stderr line must lead with the pending time Unity reported: %q", lines[0])
	}
}

// Verifies the compile wait leaves the Editor alone while the pending-start time is below the threshold.
func TestWaitForCompileCompletionDoesNotFocusUnityBelowThePendingThreshold(t *testing.T) {
	recorder := &compileStartPendingFocusRecorder{}
	var lines []string
	deps := compileStartPendingFocusDeps(recorder, pendingStartThenResult([]float64{3, 9.9, 9.9}, `{"Success":true}`), &lines)

	_, completed, _, err := waitForCompileCompletionWithDeps(context.Background(), compileStartPendingOptions(t, "compile_pending_below"), deps)

	if err != nil || !completed {
		t.Fatalf("wait failed: completed=%v err=%v", completed, err)
	}
	if len(recorder.focusCalls) != 0 || len(lines) != 0 {
		t.Fatalf("no focus expected below the threshold: calls=%v lines=%#v", recorder.focusCalls, lines)
	}
}

// Verifies a status poll that failed does not count toward the pending threshold even when it carries
// a pending value, because Unity did not answer it.
func TestWaitForCompileCompletionIgnoresPendingTimeFromFailedPolls(t *testing.T) {
	recorder := &compileStartPendingFocusRecorder{}
	var lines []string
	calls := 0
	deps := compileStartPendingFocusDeps(recorder, func(context.Context, unityipc.Connection, string) (compileStatusResponse, error) {
		calls++
		if calls == 1 {
			return compileStatusResponse{IsCompiling: true, CompileStartPendingSeconds: 30}, errors.New("status poll failed")
		}
		return compileStatusResponse{Ready: true, HasResult: true, Result: json.RawMessage(`{"Success":true}`)}, nil
	}, &lines)

	_, completed, _, err := waitForCompileCompletionWithDeps(context.Background(), compileStartPendingOptions(t, "compile_pending_failed_poll"), deps)

	if err != nil || !completed {
		t.Fatalf("wait failed: completed=%v err=%v", completed, err)
	}
	if len(recorder.focusCalls) != 0 {
		t.Fatalf("a failed poll must not trigger focus: calls=%v", recorder.focusCalls)
	}
}

// Verifies a wait that finds no running Editor does not try to focus and does not report a focus line.
func TestWaitForCompileCompletionSkipsFocusWhenNoEditorIsRunning(t *testing.T) {
	recorder := &compileStartPendingFocusRecorder{}
	var lines []string
	deps := compileStartPendingFocusDeps(recorder, pendingStartThenResult([]float64{15, 20}, `{"Success":true}`), &lines)
	deps.findRunningUnityProcess = noUnityProcessLookup

	_, completed, _, err := waitForCompileCompletionWithDeps(context.Background(), compileStartPendingOptions(t, "compile_pending_no_editor"), deps)

	if err != nil || !completed {
		t.Fatalf("wait failed: completed=%v err=%v", completed, err)
	}
	if len(recorder.focusCalls) != 0 || len(lines) != 0 {
		t.Fatalf("no focus expected without an Editor process: calls=%v lines=%#v", recorder.focusCalls, lines)
	}
}

// Verifies one missed Editor process lookup at the threshold does not end the focus rescue: the next
// answered poll looks for the Editor again and focuses it once it is found.
func TestWaitForCompileCompletionRetriesFocusAfterAMissedEditorLookup(t *testing.T) {
	recorder := &compileStartPendingFocusRecorder{}
	var lines []string
	deps := compileStartPendingFocusDeps(recorder, pendingStartThenResult([]float64{12, 14, 16}, `{"Success":true}`), &lines)
	lookups := 0
	foundUnityProcess := deps.findRunningUnityProcess
	deps.findRunningUnityProcess = func(ctx context.Context, projectRoot string) (*unityprocess.UnityProcess, error) {
		lookups++
		if lookups == 1 {
			return noUnityProcessLookup(ctx, projectRoot)
		}
		return foundUnityProcess(ctx, projectRoot)
	}

	_, completed, _, err := waitForCompileCompletionWithDeps(context.Background(), compileStartPendingOptions(t, "compile_pending_lookup_retry"), deps)

	if err != nil || !completed {
		t.Fatalf("wait failed: completed=%v err=%v", completed, err)
	}
	if len(recorder.focusCalls) != 1 {
		t.Fatalf("focus calls = %v, want exactly one once the Editor was found", recorder.focusCalls)
	}
	if lookups != 2 {
		t.Fatalf("process lookups = %d, want 2 (one miss, one hit, none after the focus)", lookups)
	}
	if len(lines) != 1 {
		t.Fatalf("stderr lines = %#v, want one focus line", lines)
	}
}

// Verifies a COMPILE_NOT_STARTED result gains the CLI-side diagnostics (focus attempt and outcome,
// OS mouse button state) in Details while keeping the Unity-side fields.
func TestWaitForCompileCompletionAddsCliDiagnosticsToNotStartedResult(t *testing.T) {
	recorder := &compileStartPendingFocusRecorder{}
	var lines []string
	unityResult := `{"Success":false,"ErrorCode":"COMPILE_NOT_STARTED","Details":{"PendingSeconds":60,"EditorWindowActive":false}}`
	deps := compileStartPendingFocusDeps(recorder, pendingStartThenResult([]float64{15}, unityResult), &lines)
	deps.readHeldMouseButtons = func() ([]string, bool) { return []string{"left"}, true }

	result, completed, _, err := waitForCompileCompletionWithDeps(context.Background(), compileStartPendingOptions(t, "compile_not_started_details"), deps)

	if err != nil || !completed {
		t.Fatalf("wait failed: completed=%v err=%v", completed, err)
	}
	details := compileResultDetails(t, result)
	if details["PendingSeconds"] != float64(60) || details["EditorWindowActive"] != false {
		t.Fatalf("Unity-side details must be kept: %#v", details)
	}
	if details["CliFocusAttempted"] != true || details["CliFocusSucceeded"] != true {
		t.Fatalf("focus diagnostics mismatch: %#v", details)
	}
	heldButtons, ok := details["HeldMouseButtons"].([]any)
	if !ok || len(heldButtons) != 1 || heldButtons[0] != "left" {
		t.Fatalf("HeldMouseButtons mismatch: %#v", details["HeldMouseButtons"])
	}
	if toolEnvelopeExitCode(result) != 1 {
		t.Fatalf("the not-started result must still read as a failed compile: %s", result)
	}
}

// Verifies a failed focus attempt is reported on stderr and recorded in the not-started Details, and
// that nothing is restored because nothing was focused.
func TestWaitForCompileCompletionReportsAFailedFocusAttempt(t *testing.T) {
	recorder := &compileStartPendingFocusRecorder{focusErr: errors.New("foreground lock")}
	var lines []string
	unityResult := `{"Success":false,"ErrorCode":"COMPILE_NOT_STARTED","Details":null}`
	deps := compileStartPendingFocusDeps(recorder, pendingStartThenResult([]float64{12}, unityResult), &lines)
	deps.readHeldMouseButtons = func() ([]string, bool) { return nil, false }

	result, _, _, err := waitForCompileCompletionWithDeps(context.Background(), compileStartPendingOptions(t, "compile_not_started_focus_failed"), deps)
	if err != nil {
		t.Fatalf("wait failed: %v", err)
	}
	if len(lines) != 1 || !strings.Contains(lines[0], "could not bring the Unity Editor to the front") {
		t.Fatalf("stderr lines = %#v, want one line about the failed focus", lines)
	}
	if recorder.restoreCalls != 0 {
		t.Fatalf("nothing to restore after a failed focus, got %d restore calls", recorder.restoreCalls)
	}
	details := compileResultDetails(t, result)
	if details["CliFocusAttempted"] != true || details["CliFocusSucceeded"] != false {
		t.Fatalf("focus diagnostics mismatch: %#v", details)
	}
	if value, present := details["HeldMouseButtons"]; !present || value != nil {
		t.Fatalf("HeldMouseButtons must be null when the OS state is not readable: %#v", details)
	}
}

// Verifies an ordinary compile result passes through byte-for-byte even after a focus attempt.
func TestWaitForCompileCompletionLeavesOrdinaryResultsUntouchedAfterFocus(t *testing.T) {
	recorder := &compileStartPendingFocusRecorder{}
	var lines []string
	deps := compileStartPendingFocusDeps(recorder, pendingStartThenResult([]float64{15}, `{"Success":false,"ErrorCount":2,"Details":null}`), &lines)

	result, _, _, err := waitForCompileCompletionWithDeps(context.Background(), compileStartPendingOptions(t, "compile_ordinary_after_focus"), deps)
	if err != nil {
		t.Fatalf("wait failed: %v", err)
	}
	if string(result) != `{"Success":false,"ErrorCount":2,"Details":null}` {
		t.Fatalf("ordinary result must not be rewritten: %s", result)
	}
}

// Verifies the attach wait, which polls the same status, also brings Unity to the front once.
func TestWaitForAttachedCompileCompletionFocusesUnityWhenCompileStartStaysPending(t *testing.T) {
	recorder := &compileStartPendingFocusRecorder{}
	var lines []string
	deps := compileStartPendingFocusDeps(recorder, pendingStartThenResult([]float64{11, 30}, `{"Success":true}`), &lines)

	_, outcome, _, err := waitForAttachedCompileCompletion(context.Background(), compileStartPendingOptions(t, "compile_attach_pending_focus"), deps)

	if err != nil || outcome != attachWaitCompleted {
		t.Fatalf("attach wait failed: outcome=%v err=%v", outcome, err)
	}
	if len(recorder.focusCalls) != 1 || recorder.restoreCalls != 1 {
		t.Fatalf("focus=%v restore=%d, want one focus and one restore", recorder.focusCalls, recorder.restoreCalls)
	}
}

// Verifies the focus attempt is written to the CLI vibe log under its own reason so a stall
// investigation can tell it from the connection-retry focus rescues.
func TestWaitForCompileCompletionLogsThePendingStartFocusReason(t *testing.T) {
	enableCliVibeLog(t)
	recorder := &compileStartPendingFocusRecorder{}
	var lines []string
	deps := compileStartPendingFocusDeps(recorder, pendingStartThenResult([]float64{10}, `{"Success":true}`), &lines)
	options := compileStartPendingOptions(t, "compile_pending_focus_log")

	_, _, _, err := waitForCompileCompletionWithDeps(context.Background(), options, deps)
	if err != nil {
		t.Fatalf("wait failed: %v", err)
	}
	logContent := readOnlyCliVibeLog(t, options.connection.ProjectRoot)
	for _, expected := range []string{
		`"operation":"cli_connection_retry_focus_attempt"`,
		`"operation":"cli_connection_retry_focus_success"`,
		`"operation":"cli_connection_retry_focus_restore_success"`,
		`"reason":"compile_start_pending"`,
		`"command":"compile"`,
	} {
		if !strings.Contains(logContent, expected) {
			t.Fatalf("CLI Vibe log missing %q:\n%s", expected, logContent)
		}
	}
}

// Verifies the compile that run-tests runs first gets the same focus rescue, since that is where the
// reported stall surfaced.
func TestRunTestsImplicitCompileFocusesUnityWhenCompileStartStaysPending(t *testing.T) {
	recorder := &compileStartPendingFocusRecorder{}
	var lines []string
	deps := compileStartPendingFocusDeps(recorder, pendingStartThenResult([]float64{12}, `{"Success":true}`), &lines)
	deps.freshWaitPollInterval = time.Millisecond
	deps.sendCompile = func(
		context.Context,
		unityipc.Connection,
		string,
		map[string]any,
		unityipc.ProgressFunc,
		time.Duration,
	) (unityipc.UnitySendOutcome, error) {
		return unityipc.UnitySendOutcome{RequestDispatched: true, RequestAccepted: true}, nil
	}
	var stderr bytes.Buffer

	result := runTestsImplicitCompileWithDeps(context.Background(), compileWaitTestConnection(t), &stderr, deps)

	if result.exitCode != 0 {
		t.Fatalf("implicit compile failed: code=%d stderr=%s", result.exitCode, stderr.String())
	}
	if len(recorder.focusCalls) != 1 {
		t.Fatalf("focus calls = %v, want exactly one", recorder.focusCalls)
	}
}

// Verifies the diagnostics helper leaves input it cannot parse unchanged.
func TestAddCliDiagnosticsToNotStartedResultKeepsUndecodableInput(t *testing.T) {
	outcome := compileStartPendingFocusOutcome{attempted: true, succeeded: true}

	result := addCliDiagnosticsToNotStartedResult(json.RawMessage("not json"), outcome, func() ([]string, bool) { return nil, false })

	if string(result) != "not json" {
		t.Fatalf("undecodable input must be returned unchanged: %s", result)
	}
}

func compileResultDetails(t *testing.T, result json.RawMessage) map[string]any {
	t.Helper()
	var payload map[string]any
	if err := json.Unmarshal(result, &payload); err != nil {
		t.Fatalf("result is not JSON: %v\n%s", err, result)
	}
	details, ok := payload["Details"].(map[string]any)
	if !ok {
		t.Fatalf("Details missing or not an object: %#v", payload["Details"])
	}
	return details
}

// Verifies the not-started diagnostics are written with the same encoder as the other result
// rewrites, so a Message that contains < > or & reaches the reader unescaped on this path too.
func TestAddCliDiagnosticsToNotStartedResultKeepsAngleBracketsUnescaped(t *testing.T) {
	outcome := compileStartPendingFocusOutcome{attempted: true, succeeded: false}
	input := json.RawMessage(`{"ErrorCode":"COMPILE_NOT_STARTED","Message":"List<int> & more","Details":null}`)

	result := addCliDiagnosticsToNotStartedResult(input, outcome, func() ([]string, bool) { return nil, false })

	if !strings.Contains(string(result), `"List<int> & more"`) {
		t.Fatalf("Message must keep its characters unescaped: %s", result)
	}
	details := compileResultDetails(t, result)
	if details["CliFocusAttempted"] != true || details["CliFocusSucceeded"] != false {
		t.Fatalf("focus diagnostics mismatch: %#v", details)
	}
}
