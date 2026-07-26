package projectrunner

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"io"
	"strings"
	"testing"
	"time"

	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

// pausePointRejectedTriggerResponse is the response Unity produces when a simulate command is
// refused before it runs anything because a pause point is holding PlayMode paused. Only Success and
// RejectedByActivePausePointId matter to the diagnosis; the rest is kept for realism.
func pausePointRejectedTriggerResponse(rejectedByID string) string {
	return `{"Success":false,` +
		`"Message":"PlayMode is paused because pause point '` + rejectedByID + `' is active ...",` +
		`"InterruptedByPausePoint":false,"PausePointId":null,` +
		`"RejectedByActivePausePointId":"` + rejectedByID + `"}`
}

func stubPausePointHit(t *testing.T, unityWarning string) {
	t.Helper()

	originalQuery := queryPausePointStatus
	t.Cleanup(func() {
		queryPausePointStatus = originalQuery
	})

	queryPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		return pausePointStatusResponse{
			Success:     true,
			Id:          id,
			Status:      pausePointStatusHit,
			IsHit:       true,
			HitCount:    1,
			Warning:     unityWarning,
			EditorState: pausePointEditorState{IsPlaying: true, IsPaused: true, CapturedAt: "PausePointHit"},
		}, nil
	}
}

func stubPausePointMatchingLogs(t *testing.T, fetchError error) {
	t.Helper()

	originalFetch := fetchMatchingLogs
	t.Cleanup(func() {
		fetchMatchingLogs = originalFetch
	})

	fetchMatchingLogs = func(
		ctx context.Context,
		connection unityipc.Connection,
		searchText string,
		maxCount int,
	) (pausePointMatchingLogsResult, error) {
		if fetchError != nil {
			return pausePointMatchingLogsResult{}, fetchError
		}
		return pausePointMatchingLogsResult{
			SearchText:     searchText,
			TotalCount:     1,
			DisplayedCount: 1,
			MaxCount:       maxCount,
			Logs:           []pausePointMatchingLog{{Type: "Log", Message: "[jump] hit"}},
		}, nil
	}
}

// stubPausePointTriggerDispatch makes the triggered command produce triggerStdout, which the wait
// path turns into TriggerResult.Response exactly as a real dispatch would.
func stubPausePointTriggerDispatch(t *testing.T, triggerStdout string) {
	t.Helper()

	originalDispatch := dispatchPausePointTriggerCommand
	t.Cleanup(func() {
		dispatchPausePointTriggerCommand = originalDispatch
	})

	dispatchPausePointTriggerCommand = func(
		ctx context.Context,
		connection unityipc.Connection,
		command string,
		commandArgs []string,
		startPath string,
		stdout io.Writer,
		stderr io.Writer,
	) int {
		_, _ = stdout.Write([]byte(triggerStdout))
		return 0
	}
}

func runAwaitWithStubbedTrigger(t *testing.T) (int, string) {
	t.Helper()

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runWaitForPausePoint(context.Background(), unityipc.Connection{}, waitForPausePointOptions{
		id:                   "jump",
		timeoutSeconds:       1,
		timeout:              time.Second,
		matchingLogsMaxCount: 5,
		triggerCommand:       "simulate-keyboard",
		triggerArgs:          []string{"--action", "Press", "--key", "W"},
	}, &stdout, &stderr)
	if stderr.Len() > 0 {
		t.Logf("stderr: %s", stderr.String())
	}
	return code, stdout.String()
}

func decodePausePointWaitResult(t *testing.T, output string) pausePointWaitResult {
	t.Helper()

	result := pausePointWaitResult{}
	if err := json.Unmarshal([]byte(output), &result); err != nil {
		t.Fatalf("stdout parse failed: %v from %s", err, output)
	}
	return result
}

// Verifies a trigger refused by the very marker being awaited is called out end to end: the hit
// still reads as a success, so without this the refusal stays buried in TriggerResult.Response.
func TestRunWaitForPausePointWarnsWhenTheTriggerWasRefusedByThisMarker(t *testing.T) {
	stubPausePointHit(t, "")
	stubPausePointMatchingLogs(t, nil)
	stubPausePointTriggerDispatch(t, pausePointRejectedTriggerResponse("jump"))

	code, output := runAwaitWithStubbedTrigger(t)

	if code != 0 {
		t.Fatalf("expected the hit to stay a success, got %d: %s", code, output)
	}
	result := decodePausePointWaitResult(t, output)
	if !strings.Contains(result.Warning, "refused") {
		t.Errorf("expected a refusal warning: %q", result.Warning)
	}
	if result.TriggerFailed == nil || !*result.TriggerFailed {
		t.Errorf("TriggerFailed must be promoted to the top level: %#v", result.TriggerFailed)
	}
}

