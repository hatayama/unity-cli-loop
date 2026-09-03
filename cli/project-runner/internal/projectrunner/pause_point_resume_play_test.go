package projectrunner

import (
	"bytes"
	"context"
	"fmt"
	"io"
	"strings"
	"testing"
	"time"

	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

// Verifies --resume-play without --await is rejected with the same require-await family of
// errors as --trigger, so the flag cannot silently do nothing on a plain enable call.
func TestExtractPausePointEnableAwaitFlagsRequiresAwaitForResumePlay(t *testing.T) {
	_, _, _, _, _, _, _, _, err := extractPausePointEnableAwaitFlags([]string{
		"--id", "jump", "--resume-play",
	})
	if err == nil {
		t.Fatalf("expected an error")
	}
	if !strings.Contains(err.Error(), "require --await") {
		t.Fatalf("error message mismatch: %v", err)
	}
	if !strings.Contains(err.Error(), "--resume-play") {
		t.Fatalf("error message must mention --resume-play: %v", err)
	}
}

// Verifies --await --resume-play extracts resumePlay=true and leaves Unity-side args untouched.
func TestExtractPausePointEnableAwaitFlagsExtractsResumePlay(t *testing.T) {
	remaining, await, _, _, _, _, _, resumePlay, err := extractPausePointEnableAwaitFlags([]string{
		"--id", "jump", "--await", "--resume-play",
	})
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if !await {
		t.Fatalf("expected await to be true")
	}
	if !resumePlay {
		t.Fatalf("expected resumePlay to be true")
	}
	if len(remaining) != 2 || remaining[0] != "--id" || remaining[1] != "jump" {
		t.Fatalf("remaining args mismatch: %#v", remaining)
	}
}

// Verifies enable-pause-point accepts --resume-play=true the same way await-pause-point does,
// so the =value form is not leaked into Unity schema parsing.
func TestExtractPausePointEnableAwaitFlagsExtractsResumePlayEqualsTrue(t *testing.T) {
	_, await, _, _, _, _, _, resumePlay, err := extractPausePointEnableAwaitFlags([]string{
		"--id", "jump", "--await", "--resume-play=true",
	})
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if !await || !resumePlay {
		t.Fatalf("expected await and resumePlay true, got await=%v resumePlay=%v", await, resumePlay)
	}
}

// Verifies await-pause-point accepts --resume-play as a boolean flag.
func TestParseWaitForPausePointOptionsParsesResumePlayFlag(t *testing.T) {
	options, err := parseWaitForPausePointOptions([]string{
		"--id", "jump", "--resume-play",
	})
	if err != nil {
		t.Fatalf("parse failed: %v", err)
	}
	if !options.resumePlay {
		t.Fatalf("expected resumePlay to be true: %#v", options)
	}

	optionsEquals, err := parseWaitForPausePointOptions([]string{
		"--id", "jump", "--resume-play=true",
	})
	if err != nil {
		t.Fatalf("parse --resume-play=true failed: %v", err)
	}
	if !optionsEquals.resumePlay {
		t.Fatalf("expected resumePlay to be true for =true form: %#v", optionsEquals)
	}
}

// Verifies armed + IsPaused=true resumes Play before dispatching --trigger, and reports
// ResumePlayResult{WasPaused:true, Resumed:true}.
func TestWaitForPausePointResumesPlayBeforeTriggerWhenPaused(t *testing.T) {
	originalQuery := queryPausePointStatus
	originalDispatch := dispatchPausePointTriggerCommand
	originalResume := resumePlayModeForPausePoint
	defer func() {
		queryPausePointStatus = originalQuery
		dispatchPausePointTriggerCommand = originalDispatch
		resumePlayModeForPausePoint = originalResume
	}()

	queryPausePointStatus = pausePointEnabledThenHitStatusQuery()

	calls := make([]string, 0, 2)
	resumePlayModeForPausePoint = func(
		ctx context.Context,
		connection unityipc.Connection,
	) pausePointResumePlayResult {
		calls = append(calls, "resume")
		return pausePointResumePlayResult{WasPaused: true, Resumed: true}
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
		calls = append(calls, "trigger")
		_, _ = stdout.Write([]byte(`{"Success":true}`))
		return 0
	}

	_, _, triggerResult, resumeResult, _, err := waitForPausePoint(context.Background(), unityipc.Connection{}, waitForPausePointOptions{
		id:             "jump",
		timeoutSeconds: 1,
		timeout:        time.Second,
		triggerCommand: "simulate-keyboard",
		triggerArgs:    []string{"--action", "Press"},
		resumePlay:     true,
	})
	if err != nil {
		t.Fatalf("waitForPausePoint failed: %v", err)
	}
	if len(calls) != 2 || calls[0] != "resume" || calls[1] != "trigger" {
		t.Fatalf("expected resume then trigger, got %#v", calls)
	}
	if resumeResult == nil || !resumeResult.WasPaused || !resumeResult.Resumed || resumeResult.Error != "" {
		t.Fatalf("ResumePlayResult mismatch: %#v", resumeResult)
	}
	if triggerResult == nil || !triggerResult.Completed {
		t.Fatalf("expected a completed trigger result, got %#v", triggerResult)
	}
}

// Verifies armed + IsPaused=false skips the Play request, reports WasPaused=false/Resumed=false,
// and still dispatches the trigger.
func TestWaitForPausePointSkipsPlayWhenAlreadyUnpaused(t *testing.T) {
	originalQuery := queryPausePointStatus
	originalDispatch := dispatchPausePointTriggerCommand
	originalResume := resumePlayModeForPausePoint
	defer func() {
		queryPausePointStatus = originalQuery
		dispatchPausePointTriggerCommand = originalDispatch
		resumePlayModeForPausePoint = originalResume
	}()

	queryPausePointStatus = pausePointEnabledThenHitStatusQuery()

	triggerDispatched := false
	resumePlayModeForPausePoint = func(
		ctx context.Context,
		connection unityipc.Connection,
	) pausePointResumePlayResult {
		// The real implementation only sends Play when Status reports IsPaused=true; this stub
		// mirrors that contract so the test asserts the wait path still accepts a no-op resume.
		return pausePointResumePlayResult{WasPaused: false, Resumed: false}
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
		triggerDispatched = true
		_, _ = stdout.Write([]byte(`{"Success":true}`))
		return 0
	}

	_, _, triggerResult, resumeResult, _, err := waitForPausePoint(context.Background(), unityipc.Connection{}, waitForPausePointOptions{
		id:             "jump",
		timeoutSeconds: 1,
		timeout:        time.Second,
		triggerCommand: "simulate-keyboard",
		triggerArgs:    []string{"--action", "Press"},
		resumePlay:     true,
	})
	if err != nil {
		t.Fatalf("waitForPausePoint failed: %v", err)
	}
	if resumeResult == nil || resumeResult.WasPaused || resumeResult.Resumed {
		t.Fatalf("expected a no-op ResumePlayResult, got %#v", resumeResult)
	}
	if !triggerDispatched {
		t.Fatal("expected trigger to still be dispatched after a no-op resume")
	}
	if triggerResult == nil || !triggerResult.Completed {
		t.Fatalf("expected a completed trigger result, got %#v", triggerResult)
	}
}

// Verifies a failed Status/Play resume skips --trigger and records the fixed skip Error on
// TriggerResult while still returning ResumePlayResult.Error.
func TestWaitForPausePointSkipsTriggerWhenResumePlayFails(t *testing.T) {
	originalQuery := queryPausePointStatus
	originalDispatch := dispatchPausePointTriggerCommand
	originalResume := resumePlayModeForPausePoint
	defer func() {
		queryPausePointStatus = originalQuery
		dispatchPausePointTriggerCommand = originalDispatch
		resumePlayModeForPausePoint = originalResume
	}()

	queryPausePointStatus = pausePointEnabledThenHitStatusQuery()

	dispatchCalled := false
	resumePlayModeForPausePoint = func(
		ctx context.Context,
		connection unityipc.Connection,
	) pausePointResumePlayResult {
		return pausePointResumePlayResult{
			WasPaused: true,
			Resumed:   false,
			Error:     "control-play-mode Status failed: boom",
		}
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
		dispatchCalled = true
		_, _ = stdout.Write([]byte(`{"Success":true}`))
		return 0
	}

	_, _, triggerResult, resumeResult, _, err := waitForPausePoint(context.Background(), unityipc.Connection{}, waitForPausePointOptions{
		id:             "jump",
		timeoutSeconds: 1,
		timeout:        time.Second,
		triggerCommand: "simulate-keyboard",
		triggerArgs:    []string{"--action", "Press"},
		resumePlay:     true,
	})
	if err != nil {
		t.Fatalf("waitForPausePoint failed: %v", err)
	}
	if dispatchCalled {
		t.Fatal("expected trigger not to be dispatched after a resume failure")
	}
	if resumeResult == nil || resumeResult.Error == "" {
		t.Fatalf("expected ResumePlayResult.Error, got %#v", resumeResult)
	}
	if triggerResult == nil || triggerResult.Completed {
		t.Fatalf("expected a skipped TriggerResult, got %#v", triggerResult)
	}
	if triggerResult.Error != "trigger was not dispatched: --resume-play failed to resume play mode" {
		t.Fatalf("TriggerResult.Error mismatch: %#v", triggerResult)
	}
	assertPausePointTriggerResultOmitsExplanation(t, triggerResult)
}

// Verifies --resume-play alone still confirms arm before resuming, and skips resume (and has no
// trigger) when the marker is not armed — matching the --trigger not-armed skip path.
func TestWaitForPausePointSkipsResumeWhenNotArmed(t *testing.T) {
	originalQuery := queryPausePointStatus
	originalResume := resumePlayModeForPausePoint
	defer func() {
		queryPausePointStatus = originalQuery
		resumePlayModeForPausePoint = originalResume
	}()

	queryPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		return pausePointStatusResponse{Id: id, Status: pausePointStatusNotEnabled}, nil
	}

	resumeCalled := false
	resumePlayModeForPausePoint = func(
		ctx context.Context,
		connection unityipc.Connection,
	) pausePointResumePlayResult {
		resumeCalled = true
		return pausePointResumePlayResult{WasPaused: true, Resumed: true}
	}

	_, state, triggerResult, resumeResult, _, err := waitForPausePoint(context.Background(), unityipc.Connection{}, waitForPausePointOptions{
		id:             "does-not-exist",
		timeoutSeconds: 1,
		timeout:        time.Second,
		resumePlay:     true,
	})
	if err != nil {
		t.Fatalf("waitForPausePoint failed: %v", err)
	}
	if state != pausePointWaitStateNotEnabled {
		t.Fatalf("expected not_enabled state, got %q", state)
	}
	if resumeCalled {
		t.Fatal("expected resumePlayModeForPausePoint not to be called for a not-armed marker")
	}
	if triggerResult != nil {
		t.Fatalf("expected nil TriggerResult when --trigger was not given, got %#v", triggerResult)
	}
	wantError := pausePointResumeNotArmedAtWaitStartError +
		" marker status was 'NotEnabled' (ClearedReason: )"
	if resumeResult == nil || resumeResult.Error != wantError {
		t.Fatalf("ResumePlayResult.Error mismatch: got %#v, want %q", resumeResult, wantError)
	}
}

