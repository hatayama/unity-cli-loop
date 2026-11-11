package projectrunner

import (
	"context"
	"errors"
	"fmt"
	"strings"
	"testing"
	"time"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

const startedCommandStoppedFragment = "stopped waiting; the command had already started and continues in Unity once the dialog is closed."

// fakeDialogLook stands in for the status probe of a dialog watch. Its clock moves one second on every
// reading, so a wait that polls in a loop reaches the stall threshold after a few polls without sleeping.
type fakeDialogLook struct {
	dialogs []statusDialog
	err     error
	calls   int
	clock   time.Time
	step    time.Duration
}

func newFakeDialogLook(dialogs ...statusDialog) *fakeDialogLook {
	return &fakeDialogLook{dialogs: dialogs, clock: time.Unix(1_700_000_000, 0), step: time.Second}
}

func (look *fakeDialogLook) deps() dialogWatchDeps {
	return dialogWatchDeps{
		probeEditorStatus: func(context.Context, unityipc.Connection) (statusReport, error) {
			look.calls++
			if look.err != nil {
				return statusReport{}, look.err
			}
			if len(look.dialogs) == 0 {
				return newStatusReport(statusStateMainThreadBlocked, "blocked"), nil
			}
			return blockedByDialogStatusReport(look.dialogs...), nil
		},
		canPressDialogs: true,
		now: func() time.Time {
			look.clock = look.clock.Add(look.step)
			return look.clock
		},
	}
}

func classifyStartedDialogError(t *testing.T, err error, command string) clierrors.CLIError {
	t.Helper()
	var blocked blockedByDialogError
	if !errors.As(err, &blocked) {
		t.Fatalf("error = %#v, want blockedByDialogError", err)
	}
	return clierrors.ClassifyError(err, clierrors.ErrorContext{ProjectRoot: "<PROJECT_ROOT>", Command: command})
}

// assertStartedBlockedByDialog checks the shape every wait that stops after the command started must
// print: the dialog, the started sentence, the press guidance first, and the wait's own last step.
func assertStartedBlockedByDialog(t *testing.T, cliError clierrors.CLIError, wantLastStep string, wantSafeToRetry bool) {
	t.Helper()
	dialog := commandDialogPlayerStoppedDialog()
	if cliError.ErrorCode != clierrors.ErrorCodeBlockedByDialog {
		t.Fatalf("ErrorCode = %q, want BLOCKED_BY_DIALOG", cliError.ErrorCode)
	}
	if !strings.HasPrefix(cliError.Message, blockedByDialogMessage([]statusDialog{dialog})) ||
		!strings.HasSuffix(cliError.Message, startedCommandStoppedFragment) {
		t.Fatalf("Message = %q, want the dialog sentence and the started sentence", cliError.Message)
	}
	if !strings.Contains(cliError.NextActions[0], "uloop press-dialog-button") {
		t.Fatalf("first NextAction = %q, want the press guidance", cliError.NextActions[0])
	}
	if last := cliError.NextActions[len(cliError.NextActions)-1]; last != wantLastStep {
		t.Fatalf("last NextAction = %q, want %q", last, wantLastStep)
	}
	if cliError.SafeToRetry != wantSafeToRetry {
		t.Fatalf("SafeToRetry = %v, want %v", cliError.SafeToRetry, wantSafeToRetry)
	}
	if _, ok := cliError.Details["Dialogs"]; !ok {
		t.Fatalf("Details = %#v, want Dialogs", cliError.Details)
	}
}

// --- dialogBlockWatch ---

// Verifies the watch never looks while the main thread has stalled for less than the threshold.
func TestDialogBlockWatchDoesNotLookBelowTheStallThreshold(t *testing.T) {
	look := newFakeDialogLook(commandDialogPlayerStoppedDialog())
	watch := newDialogBlockWatch(unreachableConnection("<PROJECT_ROOT>"), look.deps(), dialogWatchInterval, dialogWatchStallThresholdSeconds)

	for range 20 {
		if dialogs := watch.observeStall(context.Background(), 4.9); dialogs != nil {
			t.Fatalf("dialogs = %#v, want none below the threshold", dialogs)
		}
	}
	if look.calls != 0 {
		t.Fatalf("looked %d times, want 0", look.calls)
	}
}

// Verifies a stall that lasts 30 seconds with no dialog found is looked at no more than once per
// interval, so the look at Unity's windows does not run on every poll.
func TestDialogBlockWatchLooksAtMostOncePerInterval(t *testing.T) {
	look := newFakeDialogLook()
	watch := newDialogBlockWatch(unreachableConnection("<PROJECT_ROOT>"), look.deps(), dialogWatchInterval, dialogWatchStallThresholdSeconds)

	for second := range 31 {
		if dialogs := watch.observeStall(context.Background(), float64(5+second)); dialogs != nil {
			t.Fatalf("dialogs = %#v, want none", dialogs)
		}
	}
	if look.calls != 4 {
		t.Fatalf("looked %d times over 30 seconds, want 4 (at 0, 10, 20, and 30 seconds)", look.calls)
	}
}

// Verifies a failed look or one that finds no dialog leaves the wait going.
func TestDialogBlockWatchKeepsWaitingWithoutADialog(t *testing.T) {
	cases := []struct {
		name string
		look *fakeDialogLook
	}{
		{name: "no dialog", look: newFakeDialogLook()},
		{name: "look failed", look: &fakeDialogLook{err: errors.New("osascript failed"), step: time.Second}},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			watch := newDialogBlockWatch(unreachableConnection("<PROJECT_ROOT>"), testCase.look.deps(), dialogWatchInterval, dialogWatchStallThresholdSeconds)

			if dialogs := watch.observeStall(context.Background(), 12); dialogs != nil {
				t.Fatalf("dialogs = %#v, want none", dialogs)
			}
			if testCase.look.calls != 1 {
				t.Fatalf("looked %d times, want 1", testCase.look.calls)
			}
		})
	}
}

