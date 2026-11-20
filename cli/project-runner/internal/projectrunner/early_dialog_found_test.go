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

// runUntilAnswered runs the watch until it has looked looks times, then ends it the way Unity's
// answer does: the stop cancels it while it waits for its next look.
func runUntilAnswered(script *earlyDialogReportScript, looks int) []statusDialog {
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	deps := script.deps()
	deps.waitFor = func(wait time.Duration) <-chan time.Time {
		if script.probes >= looks {
			cancel()
			return make(chan time.Time)
		}
		return releasedAtOnce(wait)
	}
	return runEarlyDialogReport(ctx, unityipc.Connection{}, commandDialogBlockCommand, deps, 10*time.Second).dialogs
}

// releasedOnce releases the first wait at once and never the later ones, so a watch looks once and
// then waits until it is stopped.
func releasedOnce() func(time.Duration) <-chan time.Time {
	var calls atomic.Int32
	return func(wait time.Duration) <-chan time.Time {
		if calls.Add(1) == 1 {
			return releasedAtOnce(wait)
		}
		return make(chan time.Time)
	}
}

func otherBlockingDialog() statusDialog {
	return statusDialog{Title: "Another dialog", Message: "Close me", Buttons: []string{"OK"}}
}

// Verifies the watch hands back the dialog its last look found and reported, so the answer that the
// command was not run can name it without looking again.
func TestEarlyDialogReportHandsBackTheDialogOfItsLastReport(t *testing.T) {
	dialog := commandDialogPlayerStoppedDialog()
	script := &earlyDialogReportScript{reports: []statusReport{heldBlockedByDialogReport(commandDialogBlockCommand, dialog)}}

	found := runUntilAnswered(script, 2)

	if !reflect.DeepEqual(found, []statusDialog{dialog}) {
		t.Fatalf("found = %#v, want the reported dialog", found)
	}
}

// Verifies the reported dialogs name an answer that arrives up to 3 seconds after the look that saw
// them, the time a report-caused answer needs, and not a later one, which may have another cause.
func TestEarlyDialogFindingNamesOnlyAnAnswerSoonAfterTheReport(t *testing.T) {
	lookedAt := time.Unix(100, 0)
	dialog := commandDialogPlayerStoppedDialog()
	finding := earlyDialogFinding{dialogs: []statusDialog{dialog}, lookedAt: lookedAt}
	cases := []struct {
		name      string
		after     time.Duration
		wantFresh bool
	}{
		{name: "one second", after: time.Second, wantFresh: true},
		{name: "exactly three seconds", after: 3 * time.Second, wantFresh: true},
		{name: "just over three seconds", after: 3100 * time.Millisecond},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			got := finding.freshDialogs(lookedAt.Add(testCase.after))

			if (got != nil) != testCase.wantFresh {
				t.Fatalf("dialogs = %#v, want fresh = %v", got, testCase.wantFresh)
			}
		})
	}
}

// Verifies a pause-point trigger, whose watch looks once at 2 seconds, names an answer that comes a
// second after that report, and not one that comes 28 seconds after it.
func TestEarlyDialogReportAtThePausePointIntervalNamesOnlyASoonAnswer(t *testing.T) {
	dialog := commandDialogPlayerStoppedDialog()
	script := &earlyDialogReportScript{reports: []statusReport{heldBlockedByDialogReport(commandDialogBlockCommand, dialog)}}

	finding := script.run(pausePointDialogWatchInterval)

	if script.probes != 1 || !reflect.DeepEqual(finding.freshDialogs(script.clock.Add(time.Second)), []statusDialog{dialog}) {
		t.Fatalf("looks = %d, finding = %#v, want one look whose dialog names an answer a second later", script.probes, finding)
	}
	if got := finding.freshDialogs(script.clock.Add(28 * time.Second)); got != nil {
		t.Fatalf("dialogs = %#v, want nothing 28 seconds after the report", got)
	}
}

// Verifies freshness counts from the start of the look that saw the dialog, not from the report: an
// answer up to 3 seconds after that start names the dialog, and a later one does not, however soon it
// follows the report.
func TestEarlyDialogReportCountsFreshnessFromTheLookThatSawTheDialog(t *testing.T) {
	cases := []struct {
		name        string
		lookCost    time.Duration
		afterReport time.Duration
		wantReuse   bool
	}{
		{name: "instant look, answer a second after the report", afterReport: time.Second, wantReuse: true},
		{name: "one-second look, answer a second after the report", lookCost: time.Second, afterReport: time.Second, wantReuse: true},
		{name: "one-second look, answer exactly three seconds after the look", lookCost: time.Second, afterReport: 2 * time.Second, wantReuse: true},
		{name: "four-second look, answer a second after the report", lookCost: 4 * time.Second, afterReport: time.Second},
		{name: "instant look, answer 3.1 seconds after the report", afterReport: 3100 * time.Millisecond},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			dialog := commandDialogPlayerStoppedDialog()
			script := &earlyDialogReportScript{
				reports:   []statusReport{heldBlockedByDialogReport(commandDialogBlockCommand, dialog)},
				probeCost: testCase.lookCost,
			}

			finding := script.run(pausePointDialogWatchInterval)

			got := finding.freshDialogs(script.clock.Add(testCase.afterReport))
			if (got != nil) != testCase.wantReuse {
				t.Fatalf("dialogs = %#v, looked at %v, report done at %v, want reuse = %v",
					got, finding.lookedAt, script.clock, testCase.wantReuse)
			}
		})
	}
}

