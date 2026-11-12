package projectrunner

import (
	"context"
	"errors"
	"strings"
	"testing"
	"time"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
	"github.com/hatayama/unity-cli-loop/common/unityprocess/unitydialog"
)

// steppingClock is the dialog watch's clock: every reading moves it forward by step, so a wait
// covers seconds of watch time in a few fast loop turns.
type steppingClock struct {
	current time.Time
	step    time.Duration
	started bool
	first   time.Time
}

func newSteppingClock(step time.Duration) *steppingClock {
	return &steppingClock{current: time.Date(2026, 1, 1, 0, 0, 0, 0, time.UTC), step: step}
}

func (clock *steppingClock) now() time.Time {
	clock.current = clock.current.Add(clock.step)
	if !clock.started {
		clock.started = true
		clock.first = clock.current
	}
	return clock.current
}

// serverWaitProbe stands in for the status probe of the dialog watch and records the watch time of
// each look.
type serverWaitProbe struct {
	clock    *steppingClock
	report   statusReport
	err      error
	lookedAt []time.Time
	onLook   func()
}

func (probe *serverWaitProbe) probe(context.Context, unityipc.Connection) (statusReport, error) {
	probe.lookedAt = append(probe.lookedAt, probe.clock.current)
	if probe.onLook != nil {
		probe.onLook()
	}
	return probe.report, probe.err
}

// serverWaitDeps waits for a server that does not answer, with Unity running when running is true,
// and the dialog watch reading probe.
func serverWaitDeps(probe *serverWaitProbe, running bool, retryTimeout time.Duration) connectionRetryDeps {
	deps := defaultConnectionRetryDeps()
	deps.findRunningUnityProcess = func(context.Context, string) (*clicore.UnityProcess, error) {
		if !running {
			return nil, nil
		}
		return &clicore.UnityProcess{Pid: 123}, nil
	}
	deps.focusUnityProcess = func(context.Context, int) (clicore.RestoreFocusFunc, error) {
		return func(context.Context) error { return nil }, nil
	}
	deps.retryTimeout = retryTimeout
	deps.retryPoll = time.Millisecond
	deps.dialogWatch = dialogWatchDeps{probeEditorStatus: probe.probe, canPressDialogs: true, now: probe.clock.now}
	return deps
}

func sendToUnansweredServer(t *testing.T, connection unityipc.Connection, deps connectionRetryDeps) (unityipc.UnitySendOutcome, error) {
	t.Helper()
	return sendWithTransientConnectionRetryWithDeps(context.Background(), connection, commandDialogBlockCommand,
		map[string]any{}, nil, 0, deps)
}

func classifyServerWaitError(err error) clierrors.CLIError {
	return clierrors.ClassifyError(err, clierrors.ErrorContext{ProjectRoot: "<PROJECT_ROOT>", Command: commandDialogBlockCommand})
}

// baselineServerWaitError is how the wait ends today: the same wait with the dialog watch off.
func baselineServerWaitError(t *testing.T, connection unityipc.Connection, running bool, retryTimeout time.Duration) clierrors.CLIError {
	t.Helper()
	deps := serverWaitDeps(&serverWaitProbe{clock: newSteppingClock(time.Second)}, running, retryTimeout)
	deps.dialogWatch.probeEditorStatus = nil
	_, err := sendToUnansweredServer(t, connection, deps)
	return classifyServerWaitError(err)
}

