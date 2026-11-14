package projectrunner

import (
	"context"
	"errors"
	"fmt"
	"io"
	"sync"
	"testing"
	"time"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"

	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

// earlyDialogReportScript drives runEarlyDialogReport by hand: every wait the watch asks for is
// recorded and released at once, the probe answers from reports in order (repeating the last), and
// the watch's context is cancelled when the looks run out, the way an answered request ends it.
type earlyDialogReportScript struct {
	reports   []statusReport
	probeErr  error
	reportErr error
	waits     []time.Duration
	probes    int
	sent      int
}

func (script *earlyDialogReportScript) deps() earlyDialogReportDeps {
	return earlyDialogReportDeps{
		probeEditorStatus: func(context.Context, unityipc.Connection) (statusReport, error) {
			report := script.reports[min(script.probes, len(script.reports)-1)]
			script.probes++
			return report, script.probeErr
		},
		reportBlockingDialog: func(context.Context, unityipc.Connection) error {
			script.sent++
			return script.reportErr
		},
		waitFor: func(wait time.Duration) <-chan time.Time {
			script.waits = append(script.waits, wait)
			released := make(chan time.Time, 1)
			released <- time.Time{}
			return released
		},
	}
}

func (script *earlyDialogReportScript) run(interval time.Duration) {
	runEarlyDialogReport(context.Background(), unityipc.Connection{}, commandDialogBlockCommand, script.deps(), interval)
}

// heldBlockedByDialogReport is a BlockedByDialog report whose slot is held by holder.
func heldBlockedByDialogReport(holder string, dialogs ...statusDialog) statusReport {
	report := blockedByDialogStatusReport(dialogs...)
	report.RunningToolName = holder
	return report
}

// Verifies the watch tells Unity about a dialog only when the request holding the slot is a command
// of the same name as the send's, whatever the timing: another request, such as a compile uloop said
// keeps running, must not be failed by this send's report.
func TestEarlyDialogReportTellsUnityOnlyWhenItsOwnCommandHoldsTheSlot(t *testing.T) {
	cases := []struct {
		name       string
		holder     string
		method     string
		wantReport bool
	}{
		{name: "own command", holder: "get-logs", method: "get-logs", wantReport: true},
		{name: "compile holds the slot", holder: "compile", method: "get-logs"},
		{name: "no holder named", holder: "", method: "get-logs"},
		{name: "dynamic code", holder: "execute-dynamic-code", method: "execute-dynamic-code", wantReport: true},
	}
	for _, tc := range cases {
		t.Run(tc.name, func(t *testing.T) {
			script := &earlyDialogReportScript{reports: []statusReport{
				heldBlockedByDialogReport(tc.holder, commandDialogPlayerStoppedDialog()),
			}}

			runEarlyDialogReport(context.Background(), unityipc.Connection{}, tc.method, script.deps(), 10*time.Second)

			if (script.sent > 0) != tc.wantReport || script.probes < 2 {
				t.Fatalf("reports = %d, looks = %d, want reported=%v while the looks go on",
					script.sent, script.probes, tc.wantReport)
			}
		})
	}
}

func readyStatusReport() statusReport {
	return newStatusReport(statusStateReady, "ready")
}

// Verifies the watch tells Unity about a dialog at each look that finds one, first after the short
// delay and then at the interval, and stops looking before Unity's own 30-second answer.
func TestEarlyDialogReportTellsUnityAtEachLookThatFindsADialog(t *testing.T) {
	script := &earlyDialogReportScript{reports: []statusReport{
		readyStatusReport(),
		heldBlockedByDialogReport(commandDialogBlockCommand, commandDialogPlayerStoppedDialog()),
	}}

	script.run(10 * time.Second)

	wantWaits := []time.Duration{earlyDialogLookDelay, 10 * time.Second, 10 * time.Second}
	if len(script.waits) != len(wantWaits) {
		t.Fatalf("waits = %v, want %v", script.waits, wantWaits)
	}
	for index, want := range wantWaits {
		if script.waits[index] != want {
			t.Fatalf("waits = %v, want %v", script.waits, wantWaits)
		}
	}
	if script.probes != 3 || script.sent != 2 {
		t.Fatalf("probes = %d, reports sent = %d, want 3 and 2", script.probes, script.sent)
	}
}

// Verifies nothing is sent to Unity while no dialog is found: for an Editor that is only busy, for a
// blocked state without a dialog to name, and for a look that failed.
func TestEarlyDialogReportStaysSilentWithoutADialog(t *testing.T) {
	cases := []struct {
		name     string
		report   statusReport
		probeErr error
	}{
		{name: "busy Editor", report: newStatusReport(statusStateBusy, "busy")},
		{name: "blocked without a dialog", report: newStatusReport(statusStateBlockedByDialog, "blocked")},
		{name: "failed look", report: heldBlockedByDialogReport(commandDialogBlockCommand, commandDialogPlayerStoppedDialog()), probeErr: errors.New("unreachable")},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			script := &earlyDialogReportScript{reports: []statusReport{testCase.report}, probeErr: testCase.probeErr}

			script.run(10 * time.Second)

			if script.probes != 3 || script.sent != 0 {
				t.Fatalf("probes = %d, reports sent = %d, want 3 and 0", script.probes, script.sent)
			}
		})
	}
}

