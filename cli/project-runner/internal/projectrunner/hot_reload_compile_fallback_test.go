package projectrunner

import (
	"bytes"
	"context"
	"encoding/json"
	"io"
	"strings"
	"testing"

	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

// Verifies a hot-reload run that asks for the fallback compiles in the same command, reports the
// compile under Compile, and takes the compile's Success and exit code as its own.
func TestRunHotReloadRunsFallbackCompileAndAdoptsItsSuccess(t *testing.T) {
	stdout, stderr, compileCalls, code := runHotReloadWithFakeCompile(
		t,
		`{"Success":false,"CompileFallback":"Requested","RecommendedNextAction":"Run 'uloop compile'","Message":"Hot reload finished with one or more Failed outcomes."}`,
		compileExecutionResult{result: json.RawMessage(`{"Success":true,"Message":"ok"}`), exitCode: 0},
	)

	if code != 0 {
		t.Fatalf("exit code mismatch: code=%d stdout=%s stderr=%s", code, stdout, stderr)
	}
	if compileCalls != 1 {
		t.Fatalf("compile call count mismatch: %d", compileCalls)
	}
	fields := decodeSingleJSONObject(t, stdout)
	if string(fields["Success"]) != "true" {
		t.Fatalf("Success must become the compile's: %s", stdout)
	}
	compile := map[string]json.RawMessage{}
	if err := json.Unmarshal(fields["Compile"], &compile); err != nil {
		t.Fatalf("Compile must carry the compile response: %v\n%s", err, stdout)
	}
	if string(compile["Message"]) != `"ok"` {
		t.Fatalf("Compile.Message mismatch: %s", stdout)
	}
	note := ""
	if err := json.Unmarshal(fields["CompileFallbackNote"], &note); err != nil || note == "" {
		t.Fatalf("CompileFallbackNote must be a nonempty string: %s", stdout)
	}
	if _, present := fields["RecommendedNextAction"]; present {
		t.Fatalf("RecommendedNextAction must be dropped once the compile succeeded: %s", stdout)
	}
	message := ""
	if err := json.Unmarshal(fields["Message"], &message); err != nil {
		t.Fatalf("Message must stay a string: %v\n%s", err, stdout)
	}
	if message != "Hot reload finished with one or more Failed outcomes. A compile then ran in this same command and succeeded; see CompileFallbackNote." {
		t.Fatalf("Message must say the compile succeeded after the reload: %q", message)
	}
}

// Verifies a hot-reload response whose Message is JSON null keeps it null when the fallback compile
// succeeds, instead of becoming a Message that holds only the compile sentence.
func TestInjectHotReloadCompileFallbackLeavesANullMessageNull(t *testing.T) {
	merged, err := injectHotReloadCompileFallback(
		json.RawMessage(`{"Success":false,"CompileFallback":"Requested","Message":null}`),
		json.RawMessage(`{"Success":true}`))
	if err != nil {
		t.Fatalf("inject failed: %v", err)
	}
	fields := decodeSingleJSONObject(t, string(merged))
	if string(fields["Message"]) != "null" {
		t.Fatalf("a null Message must stay null: %s", merged)
	}
}

// Verifies a hot-reload response without a Message gets none invented when the fallback compile
// succeeds.
func TestInjectHotReloadCompileFallbackLeavesAMissingMessageMissing(t *testing.T) {
	merged, err := injectHotReloadCompileFallback(
		json.RawMessage(`{"Success":false,"CompileFallback":"Requested"}`),
		json.RawMessage(`{"Success":true}`))
	if err != nil {
		t.Fatalf("inject failed: %v", err)
	}
	fields := decodeSingleJSONObject(t, string(merged))
	if _, present := fields["Message"]; present {
		t.Fatalf("Message must not be invented: %s", merged)
	}
}

// Verifies a failed fallback compile flips Success to false, points at Compile.Errors, and returns
// the compile's exit code.
func TestRunHotReloadReportsFallbackCompileFailure(t *testing.T) {
	stdout, stderr, compileCalls, code := runHotReloadWithFakeCompile(
		t,
		`{"Success":true,"CompileFallback":"Requested"}`,
		compileExecutionResult{result: json.RawMessage(`{"Success":false,"Errors":[{"Message":"CS0103"}]}`), exitCode: 1},
	)

	if code != 1 {
		t.Fatalf("exit code mismatch: code=%d stdout=%s stderr=%s", code, stdout, stderr)
	}
	if compileCalls != 1 {
		t.Fatalf("compile call count mismatch: %d", compileCalls)
	}
	fields := decodeSingleJSONObject(t, stdout)
	if string(fields["Success"]) != "false" {
		t.Fatalf("Success must become the compile's: %s", stdout)
	}
	nextAction := ""
	if err := json.Unmarshal(fields["RecommendedNextAction"], &nextAction); err != nil {
		t.Fatalf("RecommendedNextAction must be a string: %v\n%s", err, stdout)
	}
	if nextAction != "Fix the errors in Compile.Errors, then rerun 'uloop compile' or 'uloop hot-reload'." {
		t.Fatalf("RecommendedNextAction mismatch: %q", nextAction)
	}
}

// Verifies a failed fallback compile that reports its own next actions — such as a compile Unity
// refused during Play Mode — promotes them, joined into one sentence, instead of pointing at
// Compile.Errors; an empty list keeps the fixed advice.
func TestInjectHotReloadCompileFallbackPromotesTheCompileNextActions(t *testing.T) {
	cases := []struct {
		name        string
		compile     string
		wantNextAct string
	}{
		{
			name:        "one next action",
			compile:     `{"Success":false,"NextActions":["Run 'uloop control-play-mode --action Stop' to leave Play Mode, then rerun 'uloop compile'."]}`,
			wantNextAct: "Run 'uloop control-play-mode --action Stop' to leave Play Mode, then rerun 'uloop compile'.",
		},
		{
			name:        "several next actions",
			compile:     `{"Success":false,"NextActions":["First step.","Second step."]}`,
			wantNextAct: "First step. Second step.",
		},
		{
			name:        "empty next actions",
			compile:     `{"Success":false,"NextActions":[]}`,
			wantNextAct: "Fix the errors in Compile.Errors, then rerun 'uloop compile' or 'uloop hot-reload'.",
		},
	}
	for _, tc := range cases {
		t.Run(tc.name, func(t *testing.T) {
			merged, err := injectHotReloadCompileFallback(
				json.RawMessage(`{"Success":true,"CompileFallback":"Requested"}`),
				json.RawMessage(tc.compile))
			if err != nil {
				t.Fatalf("inject failed: %v", err)
			}
			fields := decodeSingleJSONObject(t, string(merged))
			nextAction := ""
			if err := json.Unmarshal(fields["RecommendedNextAction"], &nextAction); err != nil {
				t.Fatalf("RecommendedNextAction must be a string: %v\n%s", err, merged)
			}
			if nextAction != tc.wantNextAct {
				t.Fatalf("RecommendedNextAction mismatch: %q", nextAction)
			}
		})
	}
}

// Verifies every answer other than Requested — including a missing field from an older package and
// a value this CLI does not know — leaves the response untouched and runs no compile.
func TestRunHotReloadSkipsFallbackCompileUnlessRequested(t *testing.T) {
	cases := []struct {
		name     string
		response string
	}{
		{name: "NotNeeded", response: `{"Success":true,"CompileFallback":"NotNeeded"}`},
		{name: "HeldForPlayMode", response: `{"Success":true,"CompileFallback":"HeldForPlayMode"}`},
		{name: "Disabled", response: `{"Success":true,"CompileFallback":"Disabled"}`},
		{name: "FieldMissing", response: `{"Success":true}`},
		{name: "UnknownValue", response: `{"Success":true,"CompileFallback":"Later"}`},
	}

	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			stdout, stderr, compileCalls, code := runHotReloadWithFakeCompile(
				t,
				testCase.response,
				compileExecutionResult{result: json.RawMessage(`{"Success":true}`), exitCode: 0},
			)

			if code != 0 {
				t.Fatalf("exit code mismatch: code=%d stdout=%s stderr=%s", code, stdout, stderr)
			}
			if compileCalls != 0 {
				t.Fatalf("compile must not run: %d", compileCalls)
			}
			assertCompactJSONEqual(t, stdout, testCase.response)
		})
	}
}

