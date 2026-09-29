package projectrunner

import (
	"bufio"
	"context"
	"net"
	"runtime"
	"testing"
	"time"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

// Verifies a status query that Unity acknowledged but did not answer before the deadline is marked
// unanswered, which is what lets a reattach wait for a compile that blocks the main thread.
func TestQueryCompileStatusFromUnityMarksAcknowledgedDeadlineUnanswered(t *testing.T) {
	if runtime.GOOS == "windows" {
		t.Skip("TCP endpoint injection is only used by this non-Windows client test")
	}

	endpoint := startCompileStatusSilentServer(t, true)
	ctx, cancel := context.WithTimeout(context.Background(), 200*time.Millisecond)
	defer cancel()

	_, err := queryCompileStatusFromUnity(
		ctx,
		unityipc.Connection{Endpoint: endpoint, ProjectRoot: t.TempDir()},
		"compile_status_acknowledged")
	if !isUnansweredStatusProbe(err) {
		t.Fatalf("an acknowledged query that ran out of time should be unanswered: %v", err)
	}
}

// Verifies a status query whose read deadline passed before any ack stays unmarked: without the ack
// nothing shows that Unity is processing requests at all.
func TestQueryCompileStatusFromUnityLeavesUnacknowledgedDeadlineUnmarked(t *testing.T) {
	if runtime.GOOS == "windows" {
		t.Skip("TCP endpoint injection is only used by this non-Windows client test")
	}

	endpoint := startCompileStatusSilentServer(t, false)
	ctx, cancel := context.WithTimeout(context.Background(), 200*time.Millisecond)
	defer cancel()

	_, err := queryCompileStatusFromUnity(
		ctx,
		unityipc.Connection{Endpoint: endpoint, ProjectRoot: t.TempDir()},
		"compile_status_unacknowledged")
	if !clierrors.IsFinalResponseTimeoutError(err) {
		t.Fatalf("expected the read deadline to end the query: %v", err)
	}
	if isUnansweredStatusProbe(err) {
		t.Fatalf("a query without an ack must not count as unanswered: %v", err)
	}
}

// startCompileStatusSilentServer accepts one status query, writes the dispatch ack when acknowledge is
// true, and then holds the connection without answering until the client closes it.
func startCompileStatusSilentServer(t *testing.T, acknowledge bool) unityipc.Endpoint {
	t.Helper()
	listener, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatalf("failed to listen: %v", err)
	}
	t.Cleanup(func() {
		_ = listener.Close()
	})

	go func() {
		conn, acceptErr := listener.Accept()
		if acceptErr != nil {
			return
		}
		defer func() {
			_ = conn.Close()
		}()

		reader := bufio.NewReader(conn)
		if _, readErr := unityipc.Read(reader); readErr != nil {
			return
		}
		if acknowledge {
			accepted := []byte(`{"jsonrpc":"2.0","result":{"accepted":true},"uloop":{"phase":"accepted"},"id":1}`)
			if writeErr := unityipc.Write(conn, accepted); writeErr != nil {
				return
			}
		}
		// Why read again: it blocks until the client gives up and closes the connection, so the query
		// sees Unity holding the request rather than dropping the connection.
		_, _ = unityipc.Read(reader)
	}()

	return unityipc.Endpoint{Network: "tcp", Address: listener.Addr().String()}
}
