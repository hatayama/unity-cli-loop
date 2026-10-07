package projectrunner

import (
	"bufio"
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	"io"
	"net"
	"reflect"
	"strings"
	"testing"
	"time"

	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

const (
	editorReadyRetryFirstRefused = `{"Success":false,"Outcome":"Failed","RetryAfterEditorReady":true,` +
		`"SelectedFiles":["Assets/A.cs","Assets/B.cs"],"CompileFallback":"Requested","Message":"refused","Timing":{"TotalMs":12}}`
	editorReadyRetryStatusCompiling = `{"IsBusy":false,"HasEditorState":true,"IsCompiling":true,"IsUpdating":false,` +
		`"SecondsSinceLastMainThreadTick":0.1}`
	editorReadyRetryStatusReady = `{"IsBusy":false,"HasEditorState":true,"IsCompiling":false,"IsUpdating":false,` +
		`"SecondsSinceLastMainThreadTick":0.1}`
	editorReadyRetrySecondApplied = `{"Success":true,"Outcome":"Applied","RetryAfterEditorReady":false,` +
		`"CompileFallback":"NotNeeded","Message":"applied","Timing":{"TotalMs":34}}`
)

// scriptedIPCStep is one request the fake Editor expects and how it answers it.
type scriptedIPCStep struct {
	method string
	result string
	// rpcError answers with this JSON-RPC error object instead of a result.
	rpcError string
	// drop closes the connection after reading the request, without an answer.
	drop bool
	// onServed runs after the answer was written, so a test can act at a known point of the run.
	onServed func()
}

type scriptedIPCRequest struct {
	method string
	params map[string]any
}

// serveScriptedIPCResponses answers one connection per step in order, then reports any further
// request as unexpected, which is how a third apply is caught.
func serveScriptedIPCResponses(
	listener net.Listener,
	steps []scriptedIPCStep,
	requests chan<- scriptedIPCRequest,
	serverErr chan<- error,
) {
	for index, step := range steps {
		conn, err := listener.Accept()
		if err != nil {
			// Why silent: the listener closes at cleanup, and a run that ends early leaves the rest
			// of the script unserved; the requests a test expects are checked on its own side.
			return
		}
		request, err := readScriptedIPCRequest(conn)
		if err != nil {
			_ = conn.Close()
			serverErr <- fmt.Errorf("step %d: %w", index, err)
			return
		}
		requests <- request
		if request.method != step.method {
			_ = conn.Close()
			serverErr <- fmt.Errorf("step %d: method mismatch: %s", index, request.method)
			return
		}
		if step.drop {
			_ = conn.Close()
			continue
		}
		response := []byte(fmt.Sprintf(`{"jsonrpc":"2.0","result":%s,"id":1}`, step.result))
		if step.rpcError != "" {
			response = []byte(fmt.Sprintf(`{"jsonrpc":"2.0","error":%s,"id":1}`, step.rpcError))
		}
		writeErr := unityipc.Write(conn, response)
		_ = conn.Close()
		if writeErr != nil {
			serverErr <- fmt.Errorf("step %d: %w", index, writeErr)
			return
		}
		if step.onServed != nil {
			step.onServed()
		}
	}

	conn, err := listener.Accept()
	if err != nil {
		return
	}
	defer func() { _ = conn.Close() }()
	request, err := readScriptedIPCRequest(conn)
	if err != nil {
		return
	}
	serverErr <- fmt.Errorf("unexpected request after the script: %s", request.method)
}

func readScriptedIPCRequest(conn net.Conn) (scriptedIPCRequest, error) {
	payload, err := unityipc.Read(bufio.NewReader(conn))
	if err != nil {
		return scriptedIPCRequest{}, err
	}
	request := struct {
		Method string         `json:"method"`
		Params map[string]any `json:"params"`
	}{}
	if err := json.Unmarshal(payload, &request); err != nil {
		return scriptedIPCRequest{}, err
	}
	return scriptedIPCRequest{method: request.Method, params: request.Params}, nil
}

type editorReadyRetryRun struct {
	stdout       string
	stderr       string
	code         int
	compileCalls int
	requests     []scriptedIPCRequest
}

// hotReloadRequests keeps only the apply requests, in the order they arrived.
func (run editorReadyRetryRun) hotReloadRequests() []scriptedIPCRequest {
	applies := []scriptedIPCRequest{}
	for _, request := range run.requests {
		if request.method == hotReloadCommandName {
			applies = append(applies, request)
		}
	}
	return applies
}

func (run editorReadyRetryRun) stdoutFields(t *testing.T) map[string]any {
	t.Helper()
	fields := map[string]any{}
	if err := json.Unmarshal([]byte(run.stdout), &fields); err != nil {
		t.Fatalf("stdout is not a JSON object: %v\n%s", err, run.stdout)
	}
	return fields
}

// useFastEditorReadyWait shortens the wait for the Editor so a scripted run finishes in
// milliseconds, and restores the defaults afterwards.
func useFastEditorReadyWait(t *testing.T, options hotReloadEditorReadyWaitOptions) {
	t.Helper()
	original := hotReloadEditorReadyWaitDefaults
	hotReloadEditorReadyWaitDefaults = options
	t.Cleanup(func() {
		hotReloadEditorReadyWaitDefaults = original
	})
}

func fastEditorReadyWaitOptions() hotReloadEditorReadyWaitOptions {
	return hotReloadEditorReadyWaitOptions{
		pollInterval: time.Millisecond,
		budget:       2 * time.Second,
		probeTimeout: time.Second,
	}
}

// runScriptedHotReload runs hot reload against the scripted Editor, counting the fallback compiles
// the run asks for. The fallback compile answers with compileResult.
func runScriptedHotReload(
	t *testing.T,
	ctx context.Context,
	projectRoot string,
	params map[string]any,
	steps []scriptedIPCStep,
	compileResult compileExecutionResult,
) editorReadyRetryRun {
	t.Helper()
	listener := newLoopbackIpcListener(t)
	requests := make(chan scriptedIPCRequest, len(steps)+1)
	serverErr := make(chan error, len(steps)+1)
	go serveScriptedIPCResponses(listener, steps, requests, serverErr)

	original := hotReloadFallbackCompile
	compileCalls := 0
	hotReloadFallbackCompile = func(context.Context, unityipc.Connection, io.Writer) compileExecutionResult {
		compileCalls++
		return compileResult
	}
	t.Cleanup(func() {
		hotReloadFallbackCompile = original
	})

	connection := unityipc.Connection{
		Endpoint:    unityipc.Endpoint{Network: listener.Addr().Network(), Address: listener.Addr().String()},
		ProjectRoot: projectRoot,
	}
	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runHotReloadWithCompileFallback(ctx, connection, params, &stdout, &stderr)

	// Why a short grace: the post-script Accept reports a third apply asynchronously.
	time.Sleep(20 * time.Millisecond)
	assertServerDidNotFail(t, serverErr)
	received := []scriptedIPCRequest{}
	for {
		select {
		case request := <-requests:
			received = append(received, request)
			continue
		default:
		}
		break
	}
	return editorReadyRetryRun{
		stdout:       stdout.String(),
		stderr:       stderr.String(),
		code:         code,
		compileCalls: compileCalls,
		requests:     received,
	}
}

func editorReadyRetryNoCompile() compileExecutionResult {
	return compileExecutionResult{result: json.RawMessage(`{"Success":true}`), exitCode: 0}
}

func withFiles(params map[string]any, files []any) map[string]any {
	copied := map[string]any{}
	for key, value := range params {
		copied[key] = value
	}
	copied["Files"] = files
	return copied
}

func assertSecondApplyParams(t *testing.T, run editorReadyRetryRun, want func(first map[string]any) map[string]any) {
	t.Helper()
	applies := run.hotReloadRequests()
	if len(applies) != 2 {
		t.Fatalf("hot-reload requests = %d, want 2\nstdout=%s\nstderr=%s", len(applies), run.stdout, run.stderr)
	}
	expected := want(applies[0].params)
	if !reflect.DeepEqual(applies[1].params, expected) {
		t.Fatalf("second apply params = %#v, want %#v", applies[1].params, expected)
	}
}

func assertNoteAbsent(t *testing.T, fields map[string]any) {
	t.Helper()
	if _, present := fields["EditorReadyRetryNote"]; present {
		t.Fatalf("EditorReadyRetryNote must be absent: %#v", fields["EditorReadyRetryNote"])
	}
}

func assertAppliedAfterTheWait(t *testing.T, run editorReadyRetryRun) {
	t.Helper()
	if run.code != 0 {
		t.Fatalf("exit code = %d, want 0\nstdout=%s\nstderr=%s", run.code, run.stdout, run.stderr)
	}
	if run.compileCalls != 0 {
		t.Fatalf("compile calls = %d, want 0", run.compileCalls)
	}
	fields := run.stdoutFields(t)
	if fields["Outcome"] != "Applied" {
		t.Fatalf("Outcome = %#v, want Applied", fields["Outcome"])
	}
	if note, _ := fields["EditorReadyRetryNote"].(string); note == "" {
		t.Fatalf("EditorReadyRetryNote = %#v, want a sentence", fields["EditorReadyRetryNote"])
	}
	timing, _ := fields["Timing"].(map[string]any)
	if waited, isNumber := timing["EditorReadyWaitMs"].(float64); !isNumber || waited < 0 {
		t.Fatalf("Timing.EditorReadyWaitMs = %#v, want a number of at least 0", timing["EditorReadyWaitMs"])
	}
	if timing["TotalMs"] != float64(34) {
		t.Fatalf("Timing.TotalMs = %#v, want the second apply's 34", timing["TotalMs"])
	}
	if !strings.Contains(run.stderr, "waiting for it to settle") {
		t.Fatalf("stderr must say the command is waiting: %q", run.stderr)
	}
}

// Verifies a refusal that only needs the Editor to settle is applied again once it is ready, with
// the files the first run selected, and the second apply's response is the one reported.
func TestRunHotReloadAppliesAgainOnceTheEditorIsReady(t *testing.T) {
	useFastEditorReadyWait(t, fastEditorReadyWaitOptions())
	run := runScriptedHotReload(t, context.Background(), t.TempDir(),
		map[string]any{"CompileOnSkip": "auto"},
		[]scriptedIPCStep{
			{method: hotReloadCommandName, result: editorReadyRetryFirstRefused},
			{method: editorStatusBridgeCommandName, result: editorReadyRetryStatusCompiling},
			{method: editorStatusBridgeCommandName, result: editorReadyRetryStatusReady},
			{method: hotReloadCommandName, result: editorReadyRetrySecondApplied},
		},
		editorReadyRetryNoCompile())

	assertAppliedAfterTheWait(t, run)
	assertSecondApplyParams(t, run, func(first map[string]any) map[string]any {
		return withFiles(first, []any{"Assets/A.cs", "Assets/B.cs"})
	})
}

// Verifies the second apply resends the first request unchanged when the first response names no
// files, whether or not the caller gave files.
func TestRunHotReloadSendsTheSameParamsWhenTheFirstResponseNamesNoFiles(t *testing.T) {
	cases := []struct {
		name          string
		firstResponse string
		params        map[string]any
	}{
		{
			name:          "absent",
			firstResponse: `{"Success":false,"Outcome":"Failed","RetryAfterEditorReady":true,"CompileFallback":"Requested","Message":"refused"}`,
			params:        map[string]any{"CompileOnSkip": "auto"},
		},
		{
			name:          "empty",
			firstResponse: `{"Success":false,"Outcome":"Failed","RetryAfterEditorReady":true,"SelectedFiles":[],"CompileFallback":"Requested","Message":"refused"}`,
			params:        map[string]any{"CompileOnSkip": "auto"},
		},
		{
			name:          "absent with given files",
			firstResponse: `{"Success":false,"Outcome":"Failed","RetryAfterEditorReady":true,"CompileFallback":"Requested","Message":"refused"}`,
			params:        map[string]any{"CompileOnSkip": "auto", "Files": []any{"Assets/Given.cs"}},
		},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			useFastEditorReadyWait(t, fastEditorReadyWaitOptions())
			run := runScriptedHotReload(t, context.Background(), t.TempDir(), testCase.params,
				[]scriptedIPCStep{
					{method: hotReloadCommandName, result: testCase.firstResponse},
					{method: editorStatusBridgeCommandName, result: editorReadyRetryStatusReady},
					{method: hotReloadCommandName, result: editorReadyRetrySecondApplied},
				},
				editorReadyRetryNoCompile())

			assertSecondApplyParams(t, run, func(first map[string]any) map[string]any {
				return first
			})
		})
	}
}

