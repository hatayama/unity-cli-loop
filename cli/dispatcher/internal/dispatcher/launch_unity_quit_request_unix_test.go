//go:build !windows

package dispatcher

import (
	"bufio"
	"context"
	"encoding/json"
	"net"
	"testing"
	"time"

	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

const quitRequestTestTimeout = 300 * time.Millisecond

// quitRequestTestServer serves one connection: it reads the request, records its method, and then calls
// reply, which may answer, hold the connection open, or return to drop it.
func quitRequestTestServer(t *testing.T, reply func(net.Conn)) (unityipc.Connection, <-chan string) {
	t.Helper()
	listener, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatalf("failed to listen: %v", err)
	}
	t.Cleanup(func() { _ = listener.Close() })
	methods := make(chan string, 1)
	go func() {
		conn, err := listener.Accept()
		if err != nil {
			return
		}
		defer func() { _ = conn.Close() }()
		payload, err := unityipc.Read(bufio.NewReader(conn))
		if err != nil {
			return
		}
		var request struct {
			Method string `json:"method"`
		}
		_ = json.Unmarshal(payload, &request)
		methods <- request.Method
		reply(conn)
	}()
	return unityipc.Connection{
		ProjectRoot: t.TempDir(),
		Endpoint:    unityipc.Endpoint{Network: "tcp", Address: listener.Addr().String()},
	}, methods
}

// Verifies an Editor that answers the quit request is taken as quitting, and that the request names the
// quit-editor bridge command.
func TestSendUnityQuitRequestTakesAnAnswerAsAQuitInProgress(t *testing.T) {
	connection, methods := quitRequestTestServer(t, func(conn net.Conn) {
		_ = unityipc.Write(conn, []byte(`{"jsonrpc":"2.0","result":{"Success":true},"id":1}`))
	})

	if !sendUnityQuitRequest(context.Background(), connection, quitRequestTestTimeout) {
		t.Fatal("an answered quit request must be taken as a quit in progress")
	}
	if method := <-methods; method != quitEditorBridgeCommandName {
		t.Fatalf("method = %q, want %q", method, quitEditorBridgeCommandName)
	}
}

// Verifies a connection that drops after the request reached the Editor is taken as a quit in progress,
// since an Editor that exits before its answer is written drops the connection the same way.
func TestSendUnityQuitRequestTakesADropAfterTheRequestArrivedAsAQuitInProgress(t *testing.T) {
	connection, _ := quitRequestTestServer(t, func(net.Conn) {})

	if !sendUnityQuitRequest(context.Background(), connection, quitRequestTestTimeout) {
		t.Fatal("a connection dropped after the request arrived must be taken as a quit in progress")
	}
}

// Verifies an Editor that acknowledges the request but never runs it, as a frozen main thread does, is
// not taken as quitting once the request times out.
func TestSendUnityQuitRequestDoesNotTakeAnUnansweredRequestAsAQuit(t *testing.T) {
	connection, _ := quitRequestTestServer(t, func(conn net.Conn) {
		_ = unityipc.Write(conn, []byte(`{"jsonrpc":"2.0","uloop":{"phase":"accepted"},"id":1}`))
		time.Sleep(3 * quitRequestTestTimeout)
	})

	if sendUnityQuitRequest(context.Background(), connection, quitRequestTestTimeout) {
		t.Fatal("a request the Editor never answered must not be taken as a quit in progress")
	}
}

// Verifies an Editor that rejects the request, as a package that predates quit-editor does, is not taken
// as quitting.
func TestSendUnityQuitRequestDoesNotTakeARejectedRequestAsAQuit(t *testing.T) {
	connection, _ := quitRequestTestServer(t, func(conn net.Conn) {
		_ = unityipc.Write(conn, []byte(`{"jsonrpc":"2.0","error":{"code":-32601,"message":"tool not found"},"id":1}`))
	})

	if sendUnityQuitRequest(context.Background(), connection, quitRequestTestTimeout) {
		t.Fatal("a rejected request must not be taken as a quit in progress")
	}
}

// Verifies a rejection whose message reads like a dropped connection is still taken as a rejection, since
// the Editor that sent it is up and will not exit.
func TestSendUnityQuitRequestDoesNotTakeARejectionThatMentionsEOFAsAQuit(t *testing.T) {
	connection, _ := quitRequestTestServer(t, func(conn net.Conn) {
		_ = unityipc.Write(conn, []byte(`{"jsonrpc":"2.0","error":{"code":-32603,"message":"unexpected EOF while reading settings"},"id":1}`))
	})

	if sendUnityQuitRequest(context.Background(), connection, quitRequestTestTimeout) {
		t.Fatal("a rejected request must not be taken as a quit in progress, whatever its message says")
	}
}

// Verifies an Editor whose server is not listening is not taken as quitting.
func TestSendUnityQuitRequestDoesNotTakeAnUnreachableServerAsAQuit(t *testing.T) {
	listener, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatalf("failed to listen: %v", err)
	}
	address := listener.Addr().String()
	_ = listener.Close()
	connection := unityipc.Connection{
		ProjectRoot: t.TempDir(),
		Endpoint:    unityipc.Endpoint{Network: "tcp", Address: address},
	}

	if sendUnityQuitRequest(context.Background(), connection, quitRequestTestTimeout) {
		t.Fatal("a request that never reached a server must not be taken as a quit in progress")
	}
}
