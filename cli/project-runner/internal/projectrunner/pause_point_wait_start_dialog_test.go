package projectrunner

import (
	"context"
	"errors"
	"io"
	"strings"
	"testing"
	"time"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

// triggerDialogReEnableStep is the last step when Unity answered that the marker was not armed.
const triggerDialogReEnableStep = "After the dialog is closed, run uloop pause-point-status --id trigger_dialog: the marker " +
	"was not armed when this wait started, so re-enable it with uloop enable-pause-point and wait again with the same --trigger."

// waitStartDialogFakes answers the arm query with armAnswer or armErr (both nil means no answer) and
// every poll with no answer, and looks find the dialog from look number foundFrom on. It returns the query and look counters.
type waitStartDialogFakes struct {
	queries int
	looks   int
}

func useWaitStartDialogFakes(t *testing.T, armAnswer *pausePointStatusResponse, armErr error, foundFrom int) (*waitStartDialogFakes, dialogWatchDeps) {
	t.Helper()
	useUnityProcessLookup(t, runningUnityProcessLookup)
	originalQuery := queryPausePointStatus
	originalPoll := pausePointStatusPoll
	originalResume := resumePlayModeForPausePoint
	t.Cleanup(func() {
		queryPausePointStatus = originalQuery
		pausePointStatusPoll = originalPoll
		resumePlayModeForPausePoint = originalResume
	})
	pausePointStatusPoll = time.Millisecond
	fakes := &waitStartDialogFakes{}
	queryPausePointStatus = func(context.Context, unityipc.Connection, string) (pausePointStatusResponse, error) {
		fakes.queries++
		if armAnswer != nil && fakes.queries == 1 {
			return *armAnswer, nil
		}
		if armErr != nil && fakes.queries == 1 {
			return pausePointStatusResponse{}, armErr
		}
		return pausePointStatusResponse{}, errors.New("status query timed out")
	}
	resumePlayModeForPausePoint = func(context.Context, unityipc.Connection) pausePointResumePlayResult {
		t.Error("the wait must not resume PlayMode for a marker it could not confirm armed")
		return pausePointResumePlayResult{}
	}
	clock := time.Unix(1_700_000_000, 0)
	deps := dialogWatchDeps{
		probeEditorStatus: func(context.Context, unityipc.Connection) (statusReport, error) {
			fakes.looks++
			if fakes.looks < foundFrom {
				return newStatusReport(statusStateMainThreadBlocked, "blocked"), nil
			}
			return blockedByDialogStatusReport(commandDialogPlayerStoppedDialog()), nil
		},
		canPressDialogs: true,
		now: func() time.Time {
			clock = clock.Add(time.Second)
			return clock
		},
	}
	return fakes, deps
}

func waitStartDialogOptions(deps dialogWatchDeps, trigger bool, resumePlay bool) waitForPausePointOptions {
	options := waitForPausePointOptions{
		id:          triggerDialogPausePointID,
		timeout:     5 * time.Second,
		dialogWatch: deps,
		resumePlay:  resumePlay,
	}
	if trigger {
		options.triggerCommand = "execute-dynamic-code"
		options.triggerArgs = []string{"--code", "return 1;"}
	}
	return options
}

// assertTriggerNotSentBlockedByDialog checks the error of a wait whose trigger was never sent.
func assertTriggerNotSentBlockedByDialog(t *testing.T, err error, wantLastStep string) clierrors.CLIError {
	t.Helper()
	cliError := classifyStartedDialogError(t, err, clicore.PausePointAwaitCommandName)
	if !strings.HasSuffix(cliError.Message, "uloop await-pause-point stopped waiting; its --trigger command was not sent.") {
		t.Fatalf("Message = %q, want the trigger-not-sent sentence", cliError.Message)
	}
	if last := cliError.NextActions[len(cliError.NextActions)-1]; last != wantLastStep {
		t.Fatalf("last NextAction = %q, want %q", last, wantLastStep)
	}
	if !cliError.SafeToRetry || cliError.Details[commandNotRunDetailKey] != true {
		t.Fatalf("SafeToRetry = %v, Details = %#v, want safe to retry with CommandNotRun", cliError.SafeToRetry, cliError.Details)
	}
	result, ok := cliError.Details[pausePointTriggerResultDetailKey].(*pausePointTriggerResult)
	if !ok || !strings.Contains(result.Error, "trigger was not dispatched") {
		t.Fatalf("Details.TriggerResult = %#v, want the reason the trigger was not sent", cliError.Details[pausePointTriggerResultDetailKey])
	}
	return cliError
}

// Verifies a wait whose arm query got no answer, so it sent no trigger, looks for a dialog once before
// polling and, finding one, returns at once that the trigger was not sent and to wait again with it.
func TestWaitForPausePointLooksOnceWhenTheArmQueryGoesUnanswered(t *testing.T) {
	fakes, deps := useWaitStartDialogFakes(t, nil, nil, 1)

	_, _, _, _, _, err := waitForPausePoint(context.Background(), unityipc.Connection{}, waitStartDialogOptions(deps, true, false))

	_ = assertTriggerNotSentBlockedByDialog(t, err, triggerDialogNotRunStep)
	if fakes.queries != 1 || fakes.looks != 1 {
		t.Fatalf("queries = %d, looks = %d, want the arm query and one look before any poll", fakes.queries, fakes.looks)
	}
}

// Verifies the trigger-not-sent error also comes from the wait's own watch when the first look found
// no dialog, and when Unity answered the arm query, with "not armed" or an error (no look then).
func TestPausePointWaitReportsAnUnsentTriggerWhenItsWatchFindsTheDialog(t *testing.T) {
	expired := pausePointStatusResponse{Id: triggerDialogPausePointID, Status: pausePointStatusExpired}
	cases := []struct {
		name      string
		armAnswer *pausePointStatusResponse
		armErr    error
		foundFrom int
		wantLast  string
	}{
		{name: "no dialog at the first look", foundFrom: 2, wantLast: triggerDialogNotRunStep},
		{name: "arm query answered not armed", armAnswer: &expired, foundFrom: 1, wantLast: triggerDialogReEnableStep},
		{name: "arm query answered with an error", armErr: &unityipc.RPCError{Message: "unknown pause point"}, foundFrom: 1, wantLast: triggerDialogReEnableStep},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			fakes, deps := useWaitStartDialogFakes(t, testCase.armAnswer, testCase.armErr, testCase.foundFrom)

			_, _, _, _, _, err := waitForPausePoint(context.Background(), unityipc.Connection{}, waitStartDialogOptions(deps, true, false))

			_ = assertTriggerNotSentBlockedByDialog(t, err, testCase.wantLast)
			if fakes.queries < 3 {
				t.Fatalf("queries = %d, want the wait to have polled before its watch found the dialog", fakes.queries)
			}
		})
	}
}

