package projectrunner

import (
	"context"
	"errors"
	"fmt"
	"io"
	"sync"
	"sync/atomic"
	"testing"
	"time"

	cliprogress "github.com/hatayama/unity-cli-loop/common/progress"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

const busyRPCAnswer = `{"jsonrpc":"2.0","error":{"code":-32603,"message":"busy","data":{"type":"server_busy"}},"id":1}`

// gatedEarlyDialogReport counts looks and reports. Every wait the watch asks for fires once release
// is called, so a watch still running then looks, and one that already stopped does not.
type gatedEarlyDialogReport struct {
	open     chan struct{}
	opened   sync.Once
	probes   atomic.Int32
	sent     atomic.Int32
	waits    chan struct{}
	reported chan struct{}
	once     sync.Once
}

func newGatedEarlyDialogReport() *gatedEarlyDialogReport {
	return &gatedEarlyDialogReport{open: make(chan struct{}), waits: make(chan struct{}, 16), reported: make(chan struct{})}
}

func (gated *gatedEarlyDialogReport) release() {
	gated.opened.Do(func() { close(gated.open) })
}

func (gated *gatedEarlyDialogReport) deps() earlyDialogReportDeps {
	return earlyDialogReportDeps{
		probeEditorStatus: func(context.Context, unityipc.Connection) (statusReport, error) {
			gated.probes.Add(1)
			return blockedByDialogStatusReport(commandDialogPlayerStoppedDialog()), nil
		},
		reportBlockingDialog: func(context.Context, unityipc.Connection) error {
			gated.sent.Add(1)
			gated.once.Do(func() { close(gated.reported) })
			return nil
		},
		waitFor: func(time.Duration) <-chan time.Time {
			gated.waits <- struct{}{}
			fired := make(chan time.Time, 1)
			go func() {
				<-gated.open
				fired <- time.Time{}
			}()
			return fired
		},
	}
}

// Verifies the watch of one attempt ends with that attempt: an attempt Unity accepted and then
// refused as busy leaves no watch behind, so nothing looks or reports once its waits are released.
func TestEarlyDialogReportEndsWithTheAttempt(t *testing.T) {
	gated := newGatedEarlyDialogReport()
	busy := errors.New("server busy")

	_, err := sendAttemptWithEarlyDialogReport(context.Background(), unityipc.Connection{}, "get-logs", 0, gated.deps(), nil,
		func(progress unityipc.ProgressFunc) (unityipc.UnitySendOutcome, error) {
			progress(cliprogress.Event{Stage: cliprogress.StageAccepted})
			select {
			case <-gated.waits:
			case <-time.After(5 * time.Second):
				t.Error("the accepted attempt started no watch")
			}
			return unityipc.UnitySendOutcome{RequestDispatched: true, RequestAccepted: true}, busy
		})
	gated.release()
	// Why a short pause: a watch the attempt failed to stop would look as soon as its wait fires.
	time.Sleep(50 * time.Millisecond)

	if !errors.Is(err, busy) || gated.probes.Load() != 0 || gated.sent.Load() != 0 {
		t.Fatalf("err = %v, probes = %d, reports = %d, want the busy answer without a look", err, gated.probes.Load(), gated.sent.Load())
	}
}

// Verifies an attempt Unity never accepts starts no watch, and the caller's progress still sees every event.
func TestEarlyDialogReportDoesNotWatchAnAttemptUnityDidNotAccept(t *testing.T) {
	gated := newGatedEarlyDialogReport()
	gated.release()
	var seen []cliprogress.Stage

	_, err := sendAttemptWithEarlyDialogReport(context.Background(), unityipc.Connection{}, "get-logs", 0, gated.deps(),
		func(event cliprogress.Event) { seen = append(seen, event.Stage) },
		func(progress unityipc.ProgressFunc) (unityipc.UnitySendOutcome, error) {
			progress(cliprogress.Event{Stage: cliprogress.StageConnected})
			return unityipc.UnitySendOutcome{}, io.EOF
		})

	if !errors.Is(err, io.EOF) || len(gated.waits) != 0 || gated.probes.Load() != 0 || len(seen) != 1 {
		t.Fatalf("err = %v, waits = %d, probes = %d, seen = %v, want no watch and the event passed on",
			err, len(gated.waits), gated.probes.Load(), seen)
	}
}

// serveBusyThenUnanswered refuses the first request as busy after accepting it, then accepts the
// second and answers it with main_thread_blocked only once a dialog is reported. The waits of the
// early report are released just before the second accept goes out.
func serveBusyThenUnanswered(t *testing.T, gated *gatedEarlyDialogReport) unityipc.Connection {
	t.Helper()
	listener := newLoopbackIpcListener(t)
	accepted := `{"jsonrpc":"2.0","result":{"accepted":true},"uloop":{"phase":"accepted","heartbeatIntervalSeconds":10},"id":1}`
	go func() {
		for attempt := 1; attempt <= 2; attempt++ {
			conn, acceptErr := listener.Accept()
			if acceptErr != nil {
				return
			}
			if _, readErr := readScriptedIPCRequest(conn); readErr != nil {
				_ = conn.Close()
				return
			}
			if attempt == 2 {
				gated.release()
			}
			_ = unityipc.Write(conn, []byte(accepted))
			if attempt == 1 {
				_ = unityipc.Write(conn, []byte(busyRPCAnswer))
				_ = conn.Close()
				continue
			}
			<-gated.reported
			answer := fmt.Sprintf(
				`{"jsonrpc":"2.0","error":{"code":-32603,"message":"blocked","data":%s},"id":1}`,
				fmt.Sprintf(commandDialogMainThreadData, commandDialogBlockCommand))
			_ = unityipc.Write(conn, []byte(answer))
			_, _ = io.Copy(io.Discard, conn)
			_ = conn.Close()
		}
	}()
	return unityipc.Connection{
		Endpoint:    unityipc.Endpoint{Network: listener.Addr().Network(), Address: listener.Addr().String()},
		ProjectRoot: t.TempDir(),
	}
}

// Verifies a send refused as busy and then accepted looks and reports only for the accepted attempt:
// the busy attempt's watch never looks, and the accepted one reports the dialog.
func TestSendReportsADialogOnlyForTheAttemptUnityAccepted(t *testing.T) {
	gated := newGatedEarlyDialogReport()
	deps := defaultConnectionRetryDeps()
	deps.findRunningUnityProcess = runningUnityProcessLookup
	deps.retryPoll = time.Millisecond
	deps.dialogWatch = (&commandDialogProbe{report: blockedByDialogStatusReport(commandDialogPlayerStoppedDialog())}).deps(true)
	deps.earlyDialogReport = gated.deps()
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()

	_, err := sendWithTransientConnectionRetryWithDeps(
		ctx, serveBusyThenUnanswered(t, gated), commandDialogBlockCommand, map[string]any{}, nil, 0, deps)

	// Why at least one: the accepted attempt's watch may look again before Unity's answer ends it.
	if !isMainThreadBlockedRPCError(err) || gated.sent.Load() < 1 || gated.probes.Load() < 1 {
		t.Fatalf("err = %v, probes = %d, reports = %d, want a look and a report for the accepted attempt",
			err, gated.probes.Load(), gated.sent.Load())
	}
}
