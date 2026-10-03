package clierrors

import (
	"errors"
	"testing"

	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

// Verifies the connection-attempt cause text is empty for a nil error or a missing
// cause, and is the cause message otherwise.
func TestConnectionAttemptCause(t *testing.T) {
	cases := []struct {
		name string
		err  *unityipc.ConnectionAttemptError
		want string
	}{
		{name: "nil error", err: nil, want: ""},
		{name: "nil cause", err: &unityipc.ConnectionAttemptError{}, want: ""},
		{name: "with cause", err: &unityipc.ConnectionAttemptError{Cause: errors.New("connection refused")}, want: "connection refused"},
	}

	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			if got := connectionAttemptCause(testCase.err); got != testCase.want {
				t.Fatalf("expected %q, got %q", testCase.want, got)
			}
		})
	}
}
