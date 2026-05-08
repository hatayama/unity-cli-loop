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

func TestWaitForToolReadinessProbesUnityServer(t *testing.T) {
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
	go serveToolReadinessProbes(listener, "get-version", map[string]any{"version": "test"}, toolReadinessProbeCount, served)

	if err := waitForToolReadiness(context.Background(), projectRoot); err != nil {
		t.Fatalf("waitForToolReadiness failed: %v", err)
	}

	assertToolReadinessProbeServed(t, served)
}

func TestWaitForToolReadinessUsesVersionProbeWhenToolCacheIsMissing(t *testing.T) {
	// Verifies that unknown core capabilities fall back to the always-supported version probe.
	projectRoot := t.TempDir()
	createUnityProject(t, projectRoot)
	listener, endpointPath := listenOnProjectEndpoint(t, projectRoot)
	defer func() {
		_ = listener.Close()
		_ = os.Remove(endpointPath)
	}()
	served := make(chan error, 1)
	go serveToolReadinessProbes(listener, "get-version", map[string]any{"version": "test"}, toolReadinessProbeCount, served)

	if err := waitForToolReadiness(context.Background(), projectRoot); err != nil {
		t.Fatalf("waitForToolReadiness failed: %v", err)
	}

	assertToolReadinessProbeServed(t, served)
}

func TestWaitForToolReadinessUsesDynamicCodeProbeWhenToolExists(t *testing.T) {
	// Verifies that first-run launch mirrors core tool readiness when dynamic code is available.
	projectRoot := t.TempDir()
	createUnityProject(t, projectRoot)
	createDynamicCodeToolCache(t, projectRoot)
	listener, endpointPath := listenOnProjectEndpoint(t, projectRoot)
	defer func() {
		_ = listener.Close()
		_ = os.Remove(endpointPath)
	}()
	served := make(chan error, 1)
	go serveToolReadinessProbes(listener, "execute-dynamic-code", map[string]any{"Success": true}, toolReadinessProbeCount, served)

	if err := waitForToolReadiness(context.Background(), projectRoot); err != nil {
		t.Fatalf("waitForToolReadiness failed: %v", err)
	}

	assertToolReadinessProbeServed(t, served)
}

// Verifies that readiness probes exercise the same foreground warmup path as user executions.
func TestExecuteDynamicCodeReadinessProbeParamsUseForegroundWarmup(t *testing.T) {
	params := executeDynamicCodeReadinessProbeParams()

	if params["YieldToForegroundRequests"] != false {
		t.Fatalf("readiness probe should use foreground warmup: %#v", params["YieldToForegroundRequests"])
	}
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

func serveToolReadinessProbes(
	listener net.Listener,
	expectedMethod string,
	result map[string]any,
	probeCount int,
	served chan<- error,
) {
	for probeIndex := 0; probeIndex < probeCount; probeIndex++ {
		if err := serveToolReadinessProbe(listener, expectedMethod, result); err != nil {
			served <- err
			return
		}
	}

	served <- nil
}

func serveToolReadinessProbe(listener net.Listener, expectedMethod string, result map[string]any) error {
	conn, err := listener.Accept()
	if err != nil {
		return err
	}
	defer func() {
		_ = conn.Close()
	}()

	requestPayload, err := framing.Read(bufio.NewReader(conn))
	if err != nil {
		return err
	}
	var request struct {
		Method string `json:"method"`
	}
	if err := json.Unmarshal(requestPayload, &request); err != nil {
		return err
	}
	if request.Method != expectedMethod {
		return fmt.Errorf("method mismatch: %s", request.Method)
	}

	response := map[string]any{
		"jsonrpc": "2.0",
		"result":  result,
		"id":      1,
	}
	payload, err := json.Marshal(response)
	if err != nil {
		return err
	}
	return framing.Write(conn, payload)
}

func assertToolReadinessProbeServed(t *testing.T, served <-chan error) {
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
