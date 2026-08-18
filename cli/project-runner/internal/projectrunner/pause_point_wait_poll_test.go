package projectrunner

import (
	"bytes"
	"context"
	"errors"
	"fmt"
	"io"
	"net"
	"os"
	"strings"
	"syscall"
	"testing"
	"time"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"

	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

// argumentErrorTriggerStderr is the error envelope a trigger command writes when its own argument
// parsing rejects a value, which is exactly the case that must abort the wait instead of letting
// the marker's full --timeout-seconds elapse.
const argumentErrorTriggerStderr = `{"Success":false,"Error":{"ErrorCode":"INVALID_ARGUMENT",` +
	`"Phase":"argument_parsing","Message":"Invalid value for --action: \"Hold\"","Retryable":false,` +
	`"SafeToRetry":false,"NextActions":["Pass one of: Press, KeyDown, KeyUp, ReleaseAll."]}}`

// unknownCommandTriggerStderr is the error envelope a mistyped trigger command name produces. It
// never reaches Unity at all, so it is as permanent a rejection as a bad argument value.
const unknownCommandTriggerStderr = `{"Success":false,"Error":{"ErrorCode":"UNKNOWN_COMMAND",` +
	`"Phase":"dispatch","Message":"Unknown command: simulate-keybord","Retryable":false,` +
	`"SafeToRetry":false,"NextActions":["Run ` + "`uloop list`" + ` to see the available commands."]}}`

// pausePointArmedStatusResponse is an armed-but-never-hit marker with a lifetime long enough that
// RemainingMilliseconds stays positive, so the abort response can be asserted to carry it.
func pausePointArmedStatusResponse(id string) pausePointStatusResponse {
	return pausePointStatusResponse{
		Id:                              id,
		Status:                          pausePointStatusEnabled,
		IsEnabled:                       true,
		TimeoutSeconds:                  60,
		ElapsedSinceEnabledMilliseconds: 1_000,
		EditorState:                     pausePointEditorState{IsPlaying: true, CapturedAt: "Current"},
	}
}

// Verifies a connect the operating system refused permanently aborts the wait at the first poll.
// Every poll dials again, so without the abort the refusal is reported only after the whole
// --timeout has been spent on an error that cannot clear.
func TestWaitForPausePointAbortsWhenTheConnectIsRefused(t *testing.T) {
	originalQuery := queryPausePointStatus
	originalPoll := pausePointStatusPoll
	pausePointStatusPoll = time.Millisecond
	defer func() {
		queryPausePointStatus = originalQuery
		pausePointStatusPoll = originalPoll
	}()

	refusedConnect := &unityipc.ConnectionAttemptError{
		Endpoint: "/tmp/uloop-501/UnityCliLoop-sample.sock",
		Cause: &net.OpError{
			Op:   "dial",
			Net:  "unix",
			Addr: &net.UnixAddr{Name: "/tmp/uloop-501/UnityCliLoop-sample.sock", Net: "unix"},
			Err:  os.NewSyscallError("connect", syscall.EPERM),
		},
	}
	queryCount := 0
	queryPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		queryCount++
		return pausePointStatusResponse{}, fmt.Errorf("pause point status query failed: %w", refusedConnect)
	}

	startedAt := time.Now()
	_, _, _, _, _, err := waitForPausePoint(context.Background(), unityipc.Connection{}, waitForPausePointOptions{
		id:             "jump",
		timeoutSeconds: 60,
		timeout:        10 * time.Second,
	})
	elapsed := time.Since(startedAt)

	if !errors.Is(err, refusedConnect) {
		t.Fatalf("expected the refused connect error, got %v", err)
	}
	if queryCount != 1 {
		t.Fatalf("expected the wait to stop after the first poll, got %d polls", queryCount)
	}
	if elapsed >= 5*time.Second {
		t.Fatalf("expected an early abort, waited %v of a 10s timeout", elapsed)
	}
}

