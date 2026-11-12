package projectrunner

import (
	"bytes"
	"context"
	"errors"
	"strings"
	"testing"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
	"github.com/hatayama/unity-cli-loop/common/tooldocs"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

const preSendGetLogsResult = `{"Success":true,"TotalCount":0}`

func preSendReport(state string) statusReport {
	report := newStatusReport(state, "state "+state)
	return report
}

func preSendDialogWithoutButtons() statusDialog {
	return statusDialog{Title: "Importing large assets", Message: "Please wait."}
}

type preSendRun struct {
	code   int
	stdout string
	stderr string
}

func (run preSendRun) cliError(t *testing.T) clierrors.CLIError {
	t.Helper()
	return hotReloadErrorEnvelope(t, run.stderr).Error
}

func runProjectCommandWithProbe(
	t *testing.T,
	connection unityipc.Connection,
	command string,
	args []string,
	deps dialogWatchDeps,
) preSendRun {
	t.Helper()
	var stdout, stderr bytes.Buffer
	code := runProjectCommand(context.Background(), connection, command, args, connection.ProjectRoot, &stdout, &stderr, deps)
	return preSendRun{code: code, stdout: stdout.String(), stderr: stderr.String()}
}

// assertNoRequestReached fails when the fake Editor received a request. Why no wait is needed: a
// command that sent its request waits for the answer, which the fake writes only after it recorded
// the request, so a request that was sent is already recorded when the command returns.
func assertNoRequestReached(t *testing.T, server fakeUnityServer) {
	t.Helper()
	select {
	case request := <-server.requests:
		t.Fatalf("the command was sent to Unity: %#v", request)
	default:
	}
}

func assertRefusedBeforeSend(t *testing.T, run preSendRun, command string, wantTitle string) clierrors.CLIError {
	t.Helper()
	if run.code != 1 {
		t.Fatalf("exit code = %d, want 1\nstderr=%s", run.code, run.stderr)
	}
	cliError := run.cliError(t)
	if cliError.ErrorCode != clierrors.ErrorCodeBlockedByDialog || cliError.Phase != clierrors.ErrorPhaseConnection {
		t.Fatalf("ErrorCode = %q, Phase = %q, want BLOCKED_BY_DIALOG in connection\nstderr=%s",
			cliError.ErrorCode, cliError.Phase, run.stderr)
	}
	wantPrefix := "'" + command + "' was not run: A modal dialog \"" + wantTitle + "\" is open in Unity"
	if !strings.HasPrefix(cliError.Message, wantPrefix) {
		t.Fatalf("Message = %q, want prefix %q", cliError.Message, wantPrefix)
	}
	if !cliError.Retryable || !cliError.SafeToRetry {
		t.Fatalf("Retryable = %v, SafeToRetry = %v, want both true", cliError.Retryable, cliError.SafeToRetry)
	}
	if last := cliError.NextActions[len(cliError.NextActions)-1]; last != commandDialogRunAgainLine {
		t.Fatalf("last NextAction = %q, want %q", last, commandDialogRunAgainLine)
	}
	if _, present := cliError.Details["Dialogs"]; !present {
		t.Fatalf("Details = %#v, want Dialogs", cliError.Details)
	}
	if _, present := cliError.Details["StallSeconds"]; present {
		t.Fatalf("Details = %#v, want no StallSeconds for a command that was never sent", cliError.Details)
	}
	return cliError
}

// Verifies a command sent while the Editor reports a dialog uloop can answer is not sent at all and
// is refused at once with the dialog, the press-dialog-button step first, and the step to run the
// command again last.
func TestRunProjectCommandRefusesACommandWhileADialogIsOpen(t *testing.T) {
	projectRoot := t.TempDir()
	server := startFakeUnityResultServer(t, projectRoot, commandDialogBlockCommand, preSendGetLogsResult)
	probe := &commandDialogProbe{report: blockedByDialogStatusReport(commandDialogPlayerStoppedDialog())}

	run := runProjectCommandWithProbe(t, server.connection, commandDialogBlockCommand, nil, probe.deps(true))

	assertNoRequestReached(t, server)
	cliError := assertRefusedBeforeSend(t, run, commandDialogBlockCommand, commandDialogPlayerStoppedDialog().Title)
	if !strings.Contains(cliError.NextActions[0], "press-dialog-button") {
		t.Fatalf("first NextAction = %q, want the press-dialog-button step", cliError.NextActions[0])
	}
	if probe.calls != 1 {
		t.Fatalf("probe calls = %d, want 1", probe.calls)
	}
}

// Verifies a dialog without a button uloop can press is refused with the step that asks the user to
// close it, not the press-dialog-button step.
func TestRunProjectCommandRefusesWithTheUserStepForADialogWithoutButtons(t *testing.T) {
	projectRoot := t.TempDir()
	server := startFakeUnityResultServer(t, projectRoot, commandDialogBlockCommand, preSendGetLogsResult)
	probe := &commandDialogProbe{report: blockedByDialogStatusReport(preSendDialogWithoutButtons())}

	run := runProjectCommandWithProbe(t, server.connection, commandDialogBlockCommand, nil, probe.deps(true))

	assertNoRequestReached(t, server)
	cliError := assertRefusedBeforeSend(t, run, commandDialogBlockCommand, preSendDialogWithoutButtons().Title)
	if !strings.Contains(cliError.NextActions[0], "uloop cannot press a button in this dialog") {
		t.Fatalf("first NextAction = %q, want the step that asks the user", cliError.NextActions[0])
	}
}

// Verifies every state other than BlockedByDialog, and a check that failed, lets the command go to
// Unity as before and prints Unity's answer.
func TestRunProjectCommandSendsTheCommandWhenNoDialogIsReported(t *testing.T) {
	blockedWithoutDialogs := preSendReport(statusStateBlockedByDialog)
	cases := []struct {
		name  string
		probe *commandDialogProbe
	}{
		{name: "Ready", probe: &commandDialogProbe{report: preSendReport(statusStateReady)}},
		{name: "Busy", probe: &commandDialogProbe{report: preSendReport(statusStateBusy)}},
		{name: "MainThreadBlocked", probe: &commandDialogProbe{report: preSendReport(statusStateMainThreadBlocked)}},
		{name: "check failed", probe: &commandDialogProbe{err: errors.New("dial failed")}},
		{name: "BlockedByDialog without dialogs", probe: &commandDialogProbe{report: blockedWithoutDialogs}},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			projectRoot := t.TempDir()
			server := startFakeUnityResultServer(t, projectRoot, commandDialogBlockCommand, preSendGetLogsResult)

			run := runProjectCommandWithProbe(t, server.connection, commandDialogBlockCommand, nil, testCase.probe.deps(true))

			if run.code != 0 {
				t.Fatalf("exit code = %d, want 0\nstderr=%s", run.code, run.stderr)
			}
			server.receivedRequest(t)
			if !strings.Contains(run.stdout, `"TotalCount": 0`) {
				t.Fatalf("stdout must carry Unity's answer:\n%s", run.stdout)
			}
			if testCase.probe.calls != 1 {
				t.Fatalf("probe calls = %d, want 1", testCase.probe.calls)
			}
		})
	}
}

