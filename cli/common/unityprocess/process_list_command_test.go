//go:build !windows

package unityprocess

import (
	"context"
	"strings"
	"testing"
	"time"
)

// Verifies a process-list command killed by its own time limit is reported as a timeout, not as the bare exit status the kill leaves.
func TestRunProcessListCommandWithin_TimeoutSaysPowerShellDidNotRespond(t *testing.T) {
	_, err := runProcessListCommandWithin(context.Background(), 50*time.Millisecond, "sleep", "5")

	if err == nil || !strings.Contains(err.Error(), "timed out after 50ms") || !strings.Contains(err.Error(), "PowerShell or WMI did not respond") {
		t.Fatalf("expected a timed-out process-list error, got %v", err)
	}
}

// Verifies a command stopped by the caller's own earlier deadline is not reported as the process-list time limit.
func TestRunProcessListCommandWithin_CallerDeadlineIsNotReportedAsTheListTimeout(t *testing.T) {
	callerContext, cancel := context.WithTimeout(context.Background(), 50*time.Millisecond)
	defer cancel()

	_, err := runProcessListCommandWithin(callerContext, 5*time.Second, "sleep", "5")

	if err == nil || strings.Contains(err.Error(), "timed out after") {
		t.Fatalf("expected a failure without the process-list timeout explanation, got %v", err)
	}
}

// Verifies a process-list script that fails on its own carries its stderr and no timeout explanation.
func TestRunProcessListCommandWithin_FailureIncludesStderr(t *testing.T) {
	_, err := runProcessListCommandWithin(context.Background(), 5*time.Second, "sh", "-c", "echo Get-CimInstance refused >&2; exit 1")

	if err == nil || !strings.Contains(err.Error(), "Get-CimInstance refused") {
		t.Fatalf("expected stderr in the process-list error, got %v", err)
	}
	if strings.Contains(err.Error(), "timed out") {
		t.Fatalf("expected no timeout explanation, got %v", err)
	}
}

// Verifies a process-list command that succeeds returns its output unchanged.
func TestRunProcessListCommandWithin_ReturnsTheOutput(t *testing.T) {
	output, err := runProcessListCommandWithin(context.Background(), 5*time.Second, "sh", "-c", "printf '42|encoded'")

	if err != nil || string(output) != "42|encoded" {
		t.Fatalf("expected the command output, got %q, %v", output, err)
	}
}