// Verifies the files the caller gave are replaced by the normalized list the first run selected.
func TestRunHotReloadReplacesGivenFilesWithTheSelectedOnes(t *testing.T) {
	useFastEditorReadyWait(t, fastEditorReadyWaitOptions())
	run := runScriptedHotReload(t, context.Background(), t.TempDir(),
		map[string]any{"Files": []any{"./Assets/A.cs"}},
		[]scriptedIPCStep{
			{method: hotReloadCommandName, result: `{"Success":false,"Outcome":"Failed","RetryAfterEditorReady":true,` +
				`"SelectedFiles":["Assets/A.cs"],"CompileFallback":"Requested","Message":"refused"}`},
			{method: editorStatusBridgeCommandName, result: editorReadyRetryStatusReady},
			{method: hotReloadCommandName, result: editorReadyRetrySecondApplied},
		},
		editorReadyRetryNoCompile())

	assertSecondApplyParams(t, run, func(first map[string]any) map[string]any {
		return withFiles(first, []any{"Assets/A.cs"})
	})
}

// Verifies a second apply that finds the edit already compiled in by Unity is reported as it is,
// with no compile.
func TestRunHotReloadKeepsASecondApplyThatHadNothingLeftToApply(t *testing.T) {
	useFastEditorReadyWait(t, fastEditorReadyWaitOptions())
	run := runScriptedHotReload(t, context.Background(), t.TempDir(), map[string]any{},
		[]scriptedIPCStep{
			{method: hotReloadCommandName, result: editorReadyRetryFirstRefused},
			{method: editorStatusBridgeCommandName, result: editorReadyRetryStatusReady},
			{method: hotReloadCommandName, result: `{"Success":true,"Outcome":"NothingToApply","RetryAfterEditorReady":false,` +
				`"CompileFallback":"NotNeeded","Message":"nothing"}`},
		},
		editorReadyRetryNoCompile())

	if run.code != 0 || run.compileCalls != 0 {
		t.Fatalf("exit code = %d, compile calls = %d, want 0 and 0\nstderr=%s", run.code, run.compileCalls, run.stderr)
	}
	if outcome := run.stdoutFields(t)["Outcome"]; outcome != "NothingToApply" {
		t.Fatalf("Outcome = %#v, want NothingToApply", outcome)
	}
}

