package clicore

import (
	"context"
	"encoding/json"
	"errors"
	"net"
	"os"
	"reflect"
	"syscall"
	"testing"
	"time"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
	"github.com/hatayama/unity-cli-loop/common/unityprocess"
	"github.com/hatayama/unity-cli-loop/common/unityprocess/unitydialog"
)

// readinessDialogWait drives one readiness wait on a fake clock: every probe moves the clock one
// second, so the probe at index n runs n seconds after the wait started, and what follows it in
// the same round reads n+1 seconds, whatever the real time.
type readinessDialogWait struct {
	clock     time.Time
	startedAt time.Time
	// probe answers the probe that runs elapsed after the start; nil from it means ready.
	probe func(elapsed time.Duration) error
	// dialogs and findErr are what the dialog look returns.
	dialogs []unitydialog.Dialog
	findErr error
	// lookElapsed records when, after the start, each look ran.
	lookElapsed []time.Duration
	// lookTakes is how far each look moves the clock, as a look that waits for osascript does.
	lookTakes time.Duration
	// schedule is when the wait looks; newReadinessDialogWait starts it at the default.
	schedule DialogLookSchedule
	// unityRunning is what the process lookup reports.
	unityRunning bool
}

func newReadinessDialogWait(probe func(elapsed time.Duration) error, dialogs ...unitydialog.Dialog) *readinessDialogWait {
	start := time.Unix(1_700_000_000, 0)
	return &readinessDialogWait{
		clock:        start,
		startedAt:    start,
		probe:        probe,
		dialogs:      dialogs,
		schedule:     DefaultDialogLookSchedule(),
		unityRunning: true,
	}
}

func (wait *readinessDialogWait) deps() toolReadinessDeps {
	return toolReadinessDeps{
		probeToolReadinessSequence: func(context.Context, string) error {
			elapsed := wait.clock.Sub(wait.startedAt)
			err := wait.probe(elapsed)
			wait.clock = wait.clock.Add(time.Second)
			return err
		},
		findRunningUnityProcess: func(context.Context, string) (*unityprocess.UnityProcess, error) {
			if !wait.unityRunning {
				return nil, nil
			}
			return &unityprocess.UnityProcess{Pid: 4242}, nil
		},
		now: func() time.Time { return wait.clock },
		findDialogs: func(context.Context) ([]unitydialog.Dialog, error) {
			wait.lookElapsed = append(wait.lookElapsed, wait.clock.Sub(wait.startedAt))
			wait.clock = wait.clock.Add(wait.lookTakes)
			return wait.dialogs, wait.findErr
		},
		lookSchedule: wait.schedule,
		poll:         time.Millisecond,
	}
}

func readinessTestDialog() unitydialog.Dialog {
	return unitydialog.Dialog{Title: "Package dependency resolver", Message: "Resolve now?", Buttons: []string{"Yes", "No", "Later"}}
}

func serverGoneProbe(time.Duration) error {
	return &unityipc.ConnectionAttemptError{Cause: syscall.ECONNREFUSED}
}

// Verifies a wait that stays not ready looks for a dialog only once five seconds have passed since it
// started, and stops with the dialogs at the first look that finds one instead of running to its
// timeout, whatever kind of failure the probes report (#1, #1b).
func TestWaitForToolReadinessStopsAtADialogAfterFiveSeconds(t *testing.T) {
	cases := map[string]func(time.Duration) error{
		"server gone":   serverGoneProbe,
		"probe timeout": func(time.Duration) error { return context.DeadlineExceeded },
		"rpc error": func(time.Duration) error {
			return &unityipc.RPCError{Code: -32603, Message: "not ready yet"}
		},
	}
	for name, probe := range cases {
		t.Run(name, func(t *testing.T) {
			wait := newReadinessDialogWait(probe, readinessTestDialog())

			err := waitForToolReadinessWithDeps(context.Background(), t.TempDir(), readinessDialogTestTimeout, wait.deps())

			var blocked ToolReadinessBlockedByDialogError
			if !errors.As(err, &blocked) {
				t.Fatalf("err = %v, want ToolReadinessBlockedByDialogError", err)
			}
			if !reflect.DeepEqual(blocked.Dialogs, []unitydialog.Dialog{readinessTestDialog()}) {
				t.Fatalf("Dialogs = %#v, want the one dialog found", blocked.Dialogs)
			}
			if len(wait.lookElapsed) != 1 || wait.lookElapsed[0] < DefaultDialogLookSchedule().FirstLookDelay {
				t.Fatalf("looks ran at %v, want one look at or after %s", wait.lookElapsed, DefaultDialogLookSchedule().FirstLookDelay)
			}
			if wait.lookElapsed[0] > DefaultDialogLookSchedule().FirstLookDelay+time.Second {
				t.Fatalf("first look ran at %s, want it soon after %s", wait.lookElapsed[0], DefaultDialogLookSchedule().FirstLookDelay)
			}
		})
	}
}

