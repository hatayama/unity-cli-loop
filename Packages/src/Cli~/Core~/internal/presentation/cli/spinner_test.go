package cli

import (
	"bytes"
	"strings"
	"testing"
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