// Verifies a --trigger rejected by its own argument parsing aborts the wait immediately, instead of
// waiting out the marker's --timeout-seconds, and reports the rejection on TriggerResult.
func TestWaitForPausePointAbortsWhenTriggerRejectsArguments(t *testing.T) {
	originalQuery := queryPausePointStatus
	originalDispatch := dispatchPausePointTriggerCommand
	originalPoll := pausePointStatusPoll
	pausePointStatusPoll = time.Millisecond
	defer func() {
		queryPausePointStatus = originalQuery
		dispatchPausePointTriggerCommand = originalDispatch
		pausePointStatusPoll = originalPoll
	}()

	queryPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		return pausePointArmedStatusResponse(id), nil
	}
	dispatchPausePointTriggerCommand = func(
		ctx context.Context,
		connection unityipc.Connection,
		command string,
		commandArgs []string,
		startPath string,
		stdout io.Writer,
		stderr io.Writer,
	) int {
		_, _ = stderr.Write([]byte(argumentErrorTriggerStderr))
		return 1
	}

	startedAt := time.Now()
	_, state, triggerResult, _, _, err := waitForPausePoint(context.Background(), unityipc.Connection{}, waitForPausePointOptions{
		id:             "jump",
		timeoutSeconds: 60,
		timeout:        10 * time.Second,
		triggerCommand: "simulate-keyboard",
		triggerArgs:    []string{"--action", "Hold", "--key", "A"},
	})
	elapsed := time.Since(startedAt)

	if err != nil {
		t.Fatalf("waitForPausePoint failed: %v", err)
	}
	if state != pausePointWaitStateTriggerFailed {
		t.Fatalf("expected trigger_failed state, got %q", state)
	}
	if elapsed >= 5*time.Second {
		t.Fatalf("expected an early abort, waited %v of a 10s timeout", elapsed)
	}
	if triggerResult == nil {
		t.Fatal("expected a TriggerResult reporting the rejection, got nil")
	}
	if !strings.Contains(triggerResult.Error, "INVALID_ARGUMENT") {
		t.Fatalf("expected the trigger's own rejection in Error, got %#v", triggerResult)
	}
	if triggerResult.Command != "simulate-keyboard --action Hold --key A" {
		t.Fatalf("TriggerResult command mismatch: %#v", triggerResult)
	}
}

// Verifies the abort keeps the marker armed (no clear) and reports a dedicated error code with the
// marker's remaining lifetime and a copy-pasteable recovery command, rather than reusing the
// timeout error's message and PlayMode diagnosis.
func TestRunWaitForPausePointKeepsMarkerWhenTriggerRejectsArguments(t *testing.T) {
	originalQuery := queryPausePointStatus
	originalClear := clearPausePointStatus
	originalDispatch := dispatchPausePointTriggerCommand
	originalPoll := pausePointStatusPoll
	pausePointStatusPoll = time.Millisecond
	defer func() {
		queryPausePointStatus = originalQuery
		clearPausePointStatus = originalClear
		dispatchPausePointTriggerCommand = originalDispatch
		pausePointStatusPoll = originalPoll
	}()

	queryPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		return pausePointArmedStatusResponse(id), nil
	}
	clearCalled := false
	clearPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		clearCalled = true
		return pausePointStatusResponse{Id: id, Status: pausePointStatusCleared}, nil
	}
	dispatchPausePointTriggerCommand = func(
		ctx context.Context,
		connection unityipc.Connection,
		command string,
		commandArgs []string,
		startPath string,
		stdout io.Writer,
		stderr io.Writer,
	) int {
		_, _ = stderr.Write([]byte(argumentErrorTriggerStderr))
		return 1
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runWaitForPausePoint(context.Background(), unityipc.Connection{}, waitForPausePointOptions{
		id:             "jump",
		timeoutSeconds: 60,
		timeout:        10 * time.Second,
		triggerCommand: "simulate-keyboard",
		triggerArgs:    []string{"--action", "Hold", "--key", "A"},
	}, &stdout, &stderr)

	if code != 1 {
		t.Fatalf("expected failure exit code, got %d with stdout %s", code, stdout.String())
	}
	if clearCalled {
		t.Fatal("expected the marker to stay armed: clear must not be called when the trigger was rejected")
	}

	envelope := parsePausePointErrorEnvelope(t, stderr.Bytes())
	if envelope.Error.ErrorCode != clierrors.ErrorCodePausePointTriggerFailed {
		t.Fatalf("error code mismatch: %#v", envelope.Error)
	}
	if envelope.Error.Retryable || envelope.Error.SafeToRetry {
		t.Fatalf("a rejected trigger is not fixed by retrying the same command: %#v", envelope.Error)
	}
	if strings.Contains(envelope.Error.Message, "was not hit within") {
		t.Fatalf("the timeout message must not be reused for an aborted wait: %#v", envelope.Error)
	}
	if _, hasHint := envelope.Error.Details["Hint"]; hasHint {
		t.Fatalf("the timeout PlayMode diagnosis must not be attached to an aborted wait: %#v", envelope.Error.Details)
	}
	triggerResult, ok := envelope.Error.Details["TriggerResult"].(map[string]any)
	if !ok {
		t.Fatalf("TriggerResult detail missing or wrong shape: %#v", envelope.Error.Details)
	}
	if errorText, _ := triggerResult["Error"].(string); !strings.Contains(errorText, "INVALID_ARGUMENT") {
		t.Fatalf("TriggerResult must carry the trigger's own rejection: %#v", triggerResult)
	}
	remaining, ok := envelope.Error.Details["RemainingMilliseconds"].(float64)
	if !ok || remaining <= 0 {
		t.Fatalf("expected a positive RemainingMilliseconds for the preserved marker: %#v", envelope.Error.Details)
	}

	nextActions := strings.Join(envelope.Error.NextActions, "\n")
	if !strings.Contains(nextActions, "--trigger") {
		t.Fatalf("recovery guidance must point at the --trigger value to fix: %#v", envelope.Error.NextActions)
	}
	if !strings.Contains(nextActions, `uloop await-pause-point --id "jump"`) {
		t.Fatalf("expected a copy-pasteable await command carrying the real id: %#v", envelope.Error.NextActions)
	}
}