// Verifies the await step keeps --resume-play for a wait that used it and sent no trigger.
func TestPausePointWaitKeepsResumePlayForAnUnsentTrigger(t *testing.T) {
	_, deps := useWaitStartDialogFakes(t, nil, nil, 1)

	_, _, _, _, _, err := waitForPausePoint(context.Background(), unityipc.Connection{}, waitStartDialogOptions(deps, true, true))

	cliError := assertTriggerNotSentBlockedByDialog(t, err,
		"After the dialog is closed, run uloop await-pause-point --id trigger_dialog again with the same --trigger and --resume-play.")
	if containsString(cliError.NextActions, pausePointTriggerNotRunResumedNextAction) {
		t.Fatalf("NextActions = %#v, want no re-pause step when nothing was resumed", cliError.NextActions)
	}
}

// Verifies a --resume-play-only wait whose arm query got no answer returns at once behind a dialog,
// saying nothing was resumed and to wait again with the same --resume-play.
func TestWaitForPausePointReportsAnUnsentResumeBehindADialog(t *testing.T) {
	fakes, deps := useWaitStartDialogFakes(t, nil, nil, 1)

	_, _, _, _, _, err := waitForPausePoint(context.Background(), unityipc.Connection{}, waitStartDialogOptions(deps, false, true))

	cliError := classifyStartedDialogError(t, err, clicore.PausePointAwaitCommandName)
	if !strings.HasSuffix(cliError.Message, "uloop await-pause-point stopped waiting; --resume-play did not resume PlayMode.") ||
		!cliError.SafeToRetry || fakes.looks != 1 {
		t.Fatalf("Message = %q, SafeToRetry = %v, looks = %d, want the unsent-resume form at the first look",
			cliError.Message, cliError.SafeToRetry, fakes.looks)
	}
	want := "After the dialog is closed, run uloop await-pause-point --id trigger_dialog again with the same --resume-play."
	if last := cliError.NextActions[len(cliError.NextActions)-1]; last != want {
		t.Fatalf("last NextAction = %q, want %q", last, want)
	}
}