// Verifies a fallback compile that produced no response (a transport failure already reported on
// stderr) still emits the hot-reload response and returns the compile's exit code.
func TestRunHotReloadKeepsResponseWhenFallbackCompileReturnsNothing(t *testing.T) {
	response := `{"Success":true,"CompileFallback":"Requested"}`
	stdout, stderr, compileCalls, code := runHotReloadWithFakeCompile(
		t,
		response,
		compileExecutionResult{exitCode: 1},
	)

	if code != 1 {
		t.Fatalf("exit code mismatch: code=%d stdout=%s stderr=%s", code, stdout, stderr)
	}
	if compileCalls != 1 {
		t.Fatalf("compile call count mismatch: %d", compileCalls)
	}
	assertCompactJSONEqual(t, stdout, response)
}

// Verifies a hot-reload payload that is not a JSON object is rejected instead of merged.
func TestInjectHotReloadCompileFallbackRejectsNonObjectPayload(t *testing.T) {
	if _, err := injectHotReloadCompileFallback([]byte("[]"), []byte(`{"Success":true}`)); err == nil {
		t.Fatal("expected an error for a non-object hot-reload response")
	}
}

// Verifies the fallback note points at Warnings only when the hot-reload response carries
// warnings, and at the per-method reasons when Warnings is missing, null, or empty.
func TestInjectHotReloadCompileFallbackPointsAtTheFieldThatExplainsTheSkip(t *testing.T) {
	cases := []struct {
		name     string
		response string
		want     string
	}{
		{"with warnings", `{"Warnings":["Skipped A.B(): reason"]}`, "(see Warnings)"},
		{"empty warnings", `{"Warnings":[]}`, "(see Methods[].Reason)"},
		{"null warnings", `{"Warnings":null}`, "(see Methods[].Reason)"},
		{"missing warnings", `{}`, "(see Methods[].Reason)"},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			merged, err := injectHotReloadCompileFallback([]byte(testCase.response), []byte(`{"Success":true}`))
			if err != nil {
				t.Fatalf("inject failed: %v", err)
			}
			var fields map[string]json.RawMessage
			if err := json.Unmarshal(merged, &fields); err != nil {
				t.Fatalf("merged response is not an object: %v", err)
			}
			var note string
			if err := json.Unmarshal(fields["CompileFallbackNote"], &note); err != nil {
				t.Fatalf("CompileFallbackNote must be a string: %s", merged)
			}
			if !strings.Contains(note, testCase.want) {
				t.Fatalf("note %q does not contain %q", note, testCase.want)
			}
		})
	}
}

