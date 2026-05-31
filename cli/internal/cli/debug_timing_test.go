package cli

import (
	"bytes"
	"strings"
	"testing"
	"time"

	"github.com/hatayama/unity-cli-loop/cli/internal/unityipc"
)

// Verifies that timing output stays disabled unless explicitly requested.
func TestWriteDebugTiming_WhenEnvironmentIsUnset_ShouldStaySilent(t *testing.T) {
	t.Setenv(debugTimingEnvName, "")
	var stderr bytes.Buffer

	writeDebugTiming(&stderr, "execute-dynamic-code", time.Millisecond, unityipc.UnitySendOutcome{})

	if stderr.Len() != 0 {
		t.Fatalf("debug timing wrote output while disabled: %q", stderr.String())
	}
}

// Verifies that timing output includes command and RPC phase durations when enabled.
func TestWriteDebugTiming_WhenEnvironmentIsEnabled_ShouldWriteTimingLine(t *testing.T) {
	t.Setenv(debugTimingEnvName, "1")
	var stderr bytes.Buffer

	writeDebugTiming(
		&stderr,
		"execute-dynamic-code",
		10*time.Millisecond,
		unityipc.UnitySendOutcome{
			Timing: unityipc.UnitySendTiming{
				Total:  9 * time.Millisecond,
				Dial:   time.Millisecond,
				Write:  2 * time.Millisecond,
				Read:   5 * time.Millisecond,
				Decode: time.Millisecond,
			},
		},
	)

	output := stderr.String()
	for _, expected := range []string{
		"[uloop timing]",
		"command=execute-dynamic-code",
		"total=10ms",
		"rpc_total=9ms",
		"dial=1ms",
		"write=2ms",
		"read=5ms",
		"decode=1ms",
	} {
		if !strings.Contains(output, expected) {
			t.Fatalf("debug timing output missing %q: %s", expected, output)
		}
	}
}

// Verifies that debug timing requests Unity-side timings only for execute-dynamic-code.
func TestApplyDebugTimingParams_WhenEnabledForDynamicCode_ShouldRequestUnityTimings(t *testing.T) {
	t.Setenv(debugTimingEnvName, "1")
	params := map[string]any{}

	applyDebugTimingParams(executeDynamicCodeCommandName, params)

	if params[dynamicCodeIncludeTimingsParamName] != true {
		t.Fatalf("IncludeTimings was not requested: %#v", params)
	}
}

// Verifies that debug timing does not alter other tool requests.
func TestApplyDebugTimingParams_WhenEnabledForOtherCommand_ShouldLeaveParamsUnchanged(t *testing.T) {
	t.Setenv(debugTimingEnvName, "1")
	params := map[string]any{}

	applyDebugTimingParams("get-logs", params)

	if _, ok := params[dynamicCodeIncludeTimingsParamName]; ok {
		t.Fatalf("IncludeTimings was added for another command: %#v", params)
	}
}

// Verifies that Unity-side timing entries are mirrored to stderr for diagnosis.
func TestWriteDebugTiming_WhenUnityTimingsExist_ShouldWriteUnityTimingLines(t *testing.T) {
	t.Setenv(debugTimingEnvName, "1")
	var stderr bytes.Buffer
	outcome := unityipc.UnitySendOutcome{
		Result: []byte(`{"Success":true,"Timings":["[Perf] Build: 12.3ms","[Perf] Execution: 4.5ms"]}`),
	}

	writeDebugTiming(&stderr, executeDynamicCodeCommandName, time.Millisecond, outcome)

	output := stderr.String()
	for _, expected := range []string{
		"[uloop timing] unity [Perf] Build: 12.3ms",
		"[uloop timing] unity [Perf] Execution: 4.5ms",
	} {
		if !strings.Contains(output, expected) {
			t.Fatalf("debug timing output missing %q: %s", expected, output)
		}
	}
}

// Verifies that debug-only Unity timings are removed before printing JSON stdout.
func TestStripDebugTimingResult_WhenUnityTimingsExist_ShouldRemoveTimings(t *testing.T) {
	t.Setenv(debugTimingEnvName, "1")
	result := stripDebugTimingResult(
		executeDynamicCodeCommandName,
		[]byte(`{"Success":true,"Result":"ok","Timings":["[Perf] Build: 12.3ms"]}`),
	)

	output := string(result)
	if strings.Contains(output, "Timings") {
		t.Fatalf("Timings remained in sanitized result: %s", output)
	}
	if !strings.Contains(output, `"Result":"ok"`) {
		t.Fatalf("sanitized result lost normal fields: %s", output)
	}
}
