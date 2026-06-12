package cli

import (
	"bytes"
	"strings"
	"testing"

	"github.com/hatayama/unity-cli-loop/cli/internal/unityipc"
)

func TestSpinnerDoesNotWriteWhenDisabled(t *testing.T) {
	// Verifies that disabled spinners stay silent.
	var stderr bytes.Buffer

	spinner := newSpinner(&stderr, false, "Executing compile...")
	spinner.Stop()

	if stderr.Len() != 0 {
		t.Fatalf("disabled spinner wrote output: %q", stderr.String())
	}
}

func TestSpinnerWritesMessageAndClearsLine(t *testing.T) {
	// Verifies that enabled spinners render and clean up their terminal line.
	var stderr bytes.Buffer

	spinner := newSpinner(&stderr, true, "Executing compile...")
	spinner.Stop()

	output := stderr.String()
	if !strings.Contains(output, "Executing compile...") {
		t.Fatalf("spinner output did not include message: %q", output)
	}
	if !strings.HasSuffix(output, "\r\x1b[K\n") {
		t.Fatalf("spinner output did not clear the line: %q", output)
	}
}

func TestLaunchSpinnerWritesStartupMessage(t *testing.T) {
	// Verifies that launch spinners show startup progress text.
	var stdout bytes.Buffer

	spinner := newSpinner(&stdout, true, "Waiting for Unity to finish starting...")
	spinner.Stop()

	output := stdout.String()
	if !strings.Contains(output, "Waiting for Unity to finish starting...") {
		t.Fatalf("launch spinner output did not include message: %q", output)
	}
	if !strings.HasSuffix(output, "\r\x1b[K\n") {
		t.Fatalf("launch spinner output did not clear the line before returning: %q", output)
	}
}

func TestSpinnerProgressFuncShowsStallMessageVerbatim(t *testing.T) {
	// Verifies that heartbeat stall payloads reach the spinner verbatim.
	var stderr bytes.Buffer
	spinner := newSpinner(&stderr, true, "Connecting to Unity...")
	progress := newSpinnerProgressFunc(spinner, "Executing get-logs...")

	progress("unity editor main thread busy for 38s...")
	spinner.Stop()

	if !strings.Contains(stderr.String(), "unity editor main thread busy for 38s...") {
		t.Fatalf("spinner output did not include stall message: %q", stderr.String())
	}
}

func TestSpinnerProgressFuncMapsConnectionEventsToExecutingMessage(t *testing.T) {
	// Verifies that connection-stage events show the contextual executing message
	// instead of the raw event token.
	var stderr bytes.Buffer
	spinner := newSpinner(&stderr, true, "Connecting to Unity...")
	progress := newSpinnerProgressFunc(spinner, "Executing get-logs...")

	progress(unityipc.ProgressEventConnected)
	progress(unityipc.ProgressEventAccepted)
	spinner.Stop()

	output := stderr.String()
	if !strings.Contains(output, "Executing get-logs...") {
		t.Fatalf("spinner output did not include executing message: %q", output)
	}
	if strings.Contains(output, unityipc.ProgressEventConnected) || strings.Contains(output, unityipc.ProgressEventAccepted) {
		t.Fatalf("spinner output leaked a raw progress event token: %q", output)
	}
}

func TestToolFeedbackSkipsExecuteDynamicCode(t *testing.T) {
	// Verifies that execute-dynamic-code keeps the CLI hot path quiet.
	if shouldShowToolFeedback(executeDynamicCodeCommandName) {
		t.Fatal("execute-dynamic-code should skip spinner feedback on the hot path")
	}
}

func TestToolFeedbackKeepsOtherUnityTools(t *testing.T) {
	// Verifies that regular Unity tools still show interactive feedback.
	if !shouldShowToolFeedback("get-logs") {
		t.Fatal("non-hot-path Unity tools should keep spinner feedback")
	}
}