// Verifies the wait survives the Editor dropping the connection while it reloads its domain.
func TestRunHotReloadKeepsWaitingWhileTheEditorRestarts(t *testing.T) {
	useFastEditorReadyWait(t, fastEditorReadyWaitOptions())
	run := runScriptedHotReload(t, context.Background(), t.TempDir(), map[string]any{},
		[]scriptedIPCStep{
			{method: hotReloadCommandName, result: editorReadyRetryFirstRefused},
			{method: editorStatusBridgeCommandName, result: editorReadyRetryStatusCompiling},
			{method: editorStatusBridgeCommandName, drop: true},
			{method: editorStatusBridgeCommandName, result: editorReadyRetryStatusReady},
			{method: hotReloadCommandName, result: editorReadyRetrySecondApplied},
		},
		editorReadyRetryNoCompile())

	assertAppliedAfterTheWait(t, run)
}

// Verifies a second refusal is not applied a third time; the second response decides the fallback
// compile, and both notes are reported.
func TestRunHotReloadDoesNotApplyAThirdTimeAndLetsTheSecondResponseDecideTheCompile(t *testing.T) {
	useFastEditorReadyWait(t, fastEditorReadyWaitOptions())
	run := runScriptedHotReload(t, context.Background(), t.TempDir(), map[string]any{},
		[]scriptedIPCStep{
			{method: hotReloadCommandName, result: editorReadyRetryFirstRefused},
			{method: editorStatusBridgeCommandName, result: editorReadyRetryStatusReady},
			{method: hotReloadCommandName, result: editorReadyRetryFirstRefused},
		},
		compileExecutionResult{result: json.RawMessage(`{"Success":true}`), exitCode: 0})

	if applies := len(run.hotReloadRequests()); applies != 2 {
		t.Fatalf("hot-reload requests = %d, want 2", applies)
	}
	if run.compileCalls != 1 {
		t.Fatalf("compile calls = %d, want 1", run.compileCalls)
	}
	if run.code != 0 {
		t.Fatalf("exit code = %d, want the compile's 0\nstderr=%s", run.code, run.stderr)
	}
	fields := run.stdoutFields(t)
	for _, name := range []string{"Compile", "CompileFallbackNote", "EditorReadyRetryNote"} {
		if _, present := fields[name]; !present {
			t.Fatalf("%s missing from stdout: %s", name, run.stdout)
		}
	}
}