// Verifies a mistyped trigger command name aborts the wait and keeps the marker armed, the same as
// a rejected argument value: the command never reached Unity, so the marker cannot be hit by it.
func TestRunWaitForPausePointKeepsMarkerWhenTriggerCommandIsUnknown(t *testing.T) {
	originalQuery := queryPausePointStatus
	originalClear := clearPausePointStatus
	originalDispatch := dispatchPausePointTriggerCommand
	originalPoll := pausePointStatusPoll
	pausePointStatusPoll = time.Millisecond
	defer func() {
		queryPausePointStatus = originalQuery
		clearPausePointStatus = originalClear
		dispatchPausePointTriggerCommand = originalDispatch
		pausePointStatusPoll = originalPoll
	}()

	queryPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		return pausePointArmedStatusResponse(id), nil
	}
	clearPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		t.Fatal("clearPausePointStatus must not be called when the trigger command name was unknown")
		return pausePointStatusResponse{}, nil
	}
	dispatchPausePointTriggerCommand = func(
		ctx context.Context,
		connection unityipc.Connection,
		command string,
		commandArgs []string,
		startPath string,
		stdout io.Writer,
		stderr io.Writer,
	) int {
		_, _ = stderr.Write([]byte(unknownCommandTriggerStderr))
		return 1
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	startedAt := time.Now()
	code := runWaitForPausePoint(context.Background(), unityipc.Connection{}, waitForPausePointOptions{
		id:             "jump",
		timeoutSeconds: 60,
		timeout:        10 * time.Second,
		triggerCommand: "simulate-keybord",
		triggerArgs:    []string{"--action", "Press", "--key", "Space"},
	}, &stdout, &stderr)
	elapsed := time.Since(startedAt)

	if code != 1 {
		t.Fatalf("expected failure exit code, got %d with stdout %s", code, stdout.String())
	}
	if elapsed >= 5*time.Second {
		t.Fatalf("expected an early abort, waited %v of a 10s timeout", elapsed)
	}
	envelope := parsePausePointErrorEnvelope(t, stderr.Bytes())
	if envelope.Error.ErrorCode != clierrors.ErrorCodePausePointTriggerFailed {
		t.Fatalf("error code mismatch: %#v", envelope.Error)
	}
	if envelope.Error.Retryable || envelope.Error.SafeToRetry {
		t.Fatalf("a mistyped command name is not fixed by retrying the same command: %#v", envelope.Error)
	}
	triggerResult, ok := envelope.Error.Details["TriggerResult"].(map[string]any)
	if !ok {
		t.Fatalf("TriggerResult detail missing or wrong shape: %#v", envelope.Error.Details)
	}
	if errorText, _ := triggerResult["Error"].(string); !strings.Contains(errorText, "UNKNOWN_COMMAND") {
		t.Fatalf("TriggerResult must carry the trigger's own rejection: %#v", triggerResult)
	}
}

