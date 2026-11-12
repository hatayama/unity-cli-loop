//go:build !windows

package projectrunner

import (
	"net"
	"testing"
)

// newLoopbackIpcListener creates a local listener that the shared client dial
// path can reach in tests. On POSIX the client dials the endpoint network
// verbatim, so a loopback TCP listener stands in for the Unity server.
func newLoopbackIpcListener(t *testing.T) net.Listener {
	t.Helper()

	listener, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatalf("failed to listen: %v", err)
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

	listener, err := net.Listen(network, address)
	if err != nil {
		t.Fatalf("failed to listen again on %s: %v", address, err)
	}
	t.Cleanup(func() {
		_ = listener.Close()
	})
	return listener
}
