package cli

import (
	"bytes"
	"strings"
	"testing"
	"time"

	"github.com/hatayama/unity-cli-loop/Packages/src/Cli/Shared/domain"
)

// Verifies that timing output stays disabled unless explicitly requested.
func TestWriteDebugTiming_WhenEnvironmentIsUnset_ShouldStaySilent(t *testing.T) {
	t.Setenv(debugTimingEnvName, "")
	var stderr bytes.Buffer

	writeDebugTiming(&stderr, "execute-dynamic-code", time.Millisecond, domain.UnitySendOutcome{})

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
		domain.UnitySendOutcome{
			Timing: domain.UnitySendTiming{
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