// Verifies a trigger that completes normally mid-wait never aborts the wait and is still reported
// once, so receiving it inside the poll loop does not regress the existing join behavior.
func TestWaitForPausePointDoesNotAbortWhenTriggerSucceeds(t *testing.T) {
	originalQuery := queryPausePointStatus
	originalDispatch := dispatchPausePointTriggerCommand
	originalPoll := pausePointStatusPoll
	pausePointStatusPoll = time.Millisecond
	defer func() {
		queryPausePointStatus = originalQuery
		dispatchPausePointTriggerCommand = originalDispatch
		pausePointStatusPoll = originalPoll
	}()

	queryPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		return pausePointArmedStatusResponse(id), nil
	}
	dispatchPausePointTriggerCommand = func(
		ctx context.Context,
		connection unityipc.Connection,
		command string,
		commandArgs []string,
		startPath string,
		stdout io.Writer,
		stderr io.Writer,
	) int {
		_, _ = stdout.Write([]byte(`{"Success":true}`))
		return 0
	}

	_, state, triggerResult, _, _, err := waitForPausePoint(context.Background(), unityipc.Connection{}, waitForPausePointOptions{
		id:             "jump",
		timeoutSeconds: 60,
		timeout:        50 * time.Millisecond,
		triggerCommand: "simulate-keyboard",
		triggerArgs:    []string{"--action", "Press"},
	})
	if err != nil {
		t.Fatalf("waitForPausePoint failed: %v", err)
	}
	if state != pausePointWaitStateTimeout {
		t.Fatalf("a successful trigger must leave the wait to settle on its own, got state %q", state)
	}
	if triggerResult == nil || !triggerResult.Completed {
		t.Fatalf("expected a completed trigger result, got %#v", triggerResult)
	}
	if triggerResult.Error != "" {
		t.Fatalf("expected no trigger error, got %#v", triggerResult)
	}
}

// Verifies an already-hit marker observed by the status query taken just before aborting is
// reported as a hit, so a trigger rejection racing a real hit does not discard the hit.
func TestWaitForPausePointReportsHitRacingATriggerRejection(t *testing.T) {
	originalQuery := queryPausePointStatus
	originalDispatch := dispatchPausePointTriggerCommand
	originalPoll := pausePointStatusPoll
	pausePointStatusPoll = time.Hour
	defer func() {
		queryPausePointStatus = originalQuery
		dispatchPausePointTriggerCommand = originalDispatch
		pausePointStatusPoll = originalPoll
	}()

	// Only the abort-time query can observe the hit: the poll ticker is parked for an hour, so the
	// loop's own next status query never runs within this test.
	queryCount := 0
	queryPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		queryCount++
		if queryCount <= 2 {
			return pausePointArmedStatusResponse(id), nil
		}
		return pausePointStatusResponse{
			Id:          id,
			Status:      pausePointStatusHit,
			IsHit:       true,
			HitCount:    1,
			EditorState: pausePointEditorState{IsPlaying: true, IsPaused: true, CapturedAt: "PausePointHit"},
		}, nil
	}
	dispatchPausePointTriggerCommand = func(
		ctx context.Context,
		connection unityipc.Connection,
		command string,
		commandArgs []string,
		startPath string,
		stdout io.Writer,
		stderr io.Writer,
	) int {
		_, _ = stderr.Write([]byte(argumentErrorTriggerStderr))
		return 1
	}

	response, state, triggerResult, _, _, err := waitForPausePoint(context.Background(), unityipc.Connection{}, waitForPausePointOptions{
		id:             "jump",
		timeoutSeconds: 60,
		timeout:        10 * time.Second,
		triggerCommand: "simulate-keyboard",
		triggerArgs:    []string{"--action", "Hold"},
	})
	if err != nil {
		t.Fatalf("waitForPausePoint failed: %v", err)
	}
	if state != pausePointWaitStateHit {
		t.Fatalf("expected the raced hit to win, got state %q", state)
	}
	if !response.IsHit {
		t.Fatalf("expected the hit response to be returned, got %#v", response)
	}
	if triggerResult == nil || triggerResult.Error == "" {
		t.Fatalf("expected the rejection to still be reported alongside the hit, got %#v", triggerResult)
	}
}

