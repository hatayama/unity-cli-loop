package clierrors

import "testing"

// Verifies firstNonEmpty returns the first non-empty value, or empty when all are empty.
func TestFirstNonEmpty(t *testing.T) {
	if got := firstNonEmpty("", "b", "c"); got != "b" {
		t.Fatalf("expected first non-empty value, got %q", got)
	}
	if got := firstNonEmpty("", ""); got != "" {
		t.Fatalf("expected empty result when every value is empty, got %q", got)
	}
}