// Verifies a response that does not ask for the retry is applied once, as before.
func TestRunHotReloadDoesNotApplyAgainUnlessAsked(t *testing.T) {
	cases := map[string]string{
		"absent": `{"Success":true,"Outcome":"Applied","CompileFallback":"NotNeeded","Message":"applied"}`,
		"false":  `{"Success":true,"Outcome":"Applied","RetryAfterEditorReady":false,"CompileFallback":"NotNeeded","Message":"applied"}`,
	}
	for name, response := range cases {
		t.Run(name, func(t *testing.T) {
			useFastEditorReadyWait(t, fastEditorReadyWaitOptions())
			run := runScriptedHotReload(t, context.Background(), t.TempDir(), map[string]any{},
				[]scriptedIPCStep{{method: hotReloadCommandName, result: response}},
				editorReadyRetryNoCompile())

			if len(run.requests) != 1 || run.compileCalls != 0 || run.code != 0 {
				t.Fatalf("requests = %d, compile calls = %d, exit code = %d, want 1, 0, 0",
					len(run.requests), run.compileCalls, run.code)
			}
			assertNoteAbsent(t, run.stdoutFields(t))
		})
	}
}

// Verifies an Editor that does not settle within the budget leaves the first response with a note
// saying so, and neither a second apply nor a compile runs.
func TestRunHotReloadGivesUpWhenTheEditorDoesNotSettle(t *testing.T) {
	options := fastEditorReadyWaitOptions()
	options.budget = 30 * time.Millisecond
	useFastEditorReadyWait(t, options)
	steps := []scriptedIPCStep{{method: hotReloadCommandName, result: editorReadyRetryFirstRefused}}
	for range 50 {
		steps = append(steps, scriptedIPCStep{method: editorStatusBridgeCommandName, result: editorReadyRetryStatusCompiling})
	}
	run := runScriptedHotReload(t, context.Background(), t.TempDir(), map[string]any{}, steps, editorReadyRetryNoCompile())

	if applies := len(run.hotReloadRequests()); applies != 1 {
		t.Fatalf("hot-reload requests = %d, want 1", applies)
	}
	if run.compileCalls != 0 || run.code != 1 {
		t.Fatalf("compile calls = %d, exit code = %d, want 0 and 1", run.compileCalls, run.code)
	}
	fields := run.stdoutFields(t)
	if fields["Outcome"] != "Failed" {
		t.Fatalf("Outcome = %#v, want Failed", fields["Outcome"])
	}
	if note, _ := fields["EditorReadyRetryNote"].(string); !strings.Contains(note, "did not") {
		t.Fatalf("EditorReadyRetryNote = %#v, want the gave-up sentence", fields["EditorReadyRetryNote"])
	}
}