// Verifies not-armed markers leave both ResumePlayResult.Error and TriggerResult.Error set when
// --resume-play and --trigger are requested together, without dispatching either action.
func TestWaitForPausePointSkipsResumeAndTriggerWhenNotArmed(t *testing.T) {
	originalQuery := queryPausePointStatus
	originalDispatch := dispatchPausePointTriggerCommand
	originalResume := resumePlayModeForPausePoint
	defer func() {
		queryPausePointStatus = originalQuery
		dispatchPausePointTriggerCommand = originalDispatch
		resumePlayModeForPausePoint = originalResume
	}()

	queryPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		return pausePointStatusResponse{Id: id, Status: pausePointStatusNotEnabled}, nil
	}

	resumeCalled := false
	resumePlayModeForPausePoint = func(
		ctx context.Context,
		connection unityipc.Connection,
	) pausePointResumePlayResult {
		resumeCalled = true
		return pausePointResumePlayResult{WasPaused: true, Resumed: true}
	}

	dispatchCalled := false
	dispatchPausePointTriggerCommand = func(
		ctx context.Context,
		connection unityipc.Connection,
		command string,
		commandArgs []string,
		startPath string,
		stdout io.Writer,
		stderr io.Writer,
	) int {
		dispatchCalled = true
		_, _ = stdout.Write([]byte(`{"Success":true}`))
		return 0
	}

	_, state, triggerResult, resumeResult, _, err := waitForPausePoint(context.Background(), unityipc.Connection{}, waitForPausePointOptions{
		id:             "does-not-exist",
		timeoutSeconds: 1,
		timeout:        time.Second,
		resumePlay:     true,
		triggerCommand: "simulate-keyboard",
		triggerArgs:    []string{"--action", "Press"},
	})
	if err != nil {
		t.Fatalf("waitForPausePoint failed: %v", err)
	}
	if state != pausePointWaitStateNotEnabled {
		t.Fatalf("expected not_enabled state, got %q", state)
	}
	if resumeCalled {
		t.Fatal("expected resumePlayModeForPausePoint not to be called for a not-armed marker")
	}
	if dispatchCalled {
		t.Fatal("expected dispatchPausePointTriggerCommand not to be called for a not-armed marker")
	}
	wantResumeError := pausePointResumeNotArmedAtWaitStartError +
		" marker status was 'NotEnabled' (ClearedReason: )"
	wantTriggerError := pausePointTriggerNotArmedAtWaitStartError +
		" marker status was 'NotEnabled' (ClearedReason: )"
	if resumeResult == nil || resumeResult.Error != wantResumeError {
		t.Fatalf("ResumePlayResult.Error mismatch: got %#v, want %q", resumeResult, wantResumeError)
	}
	if triggerResult == nil || triggerResult.Error != wantTriggerError {
		t.Fatalf("TriggerResult.Error mismatch: got %#v, want %q", triggerResult, wantTriggerError)
	}
}