// Verifies a dialog Unity shows before its server starts ends the server wait once the server has
// been unreachable for five seconds: uloop does not look before then, and reports the dialog with
// the command not run instead of waiting out the minute-long window.
func TestSendWithTransientConnectionRetryReportsADialogWhileTheServerIsDown(t *testing.T) {
	clock := newSteppingClock(time.Second)
	probe := &serverWaitProbe{clock: clock, report: blockedByDialogStatusReport(commandDialogPlayerStoppedDialog())}
	startedAt := time.Now()

	_, err := sendToUnansweredServer(t, unreachableConnection(t.TempDir()), serverWaitDeps(probe, true, 10*time.Second))

	if elapsed := time.Since(startedAt); elapsed > 10*time.Second {
		t.Fatalf("the wait took %s; it must end at the dialog, not at the retry window", elapsed)
	}
	if len(probe.lookedAt) != 1 || probe.lookedAt[0].Sub(clock.first) < 5*time.Second {
		t.Fatalf("looks = %v from %v, want one look at least five seconds in", probe.lookedAt, clock.first)
	}
	cliError := classifyServerWaitError(err)
	if cliError.ErrorCode != clierrors.ErrorCodeBlockedByDialog || cliError.Phase != clierrors.ErrorPhaseConnection {
		t.Fatalf("ErrorCode = %q, Phase = %q, want BLOCKED_BY_DIALOG in connection: %v", cliError.ErrorCode, cliError.Phase, err)
	}
	if !strings.HasPrefix(cliError.Message, "'"+commandDialogBlockCommand+"' was not run: ") || !cliError.SafeToRetry {
		t.Fatalf("Message = %q, SafeToRetry = %v, want the not-run form, safe to retry", cliError.Message, cliError.SafeToRetry)
	}
}

// Verifies a look that finds no dialog, and a look that fails, leave the server wait as it is: it
// goes on to the end of its window and ends with the same error as without the watch, with no hint
// about the failed look.
func TestSendWithTransientConnectionRetryKeepsWaitingWhenNoDialogIsFound(t *testing.T) {
	cases := []struct {
		name   string
		report statusReport
		err    error
	}{
		{name: "no dialog", report: newStatusReport(statusStateServerUnavailable, "down")},
		{name: "look failed", err: errors.New("osascript could not read Unity's windows")},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			connection := unreachableConnection(t.TempDir())
			want := baselineServerWaitError(t, connection, true, 50*time.Millisecond)
			probe := &serverWaitProbe{clock: newSteppingClock(time.Second), report: testCase.report, err: testCase.err}

			_, err := sendToUnansweredServer(t, connection, serverWaitDeps(probe, true, 50*time.Millisecond))

			got := classifyServerWaitError(err)
			if len(probe.lookedAt) == 0 {
				t.Fatal("the watch never looked, so the case does not show that a look keeps the wait going")
			}
			if got.ErrorCode != want.ErrorCode || !equalStrings(got.NextActions, want.NextActions) {
				t.Fatalf("got %q %v, want the error without the watch: %q %v", got.ErrorCode, got.NextActions, want.ErrorCode, want.NextActions)
			}
		})
	}
}

// Verifies no look happens when no Unity process runs: the wait ends as it does without the watch.
func TestSendWithTransientConnectionRetryDoesNotLookWithoutAUnityProcess(t *testing.T) {
	connection := unreachableConnection(t.TempDir())
	want := baselineServerWaitError(t, connection, false, 50*time.Millisecond)
	probe := &serverWaitProbe{clock: newSteppingClock(time.Second), report: blockedByDialogStatusReport(commandDialogPlayerStoppedDialog())}

	_, err := sendToUnansweredServer(t, connection, serverWaitDeps(probe, false, 50*time.Millisecond))

	if got := classifyServerWaitError(err); len(probe.lookedAt) != 0 || got.ErrorCode != want.ErrorCode {
		t.Fatalf("looks = %d, ErrorCode = %q, want 0 looks and %q", len(probe.lookedAt), got.ErrorCode, want.ErrorCode)
	}
}