// Verifies a look that finds no dialog, or fails, leaves the wait as it was: it runs to its timeout
// and ends with the server-not-responding error it gives without a look, with nothing about the
// look added (#2, #3).
func TestWaitForToolReadinessKeepsWaitingWhenTheLookFindsNothing(t *testing.T) {
	cases := map[string]error{"no dialog": nil, "look failed": errors.New("osascript could not read Unity's windows")}
	for name, findErr := range cases {
		t.Run(name, func(t *testing.T) {
			wait := newReadinessDialogWait(serverGoneProbe)
			wait.findErr = findErr

			err := waitForToolReadinessWithDeps(context.Background(), t.TempDir(), 200*time.Millisecond, wait.deps())

			var notResponding clierrors.UnityServerNotRespondingError
			if !errors.As(err, &notResponding) {
				t.Fatalf("err = %v, want UnityServerNotRespondingError", err)
			}
			if len(wait.lookElapsed) == 0 {
				t.Fatal("the wait never looked for a dialog")
			}
			if findErr != nil && errors.Is(err, findErr) {
				t.Fatalf("err = %v, want nothing of the failed look in it", err)
			}
		})
	}
}

// Verifies a wait that becomes ready after five seconds succeeds when the look found nothing (#5).
func TestWaitForToolReadinessSucceedsAfterALookThatFoundNothing(t *testing.T) {
	wait := newReadinessDialogWait(func(elapsed time.Duration) error {
		if elapsed < 7*time.Second {
			return serverGoneProbe(elapsed)
		}
		return nil
	})

	if err := waitForToolReadinessWithDeps(context.Background(), t.TempDir(), readinessDialogTestTimeout, wait.deps()); err != nil {
		t.Fatalf("err = %v, want readiness", err)
	}
	if len(wait.lookElapsed) != 1 {
		t.Fatalf("looks ran at %v, want one", wait.lookElapsed)
	}
}

// Verifies the wait looks at most once every ten seconds while nothing is found: over thirty-five
// seconds without readiness the looks run at five, fifteen, twenty-five, and thirty-five seconds at
// most (#6).
func TestWaitForToolReadinessLooksAtMostEveryTenSeconds(t *testing.T) {
	wait := newReadinessDialogWait(func(elapsed time.Duration) error {
		if elapsed <= 35*time.Second {
			return serverGoneProbe(elapsed)
		}
		return nil
	})

	if err := waitForToolReadinessWithDeps(context.Background(), t.TempDir(), readinessDialogTestTimeout, wait.deps()); err != nil {
		t.Fatalf("err = %v, want readiness", err)
	}
	if len(wait.lookElapsed) < 3 || len(wait.lookElapsed) > 4 {
		t.Fatalf("looks ran at %v, want three or four", wait.lookElapsed)
	}
	for index := 1; index < len(wait.lookElapsed); index++ {
		if gap := wait.lookElapsed[index] - wait.lookElapsed[index-1]; gap < DefaultDialogLookSchedule().LookGap {
			t.Fatalf("looks ran at %v; %d and %d are %s apart, want at least %s",
				wait.lookElapsed, index-1, index, gap, DefaultDialogLookSchedule().LookGap)
		}
	}
}

// Verifies a wait given its own schedule looks first once that schedule's delay has passed, not
// after the default five seconds.
func TestWaitForToolReadinessLooksFirstAfterTheGivenDelay(t *testing.T) {
	wait := newReadinessDialogWait(serverGoneProbe, readinessTestDialog())
	wait.schedule = DialogLookSchedule{FirstLookDelay: time.Second, LookGap: 2 * time.Second}

	err := waitForToolReadinessWithDeps(context.Background(), t.TempDir(), readinessDialogTestTimeout, wait.deps())

	var blocked ToolReadinessBlockedByDialogError
	if !errors.As(err, &blocked) {
		t.Fatalf("err = %v, want ToolReadinessBlockedByDialogError", err)
	}
	if len(wait.lookElapsed) != 1 || wait.lookElapsed[0] < time.Second || wait.lookElapsed[0] > 2*time.Second {
		t.Fatalf("looks ran at %v, want one look between one and two seconds", wait.lookElapsed)
	}
}