// Verifies armed + --resume-play alone (no --trigger) still resumes and leaves TriggerResult nil.
func TestWaitForPausePointResumesWithoutTriggerWhenArmed(t *testing.T) {
	originalQuery := queryPausePointStatus
	originalDispatch := dispatchPausePointTriggerCommand
	originalResume := resumePlayModeForPausePoint
	defer func() {
		queryPausePointStatus = originalQuery
		dispatchPausePointTriggerCommand = originalDispatch
		resumePlayModeForPausePoint = originalResume
	}()

	queryPausePointStatus = pausePointEnabledThenHitStatusQuery()

	resumeCalled := false
	resumePlayModeForPausePoint = func(
		ctx context.Context,
		connection unityipc.Connection,
	) pausePointResumePlayResult {
		resumeCalled = true
		return pausePointResumePlayResult{WasPaused: true, Resumed: true}
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
		t.Fatal("dispatchPausePointTriggerCommand must not be called without --trigger")
		return 0
	}

	_, _, triggerResult, resumeResult, _, err := waitForPausePoint(context.Background(), unityipc.Connection{}, waitForPausePointOptions{
		id:             "jump",
		timeoutSeconds: 1,
		timeout:        time.Second,
		resumePlay:     true,
	})
	if err != nil {
		t.Fatalf("waitForPausePoint failed: %v", err)
	}
	if !resumeCalled {
		t.Fatal("expected resumePlayModeForPausePoint to be called")
	}
	if resumeResult == nil || !resumeResult.WasPaused || !resumeResult.Resumed {
		t.Fatalf("ResumePlayResult mismatch: %#v", resumeResult)
	}
	if triggerResult != nil {
		t.Fatalf("expected nil TriggerResult when --trigger was not given, got %#v", triggerResult)
	}
}