// Verifies a cancel during the wait reports the cancel and leaves the first response as it was.
func TestRunHotReloadReportsACancelWhileWaitingForTheEditor(t *testing.T) {
	options := fastEditorReadyWaitOptions()
	options.pollInterval = 50 * time.Millisecond
	useFastEditorReadyWait(t, options)
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	steps := []scriptedIPCStep{
		{method: hotReloadCommandName, result: editorReadyRetryFirstRefused},
		{method: editorStatusBridgeCommandName, result: editorReadyRetryStatusCompiling, onServed: cancel},
	}
	for range 50 {
		steps = append(steps, scriptedIPCStep{method: editorStatusBridgeCommandName, result: editorReadyRetryStatusCompiling})
	}
	run := runScriptedHotReload(t, ctx, t.TempDir(), map[string]any{}, steps, editorReadyRetryNoCompile())

	if run.code != 1 || run.compileCalls != 0 {
		t.Fatalf("exit code = %d, compile calls = %d, want 1 and 0", run.code, run.compileCalls)
	}
	if !strings.Contains(run.stderr, context.Canceled.Error()) {
		t.Fatalf("stderr must report the cancel: %q", run.stderr)
	}
	fields := run.stdoutFields(t)
	if fields["Outcome"] != "Failed" {
		t.Fatalf("Outcome = %#v, want the first response's Failed", fields["Outcome"])
	}
	assertNoteAbsent(t, fields)
}

