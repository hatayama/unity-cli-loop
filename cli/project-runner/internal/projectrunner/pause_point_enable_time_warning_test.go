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

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

const enableTimePatchWarning = "GameObjects that already existed at enable time may never reach the marker."

// assertNoRetiredEnableTimeWarningKeys fails when a hit payload still carries the retired
// enable-time warning fields: the whole point of the prefix is that Warnings is the only array.
func assertNoRetiredEnableTimeWarningKeys(t *testing.T, output string) {
	t.Helper()

	raw := map[string]json.RawMessage{}
	if err := json.Unmarshal([]byte(output), &raw); err != nil {
		t.Fatalf("failed to decode raw stdout: %v\n%s", err, output)
	}
	for _, retiredKey := range []string{"EnableTimeWarning", "EnableTimeWarnings"} {
		if _, ok := raw[retiredKey]; ok {
			t.Fatalf("%s must no longer appear on a hit payload: %s", retiredKey, output)
		}
	}
}

// Verifies a successful enable --await hit lists the enable-time patch warning in Warnings with the
// enable-time prefix, and no longer answers on a warning field of its own.
func TestRunPausePointWaitAfterEnablePrefixesEnableTimeWarningOnHit(t *testing.T) {
	stubPausePointHit(t, "")
	stubPausePointMatchingLogs(t, nil)
	stubPausePointTriggerDispatch(t, `{"Success":true}`)

	code, output := runEnableAwaitWithStubbedTrigger(t, enableTimePatchWarning)
	if code != 0 {
		t.Fatalf("expected hit success, got %d: %s", code, output)
	}
	result := decodePausePointWaitResult(t, output)
	expected := pausePointEnableTimeWarningPrefix + enableTimePatchWarning
	if !slices.Contains(result.Warnings, expected) {
		t.Fatalf("Warnings mismatch: %#v", result.Warnings)
	}
	if result.Warning != strings.Join(result.Warnings, " ") {
		t.Fatalf("Warning must be the joined form of Warnings: %q vs %#v", result.Warning, result.Warnings)
	}
	assertNoRetiredEnableTimeWarningKeys(t, output)
}

// Verifies every enable-response warning reaches Warnings, prefixed and in the enable order.
func TestRunPausePointWaitAfterEnableCopiesEveryEnableWarningIntoWarnings(t *testing.T) {
	stubPausePointHit(t, "")
	stubPausePointMatchingLogs(t, nil)
	stubPausePointTriggerDispatch(t, `{"Success":true}`)

	enableWarnings := []string{"physics dispatch warning.", "mid-solver values warning."}
	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runPausePointWaitAfterEnable(
		context.Background(),
		unityipc.Connection{},
		waitForPausePointOptions{
			id:                   "jump",
			timeoutSeconds:       1,
			timeout:              time.Second,
			matchingLogsMaxCount: 5,
			triggerCommand:       "simulate-keyboard",
			triggerArgs:          []string{"--action", "Press", "--key", "W"},
		},
		enablePausePointPropagatedFields{
			Warning:  strings.Join(enableWarnings, " "),
			Warnings: enableWarnings,
		},
		&stdout,
		&stderr,
	)
	if code != 0 {
		t.Fatalf("expected hit success, got %d: %s", code, stdout.String())
	}
	result := decodePausePointWaitResult(t, stdout.String())
	expected := []string{
		pausePointEnableTimeWarningPrefix + enableWarnings[0],
		pausePointEnableTimeWarningPrefix + enableWarnings[1],
	}
	if !slices.Equal(result.Warnings, expected) {
		t.Fatalf("Warnings mismatch: %#v", result.Warnings)
	}
	assertNoRetiredEnableTimeWarningKeys(t, stdout.String())
}