// Verifies a server that comes up after a look found no dialog still gets the command, and its answer
// is returned.
func TestSendWithTransientConnectionRetrySendsOnceTheServerComesUpAfterALook(t *testing.T) {
	listener := newLoopbackIpcListener(t)
	network, address := listener.Addr().Network(), listener.Addr().String()
	_ = listener.Close()
	connection := unityipc.Connection{Endpoint: unityipc.Endpoint{Network: network, Address: address}, ProjectRoot: t.TempDir()}
	requests := make(chan map[string]any, 1)
	serverErr := make(chan error, 1)
	probe := &serverWaitProbe{clock: newSteppingClock(time.Second), report: newStatusReport(statusStateServerUnavailable, "down")}
	probe.onLook = func() {
		if len(probe.lookedAt) == 1 {
			go serveRawIPCResponse(relistenLoopbackIpc(t, network, address), commandDialogBlockCommand, requests, serverErr,
				`{"jsonrpc":"2.0","result":{"Success":true},"id":1}`)
		}
	}

	outcome, err := sendToUnansweredServer(t, connection, serverWaitDeps(probe, true, 10*time.Second))
	if err != nil {
		t.Fatalf("send failed: %v", err)
	}
	if !strings.Contains(string(outcome.Result), `"Success":true`) {
		t.Fatalf("result = %s, want the server's answer", outcome.Result)
	}
	select {
	case failure := <-serverErr:
		t.Fatalf("fake Unity server failed: %v", failure)
	default:
	}
}

// Verifies the watch looks at most once per ten seconds of a long server wait.
func TestSendWithTransientConnectionRetryLooksAtMostOncePerTenSeconds(t *testing.T) {
	clock := newSteppingClock(500 * time.Millisecond)
	probe := &serverWaitProbe{clock: clock, report: newStatusReport(statusStateServerUnavailable, "down")}

	_, _ = sendToUnansweredServer(t, unreachableConnection(t.TempDir()), serverWaitDeps(probe, true, 50*time.Millisecond))

	if len(probe.lookedAt) < 2 {
		t.Fatalf("looks = %d over %s of watch time, want at least two to compare", len(probe.lookedAt), clock.current.Sub(clock.first))
	}
	for index := 1; index < len(probe.lookedAt); index++ {
		if gap := probe.lookedAt[index].Sub(probe.lookedAt[index-1]); gap < dialogWatchInterval {
			t.Fatalf("looks %d and %d are %s apart, want at least %s", index-1, index, gap, dialogWatchInterval)
		}
	}
}

// Verifies the server wait's look does not look at Unity's windows on Linux, where uloop cannot: the
// status probe it uses reports no dialog without starting a process scan, which comes before any
// look at the windows.
func TestServerWaitLookDoesNotInspectWindowsOnLinux(t *testing.T) {
	lookups := 0
	inspector := unitydialog.NewInspector("linux", func(context.Context, string) (*clicore.UnityProcess, error) {
		lookups++
		return &clicore.UnityProcess{Pid: 123}, nil
	})
	statusDeps := statusCommandDeps{
		probeTimeout:          time.Second,
		isUnityProcessRunning: func(context.Context, string) (bool, error) { return true, nil },
		inspectDialogs:        inspector.Inspect,
	}
	clock := newSteppingClock(time.Second)
	deps := serverWaitDeps(&serverWaitProbe{clock: clock}, true, 50*time.Millisecond)
	deps.dialogWatch.probeEditorStatus = func(ctx context.Context, connection unityipc.Connection) (statusReport, error) {
		return probeEditorStatus(ctx, connection, statusDeps)
	}

	_, err := sendToUnansweredServer(t, unreachableConnection(t.TempDir()), deps)

	if code := classifyServerWaitError(err).ErrorCode; code == clierrors.ErrorCodeBlockedByDialog {
		t.Fatalf("ErrorCode = %q, want the wait to end as without a dialog", code)
	}
	if lookups != 0 {
		t.Fatalf("process lookups = %d, want 0", lookups)
	}
}

func equalStrings(left []string, right []string) bool {
	if len(left) != len(right) {
		return false
	}
	for index := range left {
		if left[index] != right[index] {
			return false
		}
	}
	return true
}
