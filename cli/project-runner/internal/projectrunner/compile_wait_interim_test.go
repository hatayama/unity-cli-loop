package projectrunner

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"strings"
	"testing"
	"time"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

func TestCompileWaitInterimReportsProgressLineAtSixtySeconds(t *testing.T) {
	// Verifies one progress line at 60s of successful polls, and nothing before that.
	start := time.Date(2026, 8, 21, 0, 0, 0, 0, time.UTC)
	state := newCompileWaitInterimState(start)
	status := compileStatusResponse{IsCompiling: true, IsDomainReloadInProgress: false}

	state.noteSuccessfulPoll(start.Add(59*time.Second), status)
	line, due := state.lineIfDue(start.Add(59*time.Second), compileWaitInterimIntervalDefault)
	if due {
		t.Fatalf("must not report before 60s: %q", line)
	}

	state.noteSuccessfulPoll(start.Add(60*time.Second), status)
	line, due = state.lineIfDue(start.Add(60*time.Second), compileWaitInterimIntervalDefault)
	if !due {
		t.Fatal("expected a progress line at 60s")
	}
	expected := "compile: still waiting for Unity (elapsed 60s; last status: is_compiling=true, is_domain_reload_in_progress=false)."
	if line != expected {
		t.Fatalf("progress line mismatch:\n got: %q\nwant: %q", line, expected)
	}

	line, due = state.lineIfDue(start.Add(119*time.Second), compileWaitInterimIntervalDefault)
	if due {
		t.Fatalf("must report progress only once before the next 60s period: %q", line)
	}
}

func TestCompileWaitInterimSwitchesToSilentLineAfterThirtySecondsWithoutSuccess(t *testing.T) {
	// Verifies a successful poll followed by 30s of silence switches to the silent line.
	start := time.Date(2026, 8, 21, 0, 0, 0, 0, time.UTC)
	state := newCompileWaitInterimState(start)
	state.noteSuccessfulPoll(start, compileStatusResponse{IsCompiling: true})

	line, due := state.lineIfDue(start.Add(29*time.Second), compileWaitInterimIntervalDefault)
	if due {
		t.Fatalf("must not switch before 30s of silence: %q", line)
	}

	line, due = state.lineIfDue(start.Add(30*time.Second), compileWaitInterimIntervalDefault)
	if !due {
		t.Fatal("expected a silent line after 30s without a successful poll")
	}
	expected := "compile: Unity has not answered status polls for 30s. The Editor may be blocked by a modal dialog — commonly Unity's 'Script Updating Consent' or 'API Update Required' dialog, which uloop cannot click — or stuck. Check the Unity window, or restart Unity with 'uloop launch -r'."
	if line != expected {
		t.Fatalf("silent line mismatch:\n got: %q\nwant: %q", line, expected)
	}
}

func TestCompileWaitInterimReportsSilentLineWhenNoSuccessfulPolls(t *testing.T) {
	// Verifies zero successful polls still use wait-start as the silent anchor.
	start := time.Date(2026, 8, 21, 0, 0, 0, 0, time.UTC)
	state := newCompileWaitInterimState(start)

	line, due := state.lineIfDue(start.Add(29*time.Second), compileWaitInterimIntervalDefault)
	if due {
		t.Fatalf("must not report silent before 30s: %q", line)
	}

	line, due = state.lineIfDue(start.Add(30*time.Second), compileWaitInterimIntervalDefault)
	if !due {
		t.Fatal("expected a silent line at 30s with zero successful polls")
	}
	expected := "compile: Unity has not answered status polls for 30s. The Editor may be blocked by a modal dialog — commonly Unity's 'Script Updating Consent' or 'API Update Required' dialog, which uloop cannot click — or stuck. Check the Unity window, or restart Unity with 'uloop launch -r'."
	if line != expected {
		t.Fatalf("silent line mismatch:\n got: %q\nwant: %q", line, expected)
	}
}

func TestWaitForCompileCompletionReportsInterimProgressLine(t *testing.T) {
	// Verifies the fresh wait loop emits the progress line after injected 60s.
	connection := compileWaitTestConnection(t)
	start := time.Date(2026, 8, 21, 0, 0, 0, 0, time.UTC)
	now := start
	var lines []string
	deps := compileWaitTestDeps(func(context.Context, unityipc.Connection, string) (compileStatusResponse, error) {
		now = now.Add(60 * time.Second)
		return compileStatusResponse{Ready: false, IsCompiling: true, IsDomainReloadInProgress: true}, nil
	})
	deps.now = func() time.Time { return now }
	deps.interimReportInterval = 60 * time.Second
	deps.reportInterim = func(line string) { lines = append(lines, line) }

	_, completed, _, err := waitForCompileCompletionWithDeps(context.Background(), compileCompletionOptions{
		connection:   connection,
		requestID:    "compile_interim_progress",
		timeout:      40 * time.Millisecond,
		pollInterval: 5 * time.Millisecond,
	}, deps)
	if err != nil {
		t.Fatalf("waitForCompileCompletion failed: %v", err)
	}
	if completed {
		t.Fatal("expected timeout so interim can report")
	}
	expected := "compile: still waiting for Unity (elapsed 60s; last status: is_compiling=true, is_domain_reload_in_progress=true)."
	if len(lines) == 0 || lines[0] != expected {
		t.Fatalf("fresh wait progress line mismatch: %#v", lines)
	}
}