// Verifies aborting a wait that resumed PlayMode itself puts PlayMode back into pause and reports
// that on ResumePlayResult, so gameplay cannot consume the preserved marker's single shot while the
// --trigger value is being fixed.
func TestRunWaitForPausePointRepausesPlayModeWhenTriggerRejectsArguments(t *testing.T) {
	originalQuery := queryPausePointStatus
	originalClear := clearPausePointStatus
	originalDispatch := dispatchPausePointTriggerCommand
	originalResume := resumePlayModeForPausePoint
	originalSend := sendControlPlayModeForPausePoint
	originalPoll := pausePointStatusPoll
	pausePointStatusPoll = time.Millisecond
	defer func() {
		queryPausePointStatus = originalQuery
		clearPausePointStatus = originalClear
		dispatchPausePointTriggerCommand = originalDispatch
		resumePlayModeForPausePoint = originalResume
		sendControlPlayModeForPausePoint = originalSend
		pausePointStatusPoll = originalPoll
	}()

	queryPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		return pausePointArmedStatusResponse(id), nil
	}
	clearPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		t.Fatal("clearPausePointStatus must not be called when the trigger was rejected")
		return pausePointStatusResponse{}, nil
	}
	resumePlayModeForPausePoint = func(
		ctx context.Context,
		connection unityipc.Connection,
	) pausePointResumePlayResult {
		return pausePointResumePlayResult{WasPaused: true, Resumed: true}
	}
	actions := make([]string, 0, 1)
	sendControlPlayModeForPausePoint = func(
		ctx context.Context,
		connection unityipc.Connection,
		action string,
	) (controlPlayModeToolResponse, error) {
		actions = append(actions, action)
		return controlPlayModeToolResponse{Success: true, IsPaused: true}, nil
	}
	dispatchPausePointTriggerCommand = func(
		ctx context.Context,
		connection unityipc.Connection,
		command string,
		commandArgs []string,
		startPath string,
		stdout io.Writer,
		stderr io.Writer,
	) int {
		_, _ = stderr.Write([]byte(argumentErrorTriggerStderr))
		return 1
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runWaitForPausePoint(context.Background(), unityipc.Connection{}, waitForPausePointOptions{
		id:             "jump",
		timeoutSeconds: 60,
		timeout:        10 * time.Second,
		triggerCommand: "simulate-keyboard",
		triggerArgs:    []string{"--action", "Hold"},
		resumePlay:     true,
	}, &stdout, &stderr)

	if code != 1 {
		t.Fatalf("expected failure exit code, got %d with stdout %s", code, stdout.String())
	}
	if len(actions) != 1 || actions[0] != "Pause" {
		t.Fatalf("expected exactly one Pause request on abort, got %#v", actions)
	}

	envelope := parsePausePointErrorEnvelope(t, stderr.Bytes())
	resumeResult, ok := envelope.Error.Details["ResumePlayResult"].(map[string]any)
	if !ok {
		t.Fatalf("ResumePlayResult detail missing or wrong shape: %#v", envelope.Error.Details)
	}
	if resumeResult["Resumed"] != true {
		t.Fatalf("ResumePlayResult must still report the resume it performed: %#v", resumeResult)
	}
	if resumeResult["Repaused"] != true {
		t.Fatalf("ResumePlayResult must report the re-pause performed on abort: %#v", resumeResult)
	}
	if _, hasError := resumeResult["RepauseError"]; hasError {
		t.Fatalf("expected no RepauseError for a successful re-pause: %#v", resumeResult)
	}
}