// Verifies resumePlayModeForPausePointFromUnity's Status/Play branches without a live Unity IPC.
func TestResumePlayModeForPausePointFromUnityBranches(t *testing.T) {
	originalSend := sendControlPlayModeForPausePoint
	defer func() { sendControlPlayModeForPausePoint = originalSend }()

	t.Run("Status transport failure", func(t *testing.T) {
		assertResumePlayModeFromUnityBranch(t, stubControlPlayModeStatusError("boom"), pausePointResumePlayResult{
			Error: "control-play-mode Status failed: boom",
		}, []string{"Status"})
	})

	t.Run("Status Success=false", func(t *testing.T) {
		assertResumePlayModeFromUnityBranch(t, stubControlPlayModeFixed(controlPlayModeToolResponse{
			Success: false,
			Message: "status denied",
		}, nil), pausePointResumePlayResult{Error: "status denied"}, []string{"Status"})
	})

	t.Run("IsPaused=false skips Play", func(t *testing.T) {
		assertResumePlayModeFromUnityBranch(t, stubControlPlayModeFixed(controlPlayModeToolResponse{
			Success:  true,
			IsPaused: false,
		}, nil), pausePointResumePlayResult{}, []string{"Status"})
	})

	t.Run("Play transport failure", func(t *testing.T) {
		assertResumePlayModeFromUnityBranch(t, stubControlPlayModePlayError("play boom"), pausePointResumePlayResult{
			WasPaused: true,
			Error:     "control-play-mode Play failed: play boom",
		}, []string{"Status", "Play"})
	})
}

