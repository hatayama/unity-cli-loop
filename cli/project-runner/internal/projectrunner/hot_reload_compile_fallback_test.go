package projectrunner

import (
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	"io"
	"reflect"
	"strings"
	"testing"
	"time"

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
	return runHotReloadWithDelayedFakeCompileInProjectRoot(t, t.TempDir(), hotReloadResponse, compileResult, 0)
}

// Same as runHotReloadWithFakeCompile, but the fake compile answers only after delay, so a test can
// tell a compile time that was measured from one that was never measured.
func runHotReloadWithDelayedFakeCompile(
	t *testing.T,
	hotReloadResponse string,
	compileResult compileExecutionResult,
	delay time.Duration,
) (string, string, int, int) {
	t.Helper()
	return runHotReloadWithDelayedFakeCompileInProjectRoot(t, t.TempDir(), hotReloadResponse, compileResult, delay)
}

// Same as runHotReloadWithDelayedFakeCompile, in the given project root, so a test can read the
// vibe log the command wrote there.
func runHotReloadWithDelayedFakeCompileInProjectRoot(
	t *testing.T,
	projectRoot string,
	hotReloadResponse string,
	compileResult compileExecutionResult,
	delay time.Duration,
) (string, string, int, int) {
	t.Helper()
	listener := newLoopbackIpcListener(t)
	requests := make(chan map[string]any, 1)
	serverErr := make(chan error, 1)
	go serveSingleIPCResponse(listener, hotReloadCommandName, requests, serverErr, hotReloadResponse)

	original := hotReloadFallbackCompile
	compileCalls := 0
	hotReloadFallbackCompile = func(context.Context, unityipc.Connection, io.Writer) compileExecutionResult {
		compileCalls++
		time.Sleep(delay)
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

// Verifies a hot-reload request Unity rejects exits with the failure on stderr and never compiles.
func TestRunHotReloadWithCompileFallbackSkipsCompileWhenReloadFails(t *testing.T) {
	original := hotReloadFallbackCompile
	t.Cleanup(func() { hotReloadFallbackCompile = original })
	hotReloadFallbackCompile = func(context.Context, unityipc.Connection, io.Writer) compileExecutionResult {
		t.Fatal("the fallback compile must not run after a failed reload request")
		return compileExecutionResult{}
	}
	server := startFakeUnityServer(t, t.TempDir(), hotReloadCommandName, testUnityRPCFailureResponse)
	var stdout, stderr bytes.Buffer

	code := runHotReloadWithCompileFallback(context.Background(), server.connection, map[string]any{}, &stdout, &stderr)

	if code != 1 || stdout.Len() != 0 {
		t.Fatalf("code=%d stdout=%q", code, stdout.String())
	}
	server.receivedRequest(t)
	if !strings.Contains(stderr.String(), "tool exploded in Unity") {
		t.Fatalf("stderr must carry the Unity error:\n%s", stderr.String())
	}
}

// Verifies a fallback compile result that cannot be decoded fails the command instead of
// printing a merged response with a guessed Success.
func TestRunHotReloadFailsWhenFallbackCompileResultIsUndecodable(t *testing.T) {
	stdout, stderr, compileCalls, code := runHotReloadWithFakeCompile(
		t,
		`{"Success":false,"CompileFallback":"Requested","Message":"Hot reload left edits unapplied."}`,
		compileExecutionResult{result: json.RawMessage(`[1]`), exitCode: 0},
	)

	if code != 1 || stdout != "" || compileCalls != 1 {
		t.Fatalf("code=%d compileCalls=%d stdout=%q", code, compileCalls, stdout)
	}
	if !strings.Contains(stderr, "cannot unmarshal array") {
		t.Fatalf("stderr must report the decode failure:\n%s", stderr)
	}
}

// Verifies a reload answer that fails to decode never counts as a fallback request, even after the
// request value itself was decoded, and a JSON null reload response is rejected rather than merged.
func TestHotReloadCompileFallbackRejectsNonObjectResponses(t *testing.T) {
	// Why the duplicate key: the decoder keeps "Requested" when the second value fails, so only the
	// decode-error check keeps the half-read answer from requesting a compile.
	if isHotReloadCompileFallbackRequested([]byte(`{"CompileFallback":"Requested","CompileFallback":1}`)) {
		t.Fatal("an answer that fails to decode must not request a compile")
	}
	_, err := injectHotReloadCompileFallback(json.RawMessage(`null`), json.RawMessage(`{"Success":true}`))
	if err == nil || err.Error() != "hot-reload response must be a JSON object" {
		t.Fatalf("expected the non-object error, got %v", err)
	}
}

// Verifies a successful fallback compile turns the reload's Outcome into ReplacedByCompile, reports
// the Auto Refresh hold released, and removes exactly the hold sentence the reload appended to
// Message, with its leading space, while AutoRefreshHoldMessage stays as the record of what was
// removed.
func TestInjectHotReloadCompileFallback_CompileSucceeded_SettlesFinalState(t *testing.T) {
	cases := []struct {
		name        string
		reload      string
		wantMessage string
	}{
		{
			name:        "nothing applied",
			reload:      `{"Success":true,"Outcome":"NothingApplied","CompileFallback":"Requested","AutoRefreshHeld":true,"AutoRefreshHoldMessage":"HOLD SENTENCE","Message":"Hot reload applied. PatchedTotal=0, ActivePatchTotal=0. Skipped: 2. HOLD SENTENCE"}`,
			wantMessage: "Hot reload applied. PatchedTotal=0, ActivePatchTotal=0. Skipped: 2.",
		},
		{
			name:        "partially applied",
			reload:      `{"Success":true,"Outcome":"PartiallyApplied","CompileFallback":"Requested","AutoRefreshHeld":true,"AutoRefreshHoldMessage":"HOLD SENTENCE","Message":"Hot reload applied. PatchedTotal=1, ActivePatchTotal=1. Skipped: 1. HOLD SENTENCE"}`,
			wantMessage: "Hot reload applied. PatchedTotal=1, ActivePatchTotal=1. Skipped: 1.",
		},
	}
	for _, tc := range cases {
		t.Run(tc.name, func(t *testing.T) {
			merged, err := injectHotReloadCompileFallback(json.RawMessage(tc.reload), json.RawMessage(`{"Success":true}`))
			if err != nil {
				t.Fatalf("inject failed: %v", err)
			}
			fields := decodeSingleJSONObject(t, string(merged))
			assertJSONStringField(t, fields, "Outcome", "ReplacedByCompile")
			if string(fields["AutoRefreshHeld"]) != "false" {
				t.Fatalf("AutoRefreshHeld must be false once the compile released the hold: %s", merged)
			}
			assertJSONStringField(t, fields, "Message", tc.wantMessage+hotReloadCompileFallbackSucceededMessageSuffix)
			assertJSONStringField(t, fields, "AutoRefreshHoldMessage", "HOLD SENTENCE")
		})
	}
}

// Verifies a failed fallback compile leaves Outcome, AutoRefreshHeld, AutoRefreshHoldMessage and
// Message as the reload wrote them.
func TestInjectHotReloadCompileFallback_CompileFailed_KeepsReloadState(t *testing.T) {
	merged, err := injectHotReloadCompileFallback(
		json.RawMessage(`{"Success":true,"Outcome":"NothingApplied","CompileFallback":"Requested","AutoRefreshHeld":true,"AutoRefreshHoldMessage":"HOLD SENTENCE","Message":"Hot reload applied. PatchedTotal=0, ActivePatchTotal=0. Skipped: 2. HOLD SENTENCE"}`),
		json.RawMessage(`{"Success":false,"Errors":[{"Message":"CS0103"}]}`))
	if err != nil {
		t.Fatalf("inject failed: %v", err)
	}
	fields := decodeSingleJSONObject(t, string(merged))
	assertJSONStringField(t, fields, "Outcome", "NothingApplied")
	if string(fields["AutoRefreshHeld"]) != "true" {
		t.Fatalf("AutoRefreshHeld must stay as the reload reported it: %s", merged)
	}
	assertJSONStringField(t, fields, "Message", "Hot reload applied. PatchedTotal=0, ActivePatchTotal=0. Skipped: 2. HOLD SENTENCE")
	assertJSONStringField(t, fields, "AutoRefreshHoldMessage", "HOLD SENTENCE")
}

// Verifies a successful fallback compile over an older package's response, which has neither
// Outcome nor AutoRefreshHoldMessage, still adds Outcome ReplacedByCompile, appends the compile
// sentence without removing anything from Message, and turns AutoRefreshHeld false only when the
// response has the field.
func TestInjectHotReloadCompileFallback_OlderPackageWithoutOutcome_StillSettles(t *testing.T) {
	cases := []struct {
		name        string
		reload      string
		wantMessage string
		// Raw JSON of AutoRefreshHeld after the merge; empty when the field must stay absent.
		wantAutoRefreshHeld string
	}{
		{
			name:                "with AutoRefreshHeld",
			reload:              `{"Success":true,"CompileFallback":"Requested","AutoRefreshHeld":true,"Message":"Hot reload applied. PatchedTotal=0, ActivePatchTotal=0. Skipped: 2. HOLD SENTENCE"}`,
			wantMessage:         "Hot reload applied. PatchedTotal=0, ActivePatchTotal=0. Skipped: 2. HOLD SENTENCE",
			wantAutoRefreshHeld: "false",
		},
		{
			name:        "without AutoRefreshHeld",
			reload:      `{"Success":true,"CompileFallback":"Requested","Message":"Hot reload applied. PatchedTotal=0, ActivePatchTotal=0. Skipped: 2."}`,
			wantMessage: "Hot reload applied. PatchedTotal=0, ActivePatchTotal=0. Skipped: 2.",
		},
	}
	for _, tc := range cases {
		t.Run(tc.name, func(t *testing.T) {
			merged, err := injectHotReloadCompileFallback(json.RawMessage(tc.reload), json.RawMessage(`{"Success":true}`))
			if err != nil {
				t.Fatalf("inject failed: %v", err)
			}
			fields := decodeSingleJSONObject(t, string(merged))
			assertJSONStringField(t, fields, "Outcome", "ReplacedByCompile")
			assertJSONStringField(t, fields, "Message", tc.wantMessage+hotReloadCompileFallbackSucceededMessageSuffix)
			if string(fields["AutoRefreshHeld"]) != tc.wantAutoRefreshHeld {
				t.Fatalf("AutoRefreshHeld mismatch: want %q, got %q", tc.wantAutoRefreshHeld, fields["AutoRefreshHeld"])
			}
		})
	}
}

// Verifies a fallback compile that succeeded after a reload with Failed rows replaces the Failed
// Outcome with ReplacedByCompile, together with Success.
func TestInjectHotReloadCompileFallback_CompileSucceededAfterFailedReload_ReplacesFailedOutcome(t *testing.T) {
	merged, err := injectHotReloadCompileFallback(
		json.RawMessage(`{"Success":false,"Outcome":"Failed","CompileFallback":"Requested","Message":"Hot reload finished with one or more Failed outcomes."}`),
		json.RawMessage(`{"Success":true}`))
	if err != nil {
		t.Fatalf("inject failed: %v", err)
	}
	fields := decodeSingleJSONObject(t, string(merged))
	if string(fields["Success"]) != "true" {
		t.Fatalf("Success must become the compile's: %s", merged)
	}
	assertJSONStringField(t, fields, "Outcome", "ReplacedByCompile")
}

// Verifies a successful fallback compile removes the hold sentence only from the end of Message,
// where the reload appended it, and leaves the same text elsewhere in Message alone.
func TestInjectHotReloadCompileFallback_CompileSucceeded_RemovesTheHoldSentenceOnlyAtTheEnd(t *testing.T) {
	merged, err := injectHotReloadCompileFallback(
		json.RawMessage(`{"Success":true,"Outcome":"NothingApplied","CompileFallback":"Requested","AutoRefreshHeld":true,"AutoRefreshHoldMessage":"HOLD SENTENCE","Message":"Skipped: 2. HOLD SENTENCE See Warnings."}`),
		json.RawMessage(`{"Success":true}`))
	if err != nil {
		t.Fatalf("inject failed: %v", err)
	}
	fields := decodeSingleJSONObject(t, string(merged))
	assertJSONStringField(t, fields, "Message", "Skipped: 2. HOLD SENTENCE See Warnings."+hotReloadCompileFallbackSucceededMessageSuffix)
}

// Verifies the fallback compile's time joins the phases the Editor reported in Timing, which stay
// as they were.
func TestAddHotReloadFallbackCompileTimingKeepsTheEditorPhases(t *testing.T) {
	fields := addHotReloadFallbackCompileTimingOf(
		t,
		`{"Success":true,"Timing":{"AnalysisMs":120,"ShimCompileMs":800,"PatchMs":3,"TotalMs":1000}}`)

	want := map[string]int64{"AnalysisMs": 120, "ShimCompileMs": 800, "PatchMs": 3, "TotalMs": 1000, "FallbackCompileMs": 1500}
	if timing := decodeHotReloadTiming(t, fields); !reflect.DeepEqual(timing, want) {
		t.Fatalf("Timing mismatch:\nwant %v\ngot  %v", want, timing)
	}
}

// Verifies a response from an older package, which sends no Timing, gets one holding only the
// fallback compile's time.
func TestAddHotReloadFallbackCompileTimingCreatesTimingForAnOlderPackage(t *testing.T) {
	fields := addHotReloadFallbackCompileTimingOf(t, `{"Success":true}`)

	want := map[string]int64{"FallbackCompileMs": 1500}
	if timing := decodeHotReloadTiming(t, fields); !reflect.DeepEqual(timing, want) {
		t.Fatalf("Timing mismatch:\nwant %v\ngot  %v", want, timing)
	}
}

// Verifies a Timing of JSON null counts as absent and becomes an object holding the fallback
// compile's time.
func TestAddHotReloadFallbackCompileTimingTreatsNullTimingAsAbsent(t *testing.T) {
	fields := addHotReloadFallbackCompileTimingOf(t, `{"Success":true,"Timing":null}`)

	want := map[string]int64{"FallbackCompileMs": 1500}
	if timing := decodeHotReloadTiming(t, fields); !reflect.DeepEqual(timing, want) {
		t.Fatalf("Timing mismatch:\nwant %v\ngot  %v", want, timing)
	}
}

// Verifies adding the fallback compile's time leaves every other field of the merged response as
// it was.
func TestAddHotReloadFallbackCompileTimingKeepsTheOtherFields(t *testing.T) {
	fields := addHotReloadFallbackCompileTimingOf(t, `{"Success":false,"CompileFallbackNote":"x"}`)

	if len(fields) != 3 || string(fields["Success"]) != "false" || string(fields["CompileFallbackNote"]) != `"x"` {
		t.Fatalf("only Timing may be added to the merged response: %v", fields)
	}
}

// Verifies a successful fallback compile reports how long it ran in Timing.FallbackCompileMs, beside
// the phases the Editor reported.
func TestRunHotReloadRecordsFallbackCompileMsWhenTheCompileSucceeded(t *testing.T) {
	stdout, stderr, _, code := runHotReloadWithDelayedFakeCompile(
		t,
		`{"Success":true,"CompileFallback":"Requested","Timing":{"AnalysisMs":120,"ShimCompileMs":800,"PatchMs":3,"TotalMs":1000}}`,
		compileExecutionResult{result: json.RawMessage(`{"Success":true}`), exitCode: 0},
		20*time.Millisecond,
	)

	if code != 0 {
		t.Fatalf("exit code mismatch: code=%d stdout=%s stderr=%s", code, stdout, stderr)
	}
	timing := decodeHotReloadTiming(t, decodeSingleJSONObject(t, stdout))
	if timing["FallbackCompileMs"] < 20 || timing["AnalysisMs"] != 120 {
		t.Fatalf("Timing must keep the Editor phases and measure the compile: %v", timing)
	}
}

// Verifies a failed fallback compile still reports how long it ran, while Success stays false.
func TestRunHotReloadRecordsFallbackCompileMsWhenTheCompileFailed(t *testing.T) {
	stdout, stderr, _, code := runHotReloadWithDelayedFakeCompile(
		t,
		`{"Success":true,"CompileFallback":"Requested"}`,
		compileExecutionResult{result: json.RawMessage(`{"Success":false,"Errors":[{"Message":"CS0103"}]}`), exitCode: 1},
		20*time.Millisecond,
	)

	if code != 1 {
		t.Fatalf("exit code mismatch: code=%d stdout=%s stderr=%s", code, stdout, stderr)
	}
	fields := decodeSingleJSONObject(t, stdout)
	if string(fields["Success"]) != "false" {
		t.Fatalf("Success must stay the failed compile's: %s", stdout)
	}
	if timing := decodeHotReloadTiming(t, fields); timing["FallbackCompileMs"] < 20 {
		t.Fatalf("Timing must measure the failed compile too: %v", timing)
	}
}

// Adds a 1500 ms fallback compile to a merged response and decodes the result.
func addHotReloadFallbackCompileTimingOf(t *testing.T, merged string) map[string]json.RawMessage {
	t.Helper()
	withTiming, err := addHotReloadFallbackCompileTiming([]byte(merged), 1500*time.Millisecond)
	if err != nil {
		t.Fatalf("adding the fallback compile time failed: %v", err)
	}
	return decodeSingleJSONObject(t, string(withTiming))
}

// Decodes the Timing object of a response into its millisecond values; a missing Timing fails.
func decodeHotReloadTiming(t *testing.T, fields map[string]json.RawMessage) map[string]int64 {
	t.Helper()
	timing := map[string]int64{}
	if err := json.Unmarshal(fields["Timing"], &timing); err != nil {
		t.Fatalf("Timing must be an object of milliseconds: %v (raw %s)", err, fields["Timing"])
	}
	return timing
}

// Fails the test unless the field holds want as a JSON string.
func assertJSONStringField(t *testing.T, fields map[string]json.RawMessage, name string, want string) {
	t.Helper()
	got := ""
	if err := json.Unmarshal(fields[name], &got); err != nil {
		t.Fatalf("%s must be a JSON string: %v (raw %s)", name, err, fields[name])
	}
	if got != want {
		t.Fatalf("%s mismatch:\nwant %q\ngot  %q", name, want, got)
	}
}

// A reload response that asks for the fallback, carrying text no log entry may copy: the warnings,
// the message, and a Timing field that is not a number.
const hotReloadVibeLogRequestedResponse = `{"Success":false,"Outcome":"Failed","CompileFallback":"Requested",` +
	`"Warnings":["` + plainToolLogSentinel + ` first","` + plainToolLogSentinel + ` second"],` +
	`"Timing":{"AnalysisMs":7,"TotalMs":12,"Note":"` + plainToolLogSentinel + `"},` +
	`"Message":"` + plainToolLogSentinel + `"}`

// Verifies a run that asks for the fallback logs the decision and the compile's completion under
// one correlation ID, besides the reload's own request and response, and copies no response text.
func TestRunHotReloadWritesFallbackDecidedAndCompleteVibeLogs(t *testing.T) {
	enableCliVibeLog(t)
	projectRoot := t.TempDir()

	_, _, _, code := runHotReloadWithDelayedFakeCompileInProjectRoot(
		t,
		projectRoot,
		hotReloadVibeLogRequestedResponse,
		compileExecutionResult{result: json.RawMessage(`{"Success":true,"Message":"ok"}`), exitCode: 0},
		0)

	if code != 0 {
		t.Fatalf("exit code = %d, want 0", code)
	}
	logContent := readOnlyCliVibeLog(t, projectRoot)
	singleCliVibeEntry(t, logContent, "cli_tool_request_sent")
	singleCliVibeEntry(t, logContent, "cli_tool_response_received")
	decided := singleCliVibeEntry(t, logContent, "cli_hot_reload_compile_fallback_decided")
	complete := singleCliVibeEntry(t, logContent, "cli_hot_reload_compile_fallback_complete")
	assertCliVibeEntryLevel(t, decided, "INFO")
	assertCliVibeEntryLevel(t, complete, "INFO")
	decidedContext := cliVibeEntryContext(t, decided)
	assertCliVibeContextValues(t, decidedContext, map[string]any{
		"requested":      true,
		"parse_error":    false,
		"success":        false,
		"outcome":        "Failed",
		"warnings_count": float64(2),
	})
	if timing := fmt.Sprint(decidedContext["timing"]); timing != "map[AnalysisMs:7 TotalMs:12]" {
		t.Fatalf("timing = %s, want only the numeric phases", timing)
	}
	completeContext := cliVibeEntryContext(t, complete)
	assertCliVibeContextValues(t, completeContext, map[string]any{
		"merged":            true,
		"succeeded":         true,
		"compile_exit_code": float64(0),
	})
	if resultBytes, _ := completeContext["compile_result_bytes"].(float64); resultBytes <= 0 {
		t.Fatalf("compile_result_bytes = %#v, want a positive number", completeContext["compile_result_bytes"])
	}
	if _, ok := completeContext["elapsed_ms"].(float64); !ok {
		t.Fatalf("elapsed_ms = %#v, want a number", completeContext["elapsed_ms"])
	}
	assertSharedCliVibeCorrelationID(t, decided, complete)
	assertCliVibeLogOmitsTheSentinel(t, logContent)
}

// Verifies a run that needs no fallback still logs the decision, so the log shows the fallback was
// weighed and not run, and logs each field the response lacks as absent.
func TestRunHotReloadWritesFallbackDecidedWhenNotRequested(t *testing.T) {
	enableCliVibeLog(t)
	projectRoot := t.TempDir()

	runHotReloadWithDelayedFakeCompileInProjectRoot(
		t,
		projectRoot,
		`{"Success":true,"CompileFallback":"NotNeeded"}`,
		compileExecutionResult{result: json.RawMessage(`{"Success":true}`), exitCode: 0},
		0)

	logContent := readOnlyCliVibeLog(t, projectRoot)
	decidedContext := cliVibeEntryContext(t, singleCliVibeEntry(t, logContent, "cli_hot_reload_compile_fallback_decided"))
	assertCliVibeContextValues(t, decidedContext, map[string]any{
		"requested":      false,
		"parse_error":    false,
		"success":        true,
		"outcome":        "",
		"warnings_count": float64(0),
	})
	assertCliVibeContextOmits(t, decidedContext, "timing")
	assertNoCliVibeEntry(t, logContent, "cli_hot_reload_compile_fallback_complete")
}

// Verifies a reload response that is not an object is logged as a parse error that requested
// nothing, with no field read from it.
func TestRunHotReloadWritesFallbackDecidedWithParseErrorForANullResponse(t *testing.T) {
	enableCliVibeLog(t)
	projectRoot := t.TempDir()

	runHotReloadWithDelayedFakeCompileInProjectRoot(
		t,
		projectRoot,
		`null`,
		compileExecutionResult{result: json.RawMessage(`{"Success":true}`), exitCode: 0},
		0)

	logContent := readOnlyCliVibeLog(t, projectRoot)
	decidedContext := cliVibeEntryContext(t, singleCliVibeEntry(t, logContent, "cli_hot_reload_compile_fallback_decided"))
	assertCliVibeContextValues(t, decidedContext, map[string]any{
		"requested":   false,
		"parse_error": true,
	})
	assertCliVibeContextOmits(t, decidedContext, "success", "outcome", "warnings_count", "timing")
	assertNoCliVibeEntry(t, logContent, "cli_hot_reload_compile_fallback_complete")
}

// Verifies a fallback compile that failed is logged as an error, though its result was merged into
// the response.
func TestRunHotReloadWritesFallbackCompleteAsErrorWhenTheCompileFails(t *testing.T) {
	enableCliVibeLog(t)

	complete, _ := runHotReloadFallbackAndReadTheCompleteEntry(
		t,
		compileExecutionResult{result: json.RawMessage(`{"Success":false,"Errors":[]}`), exitCode: 1})

	assertCliVibeEntryLevel(t, complete, "ERROR")
	assertCliVibeContextValues(t, cliVibeEntryContext(t, complete), map[string]any{
		"merged":            true,
		"succeeded":         false,
		"compile_exit_code": float64(1),
	})
}

// Verifies a fallback compile that returned nothing is logged as an error that merged nothing,
// even with exit code 0.
func TestRunHotReloadWritesFallbackCompleteAsErrorWhenTheCompileReturnsNothing(t *testing.T) {
	enableCliVibeLog(t)

	complete, _ := runHotReloadFallbackAndReadTheCompleteEntry(t, compileExecutionResult{})

	assertCliVibeEntryLevel(t, complete, "ERROR")
	assertCliVibeContextValues(t, cliVibeEntryContext(t, complete), map[string]any{
		"merged":               false,
		"succeeded":            false,
		"compile_exit_code":    float64(0),
		"compile_result_bytes": float64(0),
	})
}

// Verifies a fallback compile whose result cannot be merged is logged as an error, matching the
// command's exit code 1, although the compile itself exited 0.
func TestRunHotReloadWritesFallbackCompleteAsErrorWhenTheCompileResultIsUndecodable(t *testing.T) {
	enableCliVibeLog(t)

	complete, code := runHotReloadFallbackAndReadTheCompleteEntry(
		t,
		compileExecutionResult{result: json.RawMessage(`[1]`), exitCode: 0})

	if code != 1 {
		t.Fatalf("exit code = %d, want 1", code)
	}
	assertCliVibeEntryLevel(t, complete, "ERROR")
	assertCliVibeContextValues(t, cliVibeEntryContext(t, complete), map[string]any{
		"merged":            false,
		"succeeded":         false,
		"compile_exit_code": float64(0),
	})
}

// Runs a reload that asks for the fallback against the given compile result, and returns the one
// completion entry it logged with the command's exit code.
func runHotReloadFallbackAndReadTheCompleteEntry(t *testing.T, compileResult compileExecutionResult) (map[string]any, int) {
	t.Helper()
	projectRoot := t.TempDir()
	_, _, compileCalls, code := runHotReloadWithDelayedFakeCompileInProjectRoot(
		t,
		projectRoot,
		hotReloadVibeLogRequestedResponse,
		compileResult,
		0)
	if compileCalls != 1 {
		t.Fatalf("compile call count = %d, want 1", compileCalls)
	}
	return singleCliVibeEntry(t, readOnlyCliVibeLog(t, projectRoot), "cli_hot_reload_compile_fallback_complete"), code
}

func assertCliVibeContextValues(t *testing.T, contextMap map[string]any, want map[string]any) {
	t.Helper()
	for key, value := range want {
		if contextMap[key] != value {
			t.Fatalf("context %s = %#v, want %#v\n%#v", key, contextMap[key], value, contextMap)
		}
	}
}

func assertCliVibeContextOmits(t *testing.T, contextMap map[string]any, keys ...string) {
	t.Helper()
	for _, key := range keys {
		if value, present := contextMap[key]; present {
			t.Fatalf("context %s must be absent, got %#v", key, value)
		}
	}
}

func assertNoCliVibeEntry(t *testing.T, logContent string, operation string) {
	t.Helper()
	if entries := cliVibeEntriesForOperation(t, logContent, operation); len(entries) != 0 {
		t.Fatalf("%s entries = %d, want 0", operation, len(entries))
	}
}