// Verifies a failed re-pause is reported rather than silently dropped, since gameplay then keeps
// running and can still consume the preserved marker.
func TestRunWaitForPausePointReportsRepauseFailure(t *testing.T) {
	originalQuery := queryPausePointStatus
	originalDispatch := dispatchPausePointTriggerCommand
	originalResume := resumePlayModeForPausePoint
	originalSend := sendControlPlayModeForPausePoint
	originalPoll := pausePointStatusPoll
	pausePointStatusPoll = time.Millisecond
	defer func() {
		queryPausePointStatus = originalQuery
		dispatchPausePointTriggerCommand = originalDispatch
		resumePlayModeForPausePoint = originalResume
		sendControlPlayModeForPausePoint = originalSend
		pausePointStatusPoll = originalPoll
	}()

	queryPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		return pausePointArmedStatusResponse(id), nil
	}
	resumePlayModeForPausePoint = func(
		ctx context.Context,
		connection unityipc.Connection,
	) pausePointResumePlayResult {
		return pausePointResumePlayResult{WasPaused: true, Resumed: true}
	}
	sendControlPlayModeForPausePoint = func(
		ctx context.Context,
		connection unityipc.Connection,
		action string,
	) (controlPlayModeToolResponse, error) {
		return controlPlayModeToolResponse{Success: false, Message: "pause denied"}, nil
	}
	dispatchPausePointTriggerCommand = func(
		ctx context.Context,
		connection unityipc.Connection,
		command string,
		commandArgs []string,
		startPath string,
		stdout io.Writer,
		stderr io.Writer,
	) int {
		_, _ = stderr.Write([]byte(argumentErrorTriggerStderr))
		return 1
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runWaitForPausePoint(context.Background(), unityipc.Connection{}, waitForPausePointOptions{
		id:             "jump",
		timeoutSeconds: 60,
		timeout:        10 * time.Second,
		triggerCommand: "simulate-keyboard",
		triggerArgs:    []string{"--action", "Hold"},
		resumePlay:     true,
	}, &stdout, &stderr)

	if code != 1 {
		t.Fatalf("expected failure exit code, got %d with stdout %s", code, stdout.String())
	}
	envelope := parsePausePointErrorEnvelope(t, stderr.Bytes())
	resumeResult, ok := envelope.Error.Details["ResumePlayResult"].(map[string]any)
	if !ok {
		t.Fatalf("ResumePlayResult detail missing or wrong shape: %#v", envelope.Error.Details)
	}
	if resumeResult["Repaused"] == true {
		t.Fatalf("a denied Pause must not be reported as re-paused: %#v", resumeResult)
	}
	if repauseError, _ := resumeResult["RepauseError"].(string); !strings.Contains(repauseError, "pause denied") {
		t.Fatalf("expected the Pause failure to be reported: %#v", resumeResult)
	}
}

// Verifies --resume-play that found PlayMode already unpaused sends no Pause on abort: this command
// resumed nothing, so pausing a game the caller was running would be an unrequested side effect.
func TestWaitForPausePointDoesNotRepauseWhenResumeWasANoOp(t *testing.T) {
	originalQuery := queryPausePointStatus
	originalDispatch := dispatchPausePointTriggerCommand
	originalResume := resumePlayModeForPausePoint
	originalSend := sendControlPlayModeForPausePoint
	originalPoll := pausePointStatusPoll
	pausePointStatusPoll = time.Millisecond
	defer func() {
		queryPausePointStatus = originalQuery
		dispatchPausePointTriggerCommand = originalDispatch
		resumePlayModeForPausePoint = originalResume
		sendControlPlayModeForPausePoint = originalSend
		pausePointStatusPoll = originalPoll
	}()

	queryPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		return pausePointArmedStatusResponse(id), nil
	}
	resumePlayModeForPausePoint = func(
		ctx context.Context,
		connection unityipc.Connection,
	) pausePointResumePlayResult {
		// Mirrors the real contract: Play is only sent when Status reports IsPaused=true.
		return pausePointResumePlayResult{WasPaused: false, Resumed: false}
	}
	sendControlPlayModeForPausePoint = func(
		ctx context.Context,
		connection unityipc.Connection,
		action string,
	) (controlPlayModeToolResponse, error) {
		t.Fatalf("unexpected control-play-mode %q request after a no-op resume", action)
		return controlPlayModeToolResponse{}, nil
	}
	dispatchPausePointTriggerCommand = func(
		ctx context.Context,
		connection unityipc.Connection,
		command string,
		commandArgs []string,
		startPath string,
		stdout io.Writer,
		stderr io.Writer,
	) int {
		_, _ = stderr.Write([]byte(argumentErrorTriggerStderr))
		return 1
	}

	_, state, _, resumeResult, _, err := waitForPausePoint(context.Background(), unityipc.Connection{}, waitForPausePointOptions{
		id:             "jump",
		timeoutSeconds: 60,
		timeout:        10 * time.Second,
		triggerCommand: "simulate-keyboard",
		triggerArgs:    []string{"--action", "Hold"},
		resumePlay:     true,
	})
	if err != nil {
		t.Fatalf("waitForPausePoint failed: %v", err)
	}
	if state != pausePointWaitStateTriggerFailed {
		t.Fatalf("expected trigger_failed state, got %q", state)
	}
	if resumeResult == nil || resumeResult.Resumed {
		t.Fatalf("expected a no-op ResumePlayResult, got %#v", resumeResult)
	}
	if resumeResult.Repaused || resumeResult.RepauseError != "" {
		t.Fatalf("expected no re-pause to be reported, got %#v", resumeResult)
	}
}

