package projectrunner

import (
	"context"
	"fmt"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

const commandDialogClosedNextAction = "After the dialog is closed, run the command again."

// blockedByDialogError reports that a modal dialog keeps a command from running: the dialog holds
// Unity's main thread until someone answers it, and the agent cannot answer it while it waits.
type blockedByDialogError struct {
	command string
	// dialogs is what the Editor or Unity's windows reported, frontmost first; never empty.
	dialogs []statusDialog
	// canPress is whether uloop press-dialog-button works on this operating system.
	canPress bool
	// notRun is Unity's main_thread_blocked answer when Unity gave up waiting for its main thread;
	// nil when a uloop wait found the dialog before the command was sent.
	notRun error
}

// Error leaves out the dialog's text on purpose: the transport checks read words such as "EOF" in
// error text, so a dialog title would make this error read as a lost connection, hide the dialog, and
// send the waiting paths into their disconnect waits. The dialog's text goes into the envelope only.
func (err blockedByDialogError) Error() string {
	if err.notRun != nil {
		return err.notRun.Error()
	}
	return fmt.Sprintf("a modal dialog is open in Unity; uloop %s stopped waiting, and nothing was applied", err.command)
}

func (err blockedByDialogError) message() string {
	if err.notRun != nil {
		return fmt.Sprintf("'%s' was not run: %s", err.command, blockedByDialogMessage(err.dialogs))
	}
	return blockedByDialogMessage(err.dialogs) + fmt.Sprintf(" uloop %s stopped waiting, and nothing was applied.", err.command)
}

// Unwrap returns Unity's answer, so callers that recover by the error's type see it unchanged.
func (err blockedByDialogError) Unwrap() error {
	return err.notRun
}

// ToCLIError reports the dialogs the way uloop status does, with the last step pointing back at the
// command. Safe to retry: the command's code never started.
func (err blockedByDialogError) ToCLIError(context clierrors.ErrorContext) clierrors.CLIError {
	phase := clierrors.ErrorPhaseExecution
	closedNextAction := fmt.Sprintf("After the dialog is closed, run uloop %s again.", err.command)
	details := map[string]any{}
	if err.notRun != nil {
		notRunError := clierrors.ClassifyError(err.notRun, context)
		phase = notRunError.Phase
		closedNextAction = commandDialogClosedNextAction
		for key, value := range notRunError.Details {
			details[key] = value
		}
	}
	details["Dialogs"] = err.dialogs
	return clierrors.CLIError{
		ErrorCode:   clierrors.ErrorCodeBlockedByDialog,
		Phase:       phase,
		Message:     err.message(),
		Retryable:   true,
		SafeToRetry: true,
		ProjectRoot: context.ProjectRoot,
		Command:     context.Command,
		NextActions: dialogAnswerNextActions(err.dialogs, err.canPress, closedNextAction),
		Details:     details,
	}
}

// explainBlockedByDialog turns Unity's main_thread_blocked answer into blockedByDialogError when the
// Editor status shows a dialog, and returns err unchanged otherwise.
// Why only main_thread_blocked: other failures, such as the busy answers hot-reload resends through,
// are not explained by a dialog, and looking for one can run osascript each time.
// Why the uloop status probe: it already switches between the Editor's own report (Unity 6000.7 and
// later) and a look at Unity's windows. Its requests do not go through the retrying send, so this
// lookup cannot run again from inside itself.
func explainBlockedByDialog(
	ctx context.Context,
	connection unityipc.Connection,
	command string,
	err error,
	deps connectionRetryDeps,
) error {
	if !isMainThreadBlockedRPCError(err) {
		return err
	}
	report, probeErr := deps.probeEditorStatus(ctx, connection)
	if probeErr != nil || report.State != statusStateBlockedByDialog || len(report.Dialogs) == 0 {
		return err
	}
	return blockedByDialogError{command: command, dialogs: report.Dialogs, canPress: deps.canPressDialogs, notRun: err}
}

func probeEditorStatusForDialogs(ctx context.Context, connection unityipc.Connection) (statusReport, error) {
	return probeEditorStatus(ctx, connection, defaultStatusCommandDeps())
}
