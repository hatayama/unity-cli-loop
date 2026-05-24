package cli

import (
	"context"
	"errors"
	"testing"
	"time"

	"github.com/hatayama/unity-cli-loop/Packages/src/Cli/internal/unityipc"
)

// Verifies transient IPC connection failures focus Unity once and restore focus before reporting server-not-responding.
func TestSendWithTransientConnectionRetryReportsUnityServerNotResponding(t *testing.T) {
	originalFinder := findRunningUnityProcessForConnectionRetry
	originalFocus := focusUnityProcessForConnectionRetry
	originalTimeout := serverConnectionRetryTimeout
	originalPoll := serverConnectionRetryPoll
	focusCallCount := 0
	restoreCallCount := 0
	findRunningUnityProcessForConnectionRetry = func(context.Context, string) (*unityProcess, error) {
		return &unityProcess{pid: 123}, nil
	}
	focusUnityProcessForConnectionRetry = func(context.Context, int) (restoreFocusFunc, error) {
		focusCallCount++
		return func(context.Context) error {
			restoreCallCount++
			return nil
		}, nil
	}
	serverConnectionRetryTimeout = time.Nanosecond
	serverConnectionRetryPoll = time.Nanosecond
	t.Cleanup(func() {
		findRunningUnityProcessForConnectionRetry = originalFinder
		focusUnityProcessForConnectionRetry = originalFocus
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
	if focusCallCount != 1 {
		t.Fatalf("expected one focus attempt, got %d", focusCallCount)
	}
	if restoreCallCount != 1 {
		t.Fatalf("expected one focus restore, got %d", restoreCallCount)
	}
}