// Verifies the commands that work while a dialog is open, or never reach Unity, are not checked:
// status and get-dialogs are the check, cancel and press-dialog-button exist for a held Editor, and
// focus-window runs in uloop alone.
func TestRefuseCommandBlockedByDialogSkipsTheCommandsThatWorkDuringADialog(t *testing.T) {
	for _, command := range []string{
		clicore.StatusCommandName,
		clicore.CancelCommandName,
		clicore.GetDialogsCommandName,
		clicore.PressDialogButtonCommandName,
		"focus-window",
	} {
		t.Run(command, func(t *testing.T) {
			probe := &commandDialogProbe{report: blockedByDialogStatusReport(commandDialogPlayerStoppedDialog())}
			var stderr bytes.Buffer

			refused := refuseCommandBlockedByDialog(context.Background(), unreachableConnection(t.TempDir()), command,
				&stderr, probe.deps(true))

			if refused || probe.calls != 0 || stderr.Len() != 0 {
				t.Fatalf("refused = %v, probe calls = %d, stderr = %q, want false, 0, empty", refused, probe.calls, stderr.String())
			}
		})
	}
}

// Verifies every way a command reaches Unity is refused while a dialog is open: the schema tools
// with their own waits, the commands with their own parsers, and the ones Unity answers through an
// internal bridge command, which needs the main thread as well.
func TestRunProjectCommandRefusesEveryPathWhileADialogIsOpen(t *testing.T) {
	useUnityProcessLookup(t, noUnityProcessLookup)
	for _, command := range []string{
		commandDialogBlockCommand,
		clicore.CompileCommandName,
		clicore.ExecuteDynamicCodeCommandName,
		controlPlayModeCommandName,
		clicore.RunTestsCommandName,
		hotReloadCommandName,
		clicore.PausePointAwaitCommandName,
		pausePointEnableCommandName,
		tooldocs.RecordVideoCommandName,
		"list",
		"sync",
		clicore.PausePointStatusUserCommandName,
		clicore.SetCodeOptimizationCommandName,
	} {
		t.Run(command, func(t *testing.T) {
			probe := &commandDialogProbe{report: blockedByDialogStatusReport(commandDialogPlayerStoppedDialog())}

			run := runProjectCommandWithProbe(t, unreachableConnection(writeFakeUnityProject(t)), command, nil, probe.deps(true))

			assertRefusedBeforeSend(t, run, command, commandDialogPlayerStoppedDialog().Title)
		})
	}
}

