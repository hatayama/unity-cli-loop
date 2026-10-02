package clicore

import (
	"bytes"
	"testing"
)

// Verifies that tool feedback is suppressed for execute-dynamic-code and that a non-terminal writer
// never gets an animated spinner.
func TestNewToolSpinner(t *testing.T) {
	if shouldShowToolFeedback(ExecuteDynamicCodeCommandName) {
		t.Fatal("execute-dynamic-code should not show tool feedback")
	}
	if !shouldShowToolFeedback("compile") {
		t.Fatal("regular tools should show tool feedback")
	}

	var stderr bytes.Buffer
	spinner := NewToolSpinner(&stderr, "compile")

	if spinner.Enabled || stderr.Len() != 0 {
		t.Fatalf("spinner should stay disabled for a non-terminal writer: enabled=%v output=%q", spinner.Enabled, stderr.String())
	}
}
