package clicore

import (
	"bytes"
	"os"
	"testing"
)

// Verifies that a terminal writer gets an animated spinner for regular tools but not for
// execute-dynamic-code, and that a non-terminal writer never gets one.
func TestNewToolSpinner(t *testing.T) {
	// The null device reports itself as a character device, which is how a terminal is detected.
	terminal, err := os.OpenFile(os.DevNull, os.O_WRONLY, 0)
	if err != nil {
		t.Fatalf("failed to open null device: %v", err)
	}
	t.Cleanup(func() { _ = terminal.Close() })

	compileSpinner := NewToolSpinner(terminal, "compile")
	compileSpinner.Stop()
	if !compileSpinner.Enabled {
		t.Fatal("a regular tool should show a spinner on a terminal")
	}
	if NewToolSpinner(terminal, ExecuteDynamicCodeCommandName).Enabled {
		t.Fatal("execute-dynamic-code should not show a spinner")
	}

	var stderr bytes.Buffer
	spinner := NewToolSpinner(&stderr, "compile")

	if spinner.Enabled || stderr.Len() != 0 {
		t.Fatalf("spinner should stay disabled for a non-terminal writer: enabled=%v output=%q", spinner.Enabled, stderr.String())
	}
}
