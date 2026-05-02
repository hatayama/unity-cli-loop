package unity

import (
	"errors"
	"strings"
	"testing"

	"github.com/hatayama/unity-cli-loop/Packages/src/GoCli/internal/project"
)

func TestFormatConnectionAttemptErrorExplainsDialFailureWithoutDisconnectClaim(t *testing.T) {
	connection := project.Connection{
		Endpoint: project.Endpoint{
			Network: "unix",
			Address: "/tmp/uloop/uLoopMCP-sample.sock",
		},
		ProjectRoot: "/tmp/MyProject",
	}

	err := formatConnectionAttemptError(connection, errors.New("dial unix /tmp/uloop/uLoopMCP-sample.sock: connect: no such file or directory"))
	message := err.Error()

	for _, expected := range []string{
		"the Unity CLI Loop server is not reachable for this project.",
		"connection attempt failure before a request was sent",
		"does not mean an established connection was disconnected",
		"Project: /tmp/MyProject",
		"Endpoint: /tmp/uloop/uLoopMCP-sample.sock",
		"Cause: dial unix /tmp/uloop/uLoopMCP-sample.sock: connect: no such file or directory",
	} {
		if !strings.Contains(message, expected) {
			t.Fatalf("message missing %q:\n%s", expected, message)
		}
	}
}