// Verifies the dialogs the Editor reports end the wait at once without a look, and that a stall the
// Editor reports without dialogs is looked at (the form of an Editor older than Unity 6000.7).
func TestDialogBlockWatchReadsTheEditorStatusFirst(t *testing.T) {
	dialog := commandDialogPlayerStoppedDialog()
	look := newFakeDialogLook(dialog)
	watch := newDialogBlockWatch(unreachableConnection("<PROJECT_ROOT>"), look.deps(), dialogWatchInterval, dialogWatchStallThresholdSeconds)

	reported := watch.observeEditorStatus(context.Background(), editorStatusResponse{OpenDialogs: []statusDialog{dialog}}, true)
	if len(reported) != 1 || look.calls != 0 {
		t.Fatalf("dialogs = %#v after %d looks, want the reported dialog without a look", reported, look.calls)
	}
	found := watch.observeEditorStatus(context.Background(), editorStatusResponse{HasEditorState: true, SecondsSinceLastMainThreadTick: 6}, true)
	if len(found) != 1 || look.calls != 1 {
		t.Fatalf("dialogs = %#v after %d looks, want the found dialog after one look", found, look.calls)
	}
}

// Verifies polls that stop being answered count as a stall from the first unanswered one, and an
// answer starts the count again.
func TestDialogBlockWatchCountsUnansweredPollsAsAStall(t *testing.T) {
	look := newFakeDialogLook(commandDialogPlayerStoppedDialog())
	watch := newDialogBlockWatch(unreachableConnection("<PROJECT_ROOT>"), look.deps(), dialogWatchInterval, dialogWatchStallThresholdSeconds)

	for range 5 {
		if dialogs := watch.observePoll(context.Background(), false); dialogs != nil {
			t.Fatalf("dialogs = %#v before 5 unanswered seconds", dialogs)
		}
	}
	watch.observePoll(context.Background(), true)
	for range 5 {
		watch.observePoll(context.Background(), false)
	}
	if look.calls != 0 {
		t.Fatalf("looked %d times, want 0 after an answer restarted the count", look.calls)
	}
	if dialogs := watch.observePoll(context.Background(), false); len(dialogs) != 1 {
		t.Fatalf("dialogs = %#v, want the dialog once polls went unanswered for 5 seconds", dialogs)
	}
}

// Verifies the watch does nothing without a probe, so tests and callers that do not wire one never
// start a look at Unity's windows.
func TestDialogBlockWatchWithoutAProbeDoesNothing(t *testing.T) {
	watch := newDialogBlockWatch(unreachableConnection("<PROJECT_ROOT>"), dialogWatchDeps{now: time.Now}, dialogWatchInterval, dialogWatchStallThresholdSeconds)

	if dialogs := watch.observeStall(context.Background(), 60); dialogs != nil {
		t.Fatalf("dialogs = %#v, want none", dialogs)
	}
	if dialogs := watch.observePoll(context.Background(), false); dialogs != nil {
		t.Fatalf("dialogs = %#v, want none", dialogs)
	}
}

// --- error forms ---

// Verifies a dialog without buttons points at the user in the started form too.
func TestStartedCommandBlockedByDialogAsksTheUserForADialogWithoutButtons(t *testing.T) {
	err := startedCommandBlockedByDialog(clicore.RunTestsCommandName, []statusDialog{{Title: "Build Settings"}}, true, false)

	cliError := classifyStartedDialogError(t, err, clicore.RunTestsCommandName)

	if !strings.HasPrefix(cliError.NextActions[0], "uloop cannot press a button in this dialog") {
		t.Fatalf("first NextAction = %q, want the ask-the-user guidance", cliError.NextActions[0])
	}
}