func stubControlPlayModeFixed(
	response controlPlayModeToolResponse,
	err error,
) func(context.Context, unityipc.Connection, string) (controlPlayModeToolResponse, error) {
	return func(
		_ context.Context,
		_ unityipc.Connection,
		_ string,
	) (controlPlayModeToolResponse, error) {
		return response, err
	}
}

func stubControlPlayModeStatusError(
	message string,
) func(context.Context, unityipc.Connection, string) (controlPlayModeToolResponse, error) {
	return stubControlPlayModeFixed(controlPlayModeToolResponse{}, fmt.Errorf("%s", message))
}

func stubControlPlayModePlayError(
	message string,
) func(context.Context, unityipc.Connection, string) (controlPlayModeToolResponse, error) {
	return func(
		_ context.Context,
		_ unityipc.Connection,
		action string,
	) (controlPlayModeToolResponse, error) {
		if action == "Status" {
			return controlPlayModeToolResponse{Success: true, IsPaused: true}, nil
		}
		return controlPlayModeToolResponse{}, fmt.Errorf("%s", message)
	}
}

func assertResumePlayModeFromUnityBranch(
	t *testing.T,
	stub func(context.Context, unityipc.Connection, string) (controlPlayModeToolResponse, error),
	want pausePointResumePlayResult,
	wantActions []string,
) {
	t.Helper()
	actions := make([]string, 0, len(wantActions))
	sendControlPlayModeForPausePoint = func(
		ctx context.Context,
		connection unityipc.Connection,
		action string,
	) (controlPlayModeToolResponse, error) {
		actions = append(actions, action)
		return stub(ctx, connection, action)
	}

	result := resumePlayModeForPausePointFromUnity(context.Background(), unityipc.Connection{})
	if result != want {
		t.Fatalf("result mismatch: got %#v, want %#v", result, want)
	}
	if len(actions) != len(wantActions) {
		t.Fatalf("expected actions %#v, got %#v", wantActions, actions)
	}
	for index, action := range wantActions {
		if actions[index] != action {
			t.Fatalf("expected actions %#v, got %#v", wantActions, actions)
		}
	}
}

// Verifies wait-start unarmed errors include the observed Cleared status and AwaitTimeoutAutoClear
// reason when the arm-status query succeeded.
func TestWaitForPausePointUnarmedErrorIncludesClearedReasonWhenQuerySucceeded(t *testing.T) {
	originalQuery := queryPausePointStatus
	originalResume := resumePlayModeForPausePoint
	defer func() {
		queryPausePointStatus = originalQuery
		resumePlayModeForPausePoint = originalResume
	}()

	queryPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		return pausePointStatusResponse{
			Id:            id,
			Status:        pausePointStatusCleared,
			ClearedReason: pausePointAwaitTimeoutAutoClearReason,
		}, nil
	}

	resumePlayModeForPausePoint = func(
		ctx context.Context,
		connection unityipc.Connection,
	) pausePointResumePlayResult {
		t.Fatal("expected resumePlayModeForPausePoint not to be called for a cleared marker")
		return pausePointResumePlayResult{}
	}

	_, _, _, resumeResult, _, err := waitForPausePoint(context.Background(), unityipc.Connection{}, waitForPausePointOptions{
		id:             "jump",
		timeoutSeconds: 1,
		timeout:        time.Second,
		resumePlay:     true,
	})
	if err != nil {
		t.Fatalf("waitForPausePoint failed: %v", err)
	}
	wantError := pausePointResumeNotArmedAtWaitStartError +
		" marker status was 'Cleared' (ClearedReason: AwaitTimeoutAutoClear)"
	if resumeResult == nil || resumeResult.Error != wantError {
		t.Fatalf("ResumePlayResult.Error mismatch: got %#v, want %q", resumeResult, wantError)
	}
}