// Verifies a wait that did not resume PlayMode itself never sends Pause on abort: pausing a game
// this command did not resume would be an unrequested side effect.
func TestWaitForPausePointDoesNotRepauseWhenItDidNotResume(t *testing.T) {
	originalQuery := queryPausePointStatus
	originalDispatch := dispatchPausePointTriggerCommand
	originalSend := sendControlPlayModeForPausePoint
	originalPoll := pausePointStatusPoll
	pausePointStatusPoll = time.Millisecond
	defer func() {
		queryPausePointStatus = originalQuery
		dispatchPausePointTriggerCommand = originalDispatch
		sendControlPlayModeForPausePoint = originalSend
		pausePointStatusPoll = originalPoll
	}()

	queryPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		return pausePointArmedStatusResponse(id), nil
	}
	sendControlPlayModeForPausePoint = func(
		ctx context.Context,
		connection unityipc.Connection,
		action string,
	) (controlPlayModeToolResponse, error) {
		t.Fatalf("unexpected control-play-mode %q request", action)
		return controlPlayModeToolResponse{}, nil
	}
	dispatchPausePointTriggerCommand = func(
		ctx context.Context,
		connection unityipc.Connection,
		command string,
		commandArgs []string,
		startPath string,
		stdout io.Writer,
		stderr io.Writer,
	) int {
		_, _ = stderr.Write([]byte(argumentErrorTriggerStderr))
		return 1
	}

	_, state, _, resumeResult, _, err := waitForPausePoint(context.Background(), unityipc.Connection{}, waitForPausePointOptions{
		id:             "jump",
		timeoutSeconds: 60,
		timeout:        10 * time.Second,
		triggerCommand: "simulate-keyboard",
		triggerArgs:    []string{"--action", "Hold"},
	})
	if err != nil {
		t.Fatalf("waitForPausePoint failed: %v", err)
	}
	if state != pausePointWaitStateTriggerFailed {
		t.Fatalf("expected trigger_failed state, got %q", state)
	}
	if resumeResult != nil {
		t.Fatalf("expected nil ResumePlayResult when --resume-play was not given, got %#v", resumeResult)
	}
}

// Verifies only a pre-execution rejection (bad arguments or an unknown command name) aborts the
// wait: any other trigger failure (a connection drop, a disabled tool, unparseable output) must
// leave the hit wait running.
func TestPausePointTriggerRejectedBeforeExecution(t *testing.T) {
	cases := []struct {
		name   string
		result *pausePointTriggerResult
		want   bool
	}{
		{
			name:   "no trigger result",
			result: nil,
			want:   false,
		},
		{
			name:   "argument rejection",
			result: &pausePointTriggerResult{Completed: true, Error: argumentErrorTriggerStderr},
			want:   true,
		},
		{
			name:   "unknown command name rejection",
			result: &pausePointTriggerResult{Completed: true, Error: unknownCommandTriggerStderr},
			want:   true,
		},
		{
			name: "another error code does not abort",
			result: &pausePointTriggerResult{
				Completed: true,
				Error:     `{"Success":false,"Error":{"ErrorCode":"UNITY_NOT_REACHABLE","Phase":"connection"}}`,
			},
			want: false,
		},
		{
			name:   "unparseable error does not abort",
			result: &pausePointTriggerResult{Completed: true, Error: "unknown command: bogus-command"},
			want:   false,
		},
		{
			name:   "successful trigger response does not abort",
			result: &pausePointTriggerResult{Completed: true, Response: []byte(`{"Success":true}`)},
			want:   false,
		},
	}

	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			if got := pausePointTriggerRejectedBeforeExecution(testCase.result); got != testCase.want {
				t.Fatalf("pausePointTriggerRejectedBeforeExecution mismatch: got %v, want %v", got, testCase.want)
			}
		})
	}
}

