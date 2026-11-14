package projectrunner

import (
	"bytes"
	"context"
	"errors"
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

// triggerDialogUnity stands in for Unity's side of a dialog report: it records the reported command
// names and, when answer is set, has the trigger report it after a delay longer than the short wait
// for a result that follows the look, so only the wait for the report's answer receives it.
type triggerDialogUnity struct {
	reported []string
	answer   *pausePointTriggerResult
	handle   *pausePointTriggerHandle
	// preset is a result the trigger reported before the wait started.
	preset *pausePointTriggerResult
	// delay is how long after the report the answer comes; zero means just past the short grace.
	delay time.Duration
	// keepConfirmWait runs with the production wait for the answer instead of a short one.
	keepConfirmWait bool
}

func (unity *triggerDialogUnity) report(_ context.Context, _ unityipc.Connection, toolName string) error {
	unity.reported = append(unity.reported, toolName)
	if unity.answer != nil {
		answer := unity.answer
		delay := unity.delay
		if delay == 0 {
			delay = 2 * pausePointFinalStatusProbeTimeout
		}
		go func() {
			time.Sleep(delay)
			unity.handle.done <- answer
		}()
	}
	return nil
}

// waitForPausePointBehindDialog is waitForPausePointStatusBehindDialog for a trigger that may have
// reported already, returning only the error.
func waitForPausePointBehindDialog(
	t *testing.T,
	look *fakeDialogLook,
	triggerCommand string,
	result *pausePointTriggerResult,
	unity *triggerDialogUnity,
) error {
	t.Helper()
	unity.preset = result
	_, _, err := waitForPausePointStatusBehindDialog(t, look, triggerCommand, unity)
	return err
}

// waitForPausePointStatusBehindDialog runs the poll loop against an Editor that answers nothing while the
// look finds a dialog, with a trigger that has not reported (or has reported unity.preset).
func waitForPausePointStatusBehindDialog(
	t *testing.T,
	look *fakeDialogLook,
	triggerCommand string,
	unity *triggerDialogUnity,
) (pausePointStatusResponse, pausePointWaitState, error) {
	t.Helper()
	useUnityProcessLookup(t, runningUnityProcessLookup)
	originalConfirmWait := pausePointTriggerNotRunConfirmWait
	if unity.keepConfirmWait {
		pausePointTriggerNotRunConfirmWait = originalConfirmWait
	} else {
		pausePointTriggerNotRunConfirmWait = 4 * pausePointFinalStatusProbeTimeout
	}
	t.Cleanup(func() { pausePointTriggerNotRunConfirmWait = originalConfirmWait })
	deps := look.deps()
	deps.reportBlockingDialog = unity.report
	options := waitForPausePointOptions{
		id:             triggerDialogPausePointID,
		timeout:        5 * time.Second,
		dialogWatch:    deps,
		triggerCommand: triggerCommand,
	}
	var handle *pausePointTriggerHandle
	if triggerCommand != "" {
		handle = &pausePointTriggerHandle{command: triggerCommand, done: make(chan *pausePointTriggerResult, 1)}
		if unity.preset != nil {
			handle.done <- unity.preset
		}
	}
	unity.handle = handle
	response, state, _, _, err := waitForPausePointStatus(context.Background(), unansweredIPCConnection(t), options, handle, nil, 0, false, true)
	return response, state, err
}

// Verifies a dialog the wait's own watch finds while its trigger still waits for the main thread is
// reported to Unity once under the trigger's name, and that Unity's answer, the trigger reporting it
// was not run, ends the wait with the not-run error.
func TestPausePointWaitConfirmsTheTriggerNotRunByReportingTheDialog(t *testing.T) {
	notRun := &pausePointTriggerResult{Command: "execute-dynamic-code", Completed: true, Error: triggerNotRunDialogEnvelope(t)}
	unity := &triggerDialogUnity{answer: notRun}

	err := waitForPausePointBehindDialog(t, triggerDialogLook("execute-dynamic-code", waitingForMainThreadPhase),
		"execute-dynamic-code", nil, unity)

	cliError := assertTriggerNotRunBlockedByDialog(t, err)
	if cliError.Details["TriggerResult"] != notRun {
		t.Fatalf("Details.TriggerResult = %#v, want the trigger's answer", cliError.Details["TriggerResult"])
	}
	if len(unity.reported) != 1 || unity.reported[0] != "execute-dynamic-code" {
		t.Fatalf("reported = %#v, want one report under the trigger's name", unity.reported)
	}
}

// Verifies the wait keeps the started message and the step to wait again without --trigger whenever
// Unity does not confirm the trigger was not run, and reports the dialog only while the trigger itself
// waits for the main thread.
func TestPausePointWaitKeepsTheStartedMessageWithoutUnitysConfirmation(t *testing.T) {
	succeeded := &pausePointTriggerResult{Command: "execute-dynamic-code", Completed: true, Response: []byte(`{"Success":true}`)}
	cases := []struct {
		name           string
		look           *fakeDialogLook
		triggerCommand string
		result         *pausePointTriggerResult
		answer         *pausePointTriggerResult
		wantReports    int
	}{
		{name: "no trigger", look: newFakeDialogLook(commandDialogPlayerStoppedDialog())},
		{
			name: "trigger already succeeded", look: triggerDialogLook("execute-dynamic-code", waitingForMainThreadPhase),
			triggerCommand: "execute-dynamic-code", result: succeeded,
		},
		{
			name: "no answer to the report", look: triggerDialogLook(clicore.CompileCommandName, waitingForMainThreadPhase),
			triggerCommand: clicore.CompileCommandName, wantReports: 1,
		},
		{
			name: "another answer to the report", look: triggerDialogLook("execute-dynamic-code", waitingForMainThreadPhase),
			triggerCommand: "execute-dynamic-code", answer: succeeded, wantReports: 1,
		},
		{
			name: "run-tests trigger behind its implicit compile", look: triggerDialogLook(clicore.CompileCommandName, waitingForMainThreadPhase),
			triggerCommand: clicore.RunTestsCommandName,
		},
		{
			name: "trigger already executing", look: triggerDialogLook("execute-dynamic-code", "Executing"),
			triggerCommand: "execute-dynamic-code",
		},
		{
			name: "phase unknown", look: triggerDialogLook("execute-dynamic-code", ""),
			triggerCommand: "execute-dynamic-code",
		},
		{
			name: "another command holds the slot", look: triggerDialogLook("get-logs", waitingForMainThreadPhase),
			triggerCommand: "execute-dynamic-code",
		},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			unity := &triggerDialogUnity{answer: testCase.answer}

			err := waitForPausePointBehindDialog(t, testCase.look, testCase.triggerCommand, testCase.result, unity)

			cliError := classifyStartedDialogError(t, err, clicore.PausePointAwaitCommandName)
			assertStartedBlockedByDialog(t, cliError, triggerDialogStartedStep, false)
			if _, present := cliError.Details[commandNotRunDetailKey]; present {
				t.Fatalf("Details = %#v, want no CommandNotRun", cliError.Details)
			}
			if len(unity.reported) != testCase.wantReports {
				t.Fatalf("reported = %#v, want %d reports", unity.reported, testCase.wantReports)
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

// Verifies a trigger result that arrives shortly after the watch's look found the dialog still decides
// the error, within the same grace the wait gives a trigger once it settles.
func TestPausePointWaitReadsATriggerResultThatArrivesJustAfterTheLook(t *testing.T) {
	useUnityProcessLookup(t, runningUnityProcessLookup)
	handle := &pausePointTriggerHandle{command: "execute-dynamic-code", done: make(chan *pausePointTriggerResult, 1)}
	notRun := &pausePointTriggerResult{Command: "execute-dynamic-code", Completed: true, Error: triggerNotRunDialogEnvelope(t)}
	look := newFakeDialogLook(commandDialogPlayerStoppedDialog())
	look.report = func(report statusReport) statusReport {
		go func() {
			time.Sleep(pausePointFinalStatusProbeTimeout / 5)
			handle.done <- notRun
		}()
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

// Verifies the default watch dependencies can report a dialog to Unity: a nil report turns the
// confirmation off without any error, so a dropped default would silently lose the not-run answer.
func TestDefaultDialogWatchDependenciesReportDialogsForTheTrigger(t *testing.T) {
	if defaultDialogWatchDeps().reportBlockingDialog == nil {
		t.Fatal("reportBlockingDialog is nil, want the report to Unity")
	}
}

// Verifies the wait for Unity's answer to a dialog report ends with the wait's own deadline instead of
// running past --timeout-seconds for the whole confirmation wait.
func TestPausePointWaitStopsWaitingForUnitysAnswerAtItsDeadline(t *testing.T) {
	useUnityProcessLookup(t, runningUnityProcessLookup)
	originalConfirmWait := pausePointTriggerNotRunConfirmWait
	pausePointTriggerNotRunConfirmWait = 10 * time.Second
	t.Cleanup(func() { pausePointTriggerNotRunConfirmWait = originalConfirmWait })
	unity := &triggerDialogUnity{}
	deps := triggerDialogLook("execute-dynamic-code", waitingForMainThreadPhase).deps()
	deps.reportBlockingDialog = unity.report
	handle := &pausePointTriggerHandle{command: "execute-dynamic-code", done: make(chan *pausePointTriggerResult, 1)}
	options := waitForPausePointOptions{
		id:             triggerDialogPausePointID,
		timeout:        time.Second,
		dialogWatch:    deps,
		triggerCommand: "execute-dynamic-code",
	}

	startedAt := time.Now()
	_, _, _, _, err := waitForPausePointStatus(context.Background(), unansweredIPCConnection(t), options, handle, nil, 0, false, true)
	elapsed := time.Since(startedAt)

	if len(unity.reported) != 1 {
		t.Fatalf("reported = %#v, want the dialog reported once", unity.reported)
	}
	if elapsed >= 3*time.Second {
		t.Fatalf("waited %v, want the wait to end near its 1s deadline", elapsed)
	}
	assertStartedBlockedByDialog(t, classifyStartedDialogError(t, err, clicore.PausePointAwaitCommandName), triggerDialogStartedStep, false)
}

// Verifies a hit recorded while the wait waited for Unity's answer is reported as the hit: a trigger
// result that arrives then means the main thread ran, so the wait reads the marker once more before it
// reports the dialog.
func TestPausePointWaitReportsAHitRecordedWhileItWaitedForTheTrigger(t *testing.T) {
	useUnityProcessLookup(t, runningUnityProcessLookup)
	originalQuery := queryPausePointStatus
	t.Cleanup(func() { queryPausePointStatus = originalQuery })
	lookDone := false
	queryPausePointStatus = func(context.Context, unityipc.Connection, string) (pausePointStatusResponse, error) {
		if !lookDone {
			return pausePointStatusResponse{}, errors.New("status query timed out")
		}
		return pausePointStatusResponse{Id: triggerDialogPausePointID, Status: pausePointStatusHit, HitCount: 1}, nil
	}
	succeeded := &pausePointTriggerResult{Command: "execute-dynamic-code", Completed: true, Response: []byte(`{"Success":true}`)}
	unity := &triggerDialogUnity{answer: succeeded}
	look := triggerDialogLook("execute-dynamic-code", waitingForMainThreadPhase)
	setReport := look.report
	look.report = func(report statusReport) statusReport {
		lookDone = true
		return setReport(report)
	}

	response, state, err := waitForPausePointStatusBehindDialog(t, look, "execute-dynamic-code", unity)

	if err != nil || state != pausePointWaitStateHit || response.HitCount != 1 {
		t.Fatalf("state = %q, err = %v, response = %#v, want the recorded hit", state, err, response)
	}
}

// triggerMainThreadBlockedResult is the trigger result of Unity's bare main_thread_blocked answer, which
// the trigger's send reports when its own look found no dialog.
func triggerMainThreadBlockedResult(t *testing.T) *pausePointTriggerResult {
	t.Helper()
	return &pausePointTriggerResult{
		Command:   "execute-dynamic-code",
		Completed: true,
		Error:     triggerDialogEnvelope(t, commandDialogMainThreadBlockedRPCError("execute-dynamic-code")),
	}
}

// Verifies Unity's bare main_thread_blocked answer to a dialog report counts as the trigger not run,
// naming the dialog the wait's own watch found, since the trigger's own look found none to name.
func TestPausePointWaitTakesABareMainThreadBlockedAnswerAsNotRun(t *testing.T) {
	blocked := triggerMainThreadBlockedResult(t)
	if !strings.Contains(blocked.Error, pausePointTriggerMainThreadBlockedErrorCode) {
		t.Fatalf("trigger stderr = %s, want the %s error code", blocked.Error, pausePointTriggerMainThreadBlockedErrorCode)
	}
	unity := &triggerDialogUnity{answer: blocked}

	err := waitForPausePointBehindDialog(t, triggerDialogLook("execute-dynamic-code", waitingForMainThreadPhase),
		"execute-dynamic-code", nil, unity)

	_ = assertTriggerNotRunBlockedByDialog(t, err)
}

// Verifies Unity's bare main_thread_blocked answer that arrives without a dialog from the wait's own
// watch abandons the wait as a trigger that did not run, and tells the agent to run the same trigger
// once Unity responds instead of to fix its value.
func TestWaitForPausePointAbandonsAWaitWhoseTriggerUnityDidNotRun(t *testing.T) {
	blocked := triggerMainThreadBlockedResult(t)
	useTriggerDialogFakes(t, blocked.Error)

	startedAt := time.Now()
	_, state, triggerResult, _, _, err := waitForPausePoint(context.Background(), unityipc.Connection{}, triggerDialogWaitOptions())
	elapsed := time.Since(startedAt)

	if err != nil || state != pausePointWaitStateTriggerFailed {
		t.Fatalf("state = %q, err = %v, want the trigger_failed state", state, err)
	}
	if elapsed >= 5*time.Second {
		t.Fatalf("waited %v, want an early stop", elapsed)
	}
	actions := strings.Join(pausePointTriggerFailedNextActions(triggerDialogPausePointID, triggerResult), "\n")
	if strings.Contains(actions, "Fix the --trigger value") || strings.Contains(actions, "INVALID_ARGUMENT") {
		t.Fatalf("NextActions = %s, want no advice to fix the trigger value", actions)
	}
	if !strings.Contains(actions, "<the same trigger command>") || !strings.Contains(actions, "uloop status") {
		t.Fatalf("NextActions = %s, want the same trigger once uloop status reports Ready", actions)
	}
}

// Verifies the wait for Unity's answer is long enough for a trigger that reports 3.5 seconds after the
// dialog report, about what Unity's check and the trigger's own second look take on macOS.
func TestPausePointWaitWaitsLongEnoughForUnitysAnswer(t *testing.T) {
	notRun := &pausePointTriggerResult{Command: "execute-dynamic-code", Completed: true, Error: triggerNotRunDialogEnvelope(t)}
	unity := &triggerDialogUnity{answer: notRun, delay: 3500 * time.Millisecond, keepConfirmWait: true}

	err := waitForPausePointBehindDialog(t, triggerDialogLook("execute-dynamic-code", waitingForMainThreadPhase),
		"execute-dynamic-code", nil, unity)

	_ = assertTriggerNotRunBlockedByDialog(t, err)
}

// Verifies a genuine hit the final marker read finds wins over a trigger that reported it was not run
// behind a dialog: the game itself can reach the marker while the trigger waits.
func TestWaitForPausePointReportsAHitOverATriggerNotRunBehindADialog(t *testing.T) {
	useTriggerDialogFakes(t, triggerNotRunDialogEnvelope(t))
	queryPausePointStatus = func(ctx context.Context, _ unityipc.Connection, id string) (pausePointStatusResponse, error) {
		// Only the final read runs under the short probe deadline; the polls before it see the armed marker.
		if deadline, ok := ctx.Deadline(); ok && time.Until(deadline) <= pausePointFinalStatusProbeTimeout {
			return pausePointStatusResponse{Id: id, Status: pausePointStatusHit, HitCount: 1}, nil
		}
		return pausePointArmedStatusResponse(id), nil
	}

	response, state, _, _, _, err := waitForPausePoint(context.Background(), unityipc.Connection{}, triggerDialogWaitOptions())

	if err != nil || state != pausePointWaitStateHit || response.HitCount != 1 {
		t.Fatalf("state = %q, err = %v, HitCount = %d, want the hit", state, err, response.HitCount)
	}
}