func TestWaitForAttachedCompileCompletionReportsInterimSilentLine(t *testing.T) {
	// Verifies the attach wait loop emits the same silent line after 30s without success.
	connection := compileWaitTestConnection(t)
	start := time.Date(2026, 8, 21, 0, 0, 0, 0, time.UTC)
	now := start
	var lines []string
	deps := compileWaitTestDeps(func(context.Context, unityipc.Connection, string) (compileStatusResponse, error) {
		now = now.Add(30 * time.Second)
		return compileStatusResponse{}, errors.New("status poll failed")
	})
	deps.now = func() time.Time { return now }
	deps.interimReportInterval = 60 * time.Second
	deps.reportInterim = func(line string) { lines = append(lines, line) }

	_, outcome, _, err := waitForAttachedCompileCompletion(context.Background(), compileCompletionOptions{
		connection:   connection,
		requestID:    "compile_attach_interim_silent",
		timeout:      40 * time.Millisecond,
		pollInterval: 5 * time.Millisecond,
	}, deps)
	if err != nil {
		t.Fatalf("waitForAttachedCompileCompletion failed: %v", err)
	}
	if outcome != attachWaitTimedOut {
		t.Fatalf("expected attach timeout: %v", outcome)
	}
	expected := "compile: Unity has not answered status polls for 30s. The Editor may be blocked by a modal dialog — commonly Unity's 'Script Updating Consent' or 'API Update Required' dialog, which uloop cannot click — or stuck. Check the Unity window, or restart Unity with 'uloop launch -r'."
	if len(lines) == 0 || lines[0] != expected {
		t.Fatalf("attach wait silent line mismatch: %#v", lines)
	}
}

func TestRunCompileTimeoutAfterInterimKeepsStdoutEmptyAndPrefixesStderr(t *testing.T) {
	// Verifies timeout stderr is interim lines then the JSON envelope, and stdout stays empty.
	start := time.Date(2026, 8, 21, 0, 0, 0, 0, time.UTC)
	now := start
	deps := compileWaitTestDeps(func(context.Context, unityipc.Connection, string) (compileStatusResponse, error) {
		now = now.Add(60 * time.Second)
		return compileStatusResponse{Ready: false, IsCompiling: true}, nil
	})
	deps.now = func() time.Time { return now }
	deps.interimReportInterval = 60 * time.Second
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
	connection := compileWaitTestConnection(t)
	params := map[string]any{compileWaitTimeoutParam: 1}
	var stdout, stderr bytes.Buffer

	code := runCompileWithDomainReloadWaitWithDeps(context.Background(), connection, params, &stdout, &stderr, deps)
	if code != 1 {
		t.Fatalf("expected timeout exit 1: code=%d stderr=%s", code, stderr.String())
	}
	if stdout.Len() != 0 {
		t.Fatalf("timeout stdout must stay empty: %s", stdout.String())
	}
	stderrText := stderr.String()
	progressLine := "compile: still waiting for Unity (elapsed 60s; last status: is_compiling=true, is_domain_reload_in_progress=false).\n"
	if !strings.HasPrefix(stderrText, progressLine) {
		t.Fatalf("stderr must start with the interim line:\n%s", stderrText)
	}
	jsonStart := bytes.IndexByte(stderr.Bytes(), '{')
	if jsonStart < 0 {
		t.Fatalf("stderr has no JSON envelope: %s", stderrText)
	}
	var envelope clierrors.CLIErrorEnvelope
	if err := json.Unmarshal(stderr.Bytes()[jsonStart:], &envelope); err != nil {
		t.Fatalf("stderr JSON envelope mismatch: %v\n%s", err, stderrText)
	}
	if envelope.Error.ErrorCode != clierrors.ErrorCodeCompileWaitTimeout {
		t.Fatalf("error code mismatch: %#v", envelope.Error.ErrorCode)
	}
}

func TestRunCompileSuccessUnderSixtySecondsWritesOnlyStdoutJSON(t *testing.T) {
	// Verifies a compile that finishes before 60s keeps stdout as a single JSON object and adds no interim stderr.
	deps := compileWaitTestDeps(func(context.Context, unityipc.Connection, string) (compileStatusResponse, error) {
		return compileStatusResponse{
			Ready:     true,
			HasResult: true,
			Result:    json.RawMessage(`{"Success":false,"ErrorCount":0}`),
		}, nil
	})
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
	connection := compileWaitTestConnection(t)
	var stdout, stderr bytes.Buffer

	code := runCompileWithDomainReloadWaitWithDeps(context.Background(), connection, map[string]any{}, &stdout, &stderr, deps)
	if code != 1 {
		t.Fatalf("expected completed compile envelope exit 1: code=%d stderr=%s", code, stderr.String())
	}
	expectedStdout := "{\n  \"Success\": false,\n  \"ErrorCount\": 0\n}\n"
	if stdout.String() != expectedStdout {
		t.Fatalf("stdout must be the single compile JSON:\n got: %q\nwant: %q", stdout.String(), expectedStdout)
	}
	if strings.Contains(stderr.String(), "compile: still waiting for Unity") {
		t.Fatalf("success under 60s must not add a progress line: %s", stderr.String())
	}
	if strings.Contains(stderr.String(), "compile: Unity has not answered status polls") {
		t.Fatalf("success under 60s must not add a silent line: %s", stderr.String())
	}
}