// Verifies a wait whose arm query got no answer and that never finds a dialog ends the way it did
// before, not with a dialog error.
func TestWaitForPausePointKeepsItsOwnEndWithoutADialog(t *testing.T) {
	_, deps := useWaitStartDialogFakes(t, nil, nil, 1_000)
	options := waitStartDialogOptions(deps, true, false)
	options.timeout = 300 * time.Millisecond

	_, _, _, _, _, err := waitForPausePoint(context.Background(), unityipc.Connection{}, options)

	var blocked blockedByDialogError
	if errors.As(err, &blocked) {
		t.Fatalf("err = %v, want the wait's own end", err)
	}
}

// Verifies a wait whose expiry extension got no answer looks for a dialog at once, before the arm
// query, and ends in the form for what it was asked to do: not sent, not resumed, or started.
func TestWaitForPausePointLooksOnceWhenTheExpiryExtensionGoesUnanswered(t *testing.T) {
	cases := []struct {
		name       string
		trigger    bool
		resumePlay bool
		wantSuffix string
	}{
		{name: "trigger", trigger: true, wantSuffix: "its --trigger command was not sent."},
		{name: "resume only", resumePlay: true, wantSuffix: "--resume-play did not resume PlayMode."},
		{name: "neither", wantSuffix: "the command had already started and continues in Unity once the dialog is closed."},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			fakes, deps := useWaitStartDialogFakes(t, nil, nil, 1)
			options := waitStartDialogOptions(deps, testCase.trigger, testCase.resumePlay)
			options.expiryExtensionUnanswered = true

			_, _, triggerResult, _, _, err := waitForPausePoint(context.Background(), unityipc.Connection{}, options)

			cliError := classifyStartedDialogError(t, err, clicore.PausePointAwaitCommandName)
			if !strings.HasSuffix(cliError.Message, testCase.wantSuffix) || fakes.queries != 0 || fakes.looks != 1 {
				t.Fatalf("Message = %q, queries = %d, looks = %d, want %q from one look before any query",
					cliError.Message, fakes.queries, fakes.looks, testCase.wantSuffix)
			}
			if testCase.trigger && (triggerResult == nil || !strings.Contains(triggerResult.Error, "dialog")) {
				t.Fatalf("TriggerResult = %#v, want the dialog as the reason it was not sent", triggerResult)
			}
		})
	}
}

// Verifies a wait looks for a dialog at most once at its start: after an unanswered expiry extension
// found none, an unanswered arm query does not look again, and only the later watch finds the dialog.
func TestWaitForPausePointLooksAtMostOnceAtItsStart(t *testing.T) {
	fakes, deps := useWaitStartDialogFakes(t, nil, nil, 2)
	options := waitStartDialogOptions(deps, true, false)
	options.expiryExtensionUnanswered = true

	_, _, _, _, _, err := waitForPausePoint(context.Background(), unityipc.Connection{}, options)

	_ = assertTriggerNotSentBlockedByDialog(t, err, triggerDialogNotRunStep)
	if fakes.queries < 3 {
		t.Fatalf("queries = %d, want the dialog found by the watch after polling, not by a second start look", fakes.queries)
	}
}

