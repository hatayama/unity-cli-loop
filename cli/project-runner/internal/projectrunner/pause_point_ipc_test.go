package projectrunner

import (
	"context"
	"testing"
)

// Verifies the Debug switch posts set-code-optimization-debug to Unity and reports Unity's refusal.
func TestSendSetCodeOptimizationDebugFromUnity(t *testing.T) {
	t.Run("success", func(t *testing.T) {
		server := startFakeUnityResultServer(t, t.TempDir(), setCodeOptimizationDebugCommandName, `{"Success":true}`)
		if err := sendSetCodeOptimizationDebugFromUnity(context.Background(), server.connection); err != nil {
			t.Fatalf("switch failed: %v", err)
		}
		if request := server.receivedRequest(t); len(request) != 0 {
			t.Fatalf("switch must send no params: %#v", request)
		}
	})
	t.Run("Unity error", func(t *testing.T) {
		server := startFakeUnityServer(t, t.TempDir(), setCodeOptimizationDebugCommandName, testUnityRPCFailureResponse)
		if err := sendSetCodeOptimizationDebugFromUnity(context.Background(), server.connection); err == nil {
			t.Fatal("expected the Unity error")
		}
	})
}

// Verifies status queries surface Unity errors and undecodable results instead of an empty status.
func TestPausePointStatusQueriesReportFailures(t *testing.T) {
	t.Run("status undecodable", func(t *testing.T) {
		server := startFakeUnityResultServer(t, t.TempDir(), pausePointStatusCommandName, `[1]`)
		if _, err := queryPausePointStatusFromUnity(context.Background(), server.connection, "jump"); err == nil {
			t.Fatal("expected a decode error")
		}
	})
	t.Run("list Unity error", func(t *testing.T) {
		server := startFakeUnityServer(t, t.TempDir(), pausePointStatusCommandName, testUnityRPCFailureResponse)
		if _, err := queryPausePointStatusListFromUnity(context.Background(), server.connection); err == nil {
			t.Fatal("expected the Unity error")
		}
	})
	t.Run("list undecodable", func(t *testing.T) {
		server := startFakeUnityResultServer(t, t.TempDir(), pausePointStatusCommandName, `"text"`)
		if _, err := queryPausePointStatusListFromUnity(context.Background(), server.connection); err == nil {
			t.Fatal("expected a decode error")
		}
	})
}