// Verifies the stop of a watch hands back the reported dialog only while it is fresh, so a send that
// Unity answers long after the report looks again.
func TestStartEarlyDialogReportHandsBackOnlyAFreshDialog(t *testing.T) {
	cases := []struct {
		name      string
		after     time.Duration
		wantFresh bool
	}{
		{name: "answered a second after the report", after: time.Second, wantFresh: true},
		{name: "answered 28 seconds after the report", after: 28 * time.Second},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			// Why by call order: the watch reads the clock once, when Unity took the report, and the
			// stop reads it once more, so no timing between them decides what each read returns.
			var clockReads atomic.Int32
			reported := make(chan struct{})
			deps := earlyDialogReportDeps{
				probeEditorStatus: func(context.Context, unityipc.Connection) (statusReport, error) {
					return heldBlockedByDialogReport(commandDialogBlockCommand, commandDialogPlayerStoppedDialog()), nil
				},
				reportBlockingDialog: func(context.Context, unityipc.Connection, string) error {
					defer close(reported)
					return nil
				},
				waitFor: releasedOnce(),
				now: func() time.Time {
					if clockReads.Add(1) == 1 {
						return time.Unix(0, 0)
					}
					return time.Unix(0, 0).Add(testCase.after)
				},
			}
			stop := startEarlyDialogReport(context.Background(), unityipc.Connection{}, commandDialogBlockCommand, 0, deps)
			select {
			case <-reported:
			case <-time.After(5 * time.Second):
				t.Fatal("the watch never reported")
			}

			if got := stop(); (got != nil) != testCase.wantFresh {
				t.Fatalf("dialogs = %#v, want fresh = %v", got, testCase.wantFresh)
			}
		})
	}
}

// Verifies a dialog an earlier look reported is dropped once a later look no longer finds it, so a
// stale dialog never names the answer.
func TestEarlyDialogReportForgetsADialogTheLastLookNoLongerFinds(t *testing.T) {
	script := &earlyDialogReportScript{reports: []statusReport{
		heldBlockedByDialogReport(commandDialogBlockCommand, commandDialogPlayerStoppedDialog()),
		readyStatusReport(),
	}}

	found := runUntilAnswered(script, 2)

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

			if found := runUntilAnswered(script, 2); found != nil {
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

	if found := runUntilAnswered(script, 2); found != nil {
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
		now:                  time.Now,
	}

	found := runEarlyDialogReport(ctx, unityipc.Connection{}, commandDialogBlockCommand, deps, 10*time.Second).dialogs

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
		now:     time.Now,
	}

	found := runEarlyDialogReport(ctx, unityipc.Connection{}, commandDialogBlockCommand, deps, 10*time.Second).dialogs

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
// reported, then marks second, accepts the second request and answers that it was not run once
// secondLooked closes.
func serveReportedBusyThenNotRun(
	t *testing.T,
	reported <-chan struct{},
	second *atomic.Bool,
	secondLooked <-chan struct{},
) unityipc.Connection {
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
			select {
			case <-secondLooked:
			case <-time.After(5 * time.Second):
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
	secondLooked := make(chan struct{})
	var secondOnce sync.Once
	var firstWaits, secondWaits atomic.Int32
	looked := commandDialogPlayerStoppedDialog()
	probe := &commandDialogProbe{report: blockedByDialogStatusReport(looked)}
	deps := defaultConnectionRetryDeps()
	deps.findRunningUnityProcess = runningUnityProcessLookup
	deps.retryPoll = time.Millisecond
	deps.dialogWatch = probe.deps(true)
	deps.earlyDialogReport = earlyDialogReportDeps{
		probeEditorStatus: func(context.Context, unityipc.Connection) (statusReport, error) {
			if second.Load() {
				secondOnce.Do(func() { close(secondLooked) })
				return readyStatusReport(), nil
			}
			return heldBlockedByDialogReport(commandDialogBlockCommand, otherBlockingDialog()), nil
		},
		reportBlockingDialog: func(context.Context, unityipc.Connection, string) error {
			once.Do(func() { close(reported) })
			return nil
		},
		// Why each attempt's first wait only: each watch looks once and then stays open until its
		// attempt ends, so the first holds its report and the second looks and finds none.
		waitFor: func(wait time.Duration) <-chan time.Time {
			waits := &firstWaits
			if second.Load() {
				waits = &secondWaits
			}
			if waits.Add(1) == 1 {
				return releasedAtOnce(wait)
			}
			return make(chan time.Time)
		},
		now: time.Now,
	}
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()

	_, err := sendWithTransientConnectionRetryWithDeps(
		ctx, serveReportedBusyThenNotRun(t, reported, &second, secondLooked), commandDialogBlockCommand, map[string]any{}, nil, 0, deps)

	cliError := clierrors.ClassifyError(err, clierrors.ErrorContext{Command: commandDialogBlockCommand})
	assertCommandBlockedByDialog(t, cliError, looked.Title)
	select {
	case <-secondLooked:
	default:
		t.Fatal("the second attempt's watch never looked")
	}
	if probe.calls != 1 {
		t.Fatalf("probe calls = %d, want one fresh look", probe.calls)
	}
}
