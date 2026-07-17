package unityprocess

import (
	"context"
	"errors"
	"strings"
	"testing"
)

// Verifies command stderr is included in the returned error message.
func TestCommandErrorWithStderrAppendsTrimmedStderr(t *testing.T) {
	err := commandErrorWithStderr(errors.New("exit status 1"), "  PowerShell throw text\r\n")

	if err == nil || !strings.Contains(err.Error(), "PowerShell throw text") {
		t.Fatalf("expected stderr in error, got %v", err)
	}
}

// Verifies empty command stderr leaves the original error message intact.
func TestCommandErrorWithStderrKeepsOriginalErrorWithoutStderr(t *testing.T) {
	err := errors.New("exit status 1")

	actual := commandErrorWithStderr(err, " \r\n")

	if actual != err {
		t.Fatalf("expected original error, got %v", actual)
	}
}

// Verifies a timed-out focus script is reported as a busy-Editor timeout instead of a bare exit status.
func TestFocusCommandErrorReportsTimeoutWhenContextDeadlineExceeded(t *testing.T) {
	err := focusCommandError(context.DeadlineExceeded, errors.New("exit status 1"), "")

	if err == nil || !strings.Contains(err.Error(), "timed out") || !strings.Contains(err.Error(), "domain reload") {
		t.Fatalf("expected timeout explanation, got %v", err)
	}
}

// Verifies a focus script throw keeps the stderr text without the timeout explanation.
func TestFocusCommandErrorKeepsStderrForScriptFailures(t *testing.T) {
	err := focusCommandError(nil, errors.New("exit status 1"), "Windows refused to bring the Unity window\r\n")

	if err == nil || !strings.Contains(err.Error(), "Windows refused to bring the Unity window") {
		t.Fatalf("expected stderr in error, got %v", err)
	}
	if strings.Contains(err.Error(), "timed out") {
		t.Fatalf("expected no timeout explanation, got %v", err)
	}
}

// Verifies a nil run error yields no focus error.
func TestFocusCommandErrorReturnsNilWithoutRunError(t *testing.T) {
	if err := focusCommandError(context.DeadlineExceeded, nil, ""); err != nil {
		t.Fatalf("expected nil, got %v", err)
	}
}