// Verifies a second apply that gets no answer leaves the first response on stdout and fails the
// command with the transport error.
func TestRunHotReloadKeepsTheFirstResponseWhenTheSecondRequestFails(t *testing.T) {
	useFastEditorReadyWait(t, fastEditorReadyWaitOptions())
	run := runScriptedHotReload(t, context.Background(), t.TempDir(), map[string]any{},
		[]scriptedIPCStep{
			{method: hotReloadCommandName, result: editorReadyRetryFirstRefused},
			{method: editorStatusBridgeCommandName, result: editorReadyRetryStatusReady},
			{method: hotReloadCommandName, drop: true},
		},
		editorReadyRetryNoCompile())

	if run.code != 1 || run.compileCalls != 0 {
		t.Fatalf("exit code = %d, compile calls = %d, want 1 and 0", run.code, run.compileCalls)
	}
	// Why the error code and not any text: the waiting line is on stderr whenever the retry runs.
	if !strings.Contains(run.stderr, "UNITY_DISCONNECTED_AFTER_DISPATCH") {
		t.Fatalf("stderr must carry the transport error: %q", run.stderr)
	}
	fields := run.stdoutFields(t)
	if fields["Message"] != "refused" {
		t.Fatalf("Message = %#v, want the first response's", fields["Message"])
	}
	assertNoteAbsent(t, fields)
}

// Verifies the retry writes its decision and its end to the CLI vibe log under the first request's
// correlation ID, and names the second request's ID.
func TestRunHotReloadWritesEditorReadyRetryVibeLogs(t *testing.T) {
	enableCliVibeLog(t)
	useFastEditorReadyWait(t, fastEditorReadyWaitOptions())
	projectRoot := t.TempDir()
	run := runScriptedHotReload(t, context.Background(), projectRoot, map[string]any{},
		[]scriptedIPCStep{
			{method: hotReloadCommandName, result: editorReadyRetryFirstRefused},
			{method: editorStatusBridgeCommandName, result: editorReadyRetryStatusReady},
			{method: hotReloadCommandName, result: editorReadyRetrySecondApplied},
		},
		editorReadyRetryNoCompile())
	if run.code != 0 {
		t.Fatalf("exit code = %d, want 0\nstderr=%s", run.code, run.stderr)
	}

	logContent := readOnlyCliVibeLog(t, projectRoot)
	sent := cliVibeEntriesForOperation(t, logContent, "cli_tool_request_sent")
	if len(sent) != 2 {
		t.Fatalf("cli_tool_request_sent entries = %d, want 2\n%s", len(sent), logContent)
	}
	decided := singleCliVibeEntry(t, logContent, "cli_hot_reload_editor_ready_retry_decided")
	complete := singleCliVibeEntry(t, logContent, "cli_hot_reload_editor_ready_retry_complete")
	assertCliVibeContextValues(t, cliVibeEntryContext(t, decided), map[string]any{"requested": true})
	completeContext := cliVibeEntryContext(t, complete)
	assertCliVibeContextValues(t, completeContext, map[string]any{
		"ready":          true,
		"second_success": true,
		"second_outcome": "Applied",
	})
	if _, isNumber := completeContext["waited_ms"].(float64); !isNumber {
		t.Fatalf("waited_ms = %#v, want a number", completeContext["waited_ms"])
	}
	assertSharedCliVibeCorrelationID(t, sent[0], decided)
	assertSharedCliVibeCorrelationID(t, decided, complete)
	second := vibeLogContextString(t, sent[1], "correlation_id")
	if completeContext["second_correlation_id"] != second {
		t.Fatalf("second_correlation_id = %#v, want the second request's %q", completeContext["second_correlation_id"], second)
	}
	// The fallback decision reads the second answer, so its entry follows the second request.
	fallbackDecided := singleCliVibeEntry(t, logContent, "cli_hot_reload_compile_fallback_decided")
	assertSharedCliVibeCorrelationID(t, sent[1], fallbackDecided)
}

