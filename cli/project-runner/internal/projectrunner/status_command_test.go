package projectrunner

import (
	"bufio"
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"net"
	"os"
	"reflect"
	"sort"
	"strings"
	"testing"
	"time"

	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

const statusTestAcceptedResponse = `{"jsonrpc":"2.0","id":1,"uloop":{"phase":"accepted"}}`

// Report fields the Editor's answer adds once it has recorded its play and compile state.
var statusTestEditorStateFields = []string{
	"IsCompiling", "IsPaused", "IsPlaying", "IsUpdating", "SecondsSinceLastMainThreadTick",
}

var statusTestRunningToolFields = []string{
	"RunningToolElapsedSeconds", "RunningToolName", "RunningToolPhase",
}

// Verifies status rejects any argument other than the global ones before it contacts Unity.
func TestRunStatusCommandRejectsExtraArguments(t *testing.T) {
	projectRoot := writeFakeUnityProject(t)
	server := startFakeUnityResultServer(t, projectRoot, editorStatusBridgeCommandName, editorStatusResultJSON(t, nil))

	run := runStatusForTest(context.Background(), server.connection, []string{"--bogus"}, statusTestDeps(t))

	assertStatusError(t, run, `"Message": "status takes no options: --bogus"`)
	if !strings.Contains(run.stderr, `"ErrorCode": "INVALID_ARGUMENT"`) {
		t.Fatalf("stderr must be an argument error:\n%s", run.stderr)
	}
	assertStatusRequestCount(t, server, 0)
}

// Verifies an idle Editor with its state recorded is Ready with exit code 0 and an empty
// NextActions array, and that no connection or process fields are printed.
func TestRunStatusCommandReportsReady(t *testing.T) {
	projectRoot := writeFakeUnityProject(t)
	server := startFakeUnityResultServer(t, projectRoot, editorStatusBridgeCommandName, editorStatusResultJSON(t, nil))

	run := runStatusForTest(context.Background(), server.connection, nil, statusTestDeps(t))

	report := assertStatusReport(t, run, "Ready")
	assertStatusFields(t, report, statusTestEditorStateFields...)
	assertStatusMessage(t, report, "Unity is idle and can take a command now.")
	if !strings.Contains(run.stdout, `"NextActions": []`) {
		t.Fatalf("Ready must print NextActions as an empty array, not null:\n%s", run.stdout)
	}
	assertStatusValue(t, report, "SecondsSinceLastMainThreadTick", 0.1)
	assertStatusRequestCount(t, server, 1)
}

// Verifies a paused Play Mode session does not change the state: it stays Ready and only the
// play values say Unity is playing and paused.
func TestRunStatusCommandReportsReadyWhilePausedInPlayMode(t *testing.T) {
	projectRoot := writeFakeUnityProject(t)
	server := startFakeUnityResultServer(t, projectRoot, editorStatusBridgeCommandName, editorStatusResultJSON(t, map[string]any{
		"IsPlaying": true,
		"IsPaused":  true,
	}))

	run := runStatusForTest(context.Background(), server.connection, nil, statusTestDeps(t))

	report := assertStatusReport(t, run, "Ready")
	assertStatusFields(t, report, statusTestEditorStateFields...)
	assertStatusValue(t, report, "IsPlaying", true)
	assertStatusValue(t, report, "IsPaused", true)
	assertStatusValue(t, report, "IsCompiling", false)
	assertStatusValue(t, report, "IsUpdating", false)
	assertStatusRequestCount(t, server, 1)
}

// Verifies a command holding the execution slot is reported as Busy even when the main thread
// has not run for 30 seconds, because the running command explains the stall; no second probe
// is sent.
func TestRunStatusCommandReportsBusyBeforeMainThreadBlocked(t *testing.T) {
	projectRoot := writeFakeUnityProject(t)
	server := startFakeUnityResultServer(t, projectRoot, editorStatusBridgeCommandName, editorStatusResultJSON(t, map[string]any{
		"IsBusy":                         true,
		"RunningToolName":                "run-tests",
		"RunningToolElapsedSeconds":      12,
		"RunningToolPhase":               "Executing",
		"SecondsSinceLastMainThreadTick": 30,
	}))

	run := runStatusForTest(context.Background(), server.connection, nil, statusTestDeps(t))

	report := assertStatusReport(t, run, "Busy")
	assertStatusFields(t, report, append(append([]string{}, statusTestEditorStateFields...), statusTestRunningToolFields...)...)
	assertStatusMessage(t, report, "uloop run-tests has been running for 12s (Executing).")
	assertStatusValue(t, report, "RunningToolName", "run-tests")
	assertStatusValue(t, report, "RunningToolElapsedSeconds", 12.0)
	assertStatusValue(t, report, "RunningToolPhase", "Executing")
	assertStatusRequestCount(t, server, 1)
}

// Verifies Busy is decided before the missing Editor state, and that the play and compile
// values are left out when the Editor has not recorded them.
func TestRunStatusCommandReportsBusyBeforeStarting(t *testing.T) {
	projectRoot := writeFakeUnityProject(t)
	server := startFakeUnityResultServer(t, projectRoot, editorStatusBridgeCommandName, editorStatusResultJSON(t, map[string]any{
		"IsBusy":                         true,
		"RunningToolName":                "compile",
		"RunningToolElapsedSeconds":      2,
		"RunningToolPhase":               "WaitingForMainThread",
		"HasEditorState":                 false,
		"SecondsSinceLastMainThreadTick": 0,
	}))

	run := runStatusForTest(context.Background(), server.connection, nil, statusTestDeps(t))

	report := assertStatusReport(t, run, "Busy")
	assertStatusFields(t, report, append([]string{"SecondsSinceLastMainThreadTick"}, statusTestRunningToolFields...)...)
	assertStatusMessage(t, report, "uloop compile has been running for 2s (WaitingForMainThread).")
	assertStatusRequestCount(t, server, 1)
}

// Verifies an Editor that answered before recording its play and compile state is Starting,
// without the play and compile values.
func TestRunStatusCommandReportsStartingWithoutEditorState(t *testing.T) {
	projectRoot := writeFakeUnityProject(t)
	server := startFakeUnityResultServer(t, projectRoot, editorStatusBridgeCommandName, editorStatusResultJSON(t, map[string]any{
		"HasEditorState":                 false,
		"SecondsSinceLastMainThreadTick": 0,
	}))

	run := runStatusForTest(context.Background(), server.connection, nil, statusTestDeps(t))

	report := assertStatusReport(t, run, "Starting")
	assertStatusFields(t, report, "SecondsSinceLastMainThreadTick")
	assertStatusMessage(t, report, "The uloop server answered, but Unity has not reported its Editor state yet.")
	assertStatusRequestCount(t, server, 1)
}

// Verifies the missing Editor state is decided before a long main-thread stall, so Starting
// wins and no second probe is sent.
func TestRunStatusCommandReportsStartingBeforeMainThreadBlocked(t *testing.T) {
	projectRoot := writeFakeUnityProject(t)
	server := startFakeUnityResultServer(t, projectRoot, editorStatusBridgeCommandName, editorStatusResultJSON(t, map[string]any{
		"HasEditorState":                 false,
		"SecondsSinceLastMainThreadTick": 30,
	}))

	run := runStatusForTest(context.Background(), server.connection, nil, statusTestDeps(t))

	report := assertStatusReport(t, run, "Starting")
	assertStatusFields(t, report, "SecondsSinceLastMainThreadTick")
	assertStatusRequestCount(t, server, 1)
}

// Verifies a stall of exactly the threshold sends a second probe, and a second answer still at
// or over it is MainThreadBlocked ahead of Compiling, reported with the second answer's seconds.
func TestRunStatusCommandReportsMainThreadBlockedBeforeCompiling(t *testing.T) {
	projectRoot := writeFakeUnityProject(t)
	server := startFakeEditorStatusSequenceServer(t, projectRoot,
		editorStatusRPCResponse(editorStatusResultJSON(t, map[string]any{"SecondsSinceLastMainThreadTick": 5.0, "IsCompiling": true})),
		editorStatusRPCResponse(editorStatusResultJSON(t, map[string]any{"SecondsSinceLastMainThreadTick": 6.0, "IsCompiling": true})),
	)

	run := runStatusForTest(context.Background(), server.connection, nil, statusTestDeps(t))

	report := assertStatusReport(t, run, "MainThreadBlocked")
	assertStatusFields(t, report, statusTestEditorStateFields...)
	assertStatusMessage(t, report, "Unity's main thread has not run for 6.0s; a dialog, a long import, or running code is blocking it.")
	assertStatusValue(t, report, "SecondsSinceLastMainThreadTick", 6.0)
	assertStatusRequestCount(t, server, 2)
}

// Verifies an Editor whose loop was only sleeping is Ready: the first answer looks blocked, and
// the second answer after the wake-up shows the loop running again.
func TestRunStatusCommandReportsReadyWhenSecondProbeShowsTheLoopWoke(t *testing.T) {
	projectRoot := writeFakeUnityProject(t)
	server := startFakeEditorStatusSequenceServer(t, projectRoot,
		editorStatusRPCResponse(editorStatusResultJSON(t, map[string]any{"SecondsSinceLastMainThreadTick": 30})),
		editorStatusRPCResponse(editorStatusResultJSON(t, map[string]any{"SecondsSinceLastMainThreadTick": 0.2})),
	)

	run := runStatusForTest(context.Background(), server.connection, nil, statusTestDeps(t))

	report := assertStatusReport(t, run, "Ready")
	assertStatusFields(t, report, statusTestEditorStateFields...)
	assertStatusValue(t, report, "SecondsSinceLastMainThreadTick", 0.2)
	assertStatusRequestCount(t, server, 2)
}

// Verifies the second answer is classified from all of its values, not only its stall seconds.
func TestRunStatusCommandClassifiesFromSecondProbe(t *testing.T) {
	projectRoot := writeFakeUnityProject(t)
	server := startFakeEditorStatusSequenceServer(t, projectRoot,
		editorStatusRPCResponse(editorStatusResultJSON(t, map[string]any{"SecondsSinceLastMainThreadTick": 30})),
		editorStatusRPCResponse(editorStatusResultJSON(t, map[string]any{"SecondsSinceLastMainThreadTick": 0.2, "IsCompiling": true})),
	)

	run := runStatusForTest(context.Background(), server.connection, nil, statusTestDeps(t))

	report := assertStatusReport(t, run, "Compiling")
	assertStatusFields(t, report, statusTestEditorStateFields...)
	assertStatusValue(t, report, "IsCompiling", true)
	assertStatusRequestCount(t, server, 2)
}

// Verifies a second probe that loses its connection is judged like a first probe would be:
// the Unity process is looked up and the state becomes ServerUnavailable.
func TestRunStatusCommandClassifiesSecondProbeFailureLikeFirst(t *testing.T) {
	projectRoot := writeFakeUnityProject(t)
	server := startFakeEditorStatusSequenceServer(t, projectRoot,
		editorStatusRPCResponse(editorStatusResultJSON(t, map[string]any{"SecondsSinceLastMainThreadTick": 30})),
		"",
	)
	deps := statusTestDeps(t)
	deps.isUnityProcessRunning = fakeUnityProcessLookup(t, projectRoot, true, nil)

	run := runStatusForTest(context.Background(), server.connection, nil, deps)

	report := assertStatusReport(t, run, "ServerUnavailable")
	assertStatusFields(t, report, "ConnectionError", "UnityProcessRunning")
	assertStatusRequestCount(t, server, 1)
}

// Verifies a caller that cancels while status waits to ask again gets an error at once, and the
// second probe is never sent.
func TestRunStatusCommandStopsWhenCancelledBeforeSecondProbe(t *testing.T) {
	projectRoot := writeFakeUnityProject(t)
	server := startFakeEditorStatusSequenceServer(t, projectRoot,
		editorStatusRPCResponse(editorStatusResultJSON(t, map[string]any{"SecondsSinceLastMainThreadTick": 30})),
	)
	deps := statusTestDeps(t)
	deps.reprobeDelay = time.Hour
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	runs := make(chan statusRun, 1)

	go func() {
		runs <- runStatusForTest(ctx, server.connection, nil, deps)
	}()
	server.receivedRequest(t)
	cancel()

	select {
	case run := <-runs:
		assertStatusError(t, run, "context canceled")
	case <-time.After(5 * time.Second):
		t.Fatal("status did not stop within 5s after its context was cancelled")
	}
	select {
	case <-server.requests:
		t.Fatal("status sent a second probe after its context was cancelled")
	default:
	}
	assertServerDidNotFail(t, server.serverErr)
}

// Verifies a stall just under the threshold is Ready without a second probe.
func TestRunStatusCommandTreatsShortStallAsReady(t *testing.T) {
	projectRoot := writeFakeUnityProject(t)
	server := startFakeUnityResultServer(t, projectRoot, editorStatusBridgeCommandName, editorStatusResultJSON(t, map[string]any{
		"SecondsSinceLastMainThreadTick": 4.9,
	}))

	run := runStatusForTest(context.Background(), server.connection, nil, statusTestDeps(t))

	report := assertStatusReport(t, run, "Ready")
	assertStatusFields(t, report, statusTestEditorStateFields...)
	assertStatusRequestCount(t, server, 1)
}

// Verifies compiling is decided before importing when Unity reports both.
func TestRunStatusCommandReportsCompilingBeforeImporting(t *testing.T) {
	projectRoot := writeFakeUnityProject(t)
	server := startFakeUnityResultServer(t, projectRoot, editorStatusBridgeCommandName, editorStatusResultJSON(t, map[string]any{
		"IsCompiling": true,
		"IsUpdating":  true,
	}))

	run := runStatusForTest(context.Background(), server.connection, nil, statusTestDeps(t))

	report := assertStatusReport(t, run, "Compiling")
	assertStatusFields(t, report, statusTestEditorStateFields...)
	assertStatusMessage(t, report, "Unity is compiling scripts; a domain reload follows.")
	assertStatusRequestCount(t, server, 1)
}

// Verifies an asset import without a compile is ImportingAssets.
func TestRunStatusCommandReportsImportingAssets(t *testing.T) {
	projectRoot := writeFakeUnityProject(t)
	server := startFakeUnityResultServer(t, projectRoot, editorStatusBridgeCommandName, editorStatusResultJSON(t, map[string]any{
		"IsUpdating": true,
	}))

	run := runStatusForTest(context.Background(), server.connection, nil, statusTestDeps(t))

	report := assertStatusReport(t, run, "ImportingAssets")
	assertStatusFields(t, report, statusTestEditorStateFields...)
	assertStatusMessage(t, report, "Unity is importing assets.")
	assertStatusRequestCount(t, server, 1)
}

// Verifies an answer that is not a status object is an error, not a guessed state.
func TestRunStatusCommandFailsOnMalformedResponse(t *testing.T) {
	projectRoot := writeFakeUnityProject(t)
	server := startFakeUnityResultServer(t, projectRoot, editorStatusBridgeCommandName, `"not an object"`)

	run := runStatusForTest(context.Background(), server.connection, nil, statusTestDeps(t))

	assertStatusError(t, run, "unexpected get-editor-status response")
	assertStatusRequestCount(t, server, 1)
}

// Verifies a busy answer without the elapsed seconds is an error, since the Editor always sends
// them while a command holds the slot and the Busy message needs them.
func TestRunStatusCommandFailsOnBusyResponseWithoutElapsedSeconds(t *testing.T) {
	projectRoot := writeFakeUnityProject(t)
	server := startFakeUnityResultServer(t, projectRoot, editorStatusBridgeCommandName, editorStatusResultJSON(t, map[string]any{
		"IsBusy":           true,
		"RunningToolName":  "run-tests",
		"RunningToolPhase": "Executing",
	}))

	run := runStatusForTest(context.Background(), server.connection, nil, statusTestDeps(t))

	assertStatusError(t, run, "unexpected get-editor-status response")
	assertStatusRequestCount(t, server, 1)
}

// Verifies a reply whose framing cannot be read is an error and not a lost connection.
func TestRunStatusCommandFailsOnInvalidFraming(t *testing.T) {
	projectRoot := writeFakeUnityProject(t)
	server := startHeldEditorStatusServer(t, projectRoot, []byte("X-Not-Content-Length: 1\r\n\r\n"), true)

	run := runStatusForTest(context.Background(), server.connection, nil, statusTestDeps(t))

	assertStatusError(t, run, "Content-Length header was not found")
	assertStatusRequestCount(t, server, 1)
}

// Verifies an Editor that answers get-editor-status with a JSON-RPC error, as a package that
// predates the command does, makes status fail with the Editor's message.
func TestRunStatusCommandFailsOnRPCError(t *testing.T) {
	projectRoot := writeFakeUnityProject(t)
	server := startFakeUnityServer(t, projectRoot, editorStatusBridgeCommandName,
		`{"jsonrpc":"2.0","error":{"code":-32603,"message":"Unknown tool: get-editor-status"},"id":1}`)

	run := runStatusForTest(context.Background(), server.connection, nil, statusTestDeps(t))

	assertStatusError(t, run, "Unknown tool: get-editor-status")
	assertStatusRequestCount(t, server, 1)
}

// Verifies an Editor error whose message happens to contain EOF stays an error instead of being
// mistaken for a dropped connection.
func TestRunStatusCommandFailsOnRPCErrorMentioningEOF(t *testing.T) {
	projectRoot := writeFakeUnityProject(t)
	server := startFakeUnityServer(t, projectRoot, editorStatusBridgeCommandName,
		`{"jsonrpc":"2.0","error":{"code":-32603,"message":"Unexpected EOF while parsing"},"id":1}`)

	run := runStatusForTest(context.Background(), server.connection, nil, statusTestDeps(t))

	assertStatusError(t, run, "Unexpected EOF while parsing")
	assertStatusRequestCount(t, server, 1)
}

// Verifies a refused connection while this project's Unity process runs is ServerUnavailable,
// with the process flag and the connection error shown.
func TestRunStatusCommandReportsServerUnavailableWhenProcessRuns(t *testing.T) {
	projectRoot := writeFakeUnityProject(t)
	deps := statusTestDeps(t)
	deps.isUnityProcessRunning = fakeUnityProcessLookup(t, projectRoot, true, nil)

	run := runStatusForTest(context.Background(), unreachableConnection(projectRoot), nil, deps)

	report := assertStatusReport(t, run, "ServerUnavailable")
	assertStatusFields(t, report, "ConnectionError", "UnityProcessRunning")
	assertStatusMessage(t, report, "Unity is running for this project, but its uloop server is not accepting connections. "+
		"Unity is starting, reloading scripts, or was started in Safe Mode because of compile errors (uloop cannot run in Safe Mode).")
	assertStatusValue(t, report, "UnityProcessRunning", true)
	assertStatusConnectionError(t, report, "")
}

// Verifies a refused connection with no Unity process for this project is NotRunning.
func TestRunStatusCommandReportsNotRunning(t *testing.T) {
	projectRoot := writeFakeUnityProject(t)
	deps := statusTestDeps(t)
	deps.isUnityProcessRunning = fakeUnityProcessLookup(t, projectRoot, false, nil)

	run := runStatusForTest(context.Background(), unreachableConnection(projectRoot), nil, deps)

	report := assertStatusReport(t, run, "NotRunning")
	assertStatusFields(t, report, "ConnectionError", "UnityProcessRunning")
	assertStatusMessage(t, report, "No Unity Editor is running for this project.")
	assertStatusValue(t, report, "UnityProcessRunning", false)
	assertStatusConnectionError(t, report, "")
}

// Verifies a refused connection whose process lookup fails is Unreachable, naming the lookup
// error and leaving out the process flag it could not determine.
func TestRunStatusCommandReportsUnreachableWhenProcessLookupFails(t *testing.T) {
	projectRoot := writeFakeUnityProject(t)
	deps := statusTestDeps(t)
	deps.isUnityProcessRunning = fakeUnityProcessLookup(t, projectRoot, false, errors.New("ps failed"))

	run := runStatusForTest(context.Background(), unreachableConnection(projectRoot), nil, deps)

	report := assertStatusReport(t, run, "Unreachable")
	assertStatusFields(t, report, "ConnectionError")
	assertStatusMessage(t, report, "The uloop server is not accepting connections, and the Unity process list could not be read: ps failed.")
	assertStatusConnectionError(t, report, "")
}

// Verifies a connection the operating system refused, as a sandbox does, is returned as an
// error before the refused-connection path, so status never reports Unity as closed for it.
func TestReportFromProbeErrorTreatsPermissionDeniedAsError(t *testing.T) {
	probeErr := &unityipc.ConnectionAttemptError{Cause: os.ErrPermission}

	report, err := reportFromProbeError(context.Background(), writeFakeUnityProject(t), probeErr, statusTestDeps(t))

	if !errors.Is(err, os.ErrPermission) {
		t.Fatalf("err = %v, want the permission denial", err)
	}
	if !reflect.DeepEqual(report, statusReport{}) {
		t.Fatalf("no state may be reported for a refused connection: %#v", report)
	}
}

// Verifies a connection that never answers before the probe timeout is NotResponding.
func TestRunStatusCommandReportsNotRespondingOnTimeout(t *testing.T) {
	projectRoot := writeFakeUnityProject(t)
	server := startHeldEditorStatusServer(t, projectRoot, nil, true)
	deps := statusTestDeps(t)
	deps.probeTimeout = 200 * time.Millisecond

	run := runStatusForTest(context.Background(), server.connection, nil, deps)

	report := assertStatusReport(t, run, "NotResponding")
	assertStatusFields(t, report, "ConnectionError")
	assertStatusMessage(t, report, "Connected to Unity, but it did not answer within 0.2s.")
	assertStatusConnectionError(t, report, "")
}

// Verifies an Editor that accepted the request but sent no answer before the probe timeout is
// NotResponding too; this is how a real unanswered request ends.
func TestRunStatusCommandReportsNotRespondingAfterAccepted(t *testing.T) {
	projectRoot := writeFakeUnityProject(t)
	server := startHeldEditorStatusServer(t, projectRoot, ipcFrame(t, statusTestAcceptedResponse), true)
	deps := statusTestDeps(t)
	deps.probeTimeout = 200 * time.Millisecond

	run := runStatusForTest(context.Background(), server.connection, nil, deps)

	report := assertStatusReport(t, run, "NotResponding")
	assertStatusFields(t, report, "ConnectionError")
	assertStatusConnectionError(t, report, "context deadline exceeded")
}

// Verifies a connection the server closes before answering counts as no server, so the process
// lookup decides the state.
func TestRunStatusCommandTreatsDisconnectAsServerUnavailable(t *testing.T) {
	projectRoot := writeFakeUnityProject(t)
	server := startHeldEditorStatusServer(t, projectRoot, nil, false)
	deps := statusTestDeps(t)
	deps.isUnityProcessRunning = fakeUnityProcessLookup(t, projectRoot, true, nil)

	run := runStatusForTest(context.Background(), server.connection, nil, deps)

	report := assertStatusReport(t, run, "ServerUnavailable")
	assertStatusFields(t, report, "ConnectionError", "UnityProcessRunning")
	assertStatusConnectionError(t, report, "")
}

// Verifies a connection closed after the request was accepted, as when a domain reload starts,
// also counts as no server.
func TestRunStatusCommandTreatsDisconnectAfterAcceptedAsServerUnavailable(t *testing.T) {
	projectRoot := writeFakeUnityProject(t)
	server := startHeldEditorStatusServer(t, projectRoot, ipcFrame(t, statusTestAcceptedResponse), false)
	deps := statusTestDeps(t)
	deps.isUnityProcessRunning = fakeUnityProcessLookup(t, projectRoot, true, nil)

	run := runStatusForTest(context.Background(), server.connection, nil, deps)

	report := assertStatusReport(t, run, "ServerUnavailable")
	assertStatusFields(t, report, "ConnectionError", "UnityProcessRunning")
	assertStatusConnectionError(t, report, "")
}

type statusRun struct {
	code   int
	stdout string
	stderr string
}

func runStatusForTest(ctx context.Context, connection unityipc.Connection, args []string, deps statusCommandDeps) statusRun {
	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runStatusCommandWithDeps(ctx, connection, args, &stdout, &stderr, deps)
	return statusRun{code: code, stdout: stdout.String(), stderr: stderr.String()}
}

// statusTestDeps answers fast: a fake server replies at once, so a 2s probe timeout only runs
// out when a probe nobody expected was sent. The process lookup fails the test unless a test
// replaces it, because most outcomes must not look Unity up.
func statusTestDeps(t *testing.T) statusCommandDeps {
	return statusCommandDeps{
		probeTimeout: 2 * time.Second,
		reprobeDelay: 0,
		isUnityProcessRunning: func(context.Context, string) (bool, error) {
			t.Error("status must not look up the Unity process for this outcome")
			return false, errors.New("unexpected Unity process lookup")
		},
	}
}

func fakeUnityProcessLookup(
	t *testing.T,
	wantProjectRoot string,
	running bool,
	err error,
) func(context.Context, string) (bool, error) {
	return func(_ context.Context, projectRoot string) (bool, error) {
		if projectRoot != wantProjectRoot {
			t.Errorf("process lookup project root = %q, want %q", projectRoot, wantProjectRoot)
		}
		return running, err
	}
}

// editorStatusResultJSON returns a get-editor-status result for an idle Editor that has recorded
// its state, with the given fields replaced. Names are spelled the way the Editor sends them.
func editorStatusResultJSON(t *testing.T, overrides map[string]any) string {
	t.Helper()
	result := map[string]any{
		"Success":                        true,
		"IsBusy":                         false,
		"HasEditorState":                 true,
		"IsPlaying":                      false,
		"IsPaused":                       false,
		"IsCompiling":                    false,
		"IsUpdating":                     false,
		"SecondsSinceLastMainThreadTick": 0.1,
	}
	for name, value := range overrides {
		result[name] = value
	}
	encoded, err := json.Marshal(result)
	if err != nil {
		t.Fatalf("encode get-editor-status result: %v", err)
	}
	return string(encoded)
}

func editorStatusRPCResponse(result string) string {
	return fmt.Sprintf(`{"jsonrpc":"2.0","result":%s,"id":1}`, result)
}

func ipcFrame(t *testing.T, payload string) []byte {
	t.Helper()
	var frame bytes.Buffer
	if err := unityipc.Write(&frame, []byte(payload)); err != nil {
		t.Fatalf("frame IPC payload: %v", err)
	}
	return frame.Bytes()
}

func newFakeEditorStatusServer(listener net.Listener, projectRoot string, capacity int) fakeUnityServer {
	return fakeUnityServer{
		connection: unityipc.Connection{
			Endpoint: unityipc.Endpoint{
				Network: listener.Addr().Network(),
				Address: listener.Addr().String(),
			},
			ProjectRoot: projectRoot,
		},
		requests:  make(chan map[string]any, capacity),
		serverErr: make(chan error, capacity),
	}
}

// startFakeEditorStatusSequenceServer answers one get-editor-status request per connection with
// the given JSON-RPC responses in order. An empty response reads that connection's request and
// closes it unanswered, which the client sees as a dropped connection; that request is not
// counted.
func startFakeEditorStatusSequenceServer(t *testing.T, projectRoot string, responses ...string) fakeUnityServer {
	t.Helper()
	listener := newLoopbackIpcListener(t)
	server := newFakeEditorStatusServer(listener, projectRoot, len(responses))
	go func() {
		for _, response := range responses {
			if response == "" {
				closeAfterReadingRequest(listener, server.serverErr)
				continue
			}
			serveRawIPCResponse(listener, editorStatusBridgeCommandName, server.requests, server.serverErr, response)
		}
	}()
	return server
}

// Why read before closing: closing a socket with unread data sends a reset on TCP and Windows
// pipes, which reaches the client as a connection reset instead of the EOF under test.
func closeAfterReadingRequest(listener net.Listener, serverErr chan<- error) {
	conn, err := listener.Accept()
	if err != nil {
		serverErr <- err
		return
	}
	defer func() { _ = conn.Close() }()
	if _, err := unityipc.Read(bufio.NewReader(conn)); err != nil {
		serverErr <- err
	}
}

// startHeldEditorStatusServer accepts one connection, reads its get-editor-status request, and
// writes reply as raw bytes (nothing when empty). It then closes the connection, or with hold
// keeps it open until the test ends, so the client meets a timeout instead of an EOF.
func startHeldEditorStatusServer(t *testing.T, projectRoot string, reply []byte, hold bool) fakeUnityServer {
	t.Helper()
	listener := newLoopbackIpcListener(t)
	server := newFakeEditorStatusServer(listener, projectRoot, 1)
	testDone := make(chan struct{})
	t.Cleanup(func() { close(testDone) })
	go func() {
		conn, err := listener.Accept()
		if err != nil {
			server.serverErr <- err
			return
		}
		defer func() { _ = conn.Close() }()
		if err := forwardEditorStatusRequest(conn, server.requests); err != nil {
			server.serverErr <- err
			return
		}
		if _, err := conn.Write(reply); err != nil {
			server.serverErr <- err
			return
		}
		if hold {
			<-testDone
		}
	}()
	return server
}

func forwardEditorStatusRequest(conn net.Conn, requests chan<- map[string]any) error {
	payload, err := unityipc.Read(bufio.NewReader(conn))
	if err != nil {
		return err
	}
	request := struct {
		Method string         `json:"method"`
		Params map[string]any `json:"params"`
	}{}
	if err := json.Unmarshal(payload, &request); err != nil {
		return err
	}
	if request.Method != editorStatusBridgeCommandName {
		return fmt.Errorf("method mismatch: %s", request.Method)
	}
	requests <- request.Params
	return nil
}

// assertStatusReport checks that a state was printed rather than an error, that the exit code,
// State, and Ready agree, and that NextActions is an array that is empty exactly when Ready.
func assertStatusReport(t *testing.T, run statusRun, wantState string) map[string]any {
	t.Helper()
	wantReady := wantState == "Ready"
	wantCode := 1
	if wantReady {
		wantCode = 0
	}
	if run.code != wantCode {
		t.Fatalf("exit code = %d, want %d\nstdout=%s\nstderr=%s", run.code, wantCode, run.stdout, run.stderr)
	}
	if run.stderr != "" {
		t.Fatalf("stderr must stay empty when a state is reported:\n%s", run.stderr)
	}
	var report map[string]any
	if err := json.Unmarshal([]byte(run.stdout), &report); err != nil {
		t.Fatalf("stdout is not one JSON object: %v\n%s", err, run.stdout)
	}
	if report["State"] != wantState || report["Ready"] != wantReady {
		t.Fatalf("State/Ready = %v/%v, want %s/%v\n%s", report["State"], report["Ready"], wantState, wantReady, run.stdout)
	}
	nextActions, ok := report["NextActions"].([]any)
	if !ok || wantReady != (len(nextActions) == 0) {
		t.Fatalf("NextActions must be an array that is empty exactly when Ready: %#v", report["NextActions"])
	}
	return report
}

// assertStatusFields checks the report carries exactly the four fields every state has plus the
// given ones, so fields that do not apply to the state stay out of the output.
func assertStatusFields(t *testing.T, report map[string]any, stateFields ...string) {
	t.Helper()
	got := make([]string, 0, len(report))
	for name := range report {
		got = append(got, name)
	}
	want := append([]string{"Message", "NextActions", "Ready", "State"}, stateFields...)
	sort.Strings(got)
	sort.Strings(want)
	if !reflect.DeepEqual(got, want) {
		t.Fatalf("report fields = %v, want %v", got, want)
	}
}

func assertStatusMessage(t *testing.T, report map[string]any, want string) {
	t.Helper()
	if report["Message"] != want {
		t.Fatalf("Message = %q, want %q", report["Message"], want)
	}
}

func assertStatusValue(t *testing.T, report map[string]any, name string, want any) {
	t.Helper()
	if report[name] != want {
		t.Fatalf("%s = %#v, want %#v", name, report[name], want)
	}
}

// assertStatusConnectionError checks ConnectionError is present and non-empty, and contains want
// when want is given.
func assertStatusConnectionError(t *testing.T, report map[string]any, want string) {
	t.Helper()
	connectionError, ok := report["ConnectionError"].(string)
	if !ok || connectionError == "" || !strings.Contains(connectionError, want) {
		t.Fatalf("ConnectionError = %#v, want a message containing %q", report["ConnectionError"], want)
	}
}

func assertStatusError(t *testing.T, run statusRun, wantStderr string) {
	t.Helper()
	if run.code != 1 {
		t.Fatalf("exit code = %d, want 1\nstdout=%s\nstderr=%s", run.code, run.stdout, run.stderr)
	}
	if run.stdout != "" {
		t.Fatalf("stdout must stay empty when status fails:\n%s", run.stdout)
	}
	if !strings.Contains(run.stderr, wantStderr) {
		t.Fatalf("stderr must contain %q:\n%s", wantStderr, run.stderr)
	}
}

func assertStatusRequestCount(t *testing.T, server fakeUnityServer, want int) {
	t.Helper()
	if got := len(server.requests); got != want {
		t.Fatalf("get-editor-status requests = %d, want %d", got, want)
	}
	assertServerDidNotFail(t, server.serverErr)
}
