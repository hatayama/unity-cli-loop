package clierrors

import (
	"errors"
	"testing"
)

// Verifies the not-responding error appends its cause to the message and exposes
// the cause text only when a cause exists.
func TestUnityServerNotRespondingErrorDescribesCause(t *testing.T) {
	cases := []struct {
		name      string
		cause     error
		message   string
		causeText string
	}{
		{
			name:      "without cause",
			message:   "Unity is running but the Unity CLI Loop server is not responding",
			causeText: "",
		},
		{
			name:      "with cause",
			cause:     errors.New("i/o timeout"),
			message:   "Unity is running but the Unity CLI Loop server is not responding: i/o timeout",
			causeText: "i/o timeout",
		},
	}

	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			err := UnityServerNotRespondingError{Cause: testCase.cause}

			if err.Error() != testCase.message {
				t.Fatalf("unexpected Error(): %q", err.Error())
			}
			if err.causeText() != testCase.causeText {
				t.Fatalf("unexpected cause text: %q", err.causeText())
			}
			if err.Unwrap() != testCase.cause {
				t.Fatalf("expected Unwrap to return the cause, got %v", err.Unwrap())
			}
		})
	}
}
