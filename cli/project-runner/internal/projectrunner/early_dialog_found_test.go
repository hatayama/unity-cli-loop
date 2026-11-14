package projectrunner

import (
	"context"
	"errors"
	"fmt"
	"io"
	"reflect"
	"sync"
	"sync/atomic"
	"testing"
	"time"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

func releasedAtOnce(time.Duration) <-chan time.Time {
	released := make(chan time.Time, 1)
	released <- time.Time{}
	return released
}

func otherBlockingDialog() statusDialog {
	return statusDialog{Title: "Another dialog", Message: "Close me", Buttons: []string{"OK"}}
}

// Verifies the watch hands back the dialog its last look found and reported, so the answer that the
// command was not run can name it without looking again.
func TestEarlyDialogReportHandsBackTheDialogOfItsLastReport(t *testing.T) {
	dialog := commandDialogPlayerStoppedDialog()
	script := &earlyDialogReportScript{reports: []statusReport{heldBlockedByDialogReport(commandDialogBlockCommand, dialog)}}

	found := script.run(10 * time.Second)

	if !reflect.DeepEqual(found, []statusDialog{dialog}) {
		t.Fatalf("found = %#v, want the reported dialog", found)
	}
}

// Verifies a dialog an earlier look reported is dropped once a later look no longer finds it, so a
// stale dialog never names the answer.
func TestEarlyDialogReportForgetsADialogTheLastLookNoLongerFinds(t *testing.T) {
	script := &earlyDialogReportScript{reports: []statusReport{
		heldBlockedByDialogReport(commandDialogBlockCommand, commandDialogPlayerStoppedDialog()),
		readyStatusReport(),
	}}

	found := script.run(10 * time.Second)

	if found != nil || script.sent != 1 {
		t.Fatalf("found = %#v, reports sent = %d, want nothing after one report", found, script.sent)
	}
}

// Verifies the watch hands back nothing when no look found a dialog holding its own command.
func TestEarlyDialogReportHandsBackNothingWithoutADialog(t *testing.T) {
	cases := []struct {
		name   string
		report statusReport
	}{
		{name: "ready Editor", report: readyStatusReport()},
		{name: "another command holds the slot", report: heldBlockedByDialogReport("compile", commandDialogPlayerStoppedDialog())},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			script := &earlyDialogReportScript{reports: []statusReport{testCase.report}}

			if found := script.run(10 * time.Second); found != nil {
				t.Fatalf("found = %#v, want nothing", found)
			}
		})
	}
}

// Verifies a dialog Unity did not take the report of is not handed back: an older package never
// fails the request early, so its answer must be explained by a fresh look.
func TestEarlyDialogReportHandsBackNothingWhenUnityDoesNotTakeTheReport(t *testing.T) {
	script := &earlyDialogReportScript{
		reports:   []statusReport{heldBlockedByDialogReport(commandDialogBlockCommand, commandDialogPlayerStoppedDialog())},
		reportErr: errors.New("Unknown tool: report-blocking-dialog"),
	}

	if found := script.run(10 * time.Second); found != nil {
		t.Fatalf("found = %#v, want nothing", found)
	}
}

// Verifies a watch stopped while it looks keeps the dialog of its last successful report and ignores
// what the stopped look returned: Unity failed the request for that report.
func TestEarlyDialogReportKeepsTheReportedDialogWhenStoppedDuringALook(t *testing.T) {
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	reported := commandDialogPlayerStoppedDialog()
	looks := 0
	deps := earlyDialogReportDeps{
		probeEditorStatus: func(context.Context, unityipc.Connection) (statusReport, error) {
			looks++
			if looks == 1 {
				return heldBlockedByDialogReport(commandDialogBlockCommand, reported), nil
			}
			cancel()
			return heldBlockedByDialogReport(commandDialogBlockCommand, otherBlockingDialog()), nil
		},
		reportBlockingDialog: func(context.Context, unityipc.Connection, string) error { return nil },
		waitFor:              releasedAtOnce,
	}

	found := runEarlyDialogReport(ctx, unityipc.Connection{}, commandDialogBlockCommand, deps, 10*time.Second)

	if !reflect.DeepEqual(found, []statusDialog{reported}) {
		t.Fatalf("found = %#v, want the dialog of the successful report", found)
	}
}

// Verifies a watch stopped while it reports keeps the dialog of its earlier successful report: the
// dialog the stopped report was for never reached Unity, and a stopped report is not one Unity refused.
func TestEarlyDialogReportKeepsTheEarlierDialogWhenStoppedDuringAReport(t *testing.T) {
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	reported := commandDialogPlayerStoppedDialog()
	looks := 0
	reports := 0
	deps := earlyDialogReportDeps{
		probeEditorStatus: func(context.Context, unityipc.Connection) (statusReport, error) {
			looks++
			if looks == 1 {
				return heldBlockedByDialogReport(commandDialogBlockCommand, reported), nil
			}
			return heldBlockedByDialogReport(commandDialogBlockCommand, otherBlockingDialog()), nil
		},
		reportBlockingDialog: func(context.Context, unityipc.Connection, string) error {
			reports++
			if reports == 1 {
				return nil
			}
			cancel()
			return context.Canceled
		},
		waitFor: releasedAtOnce,
	}

	found := runEarlyDialogReport(ctx, unityipc.Connection{}, commandDialogBlockCommand, deps, 10*time.Second)

	if !reflect.DeepEqual(found, []statusDialog{reported}) {
		t.Fatalf("found = %#v, want the dialog of the earlier successful report", found)
	}
}

