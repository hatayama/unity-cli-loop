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

func unansweredCompileStatusError() error {
	return &compileStatusUnansweredError{cause: os.ErrDeadlineExceeded}
}

// unansweredCompileStatusDeps answers every status query as accepted but unanswered, and counts them.
func unansweredCompileStatusDeps(look *fakeDialogLook, queries *int) compileWaitDeps {
	deps := compileDialogWaitDeps(look, false)
	deps.queryCompileStatus = func(context.Context, unityipc.Connection, string) (compileStatusResponse, error) {
		*queries++
		return compileStatusResponse{}, unansweredCompileStatusError()
	}
	return deps
}

// Verifies a fresh compile wait whose first status query Unity accepts but does not answer looks for a
// dialog at once and stops with BLOCKED_BY_DIALOG after that one query and one look.
func TestCompileWaitLooksAtOnceWhenAStatusQueryGoesUnanswered(t *testing.T) {
	look := newFakeDialogLook(commandDialogPlayerStoppedDialog())
	queries := 0

	_, completed, _, err := waitForCompileCompletionWithDeps(context.Background(), compileDialogWaitOptions(t),
		unansweredCompileStatusDeps(look, &queries))

	if completed || queries != 1 || look.calls != 1 {
		t.Fatalf("completed = %v, queries = %d, looks = %d, want a stop after one query and one look",
			completed, queries, look.calls)
	}
	assertStartedBlockedByDialog(t, classifyStartedDialogError(t, err, clicore.CompileCommandName), compileDialogClosedNextAction, true)
}

// Verifies the compile attach wait looks at once the same way when its status query goes unanswered.
func TestCompileAttachWaitLooksAtOnceWhenAStatusQueryGoesUnanswered(t *testing.T) {
	look := newFakeDialogLook(commandDialogPlayerStoppedDialog())
	queries := 0

	_, outcome, _, err := waitForAttachedCompileCompletion(context.Background(), compileDialogWaitOptions(t),
		unansweredCompileStatusDeps(look, &queries))

	if outcome != attachWaitFailed || queries != 1 || look.calls != 1 {
		t.Fatalf("outcome = %v, queries = %d, looks = %d, want a stop after one query and one look",
			outcome, queries, look.calls)
	}
	assertStartedBlockedByDialog(t, classifyStartedDialogError(t, err, clicore.CompileCommandName), compileDialogClosedNextAction, true)
}

// Verifies an unanswered status query with no dialog looks once and keeps waiting, and that the next
// unanswered query within the interval does not look again.
func TestCompileWaitDialogsLooksOncePerIntervalForUnansweredQueries(t *testing.T) {
	look := newFakeDialogLook()
	watch := newDialogBlockWatch(unityipc.Connection{}, look.deps(), dialogWatchInterval, dialogWatchStallThresholdSeconds)

	first := compileWaitDialogs(context.Background(), watch, unansweredCompileStatusError())
	second := compileWaitDialogs(context.Background(), watch, unansweredCompileStatusError())

	if first != nil || second != nil || look.calls != 1 {
		t.Fatalf("dialogs = %v then %v, looks = %d, want none and one look", first, second, look.calls)
	}
}

// Verifies a status query that fails to connect, as during a domain reload, or that is answered,
// does not make the wait look before the stall threshold.
func TestCompileWaitDialogsDoesNotLookAtOnceForAFailedOrAnsweredQuery(t *testing.T) {
	cases := map[string]error{
		"connection failed": io.EOF,
		"other failure":     errors.New("unreachable"),
		"answered":          nil,
	}
	for name, queryErr := range cases {
		t.Run(name, func(t *testing.T) {
			look := newFakeDialogLook(commandDialogPlayerStoppedDialog())
			watch := newDialogBlockWatch(unityipc.Connection{}, look.deps(), dialogWatchInterval, dialogWatchStallThresholdSeconds)

			dialogs := compileWaitDialogs(context.Background(), watch, queryErr)

			if dialogs != nil || look.calls != 0 {
				t.Fatalf("dialogs = %v, looks = %d, want no look", dialogs, look.calls)
			}
		})
	}
}
