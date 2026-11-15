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
)

// waitStartDialogFakes answers the arm query and every poll with armAnswer (nil means no answer), and
// looks find the dialog from look number foundFrom on. It returns the query and look counters.
type waitStartDialogFakes struct {
	queries int
	looks   int
}

func useWaitStartDialogFakes(t *testing.T, armAnswer *pausePointStatusResponse, foundFrom int) (*waitStartDialogFakes, dialogWatchDeps) {
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
	fakes, deps := useWaitStartDialogFakes(t, nil, 1)

	_, _, _, _, _, err := waitForPausePoint(context.Background(), unityipc.Connection{}, waitStartDialogOptions(deps, true, false))

	_ = assertTriggerNotSentBlockedByDialog(t, err, triggerDialogNotRunStep)
	if fakes.queries != 1 || fakes.looks != 1 {
		t.Fatalf("queries = %d, looks = %d, want the arm query and one look before any poll", fakes.queries, fakes.looks)
	}
}

// Verifies the trigger-not-sent error also comes from the wait's own watch when the first look found
// no dialog, and when the arm query answered that the marker is not armed (no look then).
func TestPausePointWaitReportsAnUnsentTriggerWhenItsWatchFindsTheDialog(t *testing.T) {
	expired := pausePointStatusResponse{Id: triggerDialogPausePointID, Status: pausePointStatusExpired}
	cases := []struct {
		name      string
		armAnswer *pausePointStatusResponse
		foundFrom int
	}{
		{name: "no dialog at the first look", foundFrom: 2},
		{name: "arm query answered not armed", armAnswer: &expired, foundFrom: 1},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			fakes, deps := useWaitStartDialogFakes(t, testCase.armAnswer, testCase.foundFrom)

			_, _, _, _, _, err := waitForPausePoint(context.Background(), unityipc.Connection{}, waitStartDialogOptions(deps, true, false))

			_ = assertTriggerNotSentBlockedByDialog(t, err, triggerDialogNotRunStep)
			if fakes.queries < 3 {
				t.Fatalf("queries = %d, want the wait to have polled before its watch found the dialog", fakes.queries)
			}
		})
	}
}

// Verifies the await step keeps --resume-play for a wait that used it and sent no trigger.
func TestPausePointWaitKeepsResumePlayForAnUnsentTrigger(t *testing.T) {
	_, deps := useWaitStartDialogFakes(t, nil, 1)

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
	fakes, deps := useWaitStartDialogFakes(t, nil, 1)

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
	_, deps := useWaitStartDialogFakes(t, nil, 1_000)
	options := waitStartDialogOptions(deps, true, false)
	options.timeout = 300 * time.Millisecond

	_, _, _, _, _, err := waitForPausePoint(context.Background(), unityipc.Connection{}, options)

	var blocked blockedByDialogError
	if errors.As(err, &blocked) {
		t.Fatalf("err = %v, want the wait's own end", err)
	}
}
