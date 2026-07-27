package projectrunner

import (
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

	queryPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		return pausePointStatusResponse{
			Id:          id,
			Status:      pausePointStatusHit,
			IsHit:       true,
			HitCount:    1,
			EditorState: pausePointEditorState{IsPlaying: true, IsPaused: true, CapturedAt: "PausePointHit"},
		}, nil
	}

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

	_, _, triggerResult, resumeResult, err := waitForPausePoint(context.Background(), unityipc.Connection{}, waitForPausePointOptions{
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

	queryPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		return pausePointStatusResponse{
			Id:          id,
			Status:      pausePointStatusHit,
			IsHit:       true,
			HitCount:    1,
			EditorState: pausePointEditorState{IsPlaying: true, IsPaused: false, CapturedAt: "PausePointHit"},
		}, nil
	}

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

	_, _, triggerResult, resumeResult, err := waitForPausePoint(context.Background(), unityipc.Connection{}, waitForPausePointOptions{
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

	queryPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		return pausePointStatusResponse{
			Id:          id,
			Status:      pausePointStatusHit,
			IsHit:       true,
			HitCount:    1,
			EditorState: pausePointEditorState{IsPlaying: true, IsPaused: true, CapturedAt: "PausePointHit"},
		}, nil
	}

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

	_, _, triggerResult, resumeResult, err := waitForPausePoint(context.Background(), unityipc.Connection{}, waitForPausePointOptions{
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

	_, state, triggerResult, resumeResult, err := waitForPausePoint(context.Background(), unityipc.Connection{}, waitForPausePointOptions{
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
	if resumeResult == nil || resumeResult.Error == "" {
		t.Fatalf("expected ResumePlayResult.Error explaining the skip, got %#v", resumeResult)
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

	_, state, triggerResult, resumeResult, err := waitForPausePoint(context.Background(), unityipc.Connection{}, waitForPausePointOptions{
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
	if resumeResult == nil || resumeResult.Error == "" {
		t.Fatalf("expected ResumePlayResult.Error explaining the skip, got %#v", resumeResult)
	}
	if triggerResult == nil || triggerResult.Error == "" {
		t.Fatalf("expected TriggerResult.Error explaining the skip, got %#v", triggerResult)
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

	queryPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		return pausePointStatusResponse{
			Id:          id,
			Status:      pausePointStatusHit,
			IsHit:       true,
			HitCount:    1,
			EditorState: pausePointEditorState{IsPlaying: true, IsPaused: true, CapturedAt: "PausePointHit"},
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

	_, _, triggerResult, resumeResult, err := waitForPausePoint(context.Background(), unityipc.Connection{}, waitForPausePointOptions{
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
		actions := make([]string, 0, 1)
		sendControlPlayModeForPausePoint = func(
			ctx context.Context,
			connection unityipc.Connection,
			action string,
		) (controlPlayModeToolResponse, error) {
			actions = append(actions, action)
			return controlPlayModeToolResponse{}, fmt.Errorf("boom")
		}

		result := resumePlayModeForPausePointFromUnity(context.Background(), unityipc.Connection{})
		if result.WasPaused || result.Resumed || !strings.Contains(result.Error, "control-play-mode Status failed") {
			t.Fatalf("result mismatch: %#v", result)
		}
		if len(actions) != 1 || actions[0] != "Status" {
			t.Fatalf("expected only Status, got %#v", actions)
		}
	})

	t.Run("Status Success=false", func(t *testing.T) {
		actions := make([]string, 0, 1)
		sendControlPlayModeForPausePoint = func(
			ctx context.Context,
			connection unityipc.Connection,
			action string,
		) (controlPlayModeToolResponse, error) {
			actions = append(actions, action)
			return controlPlayModeToolResponse{Success: false, Message: "status denied"}, nil
		}

		result := resumePlayModeForPausePointFromUnity(context.Background(), unityipc.Connection{})
		if result.WasPaused || result.Resumed || result.Error != "status denied" {
			t.Fatalf("result mismatch: %#v", result)
		}
		if len(actions) != 1 || actions[0] != "Status" {
			t.Fatalf("expected only Status, got %#v", actions)
		}
	})

	t.Run("IsPaused=false skips Play", func(t *testing.T) {
		actions := make([]string, 0, 1)
		sendControlPlayModeForPausePoint = func(
			ctx context.Context,
			connection unityipc.Connection,
			action string,
		) (controlPlayModeToolResponse, error) {
			actions = append(actions, action)
			return controlPlayModeToolResponse{Success: true, IsPaused: false}, nil
		}

		result := resumePlayModeForPausePointFromUnity(context.Background(), unityipc.Connection{})
		if result.WasPaused || result.Resumed || result.Error != "" {
			t.Fatalf("result mismatch: %#v", result)
		}
		if len(actions) != 1 || actions[0] != "Status" {
			t.Fatalf("expected only Status, got %#v", actions)
		}
	})

	t.Run("Play transport failure", func(t *testing.T) {
		actions := make([]string, 0, 2)
		sendControlPlayModeForPausePoint = func(
			ctx context.Context,
			connection unityipc.Connection,
			action string,
		) (controlPlayModeToolResponse, error) {
			actions = append(actions, action)
			if action == "Status" {
				return controlPlayModeToolResponse{Success: true, IsPaused: true}, nil
			}
			return controlPlayModeToolResponse{}, fmt.Errorf("play boom")
		}

		result := resumePlayModeForPausePointFromUnity(context.Background(), unityipc.Connection{})
		if !result.WasPaused || result.Resumed || !strings.Contains(result.Error, "control-play-mode Play failed") {
			t.Fatalf("result mismatch: %#v", result)
		}
		if len(actions) != 2 || actions[0] != "Status" || actions[1] != "Play" {
			t.Fatalf("expected Status then Play, got %#v", actions)
		}
	})
}