// Verifies the check runs once per uloop run: hot-reload's resend after the busy wait goes to Unity
// without another check.
func TestRunProjectCommandChecksOnceWhenHotReloadResends(t *testing.T) {
	useFastEditorReadyWait(t, fastEditorReadyWaitOptions())
	useRunningUnityProcess(t)
	listener := newLoopbackIpcListener(t)
	steps := busyWaitT1Steps()
	requests := make(chan scriptedIPCRequest, len(steps)+1)
	serverErr := make(chan error, len(steps)+1)
	go serveScriptedIPCResponses(listener, steps, requests, serverErr)
	connection := unityipc.Connection{
		Endpoint:    unityipc.Endpoint{Network: listener.Addr().Network(), Address: listener.Addr().String()},
		ProjectRoot: writeFakeUnityProject(t),
	}
	probe := &commandDialogProbe{report: preSendReport(statusStateReady)}

	run := runProjectCommandWithProbe(t, connection, hotReloadCommandName,
		[]string{"--files", "Assets/A.cs", "--compile-on-skip", "off"}, probe.deps(true))

	select {
	case err := <-serverErr:
		t.Fatalf("fake Unity server failed: %v", err)
	default:
	}
	if run.code != 0 {
		t.Fatalf("exit code = %d, want 0\nstderr=%s", run.code, run.stderr)
	}
	applies := 0
	for len(requests) > 0 {
		if request := <-requests; request.method == hotReloadCommandName {
			applies++
		}
	}
	if applies != 2 || probe.calls != 1 {
		t.Fatalf("hot-reload requests = %d, probe calls = %d, want 2 and 1", applies, probe.calls)
	}
}

// Verifies a dialog title with words the transport checks read as a lost connection (such as "EOF")
// is still refused with the dialog instead of being treated as a disconnect.
func TestRunProjectCommandRefusesADialogWhoseTitleReadsAsADisconnect(t *testing.T) {
	useUnityProcessLookup(t, noUnityProcessLookup)
	dialog := statusDialog{Title: "Unexpected EOF while reading the scene", Buttons: []string{"OK"}}
	probe := &commandDialogProbe{report: blockedByDialogStatusReport(dialog)}

	run := runProjectCommandWithProbe(t, unreachableConnection(writeFakeUnityProject(t)), commandDialogBlockCommand, nil,
		probe.deps(true))

	assertRefusedBeforeSend(t, run, commandDialogBlockCommand, dialog.Title)
	notSent := notSentBlockedByDialog(commandDialogBlockCommand, []statusDialog{dialog}, true)
	if clierrors.IsTransportDisconnectError(notSent) || strings.Contains(notSent.Error(), dialog.Title) {
		t.Fatalf("the not-sent error reads as a disconnect or carries the dialog text: %q", notSent.Error())
	}
}

// Verifies the uloop entry checks before sending: a plain run with --project-path is refused with the
// dialog the check reports, so the production entry cannot bypass the check.
func TestRunProjectLocalChecksForADialogBeforeSending(t *testing.T) {
	useUnityProcessLookup(t, noUnityProcessLookup)
	projectRoot := writeFakeUnityProject(t)
	probe := &commandDialogProbe{report: blockedByDialogStatusReport(commandDialogPlayerStoppedDialog())}
	var stdout, stderr bytes.Buffer

	code := runProjectLocalWithDeps(context.Background(), []string{"--project-path", projectRoot, commandDialogBlockCommand},
		&stdout, &stderr, probe.deps(true))

	assertRefusedBeforeSend(t, preSendRun{code: code, stdout: stdout.String(), stderr: stderr.String()},
		commandDialogBlockCommand, commandDialogPlayerStoppedDialog().Title)
	if probe.calls != 1 {
		t.Fatalf("probe calls = %d, want 1", probe.calls)
	}
}

