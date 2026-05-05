package unity

import (
	"errors"
	"testing"

	"github.com/hatayama/unity-cli-loop/Packages/src/Cli/Shared/domain"
)

func TestFormatConnectionAttemptErrorExplainsDialFailureWithoutDisconnectClaim(t *testing.T) {
	connection := domain.Connection{
		Endpoint: domain.Endpoint{
			Network: "unix",
			Address: "/tmp/uloop/UnityCliLoop-sample.sock",
		},
		ProjectRoot: "/tmp/MyProject",
	}

	err := formatConnectionAttemptError(connection, errors.New("dial unix /tmp/uloop/UnityCliLoop-sample.sock: connect: no such file or directory"))
	connectionErr, ok := err.(*ConnectionAttemptError)
	if !ok {
		t.Fatalf("expected ConnectionAttemptError, got %T", err)
	}
	if connectionErr.ProjectRoot != "/tmp/MyProject" {
		t.Fatalf("project root mismatch: %s", connectionErr.ProjectRoot)
	}
	if connectionErr.Endpoint != "/tmp/uloop/UnityCliLoop-sample.sock" {
		t.Fatalf("endpoint mismatch: %s", connectionErr.Endpoint)
	}
	if connectionErr.Unwrap().Error() != "dial unix /tmp/uloop/UnityCliLoop-sample.sock: connect: no such file or directory" {
		t.Fatalf("cause mismatch: %v", connectionErr.Unwrap())
	}
}
