package ui

import (
	"bytes"
	"strings"
	"testing"

	"github.com/hatayama/unity-cli-loop/common/progress"
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
	progressFunc := NewSpinnerProgressFunc(spinner, "Executing get-logs...")

	progressFunc(progress.Event{
		Stage:   progress.StageMessage,
		Message: "unity editor main thread busy for 38s...",
	})
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
	progressFunc := NewSpinnerProgressFunc(spinner, "Executing get-logs...")

	progressFunc(progress.Event{Stage: progress.StageConnected})
	progressFunc(progress.Event{Stage: progress.StageAccepted})
	spinner.Stop()

	output := stderr.String()
	if !strings.Contains(output, "Executing get-logs...") {
		t.Fatalf("spinner output did not include executing message: %q", output)
	}
	if strings.Contains(output, string(progress.StageConnected)) || strings.Contains(output, string(progress.StageAccepted)) {
		t.Fatalf("spinner output leaked a raw progress event token: %q", output)
	}
}

func TestNewToolSpinnerRespectsFeedbackFlag(t *testing.T) {
	// Verifies callers can disable tool spinner feedback for hot paths.
	var stderr bytes.Buffer

	spinner := NewToolSpinner(&stderr, false)
	spinner.Stop()

	if stderr.Len() != 0 {
		t.Fatalf("disabled tool spinner wrote output: %q", stderr.String())
	}
}