// Serves one hot-reload response over IPC, replaces the fallback compile with a fixed result, and
// runs the command through runTool so a missing dispatch branch fails the test.
func runHotReloadWithFakeCompile(
	t *testing.T,
	hotReloadResponse string,
	compileResult compileExecutionResult,
) (string, string, int, int) {
	t.Helper()
	projectRoot := t.TempDir()
	listener := newLoopbackIpcListener(t)
	requests := make(chan map[string]any, 1)
	serverErr := make(chan error, 1)
	go serveSingleIPCResponse(listener, hotReloadCommandName, requests, serverErr, hotReloadResponse)

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

	code := runTool(context.Background(), connection, hotReloadCommandName, map[string]any{}, &stdout, &stderr)
	readIPCRequest(t, requests)
	assertServerDidNotFail(t, serverErr)
	return stdout.String(), stderr.String(), compileCalls, code
}

// Decodes the one JSON object the command is allowed to write to stdout.
func decodeSingleJSONObject(t *testing.T, output string) map[string]json.RawMessage {
	t.Helper()
	decoder := json.NewDecoder(bytes.NewReader([]byte(output)))
	fields := map[string]json.RawMessage{}
	if err := decoder.Decode(&fields); err != nil {
		t.Fatalf("stdout must contain JSON: %v\n%s", err, output)
	}
	var extra any
	if err := decoder.Decode(&extra); err != io.EOF {
		t.Fatalf("stdout must contain one JSON response: %s", output)
	}
	return fields
}

// Compares stdout with an expected payload after compacting both, so formatting cannot hide a
// field the command added or removed.
func assertCompactJSONEqual(t *testing.T, output string, expected string) {
	t.Helper()
	var actualCompact bytes.Buffer
	if err := json.Compact(&actualCompact, []byte(output)); err != nil {
		t.Fatalf("stdout must contain JSON: %v\n%s", err, output)
	}
	var expectedCompact bytes.Buffer
	if err := json.Compact(&expectedCompact, []byte(expected)); err != nil {
		t.Fatalf("expected payload must be JSON: %v", err)
	}
	if actualCompact.String() != expectedCompact.String() {
		t.Fatalf("response must pass through unchanged:\nwant %s\ngot  %s", expectedCompact.String(), actualCompact.String())
	}
}
