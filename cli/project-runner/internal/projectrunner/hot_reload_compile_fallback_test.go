package projectrunner

import (
	"bytes"
	"context"
	"encoding/json"
	"io"
	"testing"

	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

// Verifies a hot-reload run that asks for the fallback compiles in the same command, reports the
// compile under Compile, and takes the compile's Success and exit code as its own.
func TestRunHotReloadRunsFallbackCompileAndAdoptsItsSuccess(t *testing.T) {
	stdout, stderr, compileCalls, code := runHotReloadWithFakeCompile(
		t,
		`{"Success":false,"CompileFallback":"Requested","RecommendedNextAction":"Run 'uloop compile'"}`,
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
