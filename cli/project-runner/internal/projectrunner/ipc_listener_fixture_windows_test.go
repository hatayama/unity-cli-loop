//go:build windows

package projectrunner

import (
	"crypto/rand"
	"encoding/hex"
	"net"
	"testing"

	"github.com/Microsoft/go-winio"
)

// newLoopbackIpcListener creates a local listener that the shared client dial
// path can reach in tests. Why a named pipe: the Windows client dial always
// targets a named pipe, so a TCP fixture would never receive the connection.
// A random suffix keeps concurrently running tests on distinct pipes.
func newLoopbackIpcListener(t *testing.T) net.Listener {
	t.Helper()

	suffix := make([]byte, 8)
	if _, err := rand.Read(suffix); err != nil {
		t.Fatalf("failed to generate pipe name suffix: %v", err)
	}
	pipeName := `\\.\pipe\uloop-test-` + hex.EncodeToString(suffix)
	listener, err := winio.ListenPipe(pipeName, nil)
	if err != nil {
		t.Fatalf("failed to listen on named pipe %s: %v", pipeName, err)
	}
	t.Cleanup(func() {
		_ = listener.Close()
	})
	return listener
}

// relistenLoopbackIpc opens a listener again on an endpoint a test closed, so the server can stand
// in for one that comes up while a command waits for it.
func relistenLoopbackIpc(t *testing.T, network string, address string) net.Listener {
	t.Helper()

	listener, err := winio.ListenPipe(address, nil)
	if err != nil {
		t.Fatalf("failed to listen again on named pipe %s (%s): %v", address, network, err)
	}
	t.Cleanup(func() {
		_ = listener.Close()
	})
	return listener
}
