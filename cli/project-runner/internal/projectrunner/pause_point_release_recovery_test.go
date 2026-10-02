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

// Verifies the success probe treats undecodable enable output as a failure, and nil or failed
// responses never gain the switch warning.
func TestPausePointRecoveryProbesIgnoreUnusableResponses(t *testing.T) {
	if isSuccessfulEnableResponse([]byte("not json")) {
		t.Fatal("undecodable output must not count as a successful enable")
	}
	if isRetryablePausePointRecoveryCompileResult([]byte("not json")) {
		t.Fatal("undecodable compile output must not be retried")
	}

	applyPausePointRecoverySwitchWarning(nil)
	appendPausePointWarningToBothForms(nil, "ignored")
	failed := pausePointStatusResponse{Success: false}
	applyPausePointRecoverySwitchWarning(&failed)
	if failed.Warning != "" || len(failed.Warnings) != 0 {
		t.Fatalf("failed response must not gain warnings: %#v", failed)
	}
}

// Verifies a response carrying only the joined Warning string keeps that text as its own entry
// ahead of the switch note.
func TestInjectPausePointRecoveryWarningKeepsWarningOnlyResponse(t *testing.T) {
	rewritten, err := injectPausePointRecoveryWarning([]byte(`{"Success":true,"Warning":"physics dispatch warning.","Extra":7}`))
	if err != nil {
		t.Fatalf("inject failed: %v", err)
	}
	payload := decodePausePointRecoveryPayload(t, string(rewritten))
	assertPausePointRecoveryWarningsAgree(t, payload, 2)
	if warnings, _ := payload["Warnings"].([]any); warnings[0] != "physics dispatch warning." {
		t.Fatalf("existing warning must stay first: %#v", payload["Warnings"])
	}
	if payload["Extra"] != float64(7) {
		t.Fatalf("unrelated keys must survive: %#v", payload)
	}
}

// Verifies malformed enable responses are rejected instead of being rewritten with a guessed shape.
func TestInjectPausePointRecoveryWarningRejectsMalformedFields(t *testing.T) {
	for name, raw := range map[string]string{
		"not an object":        `[1,2]`,
		"warnings not a list":  `{"Success":true,"Warnings":"one"}`,
		"warning not a string": `{"Success":true,"Warning":5}`,
		"message not a string": `{"Success":true,"Message":["a"]}`,
	} {
		t.Run(name, func(t *testing.T) {
			if rewritten, err := injectPausePointRecoveryWarning([]byte(raw)); err == nil {
				t.Fatalf("expected an error, got %s", rewritten)
			}
		})
	}
}

// Verifies the retry wait returns nil once the duration elapses and the context error when cancelled.
func TestWaitContextDuration(t *testing.T) {
	if err := waitContextDuration(context.Background(), time.Millisecond); err != nil {
		t.Fatalf("elapsed wait must succeed: %v", err)
	}
	ctx, cancel := context.WithCancel(context.Background())
	cancel()
	if err := waitContextDuration(ctx, time.Hour); err != context.Canceled {
		t.Fatalf("cancelled wait must return context.Canceled, got %v", err)
	}
}

const serverBusyRPCErrorJSON = `{"code":-32603,"message":"busy","data":{"type":"server_busy"}}`

func serverBusyRPCError(t *testing.T) error {
	t.Helper()
	rpcErr := &unityipc.RPCError{}
	if err := json.Unmarshal([]byte(serverBusyRPCErrorJSON), rpcErr); err != nil {
		t.Fatalf("failed to build busy error: %v", err)
	}
	if !isUnityServerBusyRPCError(rpcErr) {
		t.Fatal("fixture must be recognized as server_busy")
	}
	return rpcErr
}