// Verifies runEnablePausePointAndAwait carries the enable response's Warnings end to end.
func TestRunEnablePausePointAndAwait_WhenEnableReturnsWarnings_ListsThemInWarnings(t *testing.T) {
	originalQuery := queryPausePointStatus
	originalPoll := pausePointStatusPoll
	originalFetch := fetchMatchingLogs
	originalSend := sendEnablePausePointIPC
	pausePointStatusPoll = time.Millisecond
	t.Cleanup(func() {
		queryPausePointStatus = originalQuery
		pausePointStatusPoll = originalPoll
		fetchMatchingLogs = originalFetch
		sendEnablePausePointIPC = originalSend
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

	enableWarnings := []string{"a.", "b."}
	sendEnablePausePointIPC = func(
		ctx context.Context,
		connection unityipc.Connection,
		params map[string]any,
		stderr io.Writer,
	) (unityipc.UnitySendOutcome, error) {
		payload := `{"Success":true,"Id":"jump","Status":"Enabled","IsEnabled":true,"TimeoutSeconds":30,"Warning":"a. b.","Warnings":["a.","b."]}`
		return unityipc.UnitySendOutcome{Result: []byte(payload)}, nil
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

	response := pausePointWaitResult{}
	if err := json.Unmarshal(stdout.Bytes(), &response); err != nil {
		t.Fatalf("failed to decode stdout: %v\n%s", err, stdout.String())
	}
	expected := []string{
		pausePointEnableTimeWarningPrefix + enableWarnings[0],
		pausePointEnableTimeWarningPrefix + enableWarnings[1],
	}
	if !slices.Equal(response.Warnings, expected) {
		t.Fatalf("Warnings mismatch: %#v", response.Warnings)
	}
	if response.Warning != strings.Join(expected, " ") {
		t.Fatalf("Warning must be the joined form of Warnings: %q", response.Warning)
	}
	assertNoRetiredEnableTimeWarningKeys(t, stdout.String())
}

// Verifies a non-hit enable --await path still surfaces the enable-time warning on the existing
// Details.EnableWarning key (unchanged; not folded into Details.Warning).
func TestRunPausePointWaitAfterEnableKeepsEnableWarningDetailOnExpired(t *testing.T) {
	originalQuery := queryPausePointStatus
	originalPoll := pausePointStatusPoll
	pausePointStatusPoll = time.Millisecond
	defer func() {
		queryPausePointStatus = originalQuery
		pausePointStatusPoll = originalPoll
	}()

	queryPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		return pausePointStatusResponse{
			Id:          id,
			Status:      pausePointStatusExpired,
			Expired:     true,
			EditorState: pausePointEditorState{IsPlaying: true, CapturedAt: "Current"},
			Message:     "Pause point expired before it was hit.",
		}, nil
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runPausePointWaitAfterEnable(
		context.Background(),
		unityipc.Connection{ProjectRoot: "/tmp/MyProject"},
		waitForPausePointOptions{
			id:                   "jump",
			timeoutSeconds:       1,
			timeout:              time.Second,
			matchingLogsMaxCount: 5,
			markerJustEnabled:    true,
		},
		enablePausePointPropagatedFields{Warning: enableTimePatchWarning},
		&stdout,
		&stderr,
	)
	if code != 1 {
		t.Fatalf("expected non-hit failure, got %d stdout=%s stderr=%s", code, stdout.String(), stderr.String())
	}

	var envelope struct {
		Error clierrors.CLIError `json:"Error"`
	}
	if err := json.Unmarshal(stderr.Bytes(), &envelope); err != nil {
		t.Fatalf("unmarshal stderr: %v (%s)", err, stderr.String())
	}
	if envelope.Error.ErrorCode != clierrors.ErrorCodePausePointExpired {
		t.Fatalf("error code mismatch: %s", envelope.Error.ErrorCode)
	}
	enableWarning, ok := envelope.Error.Details["EnableWarning"].(string)
	if !ok || enableWarning != enableTimePatchWarning {
		t.Fatalf("Details.EnableWarning mismatch: %#v", envelope.Error.Details["EnableWarning"])
	}
	if warning, exists := envelope.Error.Details["Warning"]; exists {
		if warningText, isString := warning.(string); isString && strings.Contains(warningText, enableTimePatchWarning) {
			t.Fatalf("enable-time warning must stay on EnableWarning, not Details.Warning: %#v", warning)
		}
	}
}