// pausePointEnabledThenHitStatusQuery answers the arm-confirmation query with an armed-but-unhit
// marker and every later poll with a hit. Why not a fixed Hit response: a marker that was already
// hit when the wait started makes the wait settle on that old hit, which is exactly the case where
// no resume or trigger may run, so it cannot model a wait whose own side effects produce the hit.
func pausePointEnabledThenHitStatusQuery() func(
	context.Context,
	unityipc.Connection,
	string,
) (pausePointStatusResponse, error) {
	queryCount := 0
	return func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		queryCount++
		if queryCount == 1 {
			return pausePointArmedStatusResponse(id), nil
		}
		return pausePointStatusResponse{
			Id:              id,
			Status:          pausePointStatusHit,
			IsHit:           true,
			HitCount:        1,
			LastHitSequence: 1,
			EditorState:     pausePointEditorState{IsPlaying: true, IsPaused: true, CapturedAt: "PausePointHit"},
		}, nil
	}
}

// pausePointAlreadyHitSingleShotStatusQuery answers every query with a single-shot marker that was
// already hit before the wait started — the case the wait settles on immediately.
func pausePointAlreadyHitSingleShotStatusQuery(mode string) func(
	context.Context,
	unityipc.Connection,
	string,
) (pausePointStatusResponse, error) {
	return func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		return pausePointStatusResponse{
			Id:              id,
			Status:          pausePointStatusHit,
			IsHit:           true,
			HitCount:        1,
			Mode:            mode,
			LastHitSequence: 1,
			EditorState:     pausePointEditorState{IsPlaying: true, IsPaused: true, CapturedAt: "PausePointHit"},
		}, nil
	}
}

// Verifies --resume-play never resumes a single-shot marker that had already hit before the wait
// started: the wait settles on that old hit, so resuming would report a hit while Unity is running.
func TestWaitForPausePointSkipsResumeWhenMarkerAlreadyHitAtWaitStart(t *testing.T) {
	originalQuery := queryPausePointStatus
	originalResume := resumePlayModeForPausePoint
	defer func() {
		queryPausePointStatus = originalQuery
		resumePlayModeForPausePoint = originalResume
	}()

	queryPausePointStatus = pausePointAlreadyHitSingleShotStatusQuery("single-shot")
	resumePlayModeForPausePoint = func(
		ctx context.Context,
		connection unityipc.Connection,
	) pausePointResumePlayResult {
		t.Fatal("expected no resume for a marker that had already hit at wait start")
		return pausePointResumePlayResult{}
	}

	_, state, triggerResult, resumeResult, hasNewHitBaseline, err := waitForPausePoint(
		context.Background(), unityipc.Connection{}, waitForPausePointOptions{
			id:             "jump",
			timeoutSeconds: 1,
			timeout:        time.Second,
			resumePlay:     true,
		})
	if err != nil {
		t.Fatalf("waitForPausePoint failed: %v", err)
	}
	if state != pausePointWaitStateHit {
		t.Fatalf("expected the recorded hit to still be returned, got %q", state)
	}
	if hasNewHitBaseline {
		t.Fatal("single-shot must not establish a new-hit baseline")
	}
	if triggerResult != nil {
		t.Fatalf("expected nil TriggerResult when --trigger was not given, got %#v", triggerResult)
	}
	if resumeResult == nil {
		t.Fatal("expected a ResumePlayResult explaining the skip, got nil")
	}
	if resumeResult.Resumed || resumeResult.WasPaused {
		t.Fatalf("expected no resume to be reported, got %#v", resumeResult)
	}
	if resumeResult.Error != "" {
		t.Fatalf("a deliberate skip must not be reported as a resume failure: %#v", resumeResult)
	}
	if resumeResult.Skipped != pausePointResumeSkippedForExistingHitMessage {
		t.Fatalf("ResumePlayResult.Skipped mismatch: %#v", resumeResult)
	}
}

