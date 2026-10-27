//go:build !windows

package projectrunner

import (
	"bufio"
	"context"
	"errors"
	"testing"
	"time"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
	"github.com/hatayama/unity-cli-loop/common/unityprocess"
)

// scriptedDropServerConnection serves one connection per entry of results, in order: an empty entry reads
// the request and drops the connection, as a server that went away does, and any other entry answers with
// that result. Every connection after the script is dropped too.
func scriptedDropServerConnection(t *testing.T, results []string) unityipc.Connection {
	t.Helper()
	listener := newLoopbackIpcListener(t)
	go func() {
		for index := 0; ; index++ {
			conn, err := listener.Accept()
			if err != nil {
				return
			}
			if _, err := unityipc.Read(bufio.NewReader(conn)); err == nil && index < len(results) && results[index] != "" {
				_ = unityipc.Write(conn, []byte(`{"jsonrpc":"2.0","result":`+results[index]+`,"id":1}`))
			}
			_ = conn.Close()
		}
	}()
	return unityipc.Connection{
		ProjectRoot: t.TempDir(),
		Endpoint:    unityipc.Endpoint{Network: "tcp", Address: listener.Addr().String()},
	}
}

// Verifies a status answer between two lost connections restarts the play mode wait's exit count, so an
// Editor that answered in between is not taken as gone after only two lookups in total.
func TestWaitForControlPlayModeStateRestartsTheExitCountAfterAnAnswer(t *testing.T) {
	originalPoll := controlPlayModeStatePoll
	controlPlayModeStatePoll = time.Millisecond
	t.Cleanup(func() { controlPlayModeStatePoll = originalPoll })
	connection := scriptedDropServerConnection(t, []string{"", `{"IsPlaying":false}`})
	lookups := 0
	clock := time.Unix(1_700_000_000, 0)
	exitWatch := clicore.NewUnityExitWatch(
		connection.ProjectRoot,
		"",
		func(context.Context, string) (*unityprocess.UnityProcess, error) {
			lookups++
			return nil, nil
		},
		func() time.Time {
			// Two seconds per reading keeps every lookup past the watch's spacing.
			clock = clock.Add(2 * time.Second)
			return clock
		},
	)

	_, _, err := waitForControlPlayModeState(context.Background(), connection, "Play", 5*time.Second, exitWatch)

	var notRunning clierrors.UnityNotRunningError
	if !errors.As(err, &notRunning) {
		t.Fatalf("err = %v, want UnityNotRunningError once the connection keeps dropping", err)
	}
	if lookups != 3 {
		t.Fatalf("lookups = %d, want 3: one before the answer, then two that confirm the exit", lookups)
	}
}

// Verifies a connection that drops after the request reached Unity comes back from the send as
// UnityNotRunningError when no Editor process is left, rather than as a disconnect to wait out.
func TestSendWithTransientConnectionRetryReportsAnEditorThatExitedAfterDispatch(t *testing.T) {
	deps := defaultConnectionRetryDeps()
	deps.findRunningUnityProcess = noUnityProcessLookup
	listener := newLoopbackIpcListener(t)
	go func() {
		conn, err := listener.Accept()
		if err != nil {
			return
		}
		defer func() { _ = conn.Close() }()
		if _, err := unityipc.Read(bufio.NewReader(conn)); err != nil {
			return
		}
		accepted := `{"jsonrpc":"2.0","result":{"accepted":true},"uloop":{"phase":"accepted"},"id":1}`
		_ = unityipc.Write(conn, []byte(accepted))
	}()
	connection := unityipc.Connection{
		ProjectRoot: t.TempDir(),
		Endpoint:    unityipc.Endpoint{Network: "tcp", Address: listener.Addr().String()},
	}

	outcome, err := sendWithTransientConnectionRetryWithDeps(
		context.Background(), connection, "compile", map[string]any{}, nil, 0, deps)

	var notRunning clierrors.UnityNotRunningError
	if !errors.As(err, &notRunning) || !notRunning.ExitedDuringCommand {
		t.Fatalf("err = %v, want UnityNotRunningError for an exit during the command", err)
	}
	if !outcome.RequestDispatched {
		t.Fatalf("outcome = %+v, want a dispatched request", outcome)
	}
}