// Verifies a response that does not ask for the retry, or cannot be read, logs the decision and no
// end entry.
func TestRunHotReloadLogsAnEditorReadyRetryThatWasNotAskedFor(t *testing.T) {
	cases := map[string]struct {
		response   string
		parseError bool
	}{
		"absent": {response: `{"Success":true,"Outcome":"Applied","CompileFallback":"NotNeeded","Message":"applied"}`},
		"null":   {response: `null`, parseError: true},
	}
	for name, testCase := range cases {
		t.Run(name, func(t *testing.T) {
			enableCliVibeLog(t)
			useFastEditorReadyWait(t, fastEditorReadyWaitOptions())
			projectRoot := t.TempDir()
			runScriptedHotReload(t, context.Background(), projectRoot, map[string]any{},
				[]scriptedIPCStep{{method: hotReloadCommandName, result: testCase.response}},
				editorReadyRetryNoCompile())

			logContent := readOnlyCliVibeLog(t, projectRoot)
			decided := singleCliVibeEntry(t, logContent, "cli_hot_reload_editor_ready_retry_decided")
			assertCliVibeContextValues(t, cliVibeEntryContext(t, decided), map[string]any{
				"requested":   false,
				"parse_error": testCase.parseError,
			})
			assertNoCliVibeEntry(t, logContent, "cli_hot_reload_editor_ready_retry_complete")
		})
	}
}

// Verifies a second response that is not a JSON object fails the command instead of being written
// without the note that says it is the second apply.
func TestRunHotReloadFailsWhenTheSecondResponseIsNotAnObject(t *testing.T) {
	enableCliVibeLog(t)
	useFastEditorReadyWait(t, fastEditorReadyWaitOptions())
	projectRoot := t.TempDir()
	run := runScriptedHotReload(t, context.Background(), projectRoot, map[string]any{},
		[]scriptedIPCStep{
			{method: hotReloadCommandName, result: editorReadyRetryFirstRefused},
			{method: editorStatusBridgeCommandName, result: editorReadyRetryStatusReady},
			{method: hotReloadCommandName, result: `null`},
		},
		editorReadyRetryNoCompile())

	if run.code != 1 || run.compileCalls != 0 {
		t.Fatalf("exit code = %d, compile calls = %d, want 1 and 0", run.code, run.compileCalls)
	}
	if run.stdout != "" || !strings.Contains(run.stderr, "must be a JSON object") {
		t.Fatalf("stdout must be empty and stderr must carry the error: stdout=%q stderr=%q", run.stdout, run.stderr)
	}
	complete := singleCliVibeEntry(t, readOnlyCliVibeLog(t, projectRoot), "cli_hot_reload_editor_ready_retry_complete")
	completeContext := cliVibeEntryContext(t, complete)
	assertCliVibeContextValues(t, completeContext, map[string]any{"second_result": true})
	assertCliVibeContextOmits(t, completeContext, "second_success", "second_outcome")
}