// stubFreshCompileSends replaces the compile sender with one that returns the given errors in
// order, then succeeds, and records the retry waits requested in between.
func stubFreshCompileSends(t *testing.T, errs []error, waitErr error) (*int, *[]time.Duration) {
	t.Helper()
	originalSend := sendFreshCompileRequest
	originalWait := waitPausePointRecoveryBusyRetry
	t.Cleanup(func() {
		sendFreshCompileRequest = originalSend
		waitPausePointRecoveryBusyRetry = originalWait
	})
	sends := 0
	waits := []time.Duration{}
	sendFreshCompileRequest = func(context.Context, unityipc.Connection, string, map[string]any, unityipc.ProgressFunc, time.Duration) (unityipc.UnitySendOutcome, error) {
		sends++
		if sends <= len(errs) {
			return unityipc.UnitySendOutcome{}, errs[sends-1]
		}
		return unityipc.UnitySendOutcome{RequestDispatched: true, Result: json.RawMessage(`{"Success":true}`)}, nil
	}
	waitPausePointRecoveryBusyRetry = func(_ context.Context, duration time.Duration) error {
		waits = append(waits, duration)
		return waitErr
	}
	return &sends, &waits
}

// Verifies busy compile sends are retried within the budget, with each wait capped by the time left.
func TestSendCompileWithBusyRetryRetriesBusyUntilSuccess(t *testing.T) {
	sends, waits := stubFreshCompileSends(t, []error{serverBusyRPCError(t)}, nil)

	outcome, err := sendCompileWithBusyRetry(context.Background(), unityipc.Connection{}, "compile", map[string]any{}, nil, 0, time.Second)

	if err != nil || string(outcome.Result) != `{"Success":true}` {
		t.Fatalf("unexpected result: outcome=%#v err=%v", outcome, err)
	}
	if *sends != 2 {
		t.Fatalf("sends = %d, want 2", *sends)
	}
	if len(*waits) != 1 || (*waits)[0] > time.Second || (*waits)[0] <= 0 {
		t.Fatalf("retry wait must be capped by the remaining budget: %v", *waits)
	}
}

// Verifies a busy send with no budget left, a non-busy error, and a cancelled retry wait all stop retrying.
func TestSendCompileWithBusyRetryStopsRetrying(t *testing.T) {
	t.Run("budget exhausted", func(t *testing.T) {
		busy := serverBusyRPCError(t)
		sends, waits := stubFreshCompileSends(t, []error{busy, busy}, nil)
		_, err := sendCompileWithBusyRetry(context.Background(), unityipc.Connection{}, "compile", map[string]any{}, nil, 0, 0)
		if err != busy || *sends != 1 || len(*waits) != 0 {
			t.Fatalf("err=%v sends=%d waits=%v", err, *sends, *waits)
		}
	})
	t.Run("non-busy error", func(t *testing.T) {
		failure := io.ErrUnexpectedEOF
		sends, _ := stubFreshCompileSends(t, []error{failure}, nil)
		_, err := sendCompileWithBusyRetry(context.Background(), unityipc.Connection{}, "compile", map[string]any{}, nil, 0, time.Minute)
		if err != failure || *sends != 1 {
			t.Fatalf("err=%v sends=%d", err, *sends)
		}
	})
	t.Run("retry wait cancelled", func(t *testing.T) {
		sends, _ := stubFreshCompileSends(t, []error{serverBusyRPCError(t)}, context.Canceled)
		_, err := sendCompileWithBusyRetry(context.Background(), unityipc.Connection{}, "compile", map[string]any{}, nil, 0, time.Minute)
		if err != context.Canceled || *sends != 1 {
			t.Fatalf("err=%v sends=%d", err, *sends)
		}
	})
}

// Verifies the default recovery compile sends through the busy-retry sender and reports an
// undispatched send failure without waiting on compile status.
func TestRunOneFreshCompileForPausePointRecoveryUsesBusyRetrySender(t *testing.T) {
	sends, _ := stubFreshCompileSends(t, []error{io.ErrUnexpectedEOF}, nil)
	var stdout, stderr bytes.Buffer

	code := runOneFreshCompileForPausePointRecoveryDefault(
		context.Background(),
		unityipc.Connection{ProjectRoot: t.TempDir()},
		map[string]any{},
		&stdout,
		&stderr,
		time.Minute,
	)

	if code != 1 {
		t.Fatalf("exit code = %d, want 1", code)
	}
	if *sends != 1 {
		t.Fatalf("sends = %d, want 1", *sends)
	}
	if stderr.Len() == 0 {
		t.Fatal("stderr must report the send failure")
	}
}

