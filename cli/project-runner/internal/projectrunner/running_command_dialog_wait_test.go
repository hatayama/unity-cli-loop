package projectrunner

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"net"
	"strings"
	"testing"
	"time"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

// --- compile (fresh wait and attach wait) ---

func compileDialogWaitDeps(look *fakeDialogLook, answered bool) compileWaitDeps {
	deps := compileWaitTestDeps(func(context.Context, unityipc.Connection, string) (compileStatusResponse, error) {
		if answered {
			return compileStatusResponse{IsCompiling: true}, nil
		}
		return compileStatusResponse{}, errors.New("status query timed out")
	})
	deps.dialogWatch = look.deps()
	return deps
}

func compileDialogWaitOptions(t *testing.T) compileCompletionOptions {
	return compileCompletionOptions{
		connection:   compileWaitTestConnection(t),
		requestID:    "compile_dialog",
		timeout:      300 * time.Millisecond,
		pollInterval: time.Millisecond,
	}
}

// Verifies the compile wait stops with BLOCKED_BY_DIALOG once its polls go unanswered and a dialog is
// found, pointing the next uloop compile at the compile still running.
func TestCompileWaitStopsWhenADialogHoldsUnity(t *testing.T) {
	look := newFakeDialogLook(commandDialogPlayerStoppedDialog())

	_, completed, _, err := waitForCompileCompletionWithDeps(context.Background(), compileDialogWaitOptions(t), compileDialogWaitDeps(look, false))

	if completed {
		t.Fatal("the wait must not complete")
	}
	assertStartedBlockedByDialog(t, classifyStartedDialogError(t, err, clicore.CompileCommandName), compileDialogClosedNextAction, true)
}

// Verifies the compile attach wait stops the same way.
func TestCompileAttachWaitStopsWhenADialogHoldsUnity(t *testing.T) {
	look := newFakeDialogLook(commandDialogPlayerStoppedDialog())

	_, outcome, _, err := waitForAttachedCompileCompletion(context.Background(), compileDialogWaitOptions(t), compileDialogWaitDeps(look, false))

	if outcome != attachWaitFailed {
		t.Fatalf("outcome = %v, want attachWaitFailed", outcome)
	}
	assertStartedBlockedByDialog(t, classifyStartedDialogError(t, err, clicore.CompileCommandName), compileDialogClosedNextAction, true)
}

// Verifies the compile wait ends as before, at its timeout, when the look finds no dialog or fails,
// and never looks while its polls are answered.
func TestCompileWaitKeepsWaitingWithoutADialog(t *testing.T) {
	cases := []struct {
		name      string
		look      *fakeDialogLook
		answered  bool
		wantLooks bool
	}{
		{name: "no dialog", look: newFakeDialogLook(), wantLooks: true},
		{name: "look failed", look: &fakeDialogLook{err: errors.New("osascript failed"), step: time.Second}, wantLooks: true},
		{name: "answered polls", look: newFakeDialogLook(commandDialogPlayerStoppedDialog()), answered: true},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			_, completed, _, err := waitForCompileCompletionWithDeps(context.Background(), compileDialogWaitOptions(t),
				compileDialogWaitDeps(testCase.look, testCase.answered))

			if completed || err != nil {
				t.Fatalf("completed = %v, err = %v, want the timeout", completed, err)
			}
			if looked := testCase.look.calls > 0; looked != testCase.wantLooks {
				t.Fatalf("looked %d times, want looks = %v", testCase.look.calls, testCase.wantLooks)
			}
		})
	}
}

// Verifies a fresh compile stopped by a dialog keeps its request, so the next uloop compile reattaches
// to the compile still running as the guidance says, and that other failures keep nothing.
func TestCompileStoppedByADialogKeepsTheRequestForTheNextCompile(t *testing.T) {
	projectRoot := t.TempDir()
	persistCompilePendingRecordWhenBlockedByDialog(projectRoot, "compile_other", errors.New("other failure"), io.Discard)
	if _, found := readCompilePendingRecord(projectRoot); found {
		t.Fatal("another failure must not keep the request")
	}

	blocked := startedCommandBlockedByDialog(clicore.CompileCommandName, []statusDialog{commandDialogPlayerStoppedDialog()}, true, true)
	persistCompilePendingRecordWhenBlockedByDialog(projectRoot, "compile_dialog", blocked, io.Discard)

	record, found := readCompilePendingRecord(projectRoot)
	if !found || record.RequestID != "compile_dialog" {
		t.Fatalf("record = %#v, found = %v, want the stopped compile's request", record, found)
	}
}

// --- control-play-mode ---

