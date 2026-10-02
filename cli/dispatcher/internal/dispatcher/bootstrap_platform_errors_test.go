package dispatcher

import (
	"errors"
	"reflect"
	"testing"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
)

func TestUnsupportedPlatformErrorGivesCommandSpecificActions(t *testing.T) {
	// Verifies each bootstrap command's unsupported-OS message maps to an invalid-argument error with its own next actions.
	cases := map[string][]string{
		updateUnsupportedOSMessage: {
			"Run `uloop update` on macOS or Windows.",
			"Install the latest uloop dispatcher manually on this platform.",
		},
		uninstallUnsupportedOSMessage: {
			"Run `uloop uninstall` on macOS or Windows.",
			"Remove the uloop dispatcher binary manually on this platform.",
		},
	}
	for message, wantActions := range cases {
		cliError, ok := unsupportedPlatformError(message, clierrors.ErrorContext{Command: "<COMMAND>"})
		if !ok || cliError.ErrorCode != clierrors.ErrorCodeInvalidArgument || cliError.Command != "<COMMAND>" {
			t.Fatalf("%q: unexpected result ok=%t error=%+v", message, ok, cliError)
		}
		if !reflect.DeepEqual(cliError.NextActions, wantActions) {
			t.Fatalf("%q: next actions mismatch: %v", message, cliError.NextActions)
		}
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