// Verifies a marker whose Mode is absent (older Unity package) is treated like single-shot, so its
// already-recorded hit also suppresses --resume-play and --trigger.
func TestWaitForPausePointSkipsSideEffectsWhenAlreadyHitMarkerHasNoMode(t *testing.T) {
	originalQuery := queryPausePointStatus
	originalResume := resumePlayModeForPausePoint
	originalDispatch := dispatchPausePointTriggerCommand
	defer func() {
		queryPausePointStatus = originalQuery
		resumePlayModeForPausePoint = originalResume
		dispatchPausePointTriggerCommand = originalDispatch
	}()

	queryPausePointStatus = pausePointAlreadyHitSingleShotStatusQuery("")
	resumePlayModeForPausePoint = func(
		ctx context.Context,
		connection unityipc.Connection,
	) pausePointResumePlayResult {
		t.Fatal("expected no resume for a marker that had already hit at wait start")
		return pausePointResumePlayResult{}
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
		t.Fatal("expected no trigger dispatch for a marker that had already hit at wait start")
		return 0
	}

	_, state, triggerResult, resumeResult, _, err := waitForPausePoint(
		context.Background(), unityipc.Connection{}, waitForPausePointOptions{
			id:             "jump",
			timeoutSeconds: 1,
			timeout:        time.Second,
			resumePlay:     true,
			triggerCommand: "simulate-keyboard",
			triggerArgs:    []string{"--action", "Press"},
		})
	if err != nil {
		t.Fatalf("waitForPausePoint failed: %v", err)
	}
	if state != pausePointWaitStateHit {
		t.Fatalf("expected the recorded hit to still be returned, got %q", state)
	}
	if resumeResult == nil || resumeResult.Skipped != pausePointResumeSkippedForExistingHitMessage {
		t.Fatalf("ResumePlayResult.Skipped mismatch: %#v", resumeResult)
	}
	if triggerResult == nil || triggerResult.Completed {
		t.Fatalf("expected a skipped TriggerResult, got %#v", triggerResult)
	}
	if triggerResult.Error != pausePointTriggerSkippedForExistingHitError {
		t.Fatalf("TriggerResult.Error mismatch: %#v", triggerResult)
	}
	if triggerResult.Command != "simulate-keyboard --action Press" {
		t.Fatalf("TriggerResult.Command mismatch: %#v", triggerResult)
	}
}

// Verifies an already-hit continuous marker keeps resuming: its wait awaits a later hit, so the
// resume is what lets that next hit happen.
func TestWaitForPausePointStillResumesAlreadyHitContinuousMarker(t *testing.T) {
	originalQuery := queryPausePointStatus
	originalResume := resumePlayModeForPausePoint
	originalPoll := pausePointStatusPoll
	pausePointStatusPoll = time.Millisecond
	defer func() {
		queryPausePointStatus = originalQuery
		resumePlayModeForPausePoint = originalResume
		pausePointStatusPoll = originalPoll
	}()

	queryCount := 0
	queryPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		queryCount++
		sequence := 5
		if queryCount >= 2 {
			sequence = 6
		}
		return pausePointStatusResponse{
			Id:              id,
			Status:          pausePointStatusHit,
			IsHit:           true,
			HitCount:        sequence,
			Mode:            pausePointModeContinuous,
			LastHitSequence: sequence,
			EditorState:     pausePointEditorState{IsPlaying: true, IsPaused: true, CapturedAt: "PausePointHit"},
		}, nil
	}
	resumeCalled := false
	resumePlayModeForPausePoint = func(
		ctx context.Context,
		connection unityipc.Connection,
	) pausePointResumePlayResult {
		resumeCalled = true
		return pausePointResumePlayResult{WasPaused: true, Resumed: true}
	}

	_, state, _, resumeResult, hasNewHitBaseline, err := waitForPausePoint(
		context.Background(), unityipc.Connection{}, waitForPausePointOptions{
			id:             "jump",
			timeoutSeconds: 1,
			timeout:        time.Second,
			resumePlay:     true,
		})
	if err != nil {
		t.Fatalf("waitForPausePoint failed: %v", err)
	}
	if state != pausePointWaitStateHit {
		t.Fatalf("state mismatch: %q", state)
	}
	if !resumeCalled {
		t.Fatal("expected an already-hit continuous marker to still be resumed")
	}
	if !hasNewHitBaseline {
		t.Fatal("expected a new-hit baseline for an already-hit continuous marker")
	}
	if resumeResult == nil || !resumeResult.Resumed || resumeResult.Skipped != "" {
		t.Fatalf("ResumePlayResult mismatch: %#v", resumeResult)
	}
}