// Verifies the second apply's Success is logged as false when the Editor refused it again.
func TestRunHotReloadLogsASecondRefusalAsUnsuccessful(t *testing.T) {
	enableCliVibeLog(t)
	useFastEditorReadyWait(t, fastEditorReadyWaitOptions())
	projectRoot := t.TempDir()
	runScriptedHotReload(t, context.Background(), projectRoot, map[string]any{},
		[]scriptedIPCStep{
			{method: hotReloadCommandName, result: editorReadyRetryFirstRefused},
			{method: editorStatusBridgeCommandName, result: editorReadyRetryStatusReady},
			{method: hotReloadCommandName, result: `{"Success":false,"Outcome":"Failed","CompileFallback":"NotNeeded","Message":"refused"}`},
		},
		editorReadyRetryNoCompile())

	complete := singleCliVibeEntry(t, readOnlyCliVibeLog(t, projectRoot), "cli_hot_reload_editor_ready_retry_complete")
	assertCliVibeContextValues(t, cliVibeEntryContext(t, complete), map[string]any{
		"second_success": false,
		"second_outcome": "Failed",
	})
}

// Verifies only a JSON object takes the note: anything else is reported as an error.
func TestInjectHotReloadEditorReadyNoteRejectsANonObject(t *testing.T) {
	for _, raw := range []string{`null`, `[1]`, `not json`} {
		if _, err := injectHotReloadEditorReadyNote([]byte(raw), "note"); err == nil {
			t.Fatalf("injectHotReloadEditorReadyNote(%s) succeeded, want an error", raw)
		}
	}
}

// Verifies a SelectedFiles value that is not a list of strings counts as no list, so the request
// is resent as it was.
func TestHotReloadSelectedFilesTreatsAnUnreadableListAsNone(t *testing.T) {
	if files := hotReloadSelectedFiles([]byte(`{"SelectedFiles":"Assets/A.cs"}`)); files != nil {
		t.Fatalf("hotReloadSelectedFiles = %#v, want nil", files)
	}
}

// Verifies a wait is added to the EditorReadyWaitMs an earlier wait left, so the field covers every
// wait in the command.
func TestAddHotReloadEditorReadyWaitMsAddsToAnEarlierWait(t *testing.T) {
	cases := map[string]struct {
		prior string
		want  float64
	}{
		"earlier wait": {prior: `{"Timing":{"EditorReadyWaitMs":1500}}`, want: 3500},
		"no Timing":    {prior: `{}`, want: 2000},
		"null Timing":  {prior: `{"Timing":null}`, want: 2000},
	}
	for name, testCase := range cases {
		t.Run(name, func(t *testing.T) {
			merged, err := addHotReloadEditorReadyWaitMs([]byte(`{"Timing":{"TotalMs":1}}`), []byte(testCase.prior), 2*time.Second)
			if err != nil {
				t.Fatal(err)
			}
			fields := map[string]any{}
			if err := json.Unmarshal(merged, &fields); err != nil {
				t.Fatal(err)
			}
			timing, _ := fields["Timing"].(map[string]any)
			if timing["EditorReadyWaitMs"] != testCase.want {
				t.Fatalf("EditorReadyWaitMs = %#v, want %v", timing["EditorReadyWaitMs"], testCase.want)
			}
		})
	}
}

// Verifies a note is appended to the note an earlier wait left, in order.
func TestComposeHotReloadEditorReadyNoteAppendsToAnEarlierNote(t *testing.T) {
	if got := composeHotReloadEditorReadyNote([]byte(`{"EditorReadyRetryNote":"A."}`), "B."); got != "A. B." {
		t.Fatalf("composeHotReloadEditorReadyNote = %q, want %q", got, "A. B.")
	}
	if got := composeHotReloadEditorReadyNote([]byte(`{}`), "B."); got != "B." {
		t.Fatalf("composeHotReloadEditorReadyNote = %q, want %q", got, "B.")
	}
}