// Verifies a Unity package that does not take the report is not asked again.
func TestEarlyDialogReportStopsWhenUnityDoesNotTakeTheReport(t *testing.T) {
	script := &earlyDialogReportScript{
		reports:   []statusReport{heldBlockedByDialogReport(commandDialogBlockCommand, commandDialogPlayerStoppedDialog())},
		reportErr: errors.New("Unknown tool: report-blocking-dialog"),
	}

	script.run(10 * time.Second)

	if script.probes != 1 || script.sent != 1 {
		t.Fatalf("probes = %d, reports sent = %d, want 1 and 1", script.probes, script.sent)
	}
}

// Verifies a send that holds the main thread on purpose, as a pause-point trigger does, looks only
// once inside the window.
func TestEarlyDialogReportLooksOnceAtThePausePointInterval(t *testing.T) {
	script := &earlyDialogReportScript{reports: []statusReport{readyStatusReport()}}

	script.run(pausePointDialogWatchInterval)

	if script.probes != 1 {
		t.Fatalf("probes = %d, want 1", script.probes)
	}
}

// Verifies the watch is off for requests Unity runs without a command slot and when its dependencies
// are not set, and that an answered request ends it without a look.
func TestStartEarlyDialogReportDoesNotLookWhenOffOrAnswered(t *testing.T) {
	script := &earlyDialogReportScript{reports: []statusReport{heldBlockedByDialogReport(commandDialogBlockCommand, commandDialogPlayerStoppedDialog())}}
	never := script.deps()
	never.waitFor = func(time.Duration) <-chan time.Time { return make(chan time.Time) }

	startEarlyDialogReport(context.Background(), unityipc.Connection{}, compileStatusCommandName, 0, script.deps())()
	startEarlyDialogReport(context.Background(), unityipc.Connection{}, "get-logs", 0, earlyDialogReportDeps{})()
	startEarlyDialogReport(context.Background(), unityipc.Connection{}, "get-logs", 0, never)()

	if script.probes != 0 || script.sent != 0 {
		t.Fatalf("probes = %d, reports sent = %d, want none", script.probes, script.sent)
	}
}

