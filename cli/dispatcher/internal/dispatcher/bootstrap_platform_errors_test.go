package dispatcher

import (
	"errors"
	"testing"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
)

func TestUnsupportedPlatformErrorGivesCommandSpecificActions(t *testing.T) {
	// Verifies each bootstrap command's unsupported-OS message maps to an invalid-argument error with its own manual fallback.
	cases := map[string]string{
		updateUnsupportedOSMessage:    "Install the latest uloop dispatcher manually on this platform.",
		uninstallUnsupportedOSMessage: "Remove the uloop dispatcher binary manually on this platform.",
	}
	for message, wantManualAction := range cases {
		cliError, ok := unsupportedPlatformError(message, clierrors.ErrorContext{Command: "<COMMAND>"})
		if !ok || cliError.ErrorCode != clierrors.ErrorCodeInvalidArgument || cliError.Command != "<COMMAND>" || cliError.Message != message {
			t.Fatalf("%q: unexpected result ok=%t error=%+v", message, ok, cliError)
		}
		if len(cliError.NextActions) != 2 || cliError.NextActions[1] != wantManualAction {
			t.Fatalf("%q: next actions mismatch: %v", message, cliError.NextActions)
		}
	}
	if _, ok := unsupportedPlatformError("some other failure", clierrors.ErrorContext{}); ok {
		t.Fatal("an unrelated message must not be classified as an unsupported platform")
	}
}

func TestWrapUnsupportedPlatformErrorKeepsNilAndMessage(t *testing.T) {
	// Verifies a nil error stays nil and a wrapped unsupported-OS error still reports the original message.
	if err := wrapUnsupportedPlatformError(nil); err != nil {
		t.Fatalf("nil must stay nil, got %v", err)
	}

	wrapped := wrapUnsupportedPlatformError(errors.New(uninstallUnsupportedOSMessage))

	var platformErr unsupportedPlatformCommandError
	if !errors.As(wrapped, &platformErr) || wrapped.Error() != uninstallUnsupportedOSMessage {
		t.Fatalf("expected a wrapped platform error with the original message, got %T %v", wrapped, wrapped)
	}
}

func TestWrapUnsupportedPlatformErrorReturnsUnrelatedErrorsUnchanged(t *testing.T) {
	// Verifies an error that does not report an unsupported OS is returned as is rather than reclassified.
	original := errors.New("disk full")

	wrapped := wrapUnsupportedPlatformError(original)

	var platformErr unsupportedPlatformCommandError
	if !errors.Is(wrapped, original) || errors.As(wrapped, &platformErr) {
		t.Fatalf("expected the original error unchanged, got %T %v", wrapped, wrapped)
	}
}
