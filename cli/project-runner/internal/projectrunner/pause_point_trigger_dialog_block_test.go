package projectrunner

import (
	"bytes"
	"context"
	"io"
	"reflect"
	"strings"
	"testing"
	"time"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

const (
	triggerDialogPausePointID = "trigger_dialog"
	triggerDialogNotRunStep   = "After the dialog is closed, run uloop await-pause-point --id trigger_dialog again with the same --trigger."
	triggerDialogStartedStep  = "After the dialog is closed, run uloop pause-point-status --id trigger_dialog to see whether the marker " +
		"was hit; if it is still waiting, run uloop await-pause-point --id trigger_dialog again without --trigger."
)

// triggerDialogEnvelope is the stderr a trigger command writes when it stops on a dialog, built from the
// error the trigger's own send returns so the test reads what a real trigger prints.
func triggerDialogEnvelope(t *testing.T, err error) string {
	t.Helper()
	var stderr bytes.Buffer
	clierrors.WriteClassifiedError(&stderr, err, clierrors.ErrorContext{ProjectRoot: "<PROJECT_ROOT>", Command: "execute-dynamic-code"})
	return stderr.String()
}

func triggerNotRunDialogEnvelope(t *testing.T) string {
	t.Helper()
	return triggerDialogEnvelope(t, blockedByDialogError{
		command:  "execute-dynamic-code",
		dialogs:  []statusDialog{commandDialogPlayerStoppedDialog()},
		canPress: true,
		notRun:   commandDialogMainThreadBlockedRPCError("execute-dynamic-code"),
	})
}

// useTriggerDialogFakes answers every status poll with the armed marker, lets the trigger write stderr,
// and fails the test if a re-pause is sent.
func useTriggerDialogFakes(t *testing.T, triggerStderr string) {
	t.Helper()
	originalQuery := queryPausePointStatus
	originalDispatch := dispatchPausePointTriggerCommand
	originalResume := resumePlayModeForPausePoint
	originalSend := sendControlPlayModeForPausePoint
	originalPoll := pausePointStatusPoll
	t.Cleanup(func() {
		queryPausePointStatus = originalQuery
		dispatchPausePointTriggerCommand = originalDispatch
		resumePlayModeForPausePoint = originalResume
		sendControlPlayModeForPausePoint = originalSend
		pausePointStatusPoll = originalPoll
	})
	pausePointStatusPoll = time.Millisecond
	queryPausePointStatus = func(context.Context, unityipc.Connection, string) (pausePointStatusResponse, error) {
		return pausePointArmedStatusResponse(triggerDialogPausePointID), nil
	}
	dispatchPausePointTriggerCommand = func(
		_ context.Context, _ unityipc.Connection, _ string, _ []string, _ string, _ io.Writer, stderr io.Writer,
	) int {
		// The trigger's own send waits a moment first, so the wait has polled before the result arrives.
		time.Sleep(20 * time.Millisecond)
		_, _ = io.WriteString(stderr, triggerStderr)
		return 1
	}
	sendControlPlayModeForPausePoint = func(context.Context, unityipc.Connection, string) (controlPlayModeToolResponse, error) {
		t.Error("the wait must not try to re-pause PlayMode while a dialog holds the main thread")
		return controlPlayModeToolResponse{}, nil
	}
}

func triggerDialogWaitOptions() waitForPausePointOptions {
	return waitForPausePointOptions{
		id:             triggerDialogPausePointID,
		timeoutSeconds: 60,
		timeout:        10 * time.Second,
		triggerCommand: "execute-dynamic-code",
		triggerArgs:    []string{"--code", "return 1;"},
		// The watch stays off (no probe); only whether a button can be pressed is read.
		dialogWatch: dialogWatchDeps{canPressDialogs: true},
	}
}

// assertTriggerNotRunBlockedByDialog checks the shape of the new error: the dialog, the sentence that
// the trigger was not run, the step to wait again with the same --trigger, and the machine-readable keys.
func assertTriggerNotRunBlockedByDialog(t *testing.T, err error) clierrors.CLIError {
	t.Helper()
	cliError := classifyStartedDialogError(t, err, clicore.PausePointAwaitCommandName)
	dialog := commandDialogPlayerStoppedDialog()
	wantMessage := blockedByDialogMessage([]statusDialog{dialog}) +
		" uloop await-pause-point stopped waiting; its --trigger command was not run, and the marker stays armed."
	if cliError.Message != wantMessage {
		t.Fatalf("Message = %q, want %q", cliError.Message, wantMessage)
	}
	if !strings.Contains(cliError.NextActions[0], "uloop press-dialog-button") {
		t.Fatalf("first NextAction = %q, want the press guidance", cliError.NextActions[0])
	}
	if !containsString(cliError.NextActions, triggerDialogNotRunStep) {
		t.Fatalf("NextActions = %#v, want %q", cliError.NextActions, triggerDialogNotRunStep)
	}
	if !cliError.SafeToRetry {
		t.Fatal("SafeToRetry = false, want true: the trigger did not run and the marker stays armed")
	}
	if cliError.Details[commandNotRunDetailKey] != true || cliError.Details[pausePointDialogIDDetailKey] != triggerDialogPausePointID {
		t.Fatalf("Details = %#v, want CommandNotRun and the marker id", cliError.Details)
	}
	if !reflect.DeepEqual(cliError.Details["Dialogs"], []statusDialog{dialog}) {
		t.Fatalf("Details.Dialogs = %#v, want the trigger's dialog", cliError.Details["Dialogs"])
	}
	return cliError
}

func containsString(values []string, want string) bool {
	for _, value := range values {
		if value == want {
			return true
		}
	}
	return false
}

// --- the trigger reports it was not run ---

// Verifies a trigger whose own send reports a dialog kept it from running ends the wait at once with
// BLOCKED_BY_DIALOG that says the trigger was not run and to wait again with the same --trigger,
// instead of waiting on until the wait's own dialog watch fires and says to drop --trigger.
func TestWaitForPausePointStopsWhenTheTriggerWasNotRunBehindADialog(t *testing.T) {
	useTriggerDialogFakes(t, triggerNotRunDialogEnvelope(t))

	startedAt := time.Now()
	_, _, triggerResult, _, _, err := waitForPausePoint(context.Background(), unityipc.Connection{}, triggerDialogWaitOptions())
	elapsed := time.Since(startedAt)

	if elapsed >= 5*time.Second {
		t.Fatalf("waited %v, want an early stop", elapsed)
	}
	cliError := assertTriggerNotRunBlockedByDialog(t, err)
	if triggerResult == nil || !strings.Contains(triggerResult.Error, clierrors.ErrorCodeBlockedByDialog) {
		t.Fatalf("triggerResult = %#v, want the trigger's dialog error", triggerResult)
	}
	if !reflect.DeepEqual(cliError.Details["TriggerResult"], triggerResult) {
		t.Fatalf("Details.TriggerResult = %#v, want the trigger result %#v", cliError.Details["TriggerResult"], triggerResult)
	}
	if _, present := cliError.Details["ResumePlayResult"]; present {
		t.Fatalf("Details = %#v, want no ResumePlayResult without --resume-play", cliError.Details)
	}
	for _, action := range cliError.NextActions {
		if strings.Contains(action, "control-play-mode") {
			t.Fatalf("NextActions = %#v, want no re-pause step without --resume-play", cliError.NextActions)
		}
	}
}

// Verifies the wait does not re-pause PlayMode it resumed when the trigger was not run behind a dialog,
// because the dialog holds the main thread, and instead reports the resume and tells the agent how to
// get back to a paused state before waiting again.
func TestWaitForPausePointPointsAtPauseWhenItResumedBeforeTheTriggerWasNotRun(t *testing.T) {
	useTriggerDialogFakes(t, triggerNotRunDialogEnvelope(t))
	resumePlayModeForPausePoint = func(context.Context, unityipc.Connection) pausePointResumePlayResult {
		return pausePointResumePlayResult{WasPaused: true, Resumed: true}
	}
	options := triggerDialogWaitOptions()
	options.resumePlay = true

	_, _, _, _, _, err := waitForPausePoint(context.Background(), unityipc.Connection{}, options)

	cliError := assertTriggerNotRunBlockedByDialog(t, err)
	resumeResult, ok := cliError.Details["ResumePlayResult"].(*pausePointResumePlayResult)
	if !ok || !resumeResult.Resumed || resumeResult.Repaused {
		t.Fatalf("Details.ResumePlayResult = %#v, want the resume without a re-pause", cliError.Details["ResumePlayResult"])
	}
	if last := cliError.NextActions[len(cliError.NextActions)-1]; last != pausePointTriggerNotRunResumedNextAction {
		t.Fatalf("last NextAction = %q, want %q", last, pausePointTriggerNotRunResumedNextAction)
	}
}

// Verifies the re-pause step is left out when --resume-play found PlayMode not paused and resumed nothing.
func TestWaitForPausePointOmitsThePauseStepWhenItDidNotResume(t *testing.T) {
	useTriggerDialogFakes(t, triggerNotRunDialogEnvelope(t))
	resumePlayModeForPausePoint = func(context.Context, unityipc.Connection) pausePointResumePlayResult {
		return pausePointResumePlayResult{WasPaused: false, Resumed: false}
	}
	options := triggerDialogWaitOptions()
	options.resumePlay = true

	_, _, _, _, _, err := waitForPausePoint(context.Background(), unityipc.Connection{}, options)

	cliError := assertTriggerNotRunBlockedByDialog(t, err)
	if _, present := cliError.Details["ResumePlayResult"]; !present {
		t.Fatalf("Details = %#v, want the ResumePlayResult reported", cliError.Details)
	}
	if containsString(cliError.NextActions, pausePointTriggerNotRunResumedNextAction) {
		t.Fatalf("NextActions = %#v, want no re-pause step when nothing was resumed", cliError.NextActions)
	}
}

// Verifies a trigger that stopped on a dialog after its command had started does not end the wait: the
// command goes on once the dialog closes and may still hit the marker.
func TestWaitForPausePointKeepsWaitingWhenTheTriggerStartedBehindADialog(t *testing.T) {
	started := triggerDialogEnvelope(t, startedCommandBlockedByDialog(
		"execute-dynamic-code", []statusDialog{commandDialogPlayerStoppedDialog()}, true, false))
	useTriggerDialogFakes(t, started)
	options := triggerDialogWaitOptions()
	options.timeout = 300 * time.Millisecond

	_, state, triggerResult, _, _, err := waitForPausePoint(context.Background(), unityipc.Connection{}, options)

	if err != nil || state != pausePointWaitStateTimeout {
		t.Fatalf("state = %q, err = %v, want the wait to run to its timeout", state, err)
	}
	if triggerResult == nil || !strings.Contains(triggerResult.Error, clierrors.ErrorCodeBlockedByDialog) {
		t.Fatalf("triggerResult = %#v, want the trigger's dialog error", triggerResult)
	}
}

// --- the wait's own watch finds the dialog ---

// triggerDialogLook is a dialog look whose report names the command holding Unity's command slot.
func triggerDialogLook(runningToolName string, phase string) *fakeDialogLook {
	look := newFakeDialogLook(commandDialogPlayerStoppedDialog())
	look.report = func(report statusReport) statusReport {
		report.RunningToolName = runningToolName
		report.RunningToolPhase = phase
		return report
	}
	return look
}

// waitForPausePointBehindDialog runs the poll loop against an Editor that answers nothing while the
// look finds a dialog, with a trigger that has not reported (or has reported result).
func waitForPausePointBehindDialog(
	t *testing.T,
	look *fakeDialogLook,
	triggerCommand string,
	result *pausePointTriggerResult,
) error {
	t.Helper()
	useUnityProcessLookup(t, runningUnityProcessLookup)
	options := waitForPausePointOptions{
		id:             triggerDialogPausePointID,
		timeout:        5 * time.Second,
		dialogWatch:    look.deps(),
		triggerCommand: triggerCommand,
	}
	var handle *pausePointTriggerHandle
	if triggerCommand != "" {
		handle = &pausePointTriggerHandle{command: triggerCommand, done: make(chan *pausePointTriggerResult, 1)}
		if result != nil {
			handle.done <- result
		}
	}
	_, _, _, _, err := waitForPausePointStatus(context.Background(), unansweredIPCConnection(t), options, handle, nil, 0, false, true)
	return err
}

// Verifies a dialog the wait's own watch finds while the trigger it sent still holds the command slot
// waiting for the main thread reports the trigger as not run: once uloop exits, the dropped connection
// cancels that request in Unity.
func TestPausePointWaitReportsTheTriggerNotRunWhenItStillWaitsForTheMainThread(t *testing.T) {
	err := waitForPausePointBehindDialog(t, triggerDialogLook("execute-dynamic-code", waitingForMainThreadPhase),
		"execute-dynamic-code", nil)

	cliError := assertTriggerNotRunBlockedByDialog(t, err)
	if _, present := cliError.Details["TriggerResult"]; present {
		t.Fatalf("Details = %#v, want no TriggerResult before the trigger reported", cliError.Details)
	}
}

// Verifies every case the watch cannot pin on a not-yet-run trigger that Unity drops on disconnect keeps
// the started message and the step to wait again without --trigger.
func TestPausePointWaitKeepsTheStartedMessageWhenTheTriggerMayRun(t *testing.T) {
	succeeded := &pausePointTriggerResult{Command: "execute-dynamic-code", Completed: true, Response: []byte(`{"Success":true}`)}
	cases := []struct {
		name           string
		look           *fakeDialogLook
		triggerCommand string
		result         *pausePointTriggerResult
	}{
		{name: "no trigger", look: newFakeDialogLook(commandDialogPlayerStoppedDialog())},
		{name: "trigger already succeeded", look: triggerDialogLook("execute-dynamic-code", waitingForMainThreadPhase),
			triggerCommand: "execute-dynamic-code", result: succeeded},
		{name: "compile survives a disconnect", look: triggerDialogLook(clicore.CompileCommandName, waitingForMainThreadPhase),
			triggerCommand: clicore.CompileCommandName},
		{name: "run-tests survives a disconnect", look: triggerDialogLook(clicore.RunTestsCommandName, waitingForMainThreadPhase),
			triggerCommand: clicore.RunTestsCommandName},
		{name: "run-tests trigger behind its implicit compile", look: triggerDialogLook(clicore.CompileCommandName, waitingForMainThreadPhase),
			triggerCommand: clicore.RunTestsCommandName},
		{name: "trigger already executing", look: triggerDialogLook("execute-dynamic-code", "Executing"),
			triggerCommand: "execute-dynamic-code"},
		{name: "phase unknown", look: triggerDialogLook("execute-dynamic-code", ""),
			triggerCommand: "execute-dynamic-code"},
		{name: "another command holds the slot", look: triggerDialogLook("get-logs", waitingForMainThreadPhase),
			triggerCommand: "execute-dynamic-code"},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			err := waitForPausePointBehindDialog(t, testCase.look, testCase.triggerCommand, testCase.result)

			cliError := classifyStartedDialogError(t, err, clicore.PausePointAwaitCommandName)
			assertStartedBlockedByDialog(t, cliError, triggerDialogStartedStep, false)
			if _, present := cliError.Details[commandNotRunDetailKey]; present {
				t.Fatalf("Details = %#v, want no CommandNotRun", cliError.Details)
			}
		})
	}
}