// Verifies the recovery compile loop rejects an invalid timeout before compiling.
func TestRunFreshCompileWithBusyRetryRejectsInvalidTimeout(t *testing.T) {
	originalAttempt := runOneFreshCompileForPausePointRecovery
	t.Cleanup(func() { runOneFreshCompileForPausePointRecovery = originalAttempt })
	runOneFreshCompileForPausePointRecovery = func(context.Context, unityipc.Connection, map[string]any, io.Writer, io.Writer, time.Duration) int {
		t.Fatal("compile must not run")
		return 0
	}
	var stdout, stderr bytes.Buffer

	code := runFreshCompileWithBusyRetryForPausePointRecovery(
		context.Background(),
		unityipc.Connection{ProjectRoot: t.TempDir()},
		map[string]any{compileWaitTimeoutParam: 0},
		&stdout,
		&stderr,
	)

	if code != 1 || !strings.Contains(stderr.String(), "--timeout-seconds") {
		t.Fatalf("code=%d stderr=%s", code, stderr.String())
	}
}

// stubRecoveryCompileAttempts makes each recovery compile attempt write the next result and
// return its code, and records retry waits.
func stubRecoveryCompileAttempts(t *testing.T, results []string, codes []int) (*int, *[]time.Duration) {
	t.Helper()
	originalAttempt := runOneFreshCompileForPausePointRecovery
	originalWait := waitPausePointRecoveryBusyRetry
	t.Cleanup(func() {
		runOneFreshCompileForPausePointRecovery = originalAttempt
		waitPausePointRecoveryBusyRetry = originalWait
	})
	attempts := 0
	waits := []time.Duration{}
	runOneFreshCompileForPausePointRecovery = func(_ context.Context, _ unityipc.Connection, _ map[string]any, stdout io.Writer, _ io.Writer, _ time.Duration) int {
		attempts++
		_, _ = stdout.Write([]byte(results[attempts-1]))
		return codes[attempts-1]
	}
	waitPausePointRecoveryBusyRetry = func(_ context.Context, duration time.Duration) error {
		waits = append(waits, duration)
		return nil
	}
	return &attempts, &waits
}

// Verifies a compile result meaning Unity is still updating is retried, with the wait capped by
// the time left, and a later success returns 0 without writing the busy result.
func TestRunFreshCompileWithBusyRetryRetriesEditorUpdating(t *testing.T) {
	attempts, waits := stubRecoveryCompileAttempts(t,
		[]string{`{"Success":false,"ErrorCode":"COMPILE_EDITOR_UPDATING"}`, `{"Success":true}`},
		[]int{1, 0})
	var stdout bytes.Buffer

	code := runFreshCompileWithBusyRetryForPausePointRecovery(
		context.Background(),
		unityipc.Connection{ProjectRoot: t.TempDir()},
		map[string]any{compileWaitTimeoutParam: 1},
		&stdout,
		io.Discard,
	)

	if code != 0 || *attempts != 2 {
		t.Fatalf("code=%d attempts=%d", code, *attempts)
	}
	if len(*waits) != 1 || (*waits)[0] > time.Second {
		t.Fatalf("retry wait must be capped by the 1s budget: %v", *waits)
	}
	if stdout.Len() != 0 {
		t.Fatalf("busy result must not be written: %s", stdout.String())
	}
}