// Verifies a pause-point trigger goes to Unity without a check of its own: the outer uloop run already
// checked, so the fake Editor sees the trigger's command as its first request, not get-editor-status.
func TestPausePointTriggerIsSentWithoutAnotherCheck(t *testing.T) {
	projectRoot := t.TempDir()
	server := startFakeUnityResultServer(t, projectRoot, commandDialogBlockCommand, preSendGetLogsResult)

	result := runPausePointTriggerSync(context.Background(), server.connection, projectRoot, commandDialogBlockCommand,
		nil, commandDialogBlockCommand)

	if request := server.receivedRequest(t); request == nil {
		t.Fatal("the trigger was not sent")
	}
	if !result.Completed || result.Error != "" {
		t.Fatalf("trigger result = %#v, want a completed trigger without an error", result)
	}
}

// statusLookCounter counts the slow parts of the status probe: the process list and the look at
// Unity's windows.
type statusLookCounter struct {
	processLookups int
	windowLooks    int
}

func (counter *statusLookCounter) deps() statusCommandDeps {
	return statusCommandDeps{
		probeTimeout: fastEditorReadyWaitOptions().probeTimeout,
		isUnityProcessRunning: func(context.Context, string) (bool, error) {
			counter.processLookups++
			return true, nil
		},
		inspectDialogs: func(context.Context, string) ([]statusDialog, error) {
			counter.windowLooks++
			return nil, nil
		},
	}
}

// Verifies the check before sending does nothing more when the Editor does not answer: it neither
// reads the process list nor looks at Unity's windows, so a command sent during a domain reload is
// not slowed by the check, and the command is then sent as before.
func TestPreSendStatusProbeDoesNotDiagnoseAnUnreachableEditor(t *testing.T) {
	counter := &statusLookCounter{}

	_, err := probeEditorStatus(context.Background(), unreachableConnection(t.TempDir()), preSendStatusDeps(counter.deps()))

	if err == nil {
		t.Fatal("the check must report the failed connection as an error, so the command is sent")
	}
	if counter.processLookups != 0 || counter.windowLooks != 0 {
		t.Fatalf("process lookups = %d, window looks = %d, want 0 and 0", counter.processLookups, counter.windowLooks)
	}
}

// Verifies a pause point's stop (Play Mode paused, main thread still running) costs the check one
// round trip: the Editor reads as Ready, with no second probe and no look at Unity's windows.
func TestPreSendStatusProbeTakesOneRoundTripDuringAPausePointStop(t *testing.T) {
	listener := newLoopbackIpcListener(t)
	pausedStatus := `{"IsBusy":false,"HasEditorState":true,"IsPlaying":true,"IsPaused":true,"IsCompiling":false,` +
		`"IsUpdating":false,"SecondsSinceLastMainThreadTick":0.1}`
	steps := []scriptedIPCStep{{method: editorStatusBridgeCommandName, result: pausedStatus}}
	requests := make(chan scriptedIPCRequest, len(steps)+1)
	serverErr := make(chan error, len(steps)+1)
	go serveScriptedIPCResponses(listener, steps, requests, serverErr)
	connection := unityipc.Connection{
		Endpoint:    unityipc.Endpoint{Network: listener.Addr().Network(), Address: listener.Addr().String()},
		ProjectRoot: t.TempDir(),
	}
	counter := &statusLookCounter{}

	report, err := probeEditorStatus(context.Background(), connection, preSendStatusDeps(counter.deps()))

	if err != nil || report.State != statusStateReady {
		t.Fatalf("report = %#v, err = %v, want Ready", report, err)
	}
	if len(requests) != 1 || counter.windowLooks != 0 {
		t.Fatalf("round trips = %d, window looks = %d, want 1 and 0", len(requests), counter.windowLooks)
	}
	select {
	case err := <-serverErr:
		t.Fatalf("fake Unity server failed: %v", err)
	default:
	}
}