// Verifies a dialog title with words the transport checks look for does not make any of the new forms
// read as a lost connection.
func TestStartedDialogErrorsDoNotReadAsADisconnect(t *testing.T) {
	dialogs := []statusDialog{{Title: "Unexpected EOF while reading the scene", Buttons: []string{"OK"}}}
	look := &fakeDialogLook{dialogs: dialogs, step: time.Second}
	unresponsive := explainBlockedByDialog(context.Background(), unreachableConnection("<PROJECT_ROOT>"), "get-logs",
		&unityipc.EditorUnresponsiveError{StallSeconds: 300}, look.deps())
	heartbeat := (&heartbeatDialogWatch{method: "get-logs", dialogs: dialogs}).blockedError()
	cases := map[string]error{
		"started":      startedCommandBlockedByDialog(clicore.CompileCommandName, dialogs, true, true),
		"heartbeat":    heartbeat,
		"unresponsive": unresponsive,
	}
	for name, err := range cases {
		t.Run(name, func(t *testing.T) {
			if clierrors.IsTransportDisconnectError(err) {
				t.Fatalf("the dialog error reads as a disconnect: %q", err.Error())
			}
			cliError := clierrors.ClassifyError(err, clierrors.ErrorContext{Command: "get-logs"})
			if cliError.ErrorCode != clierrors.ErrorCodeBlockedByDialog || !strings.Contains(cliError.Message, dialogs[0].Title) {
				t.Fatalf("ErrorCode = %q, Message = %q, want BLOCKED_BY_DIALOG naming the dialog", cliError.ErrorCode, cliError.Message)
			}
		})
	}
}

// --- the five-minute exit ---

// Verifies the five-minute UNITY_EDITOR_UNRESPONSIVE is replaced by a dialog error that does not say
// whether the command started, and is kept unchanged when no dialog is found.
func TestExplainBlockedByDialogExplainsTheFiveMinuteStall(t *testing.T) {
	original := &unityipc.EditorUnresponsiveError{StallSeconds: 300}

	withDialog := explainBlockedByDialog(context.Background(), unreachableConnection("<PROJECT_ROOT>"), "get-logs", original,
		newFakeDialogLook(commandDialogPlayerStoppedDialog()).deps())
	cliError := clierrors.ClassifyError(withDialog, clierrors.ErrorContext{ProjectRoot: "<PROJECT_ROOT>", Command: "get-logs"})
	wantMessage := blockedByDialogMessage([]statusDialog{commandDialogPlayerStoppedDialog()}) + " uloop get-logs stopped waiting."
	originalError := clierrors.ClassifyError(original, clierrors.ErrorContext{ProjectRoot: "<PROJECT_ROOT>", Command: "get-logs"})
	if cliError.ErrorCode != clierrors.ErrorCodeBlockedByDialog || cliError.Message != wantMessage ||
		cliError.SafeToRetry != originalError.SafeToRetry {
		t.Fatalf("got %q / %q / SafeToRetry %v, want BLOCKED_BY_DIALOG / %q / %v",
			cliError.ErrorCode, cliError.Message, cliError.SafeToRetry, wantMessage, originalError.SafeToRetry)
	}
	for key, value := range originalError.Details {
		if fmt.Sprint(cliError.Details[key]) != fmt.Sprint(value) {
			t.Fatalf("Details[%q] = %v, want the original %v", key, cliError.Details[key], value)
		}
	}

	withoutDialog := explainBlockedByDialog(context.Background(), unreachableConnection("<PROJECT_ROOT>"), "get-logs", original, newFakeDialogLook().deps())
	if withoutDialog != error(original) {
		t.Fatalf("error = %#v, want the original error", withoutDialog)
	}
	assertSameErrorJSON(t, withoutDialog, original)
}

// --- execute-dynamic-code's readiness wait ---

// Verifies a failed readiness wait after execute-dynamic-code is explained by a dialog when one is open,
// without saying the snippet is still running (it may have returned), kept when none is, and that a
// successful wait never looks.
func TestExplainReadinessFailureByDialog(t *testing.T) {
	failure := errors.New("timed out waiting for Unity tool readiness")

	found := explainReadinessFailureByDialog(context.Background(), unreachableConnection("<PROJECT_ROOT>"), failure,
		newFakeDialogLook(commandDialogPlayerStoppedDialog()).deps())
	cliError := classifyStartedDialogError(t, found, clicore.ExecuteDynamicCodeCommandName)
	wantMessage := blockedByDialogMessage([]statusDialog{commandDialogPlayerStoppedDialog()}) + " uloop execute-dynamic-code stopped waiting."
	if cliError.Message != wantMessage || cliError.SafeToRetry ||
		cliError.NextActions[len(cliError.NextActions)-1] != startedCommandDialogClosedNextAction {
		t.Fatalf("Message = %q, SafeToRetry = %v, NextActions = %q, want the neutral form that is not safe to retry",
			cliError.Message, cliError.SafeToRetry, cliError.NextActions)
	}

	if kept := explainReadinessFailureByDialog(context.Background(), unreachableConnection("<PROJECT_ROOT>"), failure, newFakeDialogLook().deps()); kept != failure {
		t.Fatalf("error = %#v, want the original failure", kept)
	}

	look := newFakeDialogLook(commandDialogPlayerStoppedDialog())
	if err := explainReadinessFailureByDialog(context.Background(), unreachableConnection("<PROJECT_ROOT>"), nil, look.deps()); err != nil || look.calls != 0 {
		t.Fatalf("err = %v after %d looks, want nil without a look", err, look.calls)
	}
}