// Verifies the play mode wait stops with BLOCKED_BY_DIALOG and points at --action Status.
func TestControlPlayModeWaitStopsWhenADialogHoldsUnity(t *testing.T) {
	connection := unansweredIPCConnection(t)
	exitWatch := clicore.NewUnityExitWatch(connection.ProjectRoot, connection.Endpoint.Address, runningUnityProcessLookup, time.Now)

	_, completed, err := waitForControlPlayModeState(context.Background(), connection, "Play", 5*time.Second, exitWatch,
		newFakeDialogLook(commandDialogPlayerStoppedDialog()).deps())

	if completed {
		t.Fatal("the wait must not complete")
	}
	assertStartedBlockedByDialog(t, classifyStartedDialogError(t, err, controlPlayModeCommandName), controlPlayModeDialogClosedNextAction, true)
}

// --- run-tests ---

// Verifies the run-tests result wait stops with BLOCKED_BY_DIALOG and does not offer a rerun, which
// would start a second suite.
func TestRunTestsWaitStopsWhenADialogHoldsUnity(t *testing.T) {
	unanswered := func(context.Context, unityipc.Connection, string) (runTestsStatusResponse, error) {
		return runTestsStatusResponse{}, errors.New("status query timed out")
	}

	_, completed, err := waitForRunTestsResult(context.Background(), compileWaitTestConnection(t), "run_tests_dialog",
		5*time.Second, time.Millisecond, unanswered, newFakeDialogLook(commandDialogPlayerStoppedDialog()).deps())

	if completed {
		t.Fatal("the wait must not complete")
	}
	assertStartedBlockedByDialog(t, classifyStartedDialogError(t, err, clicore.RunTestsCommandName), startedCommandDialogClosedNextAction, false)
}

// --- pause point ---

// Verifies the pause-point wait stops with BLOCKED_BY_DIALOG named after the command that waited.
func TestPausePointWaitStopsWhenADialogHoldsUnity(t *testing.T) {
	useUnityProcessLookup(t, runningUnityProcessLookup)
	options := waitForPausePointOptions{
		id:          "pause_point_dialog",
		timeout:     5 * time.Second,
		dialogWatch: newFakeDialogLook(commandDialogPlayerStoppedDialog()).deps(),
	}

	_, _, _, _, err := waitForPausePointStatus(context.Background(), unansweredIPCConnection(t), options, nil, 0, false, true)

	assertStartedBlockedByDialog(t, classifyStartedDialogError(t, err, clicore.PausePointAwaitCommandName), startedCommandDialogClosedNextAction, false)
	if cliError := clierrors.ClassifyError(err, clierrors.ErrorContext{}); !strings.Contains(cliError.Message, "uloop "+clicore.PausePointAwaitCommandName+" stopped waiting") {
		t.Fatalf("Message = %q, want it to name %s", cliError.Message, clicore.PausePointAwaitCommandName)
	}
}

// Verifies the --trigger command's send looks for a dialog at the pause-point interval, because a
// pause point holds the main thread on purpose while the trigger runs.
func TestPausePointTriggerLooksAtThePausePointInterval(t *testing.T) {
	var interval time.Duration
	original := dispatchPausePointTriggerCommand
	dispatchPausePointTriggerCommand = func(ctx context.Context, _ unityipc.Connection, _ string, _ []string, _ string, _ io.Writer, _ io.Writer) int {
		interval = heartbeatDialogInterval(ctx)
		return 0
	}
	t.Cleanup(func() { dispatchPausePointTriggerCommand = original })

	runPausePointTriggerSync(context.Background(), unansweredIPCConnection(t), "", "get-logs", nil, "get-logs")

	if interval != pausePointDialogWatchInterval {
		t.Fatalf("interval = %v, want %v", interval, pausePointDialogWatchInterval)
	}
	if heartbeatDialogInterval(context.Background()) != dialogWatchInterval {
		t.Fatal("other sends must keep the ordinary interval")
	}
}

// --- hot-reload ---

// Verifies hot-reload's settle wait also stops on an Editor older than Unity 6000.7, which reports no
// dialogs: the reported main-thread stall leads to a look at Unity's windows.
func TestRunHotReloadStopsTheSettleWaitWhenAnOlderEditorStalls(t *testing.T) {
	look := newFakeDialogLook(commandDialogPlayerStoppedDialog())
	options := fastEditorReadyWaitOptions()
	options.dialogWatch = look.deps()
	useFastEditorReadyWait(t, options)
	stalledWithoutDialogs := `{"IsBusy":false,"HasEditorState":true,"IsCompiling":false,"IsUpdating":false,` +
		`"SecondsSinceLastMainThreadTick":12}`

	run := runScriptedHotReload(t, context.Background(), t.TempDir(), map[string]any{},
		[]scriptedIPCStep{
			{method: hotReloadCommandName, result: editorReadyRetryFirstRefused},
			{method: editorStatusBridgeCommandName, result: stalledWithoutDialogs},
		},
		editorReadyRetryNoCompile())

	assertSettleWaitStoppedOnDialog(t, run)
	_ = assertBlockedByDialogError(t, run.stderr, []statusDialog{commandDialogPlayerStoppedDialog()})
	if look.calls != 1 {
		t.Fatalf("looked %d times, want 1", look.calls)
	}
}

