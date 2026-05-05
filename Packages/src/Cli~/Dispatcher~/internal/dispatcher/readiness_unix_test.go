//go:build !windows

package dispatcher

import (
	"bufio"
	"context"
	"encoding/json"
	"fmt"
	"net"
	"os"
	"path/filepath"
	"testing"
	"time"

	"github.com/hatayama/unity-cli-loop/Packages/src/Cli/Shared/adapters/framing"
	"github.com/hatayama/unity-cli-loop/Packages/src/Cli/Shared/adapters/project"
)

func TestWaitForLaunchReadyProbesUnityServer(t *testing.T) {
	// Verifies that bootstrap launch waits for a real Unity RPC response before returning.
	projectRoot := t.TempDir()
	createUnityProject(t, projectRoot)
	createToolCache(t, projectRoot)
	listener, endpointPath := listenOnProjectEndpoint(t, projectRoot)
	defer func() {
		_ = listener.Close()
		_ = os.Remove(endpointPath)
	}()
	served := make(chan error, 1)
	go serveLaunchReadyProbe(listener, "get-version", map[string]any{"version": "test"}, served)

	if err := waitForLaunchReady(context.Background(), projectRoot); err != nil {
		t.Fatalf("waitForLaunchReady failed: %v", err)
	}

	assertLaunchReadyProbeServed(t, served)
}

func TestWaitForLaunchReadyUsesVersionProbeWhenToolCacheIsMissing(t *testing.T) {
	// Verifies that unknown core capabilities fall back to the always-supported version probe.
	projectRoot := t.TempDir()
	createUnityProject(t, projectRoot)
	listener, endpointPath := listenOnProjectEndpoint(t, projectRoot)
	defer func() {
		_ = listener.Close()
		_ = os.Remove(endpointPath)
	}()
	served := make(chan error, 1)
	go serveLaunchReadyProbe(listener, "get-version", map[string]any{"version": "test"}, served)

	if err := waitForLaunchReady(context.Background(), projectRoot); err != nil {
		t.Fatalf("waitForLaunchReady failed: %v", err)
	}

	assertLaunchReadyProbeServed(t, served)
}

func TestWaitForLaunchReadyUsesDynamicCodeProbeWhenToolExists(t *testing.T) {
	// Verifies that first-run launch mirrors core launch readiness when dynamic code is available.
	projectRoot := t.TempDir()
	createUnityProject(t, projectRoot)
	createDynamicCodeToolCache(t, projectRoot)
	listener, endpointPath := listenOnProjectEndpoint(t, projectRoot)
	defer func() {
		_ = listener.Close()
		_ = os.Remove(endpointPath)
	}()
	served := make(chan error, 1)
	go serveLaunchReadyProbe(listener, "execute-dynamic-code", map[string]any{"Success": true}, served)

	if err := waitForLaunchReady(context.Background(), projectRoot); err != nil {
		t.Fatalf("waitForLaunchReady failed: %v", err)
	}

	assertLaunchReadyProbeServed(t, served)
}

func listenOnProjectEndpoint(t *testing.T, projectRoot string) (net.Listener, string) {
	t.Helper()

	connection, err := project.ResolveConnection(projectRoot, projectRoot)
	if err != nil {
		t.Fatalf("ResolveConnection failed: %v", err)
	}
	if err := os.MkdirAll(filepath.Dir(connection.Endpoint.Address), 0o755); err != nil {
		t.Fatalf("failed to create endpoint directory: %v", err)
	}
	listener, err := net.Listen(connection.Endpoint.Network, connection.Endpoint.Address)
	if err != nil {
		t.Fatalf("failed to listen on endpoint: %v", err)
	}
	return listener, connection.Endpoint.Address
}

func serveLaunchReadyProbe(listener net.Listener, expectedMethod string, result map[string]any, served chan<- error) {
	conn, err := listener.Accept()
	if err != nil {
		served <- err
		return
	}
	defer func() {
		_ = conn.Close()
	}()

	requestPayload, err := framing.Read(bufio.NewReader(conn))
	if err != nil {
		served <- err
		return
	}
	var request struct {
		Method string `json:"method"`
	}
	if err := json.Unmarshal(requestPayload, &request); err != nil {
		served <- err
		return
	}
	if request.Method != expectedMethod {
		served <- fmt.Errorf("method mismatch: %s", request.Method)
		return
	}

	response := map[string]any{
		"jsonrpc": "2.0",
		"result":  result,
		"id":      1,
	}
	payload, err := json.Marshal(response)
	if err != nil {
		served <- err
		return
	}
	served <- framing.Write(conn, payload)
}

func assertLaunchReadyProbeServed(t *testing.T, served <-chan error) {
	t.Helper()

	select {
	case err := <-served:
		if err != nil {
			t.Fatalf("probe server failed: %v", err)
		}
	case <-time.After(time.Second):
		t.Fatal("timed out waiting for probe server")
	}
}

func createDynamicCodeToolCache(t *testing.T, projectRoot string) {
	t.Helper()

	cachePath := filepath.Join(projectRoot, ".uloop", "tools.json")
	if err := os.MkdirAll(filepath.Dir(cachePath), 0o755); err != nil {
		t.Fatalf("failed to create tool cache directory: %v", err)
	}
	content := `{
  "tools": [
    {
      "name": "execute-dynamic-code",
      "description": "Execute dynamic code",
      "inputSchema": {
        "properties": {}
      }
    }
  ]
}`
	if err := os.WriteFile(cachePath, []byte(content), 0o644); err != nil {
		t.Fatalf("failed to write tool cache: %v", err)
	}
}