// Verifies the expiry extension counts as unanswered only when it failed without an answer from
// Unity, the same rule the arm query uses, so an answered error starts no early look.
func TestExtendPausePointExpiryBeforeWaitReportsAnUnansweredExtension(t *testing.T) {
	originalExtend := extendPausePointExpiry
	t.Cleanup(func() { extendPausePointExpiry = originalExtend })
	results := map[string]struct {
		err  error
		want bool
	}{
		"succeeded":           {want: false},
		"answered with error": {err: &unityipc.RPCError{Message: "unknown command"}, want: false},
		"no answer":           {err: context.DeadlineExceeded, want: true},
	}
	for name, result := range results {
		t.Run(name, func(t *testing.T) {
			extendPausePointExpiry = func(context.Context, unityipc.Connection, string, int) (pausePointStatusResponse, error) {
				return pausePointStatusResponse{}, result.err
			}
			got := extendPausePointExpiryBeforeWait(context.Background(), unityipc.Connection{}, waitForPausePointOptions{id: "x"}, io.Discard)
			if got != result.want {
				t.Fatalf("unanswered = %v, want %v", got, result.want)
			}
		})
	}
}

// Verifies an early look that found no dialog leaves the wait's watch on its usual schedule: the
// watch looks once the polls have gone unanswered for its stall threshold, not after its 30-second
// interval, so a dialog that opened just after the early look still ends the wait.
func TestWaitForPausePointEarlyLookKeepsTheWatchSchedule(t *testing.T) {
	fakes, deps := useWaitStartDialogFakes(t, nil, nil, 2)
	base := time.Unix(1_700_000_000, 0)
	// One second per query, so the polls' stall grows with the queries and 30 seconds take 30 of them.
	deps.now = func() time.Time { return base.Add(time.Duration(fakes.queries) * time.Second) }

	_, _, _, _, _, err := waitForPausePoint(context.Background(), unityipc.Connection{}, waitStartDialogOptions(deps, true, false))

	_ = assertTriggerNotSentBlockedByDialog(t, err, triggerDialogNotRunStep)
	if fakes.looks != 2 || fakes.queries >= 25 {
		t.Fatalf("looks = %d, queries = %d, want the watch's first look within its stall threshold", fakes.looks, fakes.queries)
	}
}

// Verifies a --resume-play-only wait that sent its resume keeps the started form when a dialog ends
// it later, whether Unity was not paused or the resume failed: only a resume never sent is safe to retry.
func TestPausePointWaitKeepsTheStartedFormAfterASentResume(t *testing.T) {
	enabled := pausePointStatusResponse{Id: triggerDialogPausePointID, Status: pausePointStatusEnabled}
	results := map[string]pausePointResumePlayResult{
		"not paused":    {WasPaused: false},
		"resume failed": {Error: "control-play-mode failed"},
	}
	for name, result := range results {
		t.Run(name, func(t *testing.T) {
			_, deps := useWaitStartDialogFakes(t, &enabled, nil, 1)
			resumePlayModeForPausePoint = func(context.Context, unityipc.Connection) pausePointResumePlayResult {
				return result
			}

			_, _, _, _, _, err := waitForPausePoint(context.Background(), unityipc.Connection{}, waitStartDialogOptions(deps, false, true))

			cliError := classifyStartedDialogError(t, err, clicore.PausePointAwaitCommandName)
			if !strings.HasSuffix(cliError.Message, "the command had already started and continues in Unity once the dialog is closed.") ||
				cliError.SafeToRetry {
				t.Fatalf("Message = %q, SafeToRetry = %v, want the started form", cliError.Message, cliError.SafeToRetry)
			}
		})
	}
}