// --- the response wait (heartbeats) ---

type heartbeatScript struct {
	stallSeconds float64
	beats        int
	// finish answers the request after the heartbeats; otherwise the server waits for the client to
	// hang up.
	finish bool
}

// serveHeartbeats accepts one request, accepts it, and sends heartbeats reporting stallSeconds.
func serveHeartbeats(t *testing.T, script heartbeatScript) unityipc.Connection {
	t.Helper()
	listener, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatalf("listen: %v", err)
	}
	t.Cleanup(func() { _ = listener.Close() })
	go func() {
		conn, acceptErr := listener.Accept()
		if acceptErr != nil {
			return
		}
		defer func() { _ = conn.Close() }()
		if _, readErr := readScriptedIPCRequest(conn); readErr != nil {
			return
		}
		frames := []string{`{"jsonrpc":"2.0","result":{"accepted":true},"uloop":{"phase":"accepted","heartbeatIntervalSeconds":1},"id":1}`}
		for range script.beats {
			frames = append(frames, fmt.Sprintf(
				`{"jsonrpc":"2.0","id":1,"result":{"alive":true},"uloop":{"phase":"heartbeat","mainThreadStallSeconds":%g}}`, script.stallSeconds))
		}
		if script.finish {
			frames = append(frames, `{"jsonrpc":"2.0","result":{"Success":true},"id":1}`)
		}
		for _, frame := range frames {
			if writeErr := unityipc.Write(conn, []byte(frame)); writeErr != nil {
				return
			}
		}
		_, _ = io.Copy(io.Discard, conn)
	}()
	return unityipc.Connection{
		Endpoint:    unityipc.Endpoint{Network: "tcp", Address: listener.Addr().String()},
		ProjectRoot: t.TempDir(),
	}
}

func heartbeatDialogRetryDeps(look *fakeDialogLook) connectionRetryDeps {
	deps := defaultConnectionRetryDeps()
	deps.findRunningUnityProcess = runningUnityProcessLookup
	deps.focusUnityProcess = func(context.Context, int) (clicore.RestoreFocusFunc, error) {
		return nil, errors.New("focus is not part of this test")
	}
	deps.dialogWatch = look.deps()
	return deps
}

func sendWithHeartbeats(t *testing.T, ctx context.Context, method string, script heartbeatScript, look *fakeDialogLook) (unityipc.UnitySendOutcome, error) {
	t.Helper()
	ctx, cancel := context.WithTimeout(ctx, 10*time.Second)
	defer cancel()
	return sendWithTransientConnectionRetryWithDeps(ctx, serveHeartbeats(t, script), method, map[string]any{}, nil, 0, heartbeatDialogRetryDeps(look))
}

// Verifies a request whose heartbeats report a stall past Unity's 30-second answer stops with the
// dialog, says the command had started without promising its result, and is not read as a lost
// connection by the waits that recover from one.
func TestSendStopsWaitingForTheResponseWhenADialogHoldsUnity(t *testing.T) {
	look := newFakeDialogLook(commandDialogPlayerStoppedDialog())

	outcome, err := sendWithHeartbeats(t, context.Background(), "get-logs", heartbeatScript{stallSeconds: 36, beats: 50}, look)

	cliError := classifyStartedDialogError(t, err, "get-logs")
	wantMessage := blockedByDialogMessage([]statusDialog{commandDialogPlayerStoppedDialog()}) + " " + heartbeatStoppedSentence("get-logs")
	unresponsive := clierrors.ClassifyError(&unityipc.EditorUnresponsiveError{}, clierrors.ErrorContext{Command: "get-logs"})
	if cliError.Message != wantMessage || cliError.SafeToRetry != unresponsive.SafeToRetry {
		t.Fatalf("Message = %q, SafeToRetry = %v, want %q, %v", cliError.Message, cliError.SafeToRetry, wantMessage, unresponsive.SafeToRetry)
	}
	if last := cliError.NextActions[len(cliError.NextActions)-1]; last != startedCommandDialogClosedNextAction {
		t.Fatalf("last NextAction = %q", last)
	}
	if !outcome.RequestAccepted {
		t.Fatal("the request was accepted before the stall")
	}
	if shouldWaitForCompileStatus(err, outcome) || shouldWaitForExecuteDynamicCodeDisconnect(err, outcome) ||
		shouldWaitForControlPlayModeDisconnect(err, outcome) {
		t.Fatalf("the dialog error must not start a disconnect wait: %v", err)
	}
	var buffer bytes.Buffer
	clierrors.WriteToolFailure(&buffer, err, outcome, clierrors.ErrorContext{Command: "get-logs"})
	var envelope struct {
		Error clierrors.CLIError `json:"error"`
	}
	if decodeErr := json.Unmarshal(buffer.Bytes(), &envelope); decodeErr != nil || envelope.Error.ErrorCode != clierrors.ErrorCodeBlockedByDialog {
		t.Fatalf("tool failure output = %s, want BLOCKED_BY_DIALOG", buffer.String())
	}
}

