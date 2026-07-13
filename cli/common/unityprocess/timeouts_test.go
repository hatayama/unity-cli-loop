package unityprocess

import (
	"context"
	"testing"
	"time"
)

// Verifies external-command timeouts use the parent deadline when it is sooner than the command cap.
func TestWithCommandTimeoutRespectsEarlierParentDeadline(t *testing.T) {
	parentContext, parentCancel := context.WithTimeout(context.Background(), 2*time.Second)
	defer parentCancel()

	commandContext, cancel := withCommandTimeout(parentContext, ProcessListCommandTimeout)
	defer cancel()

	parentDeadline, parentHasDeadline := parentContext.Deadline()
	commandDeadline, commandHasDeadline := commandContext.Deadline()
	if !parentHasDeadline || !commandHasDeadline {
		t.Fatal("expected both parent and command contexts to have deadlines")
	}
	if !commandDeadline.Equal(parentDeadline) {
		t.Fatalf("command deadline %s should match parent deadline %s", commandDeadline, parentDeadline)
	}
}

// Verifies external-command timeouts still bound background callers that pass no deadline.
func TestWithCommandTimeoutBoundsBackgroundContext(t *testing.T) {
	commandContext, cancel := withCommandTimeout(context.Background(), ProcessListCommandTimeout)
	defer cancel()

	deadline, hasDeadline := commandContext.Deadline()
	if !hasDeadline {
		t.Fatal("expected command context to have a deadline")
	}
	remaining := time.Until(deadline)
	if remaining < ProcessListCommandTimeout-time.Second || remaining > ProcessListCommandTimeout {
		t.Fatalf("command timeout mismatch: %s", remaining)
	}
}