// Verifies a non-retryable compile failure is written to stdout once and returned without retrying.
func TestRunFreshCompileWithBusyRetryReturnsNonRetryableFailure(t *testing.T) {
	failure := `{"Success":false,"ErrorCount":3}`
	attempts, waits := stubRecoveryCompileAttempts(t, []string{failure}, []int{1})
	var stdout bytes.Buffer

	code := runFreshCompileWithBusyRetryForPausePointRecovery(
		context.Background(),
		unityipc.Connection{ProjectRoot: t.TempDir()},
		map[string]any{},
		&stdout,
		io.Discard,
	)

	if code != 1 || *attempts != 1 || len(*waits) != 0 {
		t.Fatalf("code=%d attempts=%d waits=%v", code, *attempts, *waits)
	}
	if stdout.String() != failure {
		t.Fatalf("stdout = %q, want %q", stdout.String(), failure)
	}
}

// Verifies a failed Debug switch stops recovery before any compile runs.
func TestRecoverReleaseCodeOptimizationStopsWhenSwitchFails(t *testing.T) {
	originalSwitch := sendSetCodeOptimizationDebug
	originalCompile := runFreshCompileForPausePointRecovery
	t.Cleanup(func() {
		sendSetCodeOptimizationDebug = originalSwitch
		runFreshCompileForPausePointRecovery = originalCompile
	})
	sendSetCodeOptimizationDebug = func(context.Context, unityipc.Connection) error {
		return io.ErrUnexpectedEOF
	}
	runFreshCompileForPausePointRecovery = func(context.Context, unityipc.Connection, map[string]any, io.Writer, io.Writer) int {
		t.Fatal("compile must not run after a failed switch")
		return 0
	}
	var stdout, stderr bytes.Buffer

	code := recoverReleaseCodeOptimization(context.Background(), unityipc.Connection{ProjectRoot: t.TempDir()}, &stdout, &stderr)

	if code != 1 || stdout.Len() != 0 || stderr.Len() == 0 {
		t.Fatalf("code=%d stdout=%q stderr=%q", code, stdout.String(), stderr.String())
	}
}

// Verifies a successful resend whose warnings cannot be rewritten fails instead of printing a
// response that silently drops the switch note.
func TestCompleteEnableWithReleaseRecoveryFailsWhenRewriteFails(t *testing.T) {
	stubPausePointRecoverySwitchAndCompile(t)
	sendCount := 0
	var stdout, stderr bytes.Buffer

	code := completeEnableWithReleaseRecovery(
		context.Background(),
		unityipc.Connection{ProjectRoot: t.TempDir()},
		&stdout,
		&stderr,
		func(writer io.Writer) int {
			sendCount++
			if sendCount == 1 {
				_, _ = writer.Write([]byte(releaseCodeOptimizationEnableFailureJSON))
				return 1
			}
			_, _ = writer.Write([]byte(`{"Success":true,"Warnings":"not-a-list"}`))
			return 0
		},
	)

	if code != 1 || stdout.Len() != 0 || stderr.Len() == 0 {
		t.Fatalf("code=%d stdout=%q stderr=%q", code, stdout.String(), stderr.String())
	}
}

// Verifies enable send failures and undecodable enable results are reported and returned as errors.
func TestSendEnablePausePointAndDecodeReportsFailures(t *testing.T) {
	original := sendEnablePausePointIPC
	t.Cleanup(func() { sendEnablePausePointIPC = original })

	cases := map[string]struct {
		outcome unityipc.UnitySendOutcome
		err     error
	}{
		"send failure":       {err: io.ErrUnexpectedEOF},
		"undecodable result": {outcome: unityipc.UnitySendOutcome{Result: json.RawMessage(`"text"`)}},
	}
	for name, testCase := range cases {
		t.Run(name, func(t *testing.T) {
			sendEnablePausePointIPC = func(context.Context, unityipc.Connection, map[string]any, io.Writer) (unityipc.UnitySendOutcome, error) {
				return testCase.outcome, testCase.err
			}
			var stderr bytes.Buffer

			raw, _, _, err := sendEnablePausePointAndDecode(context.Background(), unityipc.Connection{ProjectRoot: t.TempDir()}, map[string]any{}, &stderr)

			if err == nil || raw != nil {
				t.Fatalf("expected an error and no raw result, got raw=%s err=%v", raw, err)
			}
			if stderr.Len() == 0 {
				t.Fatal("stderr must report the failure")
			}
		})
	}
}