// Verifies a send that ends its own wait for the answer, as compile does, never starts the watch:
// Unity would fail a request that runs later while uloop says it keeps running.
func TestStartEarlyDialogReportDoesNotWatchASendThatEndsItsOwnWait(t *testing.T) {
	script := &earlyDialogReportScript{reports: []statusReport{heldBlockedByDialogReport(commandDialogBlockCommand, commandDialogPlayerStoppedDialog())}}

	startEarlyDialogReport(context.Background(), unityipc.Connection{}, "compile", 2*time.Second, script.deps())()

	// Why waits: the watch asks for its first wait before it can be cancelled, so a started watch
	// always records one, while a probe may lose the race with the stop.
	if len(script.waits) != 0 || script.probes != 0 || script.sent != 0 {
		t.Fatalf("waits = %v, probes = %d, reports sent = %d, want none", script.waits, script.probes, script.sent)
	}
}

// serveUntilDialogReported accepts one request and answers it with main_thread_blocked only once
// reported is closed, the way Unity fails a request that has not started after a dialog report.
func serveUntilDialogReported(t *testing.T, command string, reported <-chan struct{}) unityipc.Connection {
	t.Helper()
	listener := newLoopbackIpcListener(t)
	go func() {
		conn, acceptErr := listener.Accept()
		if acceptErr != nil {
			return
		}
		defer func() { _ = conn.Close() }()
		if _, readErr := readScriptedIPCRequest(conn); readErr != nil {
			return
		}
		accepted := `{"jsonrpc":"2.0","result":{"accepted":true},"uloop":{"phase":"accepted","heartbeatIntervalSeconds":10},"id":1}`
		if writeErr := unityipc.Write(conn, []byte(accepted)); writeErr != nil {
			return
		}
		<-reported
		answer := fmt.Sprintf(
			`{"jsonrpc":"2.0","error":{"code":-32603,"message":"blocked","data":%s},"id":1}`,
			fmt.Sprintf(commandDialogMainThreadData, command))
		if writeErr := unityipc.Write(conn, []byte(answer)); writeErr != nil {
			return
		}
		_, _ = io.Copy(io.Discard, conn)
	}()
	return unityipc.Connection{
		Endpoint:    unityipc.Endpoint{Network: listener.Addr().Network(), Address: listener.Addr().String()},
		ProjectRoot: t.TempDir(),
	}
}

// Verifies a send whose request is unanswered tells Unity about the dialog and keeps waiting on the
// same connection, so Unity's answer that the command was not run arrives and is reported with the
// dialog. A send that hung up instead would never read that answer and would end at the test's deadline.
func TestSendReportsADialogFoundWhileUnansweredAndKeepsWaitingForUnity(t *testing.T) {
	reported := make(chan struct{})
	var once sync.Once
	probe := &commandDialogProbe{report: blockedByDialogStatusReport(commandDialogPlayerStoppedDialog())}
	deps := defaultConnectionRetryDeps()
	deps.findRunningUnityProcess = runningUnityProcessLookup
	deps.dialogWatch = probe.deps(true)
	deps.earlyDialogReport = earlyDialogReportDeps{
		probeEditorStatus: func(context.Context, unityipc.Connection) (statusReport, error) {
			return heldBlockedByDialogReport(commandDialogBlockCommand, commandDialogPlayerStoppedDialog()), nil
		},
		reportBlockingDialog: func(context.Context, unityipc.Connection) error {
			once.Do(func() { close(reported) })
			return nil
		},
		waitFor: func(time.Duration) <-chan time.Time {
			released := make(chan time.Time, 1)
			released <- time.Time{}
			return released
		},
	}
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()

	_, err := sendWithTransientConnectionRetryWithDeps(
		ctx, serveUntilDialogReported(t, commandDialogBlockCommand, reported), commandDialogBlockCommand, map[string]any{}, nil, 0, deps)

	cliError := clierrors.ClassifyError(err, clierrors.ErrorContext{Command: commandDialogBlockCommand})
	assertCommandBlockedByDialog(t, cliError, commandDialogPlayerStoppedDialog().Title)
	if !isMainThreadBlockedRPCError(err) {
		t.Fatalf("the error must still carry Unity's answer that the command was not run: %v", err)
	}
}
