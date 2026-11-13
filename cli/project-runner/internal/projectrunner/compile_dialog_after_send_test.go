package projectrunner

import (
	"context"
	"errors"
	"io"
	"os"
	"testing"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

// firstDialogLook answers the look a fresh compile wait makes right after an unanswered send, and
// counts how often it was made.
type firstDialogLook struct {
	dialogs []statusDialog
	calls   int
}

func (look *firstDialogLook) find(context.Context, unityipc.Connection) []statusDialog {
	look.calls++
	return look.dialogs
}

// countingCompileStatusQueries answers every status query as a compile still running, and counts them.
func countingCompileStatusQueries(deps *compileWaitDeps, count *int) {
	deps.queryCompileStatus = func(context.Context, unityipc.Connection, string) (compileStatusResponse, error) {
		*count++
		return compileStatusResponse{IsCompiling: true}, nil
	}
}

// Verifies a fresh compile wait whose send went unanswered stops with BLOCKED_BY_DIALOG before its
// first status query when the first look finds a dialog, and that the stop keeps the request for the
// next uloop compile.
func TestCompileWaitStopsAtOnceWhenTheUnansweredSendMeetsADialog(t *testing.T) {
	look := &firstDialogLook{dialogs: []statusDialog{commandDialogPlayerStoppedDialog()}}
	deps := compileDialogWaitDeps(newFakeDialogLook(), true)
	deps.findDialogAfterUnansweredSend = look.find
	queries := 0
	countingCompileStatusQueries(&deps, &queries)
	options := compileDialogWaitOptions(t)
	options.sendWentUnanswered = true

	_, completed, _, err := waitForCompileCompletionWithDeps(context.Background(), options, deps)

	if completed || queries != 0 || look.calls != 1 {
		t.Fatalf("completed = %v, status queries = %d, looks = %d, want a stop before any query after one look",
			completed, queries, look.calls)
	}
	assertStartedBlockedByDialog(t, classifyStartedDialogError(t, err, clicore.CompileCommandName), compileDialogClosedNextAction, true)
	projectRoot := t.TempDir()
	persistCompilePendingRecordWhenBlockedByDialog(projectRoot, "compile_dialog", err, io.Discard)
	if record, found := readCompilePendingRecord(projectRoot); !found || record.RequestID != "compile_dialog" {
		t.Fatalf("record = %#v, found = %v, want the stopped compile's request", record, found)
	}
}

// Verifies a first look that finds no dialog, or whose look fails, leaves the wait polling as before,
// and the wait looks first only once.
func TestCompileWaitKeepsPollingWhenTheFirstLookFindsNoDialog(t *testing.T) {
	look := &firstDialogLook{}
	deps := compileDialogWaitDeps(newFakeDialogLook(), true)
	deps.findDialogAfterUnansweredSend = look.find
	queries := 0
	countingCompileStatusQueries(&deps, &queries)
	options := compileDialogWaitOptions(t)
	options.sendWentUnanswered = true

	_, completed, _, err := waitForCompileCompletionWithDeps(context.Background(), options, deps)

	if completed || err != nil {
		t.Fatalf("completed = %v, err = %v, want the timeout", completed, err)
	}
	if look.calls != 1 || queries < 2 {
		t.Fatalf("looks = %d, status queries = %d, want one look and polling after it", look.calls, queries)
	}
}

// Verifies a wait whose send was answered or dropped never makes the first look.
func TestCompileWaitDoesNotLookFirstForAnAnsweredOrDroppedSend(t *testing.T) {
	look := &firstDialogLook{dialogs: []statusDialog{commandDialogPlayerStoppedDialog()}}
	deps := compileDialogWaitDeps(newFakeDialogLook(), true)
	deps.findDialogAfterUnansweredSend = look.find

	_, completed, _, err := waitForCompileCompletionWithDeps(context.Background(), compileDialogWaitOptions(t), deps)

	if completed || err != nil || look.calls != 0 {
		t.Fatalf("completed = %v, err = %v, looks = %d, want the timeout without a look", completed, err, look.calls)
	}
}

// Verifies only a send Unity accepted and then left unanswered past its response timeout counts as
// unanswered: an answer, a dropped connection, or a request Unity never accepted does not.
func TestCompileSendWentUnanswered(t *testing.T) {
	accepted := unityipc.UnitySendOutcome{RequestAccepted: true}
	cases := map[string]struct {
		err     error
		outcome unityipc.UnitySendOutcome
		want    bool
	}{
		"timed out":     {err: os.ErrDeadlineExceeded, outcome: accepted, want: true},
		"answered":      {err: nil, outcome: accepted, want: false},
		"dropped":       {err: io.EOF, outcome: accepted, want: false},
		"not accepted":  {err: os.ErrDeadlineExceeded, outcome: unityipc.UnitySendOutcome{}, want: false},
		"other failure": {err: errors.New("rejected"), outcome: accepted, want: false},
	}
	for name, tc := range cases {
		t.Run(name, func(t *testing.T) {
			if got := compileSendWentUnanswered(tc.err, tc.outcome); got != tc.want {
				t.Fatalf("compileSendWentUnanswered = %v, want %v", got, tc.want)
			}
		})
	}
}
