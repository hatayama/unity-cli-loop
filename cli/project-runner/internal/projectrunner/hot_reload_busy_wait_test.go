package projectrunner

import (
	"context"
	"encoding/json"
	"errors"
	"reflect"
	"strings"
	"testing"
	"time"

	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

const (
	busyWaitRunningCompile = `{"code":-32603,"message":"Unity is busy running 'compile'.","data":{"type":"server_busy",` +
		`"runningToolName":"compile","requestedToolName":"hot-reload","message":"busy",` +
		`"runningToolPhase":"WaitingForMainThread","runningToolElapsedSeconds":11}}`
	busyWaitRunningUnnamed = `{"code":-32603,"message":"Unity is busy.","data":{"type":"server_busy",` +
		`"requestedToolName":"hot-reload","message":"busy"}}`
	busyWaitRunningDynamicCode = `{"code":-32603,"message":"Unity is busy running 'execute-dynamic-code'.","data":{"type":"server_busy",` +
		`"runningToolName":"execute-dynamic-code","requestedToolName":"hot-reload","message":"busy",` +
		`"runningToolPhase":"Executing","runningToolElapsedSeconds":3}}`
	busyWaitStatusBusyCompile = `{"IsBusy":true,"RunningToolName":"compile","RunningToolElapsedSeconds":11,` +
		`"RunningToolPhase":"WaitingForMainThread","HasEditorState":true,"IsCompiling":false,"IsUpdating":false,` +
		`"SecondsSinceLastMainThreadTick":0.1}`
	busyWaitStatusBusyDynamicCode = `{"IsBusy":true,"RunningToolName":"execute-dynamic-code","RunningToolElapsedSeconds":3,` +
		`"RunningToolPhase":"Executing","HasEditorState":true,"IsCompiling":false,"IsUpdating":false,` +
		`"SecondsSinceLastMainThreadTick":0.1}`
	busyWaitApplied = `{"Success":true,"Outcome":"Applied","RetryAfterEditorReady":false,` +
		`"CompileFallback":"NotNeeded","Message":"applied","Timing":{"TotalMs":34}}`
	busyWaitFallbackRequested = `{"Success":false,"Outcome":"Failed","RetryAfterEditorReady":false,` +
		`"CompileFallback":"Requested","Message":"applied","Timing":{"TotalMs":34}}`
	busyWaitWaitingLine = "the Editor is busy running 'compile'; waiting for it to finish"
)

func busyWaitParams() map[string]any {
	return withFiles(map[string]any{"CompileOnSkip": "off"}, []any{"Assets/A.cs"})
}

// busyWaitT1Steps is a compile holding the Editor, the domain reload dropping the server, the
// Editor compiling and then ready, and the request sent after the wait being applied.
func busyWaitT1Steps() []scriptedIPCStep {
	return []scriptedIPCStep{
		{method: hotReloadCommandName, rpcError: busyWaitRunningCompile},
		{method: editorStatusBridgeCommandName, result: busyWaitStatusBusyCompile},
		{method: editorStatusBridgeCommandName, drop: true},
		{method: editorStatusBridgeCommandName, result: editorReadyRetryStatusCompiling},
		{method: editorStatusBridgeCommandName, result: editorReadyRetryStatusReady},
		{method: hotReloadCommandName, result: busyWaitApplied},
	}
}

func (run editorReadyRetryRun) statusRequests() int {
	count := 0
	for _, request := range run.requests {
		if request.method == editorStatusBridgeCommandName {
			count++
		}
	}
	return count
}

func assertHotReloadRequestsUnchanged(t *testing.T, run editorReadyRetryRun, want int) {
	t.Helper()
	applies := run.hotReloadRequests()
	if len(applies) != want {
		t.Fatalf("hot-reload requests = %d, want %d\nstdout=%s\nstderr=%s", len(applies), want, run.stdout, run.stderr)
	}
	for index, apply := range applies[1:] {
		if !reflect.DeepEqual(apply.params, applies[0].params) {
			t.Fatalf("hot-reload request %d params = %#v, want the first's %#v", index+1, apply.params, applies[0].params)
		}
	}
}

func assertBusyWaitNote(t *testing.T, fields map[string]any, fragments ...string) {
	t.Helper()
	note, _ := fields["EditorReadyRetryNote"].(string)
	for _, fragment := range fragments {
		if !strings.Contains(note, fragment) {
			t.Fatalf("EditorReadyRetryNote = %q, want it to contain %q", note, fragment)
		}
	}
}

func editorReadyWaitMs(t *testing.T, fields map[string]any) float64 {
	t.Helper()
	timing, _ := fields["Timing"].(map[string]any)
	waited, isNumber := timing["EditorReadyWaitMs"].(float64)
	if !isNumber {
		t.Fatalf("Timing.EditorReadyWaitMs = %#v, want a number", timing["EditorReadyWaitMs"])
	}
	return waited
}

func assertFailedWithEmptyStdout(t *testing.T, run editorReadyRetryRun, stderrFragment string) {
	t.Helper()
	if run.code != 1 {
		t.Fatalf("exit code = %d, want 1\nstderr=%s", run.code, run.stderr)
	}
	if run.stdout != "" {
		t.Fatalf("stdout must be empty: %q", run.stdout)
	}
	if !strings.Contains(run.stderr, stderrFragment) {
		t.Fatalf("stderr must contain %q: %q", stderrFragment, run.stderr)
	}
}

// Verifies a request refused because another uloop command holds the Editor waits for the Editor
// to be ready through a domain reload, then is sent once more, unchanged, and that answer is reported.
func TestRunHotReloadWaitsForTheRunningCommandAndAppliesOnce(t *testing.T) {
	useFastEditorReadyWait(t, fastEditorReadyWaitOptions())
	run := runScriptedHotReload(t, context.Background(), t.TempDir(), busyWaitParams(), busyWaitT1Steps(),
		editorReadyRetryNoCompile())

	assertHotReloadRequestsUnchanged(t, run, 2)
	if run.code != 0 || run.compileCalls != 0 {
		t.Fatalf("exit code = %d, compile calls = %d, want 0 and 0\nstderr=%s", run.code, run.compileCalls, run.stderr)
	}
	fields := run.stdoutFields(t)
	if fields["Outcome"] != "Applied" {
		t.Fatalf("Outcome = %#v, want Applied", fields["Outcome"])
	}
	assertBusyWaitNote(t, fields, "'compile'")
	editorReadyWaitMs(t, fields)
	if timing, _ := fields["Timing"].(map[string]any); timing["TotalMs"] != float64(34) {
		t.Fatalf("Timing.TotalMs = %#v, want the answer's 34", timing["TotalMs"])
	}
	if !strings.Contains(run.stderr, busyWaitWaitingLine) {
		t.Fatalf("stderr must carry the waiting line: %q", run.stderr)
	}
}

// Verifies a busy answer to the request sent after the wait is reported as UNITY_SERVER_BUSY
// without a second wait, and the wait's end entry says no answer came back.
func TestRunHotReloadReportsASecondBusyAnswerWithoutWaitingAgain(t *testing.T) {
	enableCliVibeLog(t)
	useFastEditorReadyWait(t, fastEditorReadyWaitOptions())
	projectRoot := t.TempDir()
	run := runScriptedHotReload(t, context.Background(), projectRoot, busyWaitParams(),
		[]scriptedIPCStep{
			{method: hotReloadCommandName, rpcError: busyWaitRunningCompile},
			{method: editorStatusBridgeCommandName, result: editorReadyRetryStatusReady},
			{method: hotReloadCommandName, rpcError: busyWaitRunningCompile},
		},
		editorReadyRetryNoCompile())

	assertHotReloadRequestsUnchanged(t, run, 2)
	if statuses := run.statusRequests(); statuses != 1 {
		t.Fatalf("status requests = %d, want 1", statuses)
	}
	assertFailedWithEmptyStdout(t, run, "UNITY_SERVER_BUSY")
	complete := cliVibeEntryContext(t, singleCliVibeEntry(t, readOnlyCliVibeLog(t, projectRoot), hotReloadBusyWaitCompleteOperation))
	assertCliVibeContextValues(t, complete, map[string]any{"ready": true, "second_result": false})
	if second, _ := complete["second_correlation_id"].(string); second == "" {
		t.Fatalf("second_correlation_id must name the request sent after the wait: %#v", complete)
	}
}

// Verifies a cancel during the wait sends nothing more, reports the cancel, and still writes the
// wait's end entry with no second request.
func TestRunHotReloadReportsACancelWhileWaitingForTheBusyEditor(t *testing.T) {
	enableCliVibeLog(t)
	options := fastEditorReadyWaitOptions()
	options.pollInterval = 50 * time.Millisecond
	useFastEditorReadyWait(t, options)
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	steps := []scriptedIPCStep{
		{method: hotReloadCommandName, rpcError: busyWaitRunningCompile},
		{method: editorStatusBridgeCommandName, result: busyWaitStatusBusyCompile, onServed: cancel},
	}
	for range 50 {
		steps = append(steps, scriptedIPCStep{method: editorStatusBridgeCommandName, result: busyWaitStatusBusyCompile})
	}
	projectRoot := t.TempDir()
	run := runScriptedHotReload(t, ctx, projectRoot, busyWaitParams(), steps, editorReadyRetryNoCompile())

	assertHotReloadRequestsUnchanged(t, run, 1)
	assertFailedWithEmptyStdout(t, run, context.Canceled.Error())
	if !strings.Contains(run.stderr, busyWaitWaitingLine) {
		t.Fatalf("stderr must carry the waiting line: %q", run.stderr)
	}
	// Why the log and not the request count: a request sent on the cancelled ctx fails at the dial,
	// so stderr and the server's count look the same whether or not it was sent.
	logContent := readOnlyCliVibeLog(t, projectRoot)
	if sent := cliVibeEntriesForOperation(t, logContent, "cli_tool_request_sent"); len(sent) != 1 {
		t.Fatalf("cli_tool_request_sent entries = %d, want 1", len(sent))
	}
	complete := cliVibeEntryContext(t, singleCliVibeEntry(t, logContent, hotReloadBusyWaitCompleteOperation))
	assertCliVibeContextValues(t, complete, map[string]any{
		"second_correlation_id": "",
		"ready":                 false,
		"second_result":         false,
	})
}

// Verifies the request is sent once more when the budget ends before the Editor reports ready,
// and the note says the Editor did not report ready.
func TestRunHotReloadAppliesOnceMoreWhenTheBudgetEndsBeforeTheEditorIsReady(t *testing.T) {
	options := fastEditorReadyWaitOptions()
	options.budget = 0
	useFastEditorReadyWait(t, options)
	run := runScriptedHotReload(t, context.Background(), t.TempDir(), busyWaitParams(),
		[]scriptedIPCStep{
			{method: hotReloadCommandName, rpcError: busyWaitRunningCompile},
			{method: editorStatusBridgeCommandName, result: busyWaitStatusBusyCompile},
			{method: hotReloadCommandName, result: busyWaitApplied},
		},
		editorReadyRetryNoCompile())

	assertHotReloadRequestsUnchanged(t, run, 2)
	if statuses := run.statusRequests(); statuses != 1 {
		t.Fatalf("status requests = %d, want 1", statuses)
	}
	if run.code != 0 {
		t.Fatalf("exit code = %d, want 0\nstderr=%s", run.code, run.stderr)
	}
	fields := run.stdoutFields(t)
	if fields["Outcome"] != "Applied" {
		t.Fatalf("Outcome = %#v, want Applied", fields["Outcome"])
	}
	assertBusyWaitNote(t, fields, "did not report ready")
	editorReadyWaitMs(t, fields)
}

// Verifies an apply after the busy wait that is refused for compiling runs the settle wait too,
// and the note and EditorReadyWaitMs cover both waits instead of the last one alone.
func TestRunHotReloadKeepsBothNotesWhenTheApplyAfterTheBusyWaitIsRefusedForCompiling(t *testing.T) {
	options := fastEditorReadyWaitOptions()
	options.pollInterval = 20 * time.Millisecond
	useFastEditorReadyWait(t, options)
	run := runScriptedHotReload(t, context.Background(), t.TempDir(), busyWaitParams(),
		[]scriptedIPCStep{
			{method: hotReloadCommandName, rpcError: busyWaitRunningCompile},
			{method: editorStatusBridgeCommandName, result: busyWaitStatusBusyCompile},
			{method: editorStatusBridgeCommandName, result: editorReadyRetryStatusReady},
			{method: hotReloadCommandName, result: editorReadyRetryFirstRefused},
			{method: editorStatusBridgeCommandName, result: editorReadyRetryStatusReady},
			{method: hotReloadCommandName, result: editorReadyRetrySecondApplied},
		},
		editorReadyRetryNoCompile())

	applies := run.hotReloadRequests()
	if len(applies) != 3 {
		t.Fatalf("hot-reload requests = %d, want 3\nstderr=%s", len(applies), run.stderr)
	}
	if files := applies[2].params["Files"]; !reflect.DeepEqual(files, []any{"Assets/A.cs", "Assets/B.cs"}) {
		t.Fatalf("third request Files = %#v, want the selected files", files)
	}
	if run.code != 0 {
		t.Fatalf("exit code = %d, want 0\nstderr=%s", run.code, run.stderr)
	}
	fields := run.stdoutFields(t)
	note, _ := fields["EditorReadyRetryNote"].(string)
	busyAt := strings.Index(note, "'compile'")
	settleAt := strings.Index(note, "applied the same request again")
	if busyAt < 0 || settleAt < busyAt {
		t.Fatalf("EditorReadyRetryNote = %q, want the busy sentence followed by the settle sentence", note)
	}
	if waited := editorReadyWaitMs(t, fields); waited < 20 {
		t.Fatalf("Timing.EditorReadyWaitMs = %v, want both waits (at least 20)", waited)
	}
}

// Verifies an apply after the busy wait that is refused for compiling, followed by a settle wait
// that runs out, keeps both sentences in order and both waits in EditorReadyWaitMs.
func TestRunHotReloadKeepsBothNotesWhenTheEditorDoesNotSettleAfterTheBusyWait(t *testing.T) {
	enableCliVibeLog(t)
	options := fastEditorReadyWaitOptions()
	options.pollInterval = 20 * time.Millisecond
	// Why the busy wait ends on Ready: the budget is shared, and only the settle wait may run it out.
	// It is large enough that a slow machine's busy wait still ends on Ready.
	options.budget = 400 * time.Millisecond
	useFastEditorReadyWait(t, options)
	steps := []scriptedIPCStep{{method: hotReloadCommandName, rpcError: busyWaitRunningCompile}}
	for range 4 {
		steps = append(steps, scriptedIPCStep{method: editorStatusBridgeCommandName, result: busyWaitStatusBusyCompile})
	}
	steps = append(steps,
		scriptedIPCStep{method: editorStatusBridgeCommandName, result: editorReadyRetryStatusReady},
		scriptedIPCStep{method: hotReloadCommandName, result: editorReadyRetryFirstRefused})
	for range 50 {
		steps = append(steps, scriptedIPCStep{method: editorStatusBridgeCommandName, result: editorReadyRetryStatusCompiling})
	}
	projectRoot := t.TempDir()
	run := runScriptedHotReload(t, context.Background(), projectRoot, busyWaitParams(), steps, editorReadyRetryNoCompile())

	assertHotReloadRequestsUnchanged(t, run, 2)
	if run.code != 1 || run.compileCalls != 0 {
		t.Fatalf("exit code = %d, compile calls = %d, want 1 and 0\nstderr=%s", run.code, run.compileCalls, run.stderr)
	}
	fields := run.stdoutFields(t)
	if fields["Outcome"] != "Failed" {
		t.Fatalf("Outcome = %#v, want Failed", fields["Outcome"])
	}
	note, _ := fields["EditorReadyRetryNote"].(string)
	busyAt := strings.Index(note, "'compile'")
	gaveUpAt := strings.Index(note, "did not")
	if busyAt < 0 || gaveUpAt < busyAt {
		t.Fatalf("EditorReadyRetryNote = %q, want the busy sentence followed by the gave-up sentence", note)
	}
	// Each wait logs the same whole milliseconds it adds to Timing, so the field is exactly their
	// sum: keeping only one wait leaves out the other, and neither is zero.
	logContent := readOnlyCliVibeLog(t, projectRoot)
	busyWaited, _ := cliVibeEntryContext(t, singleCliVibeEntry(t, logContent, hotReloadBusyWaitCompleteOperation))["waited_ms"].(float64)
	settleWaited, _ := cliVibeEntryContext(t, singleCliVibeEntry(t, logContent, hotReloadEditorReadyRetryCompleteOperation))["waited_ms"].(float64)
	if waited := editorReadyWaitMs(t, fields); waited != busyWaited+settleWaited {
		t.Fatalf("Timing.EditorReadyWaitMs = %v, want the busy wait %v plus the settle wait %v", waited, busyWaited, settleWaited)
	}
}

// Verifies the busy wait writes its decision and end entries under the first request's ID, and the
// later entries follow the request sent after the wait.
func TestRunHotReloadWritesBusyWaitVibeLogs(t *testing.T) {
	enableCliVibeLog(t)
	useFastEditorReadyWait(t, fastEditorReadyWaitOptions())
	projectRoot := t.TempDir()
	run := runScriptedHotReload(t, context.Background(), projectRoot, busyWaitParams(), busyWaitT1Steps(),
		editorReadyRetryNoCompile())
	if run.code != 0 {
		t.Fatalf("exit code = %d, want 0\nstderr=%s", run.code, run.stderr)
	}

	logContent := readOnlyCliVibeLog(t, projectRoot)
	sent := cliVibeEntriesForOperation(t, logContent, "cli_tool_request_sent")
	if len(sent) != 2 {
		t.Fatalf("cli_tool_request_sent entries = %d, want 2\n%s", len(sent), logContent)
	}
	failed := cliVibeEntriesForOperation(t, logContent, "cli_tool_request_failed")
	if len(failed) == 0 {
		t.Fatalf("cli_tool_request_failed entries = 0, want the busy answer\n%s", logContent)
	}
	assertCliVibeContextValues(t, cliVibeEntryContext(t, failed[0]), map[string]any{"error_kind": "rpc:server_busy"})

	decided := singleCliVibeEntry(t, logContent, hotReloadBusyWaitDecidedOperation)
	assertSharedCliVibeCorrelationID(t, sent[0], decided)
	assertCliVibeContextValues(t, cliVibeEntryContext(t, decided), map[string]any{
		"running_tool_name":            "compile",
		"running_tool_phase":           "WaitingForMainThread",
		"running_tool_elapsed_seconds": float64(11),
		"resend_interval_ms":           float64(0),
	})

	complete := singleCliVibeEntry(t, logContent, hotReloadBusyWaitCompleteOperation)
	if complete["level"] != "INFO" {
		t.Fatalf("complete entry level must be INFO: %#v", complete)
	}
	assertSharedCliVibeCorrelationID(t, sent[0], complete)
	completeContext := cliVibeEntryContext(t, complete)
	assertCliVibeContextValues(t, completeContext, map[string]any{
		"second_correlation_id": vibeLogContextString(t, sent[1], "correlation_id"),
		"ready":                 true,
		"resends":               float64(0),
		"second_result":         true,
		"second_success":        true,
		"second_outcome":        "Applied",
	})
	if _, isNumber := completeContext["waited_ms"].(float64); !isNumber {
		t.Fatalf("waited_ms = %#v, want a number", completeContext["waited_ms"])
	}
	assertSharedCliVibeCorrelationID(t, sent[1], singleCliVibeEntry(t, logContent, "cli_hot_reload_editor_ready_retry_decided"))
	assertSharedCliVibeCorrelationID(t, sent[1], singleCliVibeEntry(t, logContent, "cli_hot_reload_compile_fallback_decided"))
}

// Verifies an error other than busy is reported as before, with no wait.
func TestRunHotReloadWritesANonBusyErrorWithoutWaiting(t *testing.T) {
	enableCliVibeLog(t)
	useFastEditorReadyWait(t, fastEditorReadyWaitOptions())
	projectRoot := t.TempDir()
	run := runScriptedHotReload(t, context.Background(), projectRoot, busyWaitParams(),
		[]scriptedIPCStep{
			{method: hotReloadCommandName, rpcError: `{"code":-32603,"message":"boom","data":{"type":"internal_error","message":"boom"}}`},
		},
		editorReadyRetryNoCompile())

	if len(run.requests) != 1 {
		t.Fatalf("requests = %d, want 1", len(run.requests))
	}
	assertFailedWithEmptyStdout(t, run, "boom")
	if strings.Contains(run.stderr, "waiting for it to finish") {
		t.Fatalf("stderr must not carry the waiting line: %q", run.stderr)
	}
	assertNoCliVibeEntry(t, readOnlyCliVibeLog(t, projectRoot), hotReloadBusyWaitDecidedOperation)
}

// Verifies the running tool's name is read from the busy data, and anything unreadable falls back
// to a generic name.
func TestHotReloadBusyRunningToolNameFallsBackWhenTheBusyDataNamesNone(t *testing.T) {
	busyError := func(rpcError string) error {
		parsed := struct {
			Code    int             `json:"code"`
			Message string          `json:"message"`
			Data    json.RawMessage `json:"data"`
		}{}
		if err := json.Unmarshal([]byte(rpcError), &parsed); err != nil {
			t.Fatal(err)
		}
		return &unityipc.RPCError{Code: parsed.Code, Message: parsed.Message, Data: parsed.Data}
	}
	cases := map[string]struct {
		err  error
		want string
	}{
		"named":   {err: busyError(busyWaitRunningCompile), want: "compile"},
		"unnamed": {err: busyError(busyWaitRunningUnnamed), want: hotReloadBusyUnknownToolName},
		"not rpc": {err: errors.New("x"), want: hotReloadBusyUnknownToolName},
	}
	for name, testCase := range cases {
		t.Run(name, func(t *testing.T) {
			if got := hotReloadBusyRunningToolName(testCase.err); got != testCase.want {
				t.Fatalf("hotReloadBusyRunningToolName = %q, want %q", got, testCase.want)
			}
		})
	}
}

// Verifies --status waits the same way and carries the note, without creating a Timing the
// status answer never has.
func TestRunHotReloadStatusWaitsForTheRunningCommandToo(t *testing.T) {
	useFastEditorReadyWait(t, fastEditorReadyWaitOptions())
	steps := busyWaitT1Steps()
	steps[len(steps)-1] = scriptedIPCStep{method: hotReloadCommandName, result: `{"Success":true,"Outcome":"Status","ActivePatchTotal":0}`}
	run := runScriptedHotReload(t, context.Background(), t.TempDir(), map[string]any{"Status": true}, steps,
		editorReadyRetryNoCompile())

	assertHotReloadRequestsUnchanged(t, run, 2)
	if status := run.hotReloadRequests()[1].params["Status"]; status != true {
		t.Fatalf("second request Status = %#v, want true", status)
	}
	if run.code != 0 {
		t.Fatalf("exit code = %d, want 0\nstderr=%s", run.code, run.stderr)
	}
	fields := run.stdoutFields(t)
	assertBusyWaitNote(t, fields, "'compile'")
	if timing, present := fields["Timing"]; present {
		t.Fatalf("Timing must stay absent on a status answer: %#v", timing)
	}
}

// Verifies an answer after the busy wait that is not a JSON object fails the command.
func TestRunHotReloadFailsWhenTheApplyAfterTheBusyWaitIsNotAnObject(t *testing.T) {
	useFastEditorReadyWait(t, fastEditorReadyWaitOptions())
	run := runScriptedHotReload(t, context.Background(), t.TempDir(), busyWaitParams(),
		[]scriptedIPCStep{
			{method: hotReloadCommandName, rpcError: busyWaitRunningCompile},
			{method: editorStatusBridgeCommandName, result: editorReadyRetryStatusReady},
			{method: hotReloadCommandName, result: `null`},
		},
		editorReadyRetryNoCompile())

	assertFailedWithEmptyStdout(t, run, "must be a JSON object")
}

// Verifies a transport failure of the request sent after the busy wait is reported as such.
func TestRunHotReloadReportsATransportFailureOfTheApplyAfterTheBusyWait(t *testing.T) {
	useFastEditorReadyWait(t, fastEditorReadyWaitOptions())
	run := runScriptedHotReload(t, context.Background(), t.TempDir(), busyWaitParams(),
		[]scriptedIPCStep{
			{method: hotReloadCommandName, rpcError: busyWaitRunningCompile},
			{method: editorStatusBridgeCommandName, result: editorReadyRetryStatusReady},
			{method: hotReloadCommandName, drop: true},
		},
		editorReadyRetryNoCompile())

	assertFailedWithEmptyStdout(t, run, "UNITY_DISCONNECTED_AFTER_DISPATCH")
}

// Verifies a request with no files is sent again as it was, and the Editor's failure after the
// wait is passed through with the note.
func TestRunHotReloadPassesTheEditorAnswerThroughAfterTheBusyWaitWhenNoFilesWereGiven(t *testing.T) {
	useFastEditorReadyWait(t, fastEditorReadyWaitOptions())
	run := runScriptedHotReload(t, context.Background(), t.TempDir(), map[string]any{},
		[]scriptedIPCStep{
			{method: hotReloadCommandName, rpcError: busyWaitRunningCompile},
			{method: editorStatusBridgeCommandName, result: editorReadyRetryStatusReady},
			{method: hotReloadCommandName, result: `{"Success":false,"Outcome":"Failed","RetryAfterEditorReady":false,` +
				`"CompileFallback":"NotNeeded","Message":"no changed files"}`},
		},
		editorReadyRetryNoCompile())

	assertHotReloadRequestsUnchanged(t, run, 2)
	if _, present := run.hotReloadRequests()[1].params["Files"]; present {
		t.Fatalf("second request must not carry Files: %#v", run.hotReloadRequests()[1].params)
	}
	if run.code != 1 {
		t.Fatalf("exit code = %d, want 1", run.code)
	}
	fields := run.stdoutFields(t)
	if fields["Success"] != false {
		t.Fatalf("Success = %#v, want false", fields["Success"])
	}
	assertBusyWaitNote(t, fields, "'compile'")
}

// Verifies the fallback compile still runs when the answer after the busy wait asks for it, and
// the busy wait's note and wait time survive the merge.
func TestRunHotReloadRunsTheFallbackCompileAfterTheBusyWait(t *testing.T) {
	useFastEditorReadyWait(t, fastEditorReadyWaitOptions())
	run := runScriptedHotReload(t, context.Background(), t.TempDir(), busyWaitParams(),
		[]scriptedIPCStep{
			{method: hotReloadCommandName, rpcError: busyWaitRunningCompile},
			{method: editorStatusBridgeCommandName, result: editorReadyRetryStatusReady},
			{method: hotReloadCommandName, result: busyWaitFallbackRequested},
		},
		compileExecutionResult{result: json.RawMessage(`{"Success":true,"Message":"ok"}`), exitCode: 0})

	if run.compileCalls != 1 {
		t.Fatalf("compile calls = %d, want 1\nstderr=%s", run.compileCalls, run.stderr)
	}
	fields := run.stdoutFields(t)
	if fields["Outcome"] != "ReplacedByCompile" {
		t.Fatalf("Outcome = %#v, want ReplacedByCompile", fields["Outcome"])
	}
	assertBusyWaitNote(t, fields, "'compile'")
	editorReadyWaitMs(t, fields)
	if timing, _ := fields["Timing"].(map[string]any); timing["FallbackCompileMs"] == nil {
		t.Fatalf("Timing.FallbackCompileMs missing: %#v", fields["Timing"])
	}
}

// Verifies the request is sent again while a cancelled execute-dynamic-code holds the Editor, since
// the Editor takes that slot back only when a tool request arrives, and the resend that gets in is
// the answer.
func TestRunHotReloadSendsAgainWhileACancelledExecuteDynamicCodeHoldsTheEditor(t *testing.T) {
	enableCliVibeLog(t)
	useFastEditorReadyWait(t, fastEditorReadyWaitOptions())
	projectRoot := t.TempDir()
	run := runScriptedHotReload(t, context.Background(), projectRoot, busyWaitParams(),
		[]scriptedIPCStep{
			{method: hotReloadCommandName, rpcError: busyWaitRunningDynamicCode},
			{method: editorStatusBridgeCommandName, result: busyWaitStatusBusyDynamicCode},
			{method: hotReloadCommandName, rpcError: busyWaitRunningDynamicCode},
			{method: editorStatusBridgeCommandName, result: busyWaitStatusBusyDynamicCode},
			{method: hotReloadCommandName, result: busyWaitApplied},
		},
		editorReadyRetryNoCompile())

	assertHotReloadRequestsUnchanged(t, run, 3)
	if statuses := run.statusRequests(); statuses != 2 {
		t.Fatalf("status requests = %d, want 2", statuses)
	}
	if run.code != 0 {
		t.Fatalf("exit code = %d, want 0\nstderr=%s", run.code, run.stderr)
	}
	fields := run.stdoutFields(t)
	if fields["Outcome"] != "Applied" {
		t.Fatalf("Outcome = %#v, want Applied", fields["Outcome"])
	}
	assertBusyWaitNote(t, fields, "'execute-dynamic-code'", "for it to finish")
	editorReadyWaitMs(t, fields)

	logContent := readOnlyCliVibeLog(t, projectRoot)
	sent := cliVibeEntriesForOperation(t, logContent, "cli_tool_request_sent")
	if len(sent) != 3 {
		t.Fatalf("cli_tool_request_sent entries = %d, want 3", len(sent))
	}
	complete := cliVibeEntryContext(t, singleCliVibeEntry(t, logContent, hotReloadBusyWaitCompleteOperation))
	assertCliVibeContextValues(t, complete, map[string]any{
		"resends":               float64(2),
		"ready":                 true,
		"second_correlation_id": vibeLogContextString(t, sent[2], "correlation_id"),
	})
}

// Verifies no request is sent again before the resend interval has passed, even while
// execute-dynamic-code holds the Editor.
func TestRunHotReloadDoesNotSendAgainBeforeTheResendInterval(t *testing.T) {
	enableCliVibeLog(t)
	options := fastEditorReadyWaitOptions()
	options.busyResendInterval = time.Hour
	useFastEditorReadyWait(t, options)
	projectRoot := t.TempDir()
	run := runScriptedHotReload(t, context.Background(), projectRoot, busyWaitParams(),
		[]scriptedIPCStep{
			{method: hotReloadCommandName, rpcError: busyWaitRunningDynamicCode},
			{method: editorStatusBridgeCommandName, result: busyWaitStatusBusyDynamicCode},
			{method: editorStatusBridgeCommandName, result: editorReadyRetryStatusReady},
			{method: hotReloadCommandName, result: busyWaitApplied},
		},
		editorReadyRetryNoCompile())

	assertHotReloadRequestsUnchanged(t, run, 2)
	complete := cliVibeEntryContext(t, singleCliVibeEntry(t, readOnlyCliVibeLog(t, projectRoot), hotReloadBusyWaitCompleteOperation))
	assertCliVibeContextValues(t, complete, map[string]any{"resends": float64(0)})
}
