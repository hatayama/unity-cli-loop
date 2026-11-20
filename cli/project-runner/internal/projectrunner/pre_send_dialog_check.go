package projectrunner

import (
	"context"
	"io"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

// focusWindowCommandName is the runner command that raises Unity's window without contacting it.
const focusWindowCommandName = "focus-window"

// commandsSentWithoutDialogCheck are the commands uloop runs without first asking whether a dialog
// holds Unity. Why each: status and get-dialogs are that check themselves; cancel and
// press-dialog-button exist for an Editor that is held, and Unity answers cancel off the main
// thread; focus-window never contacts Unity.
var commandsSentWithoutDialogCheck = map[string]bool{
	clicore.StatusCommandName:            true,
	clicore.CancelCommandName:            true,
	clicore.GetDialogsCommandName:        true,
	clicore.PressDialogButtonCommandName: true,
	focusWindowCommandName:               true,
}

// runProjectCommand is the entry of one uloop run against a resolved project: it refuses the command
// at once when a modal dialog holds Unity, and otherwise runs it as before.
// Why here and not in runResolvedProjectCommand: a pause-point or record-video trigger is dispatched
// through runResolvedProjectCommand from inside a run that was already checked, so the check stays
// at one per uloop run without anything to remember. The waits and resends further down never pass
// through here either; the dialog watches of the sent request cover them.
func runProjectCommand(
	ctx context.Context,
	connection unityipc.Connection,
	command string,
	commandArgs []string,
	startPath string,
	stdout io.Writer,
	stderr io.Writer,
	deps dialogWatchDeps,
) int {
	if refuseCommandBlockedByDialog(ctx, connection, command, stderr, deps) {
		return 1
	}
	return runResolvedProjectCommand(ctx, connection, command, commandArgs, startPath, stdout, stderr)
}

// refuseCommandBlockedByDialog asks the Editor once whether a modal dialog holds it and, when one
// does, writes BLOCKED_BY_DIALOG for command and reports true. Every other answer, and a check that
// failed, reports false so the command is sent as before; the sent request still meets Unity's
// 30-second main_thread_blocked answer when a dialog opens after this check.
func refuseCommandBlockedByDialog(
	ctx context.Context,
	connection unityipc.Connection,
	command string,
	stderr io.Writer,
	deps dialogWatchDeps,
) bool {
	// Why the disabled-tool check first: that error says what to change, and the command would not
	// run once the dialog closed either.
	if commandsSentWithoutDialogCheck[command] || isNativeCommandDisabled(connection, command) {
		return false
	}
	dialogs := findBlockingDialogs(ctx, connection, deps)
	if len(dialogs) == 0 {
		return false
	}
	clierrors.WriteClassifiedError(stderr, notSentBlockedByDialog(command, dialogs, deps.canPressDialogs),
		clierrors.ErrorContext{ProjectRoot: connection.ProjectRoot, Command: command})
	return true
}

// defaultPreSendDialogWatchDeps reads the Editor the way uloop status does, except that an Editor
// that does not answer is left to the send.
func defaultPreSendDialogWatchDeps() dialogWatchDeps {
	deps := defaultDialogWatchDeps()
	deps.probeEditorStatus = probeEditorStatusBeforeSend
	return deps
}

func probeEditorStatusBeforeSend(ctx context.Context, connection unityipc.Connection) (statusReport, error) {
	return probeEditorStatus(ctx, connection, preSendStatusDeps(defaultStatusCommandDeps()))
}

// preSendStatusDeps turns off what status does for an Editor that does not answer: reading the
// process list and looking at Unity's windows. Why: a command sent during a domain reload would wait
// about a second longer for that look, and the send already waits for the server to come back.
func preSendStatusDeps(deps statusCommandDeps) statusCommandDeps {
	deps.returnUnreachableAsError = true
	return deps
}