// Verifies a trigger result that arrives while the wait's own watch is looking at the dialog still
// decides the error: a trigger that reported it was not run gets the not-run error, not the started one.
func TestPausePointWaitReadsATriggerResultThatArrivesDuringTheLook(t *testing.T) {
	useUnityProcessLookup(t, runningUnityProcessLookup)
	handle := &pausePointTriggerHandle{command: "execute-dynamic-code", done: make(chan *pausePointTriggerResult, 1)}
	notRun := &pausePointTriggerResult{Command: "execute-dynamic-code", Completed: true, Error: triggerNotRunDialogEnvelope(t)}
	look := newFakeDialogLook(commandDialogPlayerStoppedDialog())
	look.report = func(report statusReport) statusReport {
		// The trigger reports during the look; the report names no slot holder, so only the result tells.
		handle.done <- notRun
		return report
	}
	options := waitForPausePointOptions{
		id:             triggerDialogPausePointID,
		timeout:        5 * time.Second,
		dialogWatch:    look.deps(),
		triggerCommand: "execute-dynamic-code",
	}

	_, _, triggerResult, _, err := waitForPausePointStatus(
		context.Background(), unansweredIPCConnection(t), options, handle, nil, 0, false, true)

	cliError := assertTriggerNotRunBlockedByDialog(t, err)
	if triggerResult != notRun || cliError.Details["TriggerResult"] != notRun {
		t.Fatalf("triggerResult = %#v, Details.TriggerResult = %#v, want the result that arrived", triggerResult, cliError.Details["TriggerResult"])
	}
}
