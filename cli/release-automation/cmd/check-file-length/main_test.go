package main

import "testing"

func TestFailOnExceededEnabledTreatsFalseTokenAsReportOnly(t *testing.T) {
	// Verifies the POSIX wrapper's `--fail-on-exceeded false` does not enable fail mode.
	if failOnExceededEnabled("false") {
		t.Fatal("expected false to keep report-only mode")
	}
	if failOnExceededEnabled("FALSE") {
		t.Fatal("expected FALSE to keep report-only mode")
	}
	if failOnExceededEnabled("") {
		t.Fatal("expected empty value to keep report-only mode")
	}
	if !failOnExceededEnabled("true") {
		t.Fatal("expected true to enable fail mode")
	}
	if !failOnExceededEnabled("TRUE") {
		t.Fatal("expected TRUE to enable fail mode")
	}
}
