package cli

import (
	"bytes"
	"strings"
	"testing"
)

func TestSpinnerDoesNotWriteWhenDisabled(t *testing.T) {
	var stderr bytes.Buffer

	spinner := newSpinner(&stderr, false, "Executing compile...")
	spinner.Stop()

	if stderr.Len() != 0 {
		t.Fatalf("disabled spinner wrote output: %q", stderr.String())
	}
}

func TestSpinnerWritesMessageAndClearsLine(t *testing.T) {
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