// Verifies Unity's not-run answer is named by the dialog the watch reported without looking again,
// and reads exactly as the answer named by a fresh look.
func TestExplainBlockedByDialogNamesTheReportedDialogWithoutLooking(t *testing.T) {
	dialog := commandDialogPlayerStoppedDialog()
	notRun := commandDialogMainThreadBlockedRPCError(commandDialogBlockCommand)
	looked := &commandDialogProbe{report: blockedByDialogStatusReport(dialog)}
	wantError := classifyCommandDialogError(explainCommandDialogBlock(notRun, looked.deps(true)))
	probe := &commandDialogProbe{report: blockedByDialogStatusReport(otherBlockingDialog())}

	err := explainBlockedByDialog(context.Background(), unreachableConnection("<PROJECT_ROOT>"), commandDialogBlockCommand,
		notRun, []statusDialog{dialog}, probe.deps(true))

	if probe.calls != 0 {
		t.Fatalf("probe calls = %d, want none", probe.calls)
	}
	if cliError := classifyCommandDialogError(err); !reflect.DeepEqual(cliError, wantError) {
		t.Fatalf("error = %#v, want %#v", cliError, wantError)
	}
}

// Verifies a reported dialog does not stand in for a look when the wait ended unresponsive, since
// Unity did not fail that request for the report, and is ignored for any other failure.
func TestExplainBlockedByDialogIgnoresTheReportedDialogForOtherFailures(t *testing.T) {
	reported := []statusDialog{otherBlockingDialog()}
	dialog := commandDialogPlayerStoppedDialog()
	probe := &commandDialogProbe{report: blockedByDialogStatusReport(dialog)}

	unresponsive := explainBlockedByDialog(context.Background(), unreachableConnection("<PROJECT_ROOT>"), commandDialogBlockCommand,
		&unityipc.EditorUnresponsiveError{}, reported, probe.deps(true))
	other := errors.New("connection reset")
	unchanged := explainBlockedByDialog(context.Background(), unreachableConnection("<PROJECT_ROOT>"), commandDialogBlockCommand,
		other, reported, probe.deps(true))

	cliError := classifyCommandDialogError(unresponsive)
	if probe.calls != 1 || !reflect.DeepEqual(cliError.Details["Dialogs"], []statusDialog{dialog}) {
		t.Fatalf("probe calls = %d, Dialogs = %#v, want one look naming the looked-up dialog", probe.calls, cliError.Details["Dialogs"])
	}
	if !errors.Is(unchanged, other) {
		t.Fatalf("err = %v, want the other failure unchanged", unchanged)
	}
}

// serveReportedBusyThenNotRun accepts the first request and refuses it as busy once a dialog is
// reported, then marks second, accepts the second request and answers at once that it was not run.
func serveReportedBusyThenNotRun(t *testing.T, reported <-chan struct{}, second *atomic.Bool) unityipc.Connection {
	t.Helper()
	listener := newLoopbackIpcListener(t)
	accepted := `{"jsonrpc":"2.0","result":{"accepted":true},"uloop":{"phase":"accepted","heartbeatIntervalSeconds":10},"id":1}`
	notRun := fmt.Sprintf(`{"jsonrpc":"2.0","error":{"code":-32603,"message":"blocked","data":%s},"id":1}`,
		fmt.Sprintf(commandDialogMainThreadData, commandDialogBlockCommand))
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
				second.Store(true)
			}
			_ = unityipc.Write(conn, []byte(accepted))
			if attempt == 1 {
				select {
				case <-reported:
				case <-time.After(5 * time.Second):
				}
				_ = unityipc.Write(conn, []byte(busyRPCAnswer))
				_ = conn.Close()
				continue
			}
			_ = unityipc.Write(conn, []byte(notRun))
			_, _ = io.Copy(io.Discard, conn)
			_ = conn.Close()
		}
	}()
	return unityipc.Connection{
		Endpoint:    unityipc.Endpoint{Network: listener.Addr().Network(), Address: listener.Addr().String()},
		ProjectRoot: t.TempDir(),
	}
}

// Verifies a dialog an earlier attempt's watch reported does not name the not-run answer of a later
// attempt whose watch found none: the answer is named by a fresh look.
func TestSendDoesNotCarryAReportedDialogIntoTheNextAttempt(t *testing.T) {
	reported := make(chan struct{})
	var once sync.Once
	var second atomic.Bool
	looked := commandDialogPlayerStoppedDialog()
	probe := &commandDialogProbe{report: blockedByDialogStatusReport(looked)}
	deps := defaultConnectionRetryDeps()
	deps.findRunningUnityProcess = runningUnityProcessLookup
	deps.retryPoll = time.Millisecond
	deps.dialogWatch = probe.deps(true)
	deps.earlyDialogReport = earlyDialogReportDeps{
		probeEditorStatus: func(context.Context, unityipc.Connection) (statusReport, error) {
			if second.Load() {
				return readyStatusReport(), nil
			}
			return heldBlockedByDialogReport(commandDialogBlockCommand, otherBlockingDialog()), nil
		},
		reportBlockingDialog: func(context.Context, unityipc.Connection, string) error {
			once.Do(func() { close(reported) })
			return nil
		},
		waitFor: releasedAtOnce,
	}
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()

	_, err := sendWithTransientConnectionRetryWithDeps(
		ctx, serveReportedBusyThenNotRun(t, reported, &second), commandDialogBlockCommand, map[string]any{}, nil, 0, deps)

	cliError := clierrors.ClassifyError(err, clierrors.ErrorContext{Command: commandDialogBlockCommand})
	assertCommandBlockedByDialog(t, cliError, looked.Title)
	if probe.calls != 1 {
		t.Fatalf("probe calls = %d, want one fresh look", probe.calls)
	}
}