// Verifies enable-pause-point --await --resume-play still resumes when its own enable raced a hit:
// that hit is the wait's success, and the marker was armed by this very command.
func TestWaitForPausePointStillResumesWhenMarkerJustEnabled(t *testing.T) {
	originalQuery := queryPausePointStatus
	originalResume := resumePlayModeForPausePoint
	defer func() {
		queryPausePointStatus = originalQuery
		resumePlayModeForPausePoint = originalResume
	}()

	queryPausePointStatus = pausePointAlreadyHitSingleShotStatusQuery("single-shot")
	resumeCalled := false
	resumePlayModeForPausePoint = func(
		ctx context.Context,
		connection unityipc.Connection,
	) pausePointResumePlayResult {
		resumeCalled = true
		return pausePointResumePlayResult{WasPaused: true, Resumed: true}
	}

	_, state, _, resumeResult, _, err := waitForPausePoint(
		context.Background(), unityipc.Connection{}, waitForPausePointOptions{
			id:                "jump",
			timeoutSeconds:    1,
			timeout:           time.Second,
			resumePlay:        true,
			markerJustEnabled: true,
		})
	if err != nil {
		t.Fatalf("waitForPausePoint failed: %v", err)
	}
	if state != pausePointWaitStateHit {
		t.Fatalf("state mismatch: %q", state)
	}
	if !resumeCalled {
		t.Fatal("expected enable --await --resume-play to still resume")
	}
	if resumeResult == nil || resumeResult.Skipped != "" {
		t.Fatalf("ResumePlayResult mismatch: %#v", resumeResult)
	}
}

// Verifies the public await-pause-point success payload carries the skip on
// ResumePlayResult.Skipped (and no Error), so a caller reading only the JSON learns that Unity is
// still paused by the hit it is looking at rather than running.
func TestRunWaitForPausePointCommandReportsResumeSkipForAlreadyHitMarker(t *testing.T) {
	originalQuery := queryPausePointStatus
	originalResume := resumePlayModeForPausePoint
	originalFetch := fetchMatchingLogs
	t.Cleanup(func() {
		queryPausePointStatus = originalQuery
		resumePlayModeForPausePoint = originalResume
		fetchMatchingLogs = originalFetch
	})

	queryPausePointStatus = pausePointAlreadyHitSingleShotStatusQuery("single-shot")
	resumePlayModeForPausePoint = func(
		ctx context.Context,
		connection unityipc.Connection,
	) pausePointResumePlayResult {
		t.Fatal("expected no resume for a marker that had already hit at wait start")
		return pausePointResumePlayResult{}
	}
	fetchMatchingLogs = func(
		ctx context.Context,
		connection unityipc.Connection,
		searchText string,
		maxCount int,
	) (pausePointMatchingLogsResult, error) {
		return pausePointMatchingLogsResult{SearchText: searchText}, nil
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runWaitForPausePointCommand(
		context.Background(),
		unityipc.Connection{},
		[]string{"--id", "jump", "--timeout-seconds", "1", "--resume-play"},
		"",
		&stdout,
		&stderr,
	)
	if code != 0 {
		t.Fatalf("expected hit success, got %d stdout=%s stderr=%s", code, stdout.String(), stderr.String())
	}

	payload := decodeJSONObject(t, stdout.Bytes())
	resumeResult, ok := payload["ResumePlayResult"].(map[string]any)
	if !ok {
		t.Fatalf("ResumePlayResult missing or wrong shape: %#v", payload)
	}
	if resumeResult["Resumed"] != false {
		t.Fatalf("expected Resumed=false, got %#v", resumeResult)
	}
	if _, hasError := resumeResult["Error"]; hasError {
		t.Fatalf("a deliberate skip must not be reported as an Error: %#v", resumeResult)
	}
	skipped, _ := resumeResult["Skipped"].(string)
	if skipped != pausePointResumeSkippedForExistingHitMessage {
		t.Fatalf("Skipped mismatch: %#v", resumeResult)
	}
}