// Verifies the refusal warning still reaches the caller when the matching-log fetch fails, the
// branch that used to build a payload with no Warning field at all.
func TestRunWaitForPausePointKeepsTheRefusalWarningWhenTheLogFetchFails(t *testing.T) {
	stubPausePointHit(t, "")
	stubPausePointMatchingLogs(t, errors.New("unity busy"))
	stubPausePointTriggerDispatch(t, pausePointRejectedTriggerResponse("jump"))

	_, output := runAwaitWithStubbedTrigger(t)

	if strings.Contains(output, `"MatchingLogs"`) {
		t.Errorf("a failed fetch must omit MatchingLogs entirely: %s", output)
	}
	result := decodePausePointWaitResult(t, output)
	if !strings.Contains(result.Warning, "refused") {
		t.Errorf("expected the refusal warning despite the failed log fetch: %q", result.Warning)
	}
	if result.TriggerFailed == nil || !*result.TriggerFailed {
		t.Errorf("TriggerFailed must survive the failed log fetch: %#v", result.TriggerFailed)
	}
}

// Verifies Unity's own warning survives alongside the CLI's. Both use the JSON name "Warning", where
// the CLI's outer field shadows the embedded Unity one, so Unity's text is lost unless joined in.
func TestRunWaitForPausePointKeepsUnityWarningAlongsideCliWarnings(t *testing.T) {
	stubPausePointHit(t, "Unity-side enable warning.")
	stubPausePointMatchingLogs(t, nil)
	stubPausePointTriggerDispatch(t, pausePointRejectedTriggerResponse("jump"))

	_, output := runAwaitWithStubbedTrigger(t)

	result := decodePausePointWaitResult(t, output)
	if !strings.Contains(result.Warning, "Unity-side enable warning.") {
		t.Errorf("Unity's warning was dropped: %q", result.Warning)
	}
	if !strings.Contains(result.Warning, "refused") {
		t.Errorf("the CLI warning was dropped: %q", result.Warning)
	}
}

// Verifies the refusal is only blamed on this wait when the refusing marker is the one being
// awaited, so a pause owned by some other marker does not produce advice about this one.
func TestPausePointTriggerRefusalWarningRequiresTheAwaitedMarker(t *testing.T) {
	refusedByThisMarker := &pausePointTriggerResult{
		Completed: true,
		Response:  json.RawMessage(pausePointRejectedTriggerResponse("jump")),
	}
	if pausePointTriggerRefusalWarning(refusedByThisMarker, "jump") == "" {
		t.Error("expected a warning when this marker refused the trigger")
	}

	refusedByAnotherMarker := &pausePointTriggerResult{
		Completed: true,
		Response:  json.RawMessage(pausePointRejectedTriggerResponse("other-marker")),
	}
	if warning := pausePointTriggerRefusalWarning(refusedByAnotherMarker, "jump"); warning != "" {
		t.Errorf("a refusal by another marker must not be warned about here: %q", warning)
	}
}

// Verifies a marker hit while the trigger's input was being applied produces no warning: that is
// the normal, working case, and the earlier draft predicate warned on exactly it.
func TestPausePointTriggerRefusalWarningIgnoresAMidExecutionInterruption(t *testing.T) {
	interrupted := &pausePointTriggerResult{
		Completed: true,
		Response: json.RawMessage(
			`{"Success":true,"InterruptedByPausePoint":true,"PausePointId":"jump"}`),
	}

	if warning := pausePointTriggerRefusalWarning(interrupted, "jump"); warning != "" {
		t.Errorf("a mid-execution interruption is the normal case: %q", warning)
	}
}

// Verifies a trigger that never reported back within the grace window is neither warned about nor
// called failed: its outcome is unknown, and claiming failure would be as wrong as claiming success.
func TestPausePointTriggerDiagnosisTreatsAnIncompleteTriggerAsUnknown(t *testing.T) {
	incomplete := &pausePointTriggerResult{Completed: false}

	if warning := pausePointTriggerRefusalWarning(incomplete, "jump"); warning != "" {
		t.Errorf("an unfinished trigger cannot be diagnosed: %q", warning)
	}
	if pausePointTriggerFailed(incomplete) {
		t.Error("an unfinished trigger has no known outcome")
	}
}

// Verifies the two failure shapes a completed trigger can have are both promoted, and a plain
// success is not.
func TestPausePointTriggerFailedCoversBothFailureShapes(t *testing.T) {
	failedResponse := &pausePointTriggerResult{
		Completed: true,
		Response:  json.RawMessage(`{"Success":false,"Message":"no keyboard device found"}`),
	}
	if !pausePointTriggerFailed(failedResponse) {
		t.Error("a completed trigger reporting Success:false has failed")
	}

	failedDispatch := &pausePointTriggerResult{
		Completed: true,
		Error:     `{"Error":{"ErrorCode":"UNITY_NOT_REACHABLE"}}`,
	}
	if !pausePointTriggerFailed(failedDispatch) {
		t.Error("a trigger whose dispatch failed has failed")
	}

	succeeded := &pausePointTriggerResult{
		Completed: true,
		Response:  json.RawMessage(`{"Success":true}`),
	}
	if pausePointTriggerFailed(succeeded) {
		t.Error("a successful trigger must not be reported as failed")
	}

	if pausePointTriggerFailed(nil) {
		t.Error("no trigger at all cannot have failed")
	}
}
