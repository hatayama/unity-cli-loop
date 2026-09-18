//go:build darwin

package unityprocess

import (
	"context"
	"strings"
	"testing"
	"time"
)

// Verifies a macOS focus command that outlives its timeout reports the stalled-Editor hint instead of a bare kill error.
func TestRunFocusCommandNoOutputWithin_TimeoutSaysTheEditorMayBeBusy(t *testing.T) {
	err := runFocusCommandNoOutputWithin(context.Background(), 50*time.Millisecond, "sleep", "5")

	if err == nil || !strings.Contains(err.Error(), "timed out after") {
		t.Fatalf("expected a timed-out focus error, got %v", err)
	}
}

// Verifies the output-capturing focus command maps a timeout the same way.
func TestRunFocusCommandWithin_TimeoutSaysTheEditorMayBeBusy(t *testing.T) {
	_, err := runFocusCommandWithin(context.Background(), 50*time.Millisecond, "sleep", "5")

	if err == nil || !strings.Contains(err.Error(), "timed out after") {
		t.Fatalf("expected a timed-out focus error, got %v", err)
	}
}

// Verifies a focus command that fails on its own carries its stderr in the error.
func TestRunFocusCommandNoOutputWithin_FailureIncludesStderr(t *testing.T) {
	err := runFocusCommandNoOutputWithin(context.Background(), 5*time.Second, "sh", "-c", "echo refused >&2; exit 1")

	if err == nil || !strings.Contains(err.Error(), "refused") {
		t.Fatalf("expected stderr in the focus error, got %v", err)
	}
}
