package unityprocess

import (
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
