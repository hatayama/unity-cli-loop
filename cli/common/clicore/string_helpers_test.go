package clicore

import (
	"errors"
	"testing"
)

// Verifies that FirstNonEmpty returns the first non-empty value, or empty when every value is empty.
func TestFirstNonEmpty(t *testing.T) {
	if actual := FirstNonEmpty("", "second", "third"); actual != "second" {
		t.Fatalf("FirstNonEmpty = %q, want second", actual)
	}
	if actual := FirstNonEmpty("", ""); actual != "" {
		t.Fatalf("FirstNonEmpty = %q, want empty", actual)
	}
}

// Verifies that ErrorMessage renders a nil error as empty text and otherwise uses Error().
func TestErrorMessage(t *testing.T) {
	if actual := ErrorMessage(nil); actual != "" {
		t.Fatalf("ErrorMessage(nil) = %q, want empty", actual)
	}
	if actual := ErrorMessage(errors.New("boom")); actual != "boom" {
		t.Fatalf("ErrorMessage = %q, want boom", actual)
	}
}
