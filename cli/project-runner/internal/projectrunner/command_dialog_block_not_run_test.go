package projectrunner

import (
	"context"
	"testing"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

// Verifies Details.CommandNotRun is true exactly for the forms that know the command was not run (Unity's
// main_thread_blocked answer, and a command uloop did not send), and is absent for the forms where the
// command had started or may have started, so a reader can tell the two apart without the Message text.
func TestBlockedByDialogErrorMarksOnlyTheNotRunFormsAsNotRun(t *testing.T) {
	dialogs := []statusDialog{commandDialogPlayerStoppedDialog()}
	neutral := explainBlockedByDialog(context.Background(), unreachableConnection("<PROJECT_ROOT>"), "get-logs",
		&unityipc.EditorUnresponsiveError{StallSeconds: 300}, nil, newFakeDialogLook(dialogs...).deps())
	cases := []struct {
		name       string
		err        error
		wantNotRun bool
	}{
		{name: "not run", err: blockedByDialogError{command: "get-logs", dialogs: dialogs, notRun: commandDialogMainThreadBlockedRPCError("get-logs")}, wantNotRun: true},
		{name: "not sent", err: notSentBlockedByDialog("get-logs", dialogs, true), wantNotRun: true},
		{name: "started", err: startedCommandBlockedByDialog(clicore.CompileCommandName, dialogs, true, true), wantNotRun: false},
		{name: "heartbeat", err: (&heartbeatDialogWatch{method: "get-logs", dialogs: dialogs}).blockedError(), wantNotRun: false},
		{name: "neutral", err: neutral, wantNotRun: false},
		{name: "hot-reload wait", err: hotReloadWaitBlockedByDialog(dialogs, true), wantNotRun: false},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			cliError := clierrors.ClassifyError(testCase.err, clierrors.ErrorContext{Command: "get-logs"})
			if cliError.ErrorCode != clierrors.ErrorCodeBlockedByDialog {
				t.Fatalf("ErrorCode = %q, want BLOCKED_BY_DIALOG", cliError.ErrorCode)
			}
			value, present := cliError.Details[commandNotRunDetailKey]
			if testCase.wantNotRun && value != true {
				t.Fatalf("Details.%s = %#v, want true", commandNotRunDetailKey, value)
			}
			if !testCase.wantNotRun && present {
				t.Fatalf("Details.%s = %#v, want the key absent", commandNotRunDetailKey, value)
			}
		})
	}
}
