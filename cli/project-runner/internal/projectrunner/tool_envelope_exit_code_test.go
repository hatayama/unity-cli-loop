package projectrunner

import "testing"

// Verifies a true tool envelope reports a successful process exit.
func TestToolEnvelopeExitCodeReturnsZeroForSuccess(t *testing.T) {
	if code := toolEnvelopeExitCode([]byte(`{"Success":true}`)); code != 0 {
		t.Fatalf("expected exit code 0, got %d", code)
	}
}

// Verifies a false tool envelope reports a failed process exit.
func TestToolEnvelopeExitCodeReturnsOneForFailure(t *testing.T) {
	if code := toolEnvelopeExitCode([]byte(`{"Success":false}`)); code != 1 {
		t.Fatalf("expected exit code 1, got %d", code)
	}
}

// Verifies a missing Success field fails closed.
func TestToolEnvelopeExitCodeRejectsMissingSuccess(t *testing.T) {
	if code := toolEnvelopeExitCode([]byte(`{"Message":"missing"}`)); code != 1 {
		t.Fatalf("expected exit code 1, got %d", code)
	}
}

// Verifies a non-boolean Success field fails closed.
func TestToolEnvelopeExitCodeRejectsNonBooleanSuccess(t *testing.T) {
	if code := toolEnvelopeExitCode([]byte(`{"Success":"true"}`)); code != 1 {
		t.Fatalf("expected exit code 1, got %d", code)
	}
}

// Verifies malformed tool JSON fails closed.
func TestToolEnvelopeExitCodeRejectsMalformedJSON(t *testing.T) {
	if code := toolEnvelopeExitCode([]byte(`{"Success":`)); code != 1 {
		t.Fatalf("expected exit code 1, got %d", code)
	}
}