// Verifies the gap between two looks is counted from the end of the earlier look: with looks that
// take a second and a half and a two-second gap, every look starts at least two seconds after the
// one before it ended, so slow looks cannot run back to back and crowd out the probes.
func TestWaitForToolReadinessCountsTheLookGapFromTheEndOfTheLook(t *testing.T) {
	const lookTakes = 1500 * time.Millisecond
	const gap = 2 * time.Second
	wait := newReadinessDialogWait(func(elapsed time.Duration) error {
		if elapsed <= 20*time.Second {
			return serverGoneProbe(elapsed)
		}
		return nil
	})
	wait.lookTakes = lookTakes
	wait.schedule = DialogLookSchedule{FirstLookDelay: time.Second, LookGap: gap}

	if err := waitForToolReadinessWithDeps(context.Background(), t.TempDir(), readinessDialogTestTimeout, wait.deps()); err != nil {
		t.Fatalf("err = %v, want readiness", err)
	}
	if len(wait.lookElapsed) < 3 {
		t.Fatalf("looks ran at %v, want at least three", wait.lookElapsed)
	}
	for index := 1; index < len(wait.lookElapsed); index++ {
		if sinceEnd := wait.lookElapsed[index] - (wait.lookElapsed[index-1] + lookTakes); sinceEnd < gap {
			t.Fatalf("looks ran at %v; look %d started %s after look %d ended, want at least %s",
				wait.lookElapsed, index, sinceEnd, index-1, gap)
		}
	}
}

// Verifies the answers that already end the wait still end it when a look would find a dialog in the
// same round: a CLI that must be updated, a connect refused for good, and an Editor that exited (#7).
func TestWaitForToolReadinessEndsWithItsOwnAnswersBeforeTheLook(t *testing.T) {
	updateRequired := &unityipc.RPCError{Code: -32603, Message: "update", Data: json.RawMessage(`{"type":"cli_update_required"}`)}
	refused := &unityipc.ConnectionAttemptError{
		Endpoint: "<ENDPOINT>",
		Cause:    &net.OpError{Op: "dial", Net: "unix", Err: os.NewSyscallError("connect", syscall.EPERM)},
	}
	cases := map[string]struct {
		probe        func(time.Duration) error
		unityRunning bool
		want         func(error) bool
	}{
		"cli update required": {
			probe:        failingFrom(DefaultDialogLookSchedule().FirstLookDelay-time.Second, updateRequired),
			unityRunning: true,
			want:         func(err error) bool { return errors.Is(err, updateRequired) },
		},
		"connect refused for good": {
			probe:        failingFrom(DefaultDialogLookSchedule().FirstLookDelay-time.Second, refused),
			unityRunning: true,
			want:         func(err error) bool { return err == error(refused) },
		},
		"unity exited": {
			// The exit watch looks up first in the round that reads three seconds and confirms two seconds
			// later, in the round the first dialog look is due.
			probe:        failingFrom(DefaultDialogLookSchedule().FirstLookDelay-3*time.Second, &unityipc.ConnectionAttemptError{Cause: syscall.ECONNREFUSED}),
			unityRunning: false,
			want: func(err error) bool {
				var notRunning clierrors.UnityNotRunningError
				return errors.As(err, &notRunning)
			},
		},
	}
	for name, testCase := range cases {
		t.Run(name, func(t *testing.T) {
			wait := newReadinessDialogWait(testCase.probe, readinessTestDialog())
			wait.unityRunning = testCase.unityRunning

			err := waitForToolReadinessWithDeps(context.Background(), t.TempDir(), readinessDialogTestTimeout, wait.deps())

			if !testCase.want(err) {
				t.Fatalf("err = %v, want the wait's own answer", err)
			}
		})
	}
}

// failingFrom fails each probe with a plain not-ready error before from, and with err from then on.
// Why the plain error is not a lost server: the exit watch must not start counting before from.
func failingFrom(from time.Duration, err error) func(time.Duration) error {
	return func(elapsed time.Duration) error {
		if elapsed < from {
			return errors.New("not ready yet")
		}
		return err
	}
}

// Verifies the error that reports the dialogs keeps their text out of Error(), so a title such as
// "EOF" cannot make the error read as a lost connection (#13).
func TestToolReadinessBlockedByDialogErrorLeavesTheDialogOutOfItsText(t *testing.T) {
	err := ToolReadinessBlockedByDialogError{
		Dialogs: []unitydialog.Dialog{{Title: "Unexpected EOF", Message: "connection reset", Buttons: []string{"OK"}}},
		Cause:   errors.New("EOF"),
	}

	if text := err.Error(); text != "a modal dialog is open in Unity; the wait for the Editor to be ready stopped" {
		t.Fatalf("Error() = %q, want the fixed sentence without the dialog", text)
	}
	if clierrors.IsTransportDisconnectError(err) {
		t.Fatal("the error reads as a lost connection")
	}
}

// readinessDialogTestTimeout bounds a wait that should end at a look or by readiness. The fake clock
// passes the look's five seconds within a few real milliseconds, so a wait that reaches this timeout
// missed what it should have stopped at.
const readinessDialogTestTimeout = 3 * time.Second
