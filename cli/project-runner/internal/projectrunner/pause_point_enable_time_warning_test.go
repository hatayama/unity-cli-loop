package projectrunner

import (
	"bytes"
	"context"
	"encoding/json"
	"io"
	"strings"
	"testing"
	"time"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

const enableTimePatchWarning = "GameObjects that already existed at enable time may never reach the marker."

// Verifies a successful enable --await hit keeps the enable-time patch warning out of Warning and
// exposes it on EnableTimeWarning instead.
func TestRunPausePointWaitAfterEnableSeparatesEnableTimeWarningOnHit(t *testing.T) {
	stubPausePointHit(t, "")
	stubPausePointMatchingLogs(t, nil)
	stubPausePointTriggerDispatch(t, `{"Success":true}`)

	code, output := runEnableAwaitWithStubbedTrigger(t, enableTimePatchWarning)
	if code != 0 {
		t.Fatalf("expected hit success, got %d: %s", code, output)
	}
	result := decodePausePointWaitResult(t, output)
	if result.EnableTimeWarning != enableTimePatchWarning {
		t.Fatalf("EnableTimeWarning mismatch: %q", result.EnableTimeWarning)
	}
	if strings.Contains(result.Warning, enableTimePatchWarning) {
		t.Fatalf("enable-time warning must not appear in Warning: %q", result.Warning)
	}
}

// Verifies enable-response Warnings are copied onto EnableTimeWarnings on a successful hit.
func TestRunPausePointWaitAfterEnableCopiesWarningsOntoEnableTimeWarnings(t *testing.T) {
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
	if result.EnableTimeWarning != strings.Join(enableWarnings, " ") {
		t.Fatalf("EnableTimeWarning mismatch: %q", result.EnableTimeWarning)
	}
	if len(result.EnableTimeWarnings) != 2 ||
		result.EnableTimeWarnings[0] != enableWarnings[0] ||
		result.EnableTimeWarnings[1] != enableWarnings[1] {
		t.Fatalf("EnableTimeWarnings mismatch: %#v", result.EnableTimeWarnings)
	}
}

// Verifies runEnablePausePointAndAwait copies enable-response Warnings onto EnableTimeWarnings.
func TestRunEnablePausePointAndAwait_WhenEnableReturnsWarnings_CopiesThemOntoEnableTimeWarnings(t *testing.T) {
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

	var response pausePointWaitResult
	if err := json.Unmarshal(stdout.Bytes(), &response); err != nil {
		t.Fatalf("failed to decode stdout: %v\n%s", err, stdout.String())
	}
	if response.EnableTimeWarning != "a. b." {
		t.Fatalf("EnableTimeWarning mismatch: %q", response.EnableTimeWarning)
	}
	if len(response.EnableTimeWarnings) != 2 ||
		response.EnableTimeWarnings[0] != enableWarnings[0] ||
		response.EnableTimeWarnings[1] != enableWarnings[1] {
		t.Fatalf("EnableTimeWarnings mismatch: %#v", response.EnableTimeWarnings)
	}
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