// Verifies the response wait keeps going without a look below the threshold, without a dialog (as an
// execute-dynamic-code snippet that holds the main thread itself does), and for internal bridge
// requests, which Unity never answers at 30 seconds.
func TestSendKeepsWaitingForTheResponseWithoutADialog(t *testing.T) {
	cases := []struct {
		name      string
		method    string
		stall     float64
		look      *fakeDialogLook
		wantLooks bool
	}{
		{name: "stall within Unity's 30-second answer", method: "get-logs", stall: 34, look: newFakeDialogLook(commandDialogPlayerStoppedDialog())},
		{name: "execute-dynamic-code holding the main thread", method: clicore.ExecuteDynamicCodeCommandName, stall: 120, look: newFakeDialogLook(), wantLooks: true},
		{name: "internal bridge request", method: "get-compile-status", stall: 120, look: newFakeDialogLook(commandDialogPlayerStoppedDialog())},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			outcome, err := sendWithHeartbeats(t, context.Background(), testCase.method,
				heartbeatScript{stallSeconds: testCase.stall, beats: 5, finish: true}, testCase.look)

			if err != nil || !strings.Contains(string(outcome.Result), "Success") {
				t.Fatalf("err = %v, result = %s, want the answer", err, outcome.Result)
			}
			if looked := testCase.look.calls > 0; looked != testCase.wantLooks {
				t.Fatalf("looked %d times, want looks = %v", testCase.look.calls, testCase.wantLooks)
			}
		})
	}
}

// Verifies the response wait looks at most once per interval, and at the pause-point interval for a
// send made with it.
func TestSendLooksAtMostOncePerIntervalWhileWaitingForTheResponse(t *testing.T) {
	cases := []struct {
		name      string
		ctx       context.Context
		wantLooks int
	}{
		// 20 heartbeats on a clock that moves a second per reading span 20 seconds.
		{name: "ordinary", ctx: context.Background(), wantLooks: 2},
		{name: "pause-point trigger", ctx: withHeartbeatDialogInterval(context.Background(), pausePointDialogWatchInterval), wantLooks: 1},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			look := newFakeDialogLook()

			_, err := sendWithHeartbeats(t, testCase.ctx, "get-logs", heartbeatScript{stallSeconds: 36, beats: 20, finish: true}, look)
			if err != nil {
				t.Fatalf("err = %v", err)
			}
			if look.calls != testCase.wantLooks {
				t.Fatalf("looked %d times, want %d", look.calls, testCase.wantLooks)
			}
		})
	}
}

// Verifies a send that ends after the five-minute stall is explained by a dialog with the neutral
// sentence, which a request the response wait does not watch reaches.
func TestSendExplainsTheFiveMinuteStallByADialog(t *testing.T) {
	look := newFakeDialogLook(commandDialogPlayerStoppedDialog())

	_, err := sendWithHeartbeats(t, context.Background(), "get-compile-status", heartbeatScript{stallSeconds: 301, beats: 1}, look)

	cliError := clierrors.ClassifyError(err, clierrors.ErrorContext{Command: "get-compile-status"})
	if cliError.ErrorCode != clierrors.ErrorCodeBlockedByDialog || !strings.HasSuffix(cliError.Message, "uloop get-compile-status stopped waiting.") {
		t.Fatalf("ErrorCode = %q, Message = %q, want BLOCKED_BY_DIALOG with the neutral sentence", cliError.ErrorCode, cliError.Message)
	}
}

// unansweredIPCConnection is a server that hangs up on every request without an answer, the way a
// poll looks while Unity's main thread is held.
func unansweredIPCConnection(t *testing.T) unityipc.Connection {
	t.Helper()
	listener, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatalf("listen: %v", err)
	}
	t.Cleanup(func() { _ = listener.Close() })
	go func() {
		for {
			conn, acceptErr := listener.Accept()
			if acceptErr != nil {
				return
			}
			_ = conn.Close()
		}
	}()
	return unityipc.Connection{
		Endpoint:    unityipc.Endpoint{Network: "tcp", Address: listener.Addr().String()},
		ProjectRoot: t.TempDir(),
	}
}
