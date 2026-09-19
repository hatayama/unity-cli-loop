//go:build linux

package unityprocess

import (
	"context"
	"os"
	"strconv"
	"testing"
)

// Verifies the owner check accepts this test process, so a broken UID comparison cannot silently empty every Linux listing.
func TestIsOwnedByCurrentUserAcceptsOwnProcess(t *testing.T) {
	if !isOwnedByCurrentUser(strconv.Itoa(os.Getpid())) {
		t.Fatal("expected the current process to be owned by the current user")
	}
}

// Verifies the owner check fails closed for a /proc entry that does not exist.
func TestIsOwnedByCurrentUserRejectsMissingProcess(t *testing.T) {
	if isOwnedByCurrentUser("0") {
		t.Fatal("expected a missing /proc entry to be treated as not owned")
	}
}

// Verifies the shared entry point routes Linux to the /proc reader: it honors cancellation and lists without error otherwise.
func TestListUnityProcessesUsesProcReaderOnLinux(t *testing.T) {
	cancelledContext, cancel := context.WithCancel(context.Background())
	cancel()
	if _, err := listUnityProcesses(cancelledContext); err == nil {
		t.Fatal("expected a cancelled context to fail the Linux listing")
	}

	// The process count is not asserted because CI hosts run no Unity Editor.
	if _, err := listUnityProcesses(context.Background()); err != nil {
		t.Fatalf("expected the Linux listing to succeed, got %v", err)
	}
}