// Verifies a successful arm-status query appends the observed status and ClearedReason, while an
// IPC error leaves the unarmed-at-wait-start message unchanged.
func TestPausePointUnarmedAtWaitStartSuffix(t *testing.T) {
	clearedSuffix := pausePointUnarmedAtWaitStartSuffix(
		pausePointStatusResponse{
			Status:        pausePointStatusCleared,
			ClearedReason: pausePointAwaitTimeoutAutoClearReason,
		},
		nil,
	)
	wantCleared := " marker status was 'Cleared' (ClearedReason: AwaitTimeoutAutoClear)"
	if clearedSuffix != wantCleared {
		t.Fatalf("cleared suffix mismatch: got %q, want %q", clearedSuffix, wantCleared)
	}

	notEnabledSuffix := pausePointUnarmedAtWaitStartSuffix(
		pausePointStatusResponse{Status: pausePointStatusNotEnabled},
		nil,
	)
	wantNotEnabled := " marker status was 'NotEnabled' (ClearedReason: )"
	if notEnabledSuffix != wantNotEnabled {
		t.Fatalf("not-enabled suffix mismatch: got %q, want %q", notEnabledSuffix, wantNotEnabled)
	}

	errorSuffix := pausePointUnarmedAtWaitStartSuffix(
		pausePointStatusResponse{},
		errors.New("pause point status query failed"),
	)
	if errorSuffix != "" {
		t.Fatalf("IPC error must keep the original unarmed message, got %q", errorSuffix)
	}
}

// Verifies --resume-play skip text stays the original unarmed-at-wait-start message when the
// arm-status query itself failed, rather than appending a fabricated marker status.
func TestWaitForPausePointUnarmedErrorOmitsStatusSuffixWhenArmQueryFails(t *testing.T) {
	originalQuery := queryPausePointStatus
	originalResume := resumePlayModeForPausePoint
	originalPoll := pausePointStatusPoll
	pausePointStatusPoll = time.Millisecond
	defer func() {
		queryPausePointStatus = originalQuery
		resumePlayModeForPausePoint = originalResume
		pausePointStatusPoll = originalPoll
	}()

	refusedConnect := &unityipc.ConnectionAttemptError{
		Endpoint: "/tmp/uloop-501/UnityCliLoop-sample.sock",
		Cause: &net.OpError{
			Op:   "dial",
			Net:  "unix",
			Addr: &net.UnixAddr{Name: "/tmp/uloop-501/UnityCliLoop-sample.sock", Net: "unix"},
			Err:  os.NewSyscallError("connect", syscall.EPERM),
		},
	}
	queryPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		return pausePointStatusResponse{}, fmt.Errorf("pause point status query failed: %w", refusedConnect)
	}
	resumePlayModeForPausePoint = func(
		ctx context.Context,
		connection unityipc.Connection,
	) pausePointResumePlayResult {
		t.Fatal("expected resumePlayModeForPausePoint not to be called when arm query failed")
		return pausePointResumePlayResult{}
	}

	_, _, _, resumeResult, _, err := waitForPausePoint(context.Background(), unityipc.Connection{}, waitForPausePointOptions{
		id:             "jump",
		timeoutSeconds: 60,
		timeout:        10 * time.Second,
		resumePlay:     true,
	})
	if !errors.Is(err, refusedConnect) {
		t.Fatalf("expected the refused connect error, got %v", err)
	}
	if resumeResult == nil || resumeResult.Error != pausePointResumeNotArmedAtWaitStartError {
		t.Fatalf("ResumePlayResult.Error mismatch: got %#v, want %q", resumeResult, pausePointResumeNotArmedAtWaitStartError)
	}
}
