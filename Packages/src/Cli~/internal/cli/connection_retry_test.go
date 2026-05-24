package cli

import (
	"context"
	"errors"
	"testing"
	"time"

	"github.com/hatayama/unity-cli-loop/Packages/src/Cli/internal/unityipc"
)

// Verifies transient IPC connection failures become server-not-responding when Unity is alive.
func TestSendWithTransientConnectionRetryReportsUnityServerNotResponding(t *testing.T) {
	originalFinder := findRunningUnityProcessForConnectionRetry
	originalTimeout := serverConnectionRetryTimeout
	originalPoll := serverConnectionRetryPoll
	findRunningUnityProcessForConnectionRetry = func(context.Context, string) (*unityProcess, error) {
		return &unityProcess{pid: 123}, nil
	}
	serverConnectionRetryTimeout = time.Nanosecond
	serverConnectionRetryPoll = time.Nanosecond
	t.Cleanup(func() {
		findRunningUnityProcessForConnectionRetry = originalFinder
		serverConnectionRetryTimeout = originalTimeout
		serverConnectionRetryPoll = originalPoll
	})

	connection := unityipc.Connection{
		Endpoint: unityipc.Endpoint{
			Network: "unix",
			Address: t.TempDir() + "/missing.sock",
		},
		ProjectRoot: t.TempDir(),
	}

	_, err := sendWithTransientConnectionRetry(
		context.Background(),
		connection,
		"get-logs",
		map[string]any{},
		nil)

	var notRespondingErr unityServerNotRespondingError
	if !errors.As(err, &notRespondingErr) {
		t.Fatalf("expected unityServerNotRespondingError, got %v", err)
	}
}
