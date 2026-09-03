package projectrunner

import (
	"bytes"
	"context"
	"encoding/json"
	"io"
	"slices"
	"strings"
	"testing"
	"time"

	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

// Verifies the automatic Debug-switch warning gives the exact approved persistence guidance.
func TestPausePointAutoDebugSwitchWarningRecommendsApprovedStartupCommand(t *testing.T) {
	const expected = "Code Optimization was Release; switched to Debug and recompiled before arming the pause point. This setting reverts on every Editor restart, and each re-switch costs a full script recompile. Once the current task reaches a natural stopping point, suggest making Debug permanent: with the user's approval, run uloop set-code-optimization debug --startup (machine-wide: applies to every Unity project on this machine; only your project's C# script execution slows down, mainly during Play Mode - the Unity Editor itself is not slowed)."
	if pausePointAutoDebugSwitchWarning != expected {
		t.Fatalf("warning = %q, want %q", pausePointAutoDebugSwitchWarning, expected)
	}
}

// Verifies a successful enable that reported only the joined Warning still lists both topics in
// Warnings after recovery, rather than answering on a string the array does not match.
func TestApplyPausePointRecoverySwitchWarning_WhenWarningsOmitted_ListsBothTopics(t *testing.T) {
	response := pausePointStatusResponse{
		Success: true,
		Warning: "physics dispatch warning.",
	}
	applyPausePointRecoverySwitchWarning(&response)
	want := []string{"physics dispatch warning.", pausePointAutoDebugSwitchWarning}
	if !slices.Equal(response.Warnings, want) {
		t.Fatalf("Warnings mismatch: %#v", response.Warnings)
	}
	if response.Warning != strings.Join(want, " ") {
		t.Fatalf("Warning must be the joined form of Warnings: %q", response.Warning)
	}
}

// Verifies a successful enable with existing Warnings appends the switch note.
func TestApplyPausePointRecoverySwitchWarning_WhenWarningsPresent_AppendsSwitchEntry(t *testing.T) {
	response := pausePointStatusResponse{
		Success:  true,
		Warning:  "physics dispatch warning.",
		Warnings: []string{"physics dispatch warning."},
	}
	applyPausePointRecoverySwitchWarning(&response)
	if len(response.Warnings) != 2 || response.Warnings[1] != pausePointAutoDebugSwitchWarning {
		t.Fatalf("Warnings mismatch: %#v", response.Warnings)
	}
}

// Verifies a present empty Warnings array receives the switch note when Warning is empty.
func TestApplyPausePointRecoverySwitchWarning_WhenWarningsEmptyAndWarningEmpty_AppendsSwitchEntry(t *testing.T) {
	response := pausePointStatusResponse{
		Success:  true,
		Warnings: []string{},
	}
	applyPausePointRecoverySwitchWarning(&response)
	if len(response.Warnings) != 1 || response.Warnings[0] != pausePointAutoDebugSwitchWarning {
		t.Fatalf("Warnings mismatch: %#v", response.Warnings)
	}
}

const releaseCodeOptimizationEnableFailureJSON = `{"Success":false,"ErrorCode":"PAUSE_POINT_RELEASE_CODE_OPTIMIZATION","Message":"Release code optimization"}`

const unrelatedEnableFailureJSON = `{"Success":false,"ErrorCode":"PAUSE_POINT_RESOLVE_FAILED","Message":"No sequence point found"}`

const successfulEnableJSON = `{"Success":true,"Id":"jump","Status":"Enabled","IsEnabled":true,"TimeoutSeconds":30}`

// Verifies a non-matching enable failure is written byte-identically and recovery is not started.
func TestCompleteEnableWithReleaseRecovery_WhenUnrelatedFailure_PassesThroughUnchanged(t *testing.T) {
	raw := []byte(unrelatedEnableFailureJSON)
	sendCount := 0
	var stdout bytes.Buffer
	code := completeEnableWithReleaseRecovery(
		context.Background(),
		unityipc.Connection{ProjectRoot: t.TempDir()},
		&stdout,
		io.Discard,
		func(writer io.Writer) int {
			sendCount++
			_, _ = writer.Write(raw)
			return 1
		},
	)
	if code != 1 {
		t.Fatalf("expected failure exit, got %d", code)
	}
	if sendCount != 1 {
		t.Fatalf("send count mismatch: %d", sendCount)
	}
	if !bytes.Equal(stdout.Bytes(), raw) {
		t.Fatalf("passthrough mismatch: %q", stdout.Bytes())
	}
}

// Verifies a Release rejection runs switch + fresh compile + one resend, and joins the
// recovery Warning onto the successful non-await output.
func TestCompleteEnableWithReleaseRecovery_WhenReleaseError_RecoversAndJoinsWarning(t *testing.T) {
	originalSwitch := sendSetCodeOptimizationDebug
	originalCompile := runFreshCompileForPausePointRecovery
	t.Cleanup(func() {
		sendSetCodeOptimizationDebug = originalSwitch
		runFreshCompileForPausePointRecovery = originalCompile
	})

	switchCount := 0
	sendSetCodeOptimizationDebug = func(ctx context.Context, connection unityipc.Connection) error {
		switchCount++
		return nil
	}
	freshCompileCount := 0
	runFreshCompileForPausePointRecovery = func(
		ctx context.Context,
		connection unityipc.Connection,
		params map[string]any,
		stdout io.Writer,
		stderr io.Writer,
	) int {
		freshCompileCount++
		return 0
	}

	sendCount := 0
	var stdout bytes.Buffer
	code := completeEnableWithReleaseRecovery(
		context.Background(),
		unityipc.Connection{ProjectRoot: t.TempDir()},
		&stdout,
		io.Discard,
		func(writer io.Writer) int {
			sendCount++
			if sendCount == 1 {
				_, _ = writer.Write([]byte(releaseCodeOptimizationEnableFailureJSON))
				return 1
			}
			_, _ = writer.Write([]byte(successfulEnableJSON))
			return 0
		},
	)
	if code != 0 {
		t.Fatalf("expected success, got %d with stdout %s", code, stdout.String())
	}
	if sendCount != 2 {
		t.Fatalf("send count mismatch: %d", sendCount)
	}
	if switchCount != 1 || freshCompileCount != 1 {
		t.Fatalf("recovery sequence mismatch: switch=%d compile=%d", switchCount, freshCompileCount)
	}
	var payload map[string]any
	if err := json.Unmarshal(stdout.Bytes(), &payload); err != nil {
		t.Fatalf("stdout is not JSON: %v\n%s", err, stdout.String())
	}
	warning, _ := payload["Warning"].(string)
	if warning != pausePointAutoDebugSwitchWarning {
		t.Fatalf("Warning mismatch: %q", warning)
	}
	assertPausePointRecoveryWarningsAgree(t, payload, 1)
}

// Verifies non-await recovery appends the switch note onto a present empty Warnings array.
func TestCompleteEnableWithReleaseRecovery_WhenEmptyWarnings_AppendsSwitchEntry(t *testing.T) {
	originalSwitch := sendSetCodeOptimizationDebug
	originalCompile := runFreshCompileForPausePointRecovery
	t.Cleanup(func() {
		sendSetCodeOptimizationDebug = originalSwitch
		runFreshCompileForPausePointRecovery = originalCompile
	})
	sendSetCodeOptimizationDebug = func(ctx context.Context, connection unityipc.Connection) error {
		return nil
	}
	runFreshCompileForPausePointRecovery = func(
		ctx context.Context,
		connection unityipc.Connection,
		params map[string]any,
		stdout io.Writer,
		stderr io.Writer,
	) int {
		return 0
	}

	sendCount := 0
	var stdout bytes.Buffer
	code := completeEnableWithReleaseRecovery(
		context.Background(),
		unityipc.Connection{ProjectRoot: t.TempDir()},
		&stdout,
		io.Discard,
		func(writer io.Writer) int {
			sendCount++
			if sendCount == 1 {
				_, _ = writer.Write([]byte(releaseCodeOptimizationEnableFailureJSON))
				return 1
			}
			_, _ = writer.Write([]byte(
				`{"Success":true,"Id":"jump","Status":"Enabled","IsEnabled":true,"TimeoutSeconds":30,"Warnings":[]}`))
			return 0
		},
	)
	if code != 0 {
		t.Fatalf("expected success, got %d with stdout %s", code, stdout.String())
	}
	var payload map[string]any
	if err := json.Unmarshal(stdout.Bytes(), &payload); err != nil {
		t.Fatalf("stdout is not JSON: %v\n%s", err, stdout.String())
	}
	warnings, _ := payload["Warnings"].([]any)
	if len(warnings) != 1 || warnings[0] != pausePointAutoDebugSwitchWarning {
		t.Fatalf("Warnings mismatch: %#v", payload["Warnings"])
	}
}

// Verifies a failed resend is written as-is and enable is not sent a third time.
func TestCompleteEnableWithReleaseRecovery_WhenResendFails_ReturnsFailureWithoutThirdSend(t *testing.T) {
	originalSwitch := sendSetCodeOptimizationDebug
	originalCompile := runFreshCompileForPausePointRecovery
	t.Cleanup(func() {
		sendSetCodeOptimizationDebug = originalSwitch
		runFreshCompileForPausePointRecovery = originalCompile
	})
	sendSetCodeOptimizationDebug = func(ctx context.Context, connection unityipc.Connection) error {
		return nil
	}
	runFreshCompileForPausePointRecovery = func(
		ctx context.Context,
		connection unityipc.Connection,
		params map[string]any,
		stdout io.Writer,
		stderr io.Writer,
	) int {
		return 0
	}

	resend := []byte(`{"Success":false,"ErrorCode":"PAUSE_POINT_RESOLVE_FAILED","Message":"still failed"}`)
	sendCount := 0
	var stdout bytes.Buffer
	code := completeEnableWithReleaseRecovery(
		context.Background(),
		unityipc.Connection{ProjectRoot: t.TempDir()},
		&stdout,
		io.Discard,
		func(writer io.Writer) int {
			sendCount++
			if sendCount == 1 {
				_, _ = writer.Write([]byte(releaseCodeOptimizationEnableFailureJSON))
				return 1
			}
			_, _ = writer.Write(resend)
			return 1
		},
	)
	if code != 1 {
		t.Fatalf("expected failure, got %d", code)
	}
	if sendCount != 2 {
		t.Fatalf("send count mismatch: %d", sendCount)
	}
	if !bytes.Equal(stdout.Bytes(), resend) {
		t.Fatalf("resend failure passthrough mismatch: %q", stdout.Bytes())
	}
	if strings.Contains(stdout.String(), pausePointAutoDebugSwitchWarning) {
		t.Fatalf("failed resend must not inject Warning: %s", stdout.String())
	}
}

// Verifies recovery calls the fresh compile function rather than the attach-capable compile entry.
func TestRecoverReleaseCodeOptimization_CallsFreshCompileNotAttach(t *testing.T) {
	originalSwitch := sendSetCodeOptimizationDebug
	originalCompile := runFreshCompileForPausePointRecovery
	t.Cleanup(func() {
		sendSetCodeOptimizationDebug = originalSwitch
		runFreshCompileForPausePointRecovery = originalCompile
	})

	sendSetCodeOptimizationDebug = func(ctx context.Context, connection unityipc.Connection) error {
		return nil
	}
	freshCalled := false
	runFreshCompileForPausePointRecovery = func(
		ctx context.Context,
		connection unityipc.Connection,
		params map[string]any,
		stdout io.Writer,
		stderr io.Writer,
	) int {
		freshCalled = true
		return 0
	}

	errCode := recoverReleaseCodeOptimization(
		context.Background(),
		unityipc.Connection{ProjectRoot: t.TempDir()},
		io.Discard,
		io.Discard,
	)
	if errCode != 0 {
		t.Fatalf("unexpected recover exit: %d", errCode)
	}
	if !freshCalled {
		t.Fatal("expected recoverReleaseCodeOptimization to call the fresh compile function")
	}
}

// Verifies await recovery carries the switch warning into the hit response's Warnings aggregate
// with the enable-time prefix, rather than on a warning field of its own.
func TestRunEnablePausePointAndAwait_WhenReleaseError_RecoversAndListsSwitchWarning(t *testing.T) {
	originalQuery := queryPausePointStatus
	originalPoll := pausePointStatusPoll
	originalFetch := fetchMatchingLogs
	originalSend := sendEnablePausePointIPC
	originalSwitch := sendSetCodeOptimizationDebug
	originalCompile := runFreshCompileForPausePointRecovery
	pausePointStatusPoll = time.Millisecond
	t.Cleanup(func() {
		queryPausePointStatus = originalQuery
		pausePointStatusPoll = originalPoll
		fetchMatchingLogs = originalFetch
		sendEnablePausePointIPC = originalSend
		sendSetCodeOptimizationDebug = originalSwitch
		runFreshCompileForPausePointRecovery = originalCompile
	})

	statusResponses := []pausePointStatusResponse{
		{Id: "jump", Status: pausePointStatusEnabled, IsEnabled: true},
		{Id: "jump", Status: pausePointStatusHit, IsHit: true, HitCount: 1},
	}
	statusCallCount := 0
	queryPausePointStatus = func(ctx context.Context, connection unityipc.Connection, id string) (pausePointStatusResponse, error) {
		response := statusResponses[statusCallCount]
		statusCallCount++
		return response, nil
	}
	fetchMatchingLogs = func(
		ctx context.Context,
		connection unityipc.Connection,
		searchText string,
		maxCount int,
	) (pausePointMatchingLogsResult, error) {
		return pausePointMatchingLogsResult{SearchText: searchText, Logs: []pausePointMatchingLog{}}, nil
	}

	enableSends := 0
	sendEnablePausePointIPC = func(
		ctx context.Context,
		connection unityipc.Connection,
		params map[string]any,
		stderr io.Writer,
	) (unityipc.UnitySendOutcome, error) {
		enableSends++
		if enableSends == 1 {
			return unityipc.UnitySendOutcome{Result: []byte(releaseCodeOptimizationEnableFailureJSON)}, nil
		}
		return unityipc.UnitySendOutcome{Result: []byte(successfulEnableJSON)}, nil
	}
	sendSetCodeOptimizationDebug = func(ctx context.Context, connection unityipc.Connection) error {
		return nil
	}
	runFreshCompileForPausePointRecovery = func(
		ctx context.Context,
		connection unityipc.Connection,
		params map[string]any,
		stdout io.Writer,
		stderr io.Writer,
	) int {
		return 0
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runEnablePausePointAndAwait(
		context.Background(),
		unityipc.Connection{ProjectRoot: t.TempDir()},
		map[string]any{"Id": "jump"},
		pausePointCapturedVariablesModeFull,
		nil,
		nil,
		"",
		nil,
		false,
		t.TempDir(),
		&stdout,
		&stderr,
	)
	if code != 0 {
		t.Fatalf("expected success, got %d with stderr %s", code, stderr.String())
	}
	if enableSends != 2 {
		t.Fatalf("enable send count mismatch: %d", enableSends)
	}

	var response pausePointWaitResult
	if err := json.Unmarshal(stdout.Bytes(), &response); err != nil {
		t.Fatalf("failed to decode stdout: %v\n%s", err, stdout.String())
	}
	prefixedSwitchWarning := pausePointEnableTimeWarningPrefix + pausePointAutoDebugSwitchWarning
	if !slices.Contains(response.Warnings, prefixedSwitchWarning) {
		t.Fatalf("Warnings mismatch: %#v", response.Warnings)
	}
	if response.Warning != strings.Join(response.Warnings, " ") {
		t.Fatalf("Warning must be the joined form of Warnings: %q vs %#v", response.Warning, response.Warnings)
	}
	var raw map[string]json.RawMessage
	if err := json.Unmarshal(stdout.Bytes(), &raw); err != nil {
		t.Fatalf("failed to decode raw stdout: %v\n%s", err, stdout.String())
	}
	for _, retiredKey := range []string{"EnableTimeWarning", "EnableTimeWarnings"} {
		if _, ok := raw[retiredKey]; ok {
			t.Fatalf("%s must no longer appear on a hit payload", retiredKey)
		}
	}
}

// Verifies a COMPILE_ALREADY_IN_PROGRESS compile result is retried once, then recovery completes.
func TestCompleteEnableWithReleaseRecovery_WhenCompileAlreadyInProgressOnce_RetriesAndCompletes(t *testing.T) {
	originalSwitch := sendSetCodeOptimizationDebug
	originalCompile := runFreshCompileForPausePointRecovery
	originalAttempt := runOneFreshCompileForPausePointRecovery
	originalWait := waitPausePointRecoveryBusyRetry
	t.Cleanup(func() {
		sendSetCodeOptimizationDebug = originalSwitch
		runFreshCompileForPausePointRecovery = originalCompile
		runOneFreshCompileForPausePointRecovery = originalAttempt
		waitPausePointRecoveryBusyRetry = originalWait
	})

	sendSetCodeOptimizationDebug = func(ctx context.Context, connection unityipc.Connection) error {
		return nil
	}
	waitPausePointRecoveryBusyRetry = func(ctx context.Context, duration time.Duration) error {
		return nil
	}
	compileAttemptCount := 0
	runOneFreshCompileForPausePointRecovery = func(
		ctx context.Context,
		connection unityipc.Connection,
		params map[string]any,
		stdout io.Writer,
		stderr io.Writer,
		budget time.Duration,
	) int {
		compileAttemptCount++
		if compileAttemptCount == 1 {
			_, _ = stdout.Write([]byte(`{"Success":false,"ErrorCode":"COMPILE_ALREADY_IN_PROGRESS"}`))
			return 1
		}
		return 0
	}
	runFreshCompileForPausePointRecovery = runFreshCompileWithBusyRetryForPausePointRecovery

	sendCount := 0
	var stdout bytes.Buffer
	code := completeEnableWithReleaseRecovery(
		context.Background(),
		unityipc.Connection{ProjectRoot: t.TempDir()},
		&stdout,
		io.Discard,
		func(writer io.Writer) int {
			sendCount++
			if sendCount == 1 {
				_, _ = writer.Write([]byte(releaseCodeOptimizationEnableFailureJSON))
				return 1
			}
			_, _ = writer.Write([]byte(successfulEnableJSON))
			return 0
		},
	)
	if code != 0 {
		t.Fatalf("expected success, got %d with stdout %s", code, stdout.String())
	}
	if compileAttemptCount != 2 {
		t.Fatalf("compile attempt count mismatch: %d", compileAttemptCount)
	}
	if sendCount != 2 {
		t.Fatalf("enable send count mismatch: %d", sendCount)
	}
	var payload map[string]any
	if err := json.Unmarshal(stdout.Bytes(), &payload); err != nil {
		t.Fatalf("stdout is not JSON: %v\n%s", err, stdout.String())
	}
	warning, _ := payload["Warning"].(string)
	if warning != pausePointAutoDebugSwitchWarning {
		t.Fatalf("Warning mismatch: %q", warning)
	}
}

// Verifies a cancelled retry wait reports the cancel error instead of the busy compile JSON.
func TestRunFreshCompileWithBusyRetry_WhenRetryWaitCancelled_ReportsCancelNotBusyResult(t *testing.T) {
	originalAttempt := runOneFreshCompileForPausePointRecovery
	originalWait := waitPausePointRecoveryBusyRetry
	t.Cleanup(func() {
		runOneFreshCompileForPausePointRecovery = originalAttempt
		waitPausePointRecoveryBusyRetry = originalWait
	})

	busyResult := []byte(`{"Success":false,"ErrorCode":"COMPILE_ALREADY_IN_PROGRESS"}`)
	runOneFreshCompileForPausePointRecovery = func(
		ctx context.Context,
		connection unityipc.Connection,
		params map[string]any,
		stdout io.Writer,
		stderr io.Writer,
		budget time.Duration,
	) int {
		_, _ = stdout.Write(busyResult)
		return 1
	}
	waitPausePointRecoveryBusyRetry = func(ctx context.Context, duration time.Duration) error {
		return context.Canceled
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runFreshCompileWithBusyRetryForPausePointRecovery(
		context.Background(),
		unityipc.Connection{ProjectRoot: t.TempDir()},
		map[string]any{},
		&stdout,
		&stderr,
	)
	if code != 1 {
		t.Fatalf("expected cancel exit 1, got %d", code)
	}
	if bytes.Contains(stdout.Bytes(), []byte("COMPILE_ALREADY_IN_PROGRESS")) {
		t.Fatalf("cancelled wait must not write busy compile JSON: %s", stdout.String())
	}
	if !strings.Contains(stderr.String(), "context canceled") && !strings.Contains(stderr.String(), "canceled") {
		t.Fatalf("expected classified cancel error on stderr, got %s", stderr.String())
	}
}

// Verifies a failed recovery compile writes its stdout buffer and does not resend enable.
func TestCompleteEnableWithReleaseRecovery_WhenCompileFails_WritesStdoutAndDoesNotResend(t *testing.T) {
	originalSwitch := sendSetCodeOptimizationDebug
	originalCompile := runFreshCompileForPausePointRecovery
	t.Cleanup(func() {
		sendSetCodeOptimizationDebug = originalSwitch
		runFreshCompileForPausePointRecovery = originalCompile
	})

	sendSetCodeOptimizationDebug = func(ctx context.Context, connection unityipc.Connection) error {
		return nil
	}
	compileFailure := []byte(`{"Success":false,"ErrorCount":1,"Message":"compile failed"}`)
	runFreshCompileForPausePointRecovery = func(
		ctx context.Context,
		connection unityipc.Connection,
		params map[string]any,
		stdout io.Writer,
		stderr io.Writer,
	) int {
		_, _ = stdout.Write(compileFailure)
		return 2
	}

	sendCount := 0
	var stdout bytes.Buffer
	code := completeEnableWithReleaseRecovery(
		context.Background(),
		unityipc.Connection{ProjectRoot: t.TempDir()},
		&stdout,
		io.Discard,
		func(writer io.Writer) int {
			sendCount++
			if sendCount == 1 {
				_, _ = writer.Write([]byte(releaseCodeOptimizationEnableFailureJSON))
				return 1
			}
			t.Fatal("enable must not be resent after compile failure")
			return 0
		},
	)
	if code != 2 {
		t.Fatalf("expected compile exit 2, got %d", code)
	}
	if sendCount != 1 {
		t.Fatalf("enable send count mismatch: %d", sendCount)
	}
	if !bytes.Equal(stdout.Bytes(), compileFailure) {
		t.Fatalf("compile stdout mismatch: %q", stdout.Bytes())
	}
}

// Verifies the recovery injection restates Message's warning count so its N matches the list it
// points at, instead of leaving the count Unity computed before the switch note was added.
func TestCompleteEnableWithReleaseRecovery_WhenUnityAlreadyWarned_RestatesMessageWarningCount(t *testing.T) {
	stubPausePointRecoverySwitchAndCompile(t)

	stdout := runReleaseRecoveryEnable(t, `{"Success":true,"Id":"jump","Status":"Enabled","IsEnabled":true,`+
		`"TimeoutSeconds":30,"Message":"Pause point enabled. 1 warning(s). See Warnings.",`+
		`"Warning":"physics dispatch warning.","Warnings":["physics dispatch warning."]}`)

	payload := decodePausePointRecoveryPayload(t, stdout)
	message, _ := payload["Message"].(string)
	if message != "Pause point enabled. 2 warning(s). See Warnings." {
		t.Fatalf("Message mismatch: %q", message)
	}
	assertPausePointRecoveryWarningsAgree(t, payload, 2)
}

// Verifies an enable that warned about nothing gains the pointer rather than staying silent about
// the switch note the recovery just added.
func TestCompleteEnableWithReleaseRecovery_WhenUnityDidNotWarn_AddsMessageWarningPointer(t *testing.T) {
	stubPausePointRecoverySwitchAndCompile(t)

	stdout := runReleaseRecoveryEnable(t, `{"Success":true,"Id":"jump","Status":"Enabled","IsEnabled":true,`+
		`"TimeoutSeconds":30,"Message":"Pause point enabled."}`)

	payload := decodePausePointRecoveryPayload(t, stdout)
	message, _ := payload["Message"].(string)
	if message != "Pause point enabled. 1 warning(s). See Warnings." {
		t.Fatalf("Message mismatch: %q", message)
	}
	assertPausePointRecoveryWarningsAgree(t, payload, 1)
}

// stubPausePointRecoverySwitchAndCompile makes the Debug switch and the recovery compile succeed
// without touching Unity, so a test can exercise the response rewrite alone.
func stubPausePointRecoverySwitchAndCompile(t *testing.T) {
	t.Helper()

	originalSwitch := sendSetCodeOptimizationDebug
	originalCompile := runFreshCompileForPausePointRecovery
	t.Cleanup(func() {
		sendSetCodeOptimizationDebug = originalSwitch
		runFreshCompileForPausePointRecovery = originalCompile
	})
	sendSetCodeOptimizationDebug = func(ctx context.Context, connection unityipc.Connection) error {
		return nil
	}
	runFreshCompileForPausePointRecovery = func(
		ctx context.Context,
		connection unityipc.Connection,
		params map[string]any,
		stdout io.Writer,
		stderr io.Writer,
	) int {
		return 0
	}
}

// runReleaseRecoveryEnable drives one Release rejection followed by the given successful enable
// response and returns the raw stdout the recovery wrote.
func runReleaseRecoveryEnable(t *testing.T, successJSON string) string {
	t.Helper()

	sendCount := 0
	var stdout bytes.Buffer
	code := completeEnableWithReleaseRecovery(
		context.Background(),
		unityipc.Connection{ProjectRoot: t.TempDir()},
		&stdout,
		io.Discard,
		func(writer io.Writer) int {
			sendCount++
			if sendCount == 1 {
				_, _ = writer.Write([]byte(releaseCodeOptimizationEnableFailureJSON))
				return 1
			}
			_, _ = writer.Write([]byte(successJSON))
			return 0
		},
	)
	if code != 0 {
		t.Fatalf("expected success, got %d with stdout %s", code, stdout.String())
	}
	return stdout.String()
}

// decodePausePointRecoveryPayload reads the rewritten enable response as raw JSON, so a test sees
// the keys a caller sees rather than the ones a typed struct would invent.
func decodePausePointRecoveryPayload(t *testing.T, stdout string) map[string]any {
	t.Helper()

	payload := map[string]any{}
	if err := json.Unmarshal([]byte(stdout), &payload); err != nil {
		t.Fatalf("stdout is not JSON: %v\n%s", err, stdout)
	}
	return payload
}

// assertPausePointRecoveryWarningsAgree checks Warnings holds the expected count, ends with the
// switch note, and that Warning is exactly its joined form.
func assertPausePointRecoveryWarningsAgree(t *testing.T, payload map[string]any, expectedCount int) {
	t.Helper()

	warnings, _ := payload["Warnings"].([]any)
	if len(warnings) != expectedCount {
		t.Fatalf("Warnings mismatch: %#v", payload["Warnings"])
	}
	entries := make([]string, 0, len(warnings))
	for _, warning := range warnings {
		text, _ := warning.(string)
		entries = append(entries, text)
	}
	if entries[len(entries)-1] != pausePointAutoDebugSwitchWarning {
		t.Fatalf("switch note must be the last entry: %#v", entries)
	}
	warning, _ := payload["Warning"].(string)
	if warning != strings.Join(entries, " ") {
		t.Fatalf("Warning must be the joined form of Warnings: %q vs %#v", warning, entries)
	}
}
