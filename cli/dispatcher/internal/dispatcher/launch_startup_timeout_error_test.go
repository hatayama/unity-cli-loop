package dispatcher

import (
	"errors"
	"testing"
	"time"
)

func TestLaunchTimeoutErrorMessages(t *testing.T) {
	// Verifies the startup timeout includes its cause only when one exists, and the exit timeout names the pid and duration.
	if message := (launchStartupTimeoutError{}).Error(); message != "Unity startup did not finish before the launch timeout" {
		t.Fatalf("unexpected message without cause: %s", message)
	}
	withCause := launchStartupTimeoutError{cause: errors.New("tools not ready")}
	if message := withCause.Error(); message != "Unity startup did not finish before the launch timeout: tools not ready" {
		t.Fatalf("unexpected message with cause: %s", message)
	}
	exitTimeout := launchProcessExitTimeoutError{pid: 42, timeout: 3 * time.Second}
	if message := exitTimeout.Error(); message != "Unity process 42 did not exit within 3s" {
		t.Fatalf("unexpected exit timeout message: %s", message)
	}
}
