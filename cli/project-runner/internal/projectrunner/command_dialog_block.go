package projectrunner

import (
	"context"
	"errors"
	"fmt"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

const (
	commandDialogClosedNextAction = "After the dialog is closed, run the command again."
	// startedCommandDialogClosedNextAction is the last step of a command that had already started,
	// for the commands that have no step of their own.
	startedCommandDialogClosedNextAction = "After the dialog is closed, run uloop status; when it reports Ready, " +
		"check whether the command took effect before running it again."
	compileDialogClosedNextAction = "After the dialog is closed, run uloop compile again; " +
		"it reattaches to the compile that is still running."
	executeDynamicCodeDialogClosedNextAction = "After the dialog is closed, the snippet finishes in Unity but its " +
		"result is not returned; run uloop status, then check the effect before running it again."
	controlPlayModeDialogClosedNextAction = "After the dialog is closed, run uloop control-play-mode --action Status " +
		"to see the state it reached."
	// The pause-point wait ends in uloop only; the marker stays armed in Unity, and a repeated --trigger
	// would send the trigger again.
	pausePointDialogClosedNextAction = "After the dialog is closed, run uloop pause-point-status to see whether the " +
		"marker was hit; if it is still waiting, run uloop await-pause-point again without --trigger."
	startedCommandStoppedFormat = "uloop %s stopped waiting; the command had already started and continues in " +
		"Unity once the dialog is closed."
	// The heartbeat wait gives up on the response itself, so it cannot promise the command goes on.
	heartbeatStoppedFormat = "uloop %s stopped waiting; the command had already started, and uloop no longer " +
		"waits for its result. Closing the dialog lets Unity go on, and the command may still finish there."
	neutralStoppedFormat = "uloop %s stopped waiting."
)

// blockedByDialogError reports that a modal dialog keeps a command from going on: the dialog holds
// Unity's main thread until someone answers it, and the agent cannot answer it while uloop waits.
type blockedByDialogError struct {
	command string
	// dialogs is what the Editor or Unity's windows reported, frontmost first; never empty.
	dialogs []statusDialog
	// canPress is whether uloop press-dialog-button works on this operating system.
	canPress bool
	// notRun is Unity's main_thread_blocked answer when Unity gave up waiting for its main thread
	// before the command ran. The other fields below are unused then.
	notRun error
	// stopped is the sentence after the dialog sentence: what uloop stopped and what became of the
	// command.
	stopped          string
	closedNextAction string
	phase            string
	safeToRetry      bool
	// details are the replaced error's details, kept beside Dialogs.
	details map[string]any
}

// hotReloadWaitBlockedByDialog is the error of hot-reload's waits before it applies.
func hotReloadWaitBlockedByDialog(dialogs []statusDialog, canPress bool) blockedByDialogError {
	return blockedByDialogError{
		command:          hotReloadCommandName,
		dialogs:          dialogs,
		canPress:         canPress,
		stopped:          "uloop hot-reload stopped waiting, and nothing was applied.",
		closedNextAction: "After the dialog is closed, run uloop hot-reload again.",
		phase:            clierrors.ErrorPhaseExecution,
		safeToRetry:      true,
	}
}

// startedCommandBlockedByDialog is the error of a wait that polls Unity after the command started.
// Stopping the poll leaves Unity's work alone, so the command goes on once the dialog closes.
func startedCommandBlockedByDialog(
	command string,
	dialogs []statusDialog,
	canPress bool,
	safeToRetry bool,
) blockedByDialogError {
	return blockedByDialogError{
		command:          command,
		dialogs:          dialogs,
		canPress:         canPress,
		stopped:          fmt.Sprintf(startedCommandStoppedFormat, command),
		closedNextAction: startedCommandDialogClosedNextActionFor(command),
		phase:            clierrors.ErrorPhaseResponseWaiting,
		safeToRetry:      safeToRetry,
	}
}

func heartbeatStoppedSentence(command string) string {
	return fmt.Sprintf(heartbeatStoppedFormat, command)
}

func startedCommandDialogClosedNextActionFor(command string) string {
	switch command {
	case clicore.CompileCommandName:
		return compileDialogClosedNextAction
	case clicore.ExecuteDynamicCodeCommandName:
		return executeDynamicCodeDialogClosedNextAction
	case controlPlayModeCommandName:
		return controlPlayModeDialogClosedNextAction
	case clicore.PausePointAwaitCommandName, pausePointEnableCommandName:
		return pausePointDialogClosedNextAction
	default:
		return startedCommandDialogClosedNextAction
	}
}

// Error leaves out the dialog's text on purpose: the transport checks read words such as "EOF" in
// error text, so a dialog title would make this error read as a lost connection, hide the dialog, and
// send the waiting paths into their disconnect waits. The dialog's text goes into the envelope only.
func (err blockedByDialogError) Error() string {
	if err.notRun != nil {
		return err.notRun.Error()
	}
	return fmt.Sprintf("a modal dialog is open in Unity; uloop %s stopped waiting", err.command)
}

func (err blockedByDialogError) message() string {
	if err.notRun != nil {
		return fmt.Sprintf("'%s' was not run: %s", err.command, blockedByDialogMessage(err.dialogs))
	}
	return blockedByDialogMessage(err.dialogs) + " " + err.stopped
}

// Unwrap returns Unity's answer, so callers that recover by the error's type see it unchanged. Why
// nothing for the other forms: they replace a cancelled send or a timed-out wait, and handing that
// back would make the callers read a lost connection and start their disconnect waits.
func (err blockedByDialogError) Unwrap() error {
	return err.notRun
}

// ToCLIError reports the dialogs the way uloop status does, with the last step pointing back at the
// command.
func (err blockedByDialogError) ToCLIError(context clierrors.ErrorContext) clierrors.CLIError {
	phase := err.phase
	closedNextAction := err.closedNextAction
	safeToRetry := err.safeToRetry
	details := map[string]any{}
	for key, value := range err.details {
		details[key] = value
	}
	if err.notRun != nil {
		notRunError := clierrors.ClassifyError(err.notRun, context)
		phase = notRunError.Phase
		closedNextAction = commandDialogClosedNextAction
		// Safe to retry: the command's code never started.
		safeToRetry = true
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
		SafeToRetry: safeToRetry,
		ProjectRoot: context.ProjectRoot,
		Command:     context.Command,
		NextActions: dialogAnswerNextActions(err.dialogs, err.canPress, closedNextAction),
		Details:     details,
	}
}

// explainBlockedByDialog turns a failure of the retrying send into blockedByDialogError when the
// Editor status shows a dialog, and returns err unchanged otherwise. It explains two failures:
//   - Unity's main_thread_blocked answer: the command was not run.
//   - EditorUnresponsiveError after the five-minute stall: whether the command started is unknown,
//     so the message does not say, and SafeToRetry stays what UNITY_EDITOR_UNRESPONSIVE says.
//
// Why only these: other failures, such as the busy answers hot-reload resends through, are not
// explained by a dialog, and looking for one can run osascript each time.
// Why the uloop status probe: it already switches between the Editor's own report (Unity 6000.7 and
// later) and a look at Unity's windows. Its requests do not go through the retrying send, so this
// lookup cannot run again from inside itself.
func explainBlockedByDialog(
	ctx context.Context,
	connection unityipc.Connection,
	command string,
	err error,
	deps dialogWatchDeps,
) error {
	notRun := isMainThreadBlockedRPCError(err)
	var unresponsiveErr *unityipc.EditorUnresponsiveError
	if !notRun && !errors.As(err, &unresponsiveErr) {
		return err
	}
	dialogs := findBlockingDialogs(ctx, connection, deps)
	if len(dialogs) == 0 {
		return err
	}
	if notRun {
		return blockedByDialogError{command: command, dialogs: dialogs, canPress: deps.canPressDialogs, notRun: err}
	}
	original := clierrors.ClassifyError(err, clierrors.ErrorContext{ProjectRoot: connection.ProjectRoot, Command: command})
	return neutralBlockedByDialog(command, dialogs, deps.canPressDialogs, original)
}

// neutralBlockedByDialog replaces a failed wait whose command may or may not still be running: its
// Message does not say, and it keeps the replaced error's phase, SafeToRetry, and details.
func neutralBlockedByDialog(command string, dialogs []statusDialog, canPress bool, original clierrors.CLIError) blockedByDialogError {
	return blockedByDialogError{
		command:          command,
		dialogs:          dialogs,
		canPress:         canPress,
		stopped:          fmt.Sprintf(neutralStoppedFormat, command),
		closedNextAction: startedCommandDialogClosedNextAction,
		phase:            original.Phase,
		safeToRetry:      original.SafeToRetry,
		details:          original.Details,
	}
}

// findBlockingDialogs reads the Editor once and returns the dialogs holding its main thread; nil
// when there are none, the read failed, or the dependencies turn the look off.
func findBlockingDialogs(ctx context.Context, connection unityipc.Connection, deps dialogWatchDeps) []statusDialog {
	if deps.probeEditorStatus == nil {
		return nil
	}
	report, err := deps.probeEditorStatus(ctx, connection)
	if err != nil || report.State != statusStateBlockedByDialog || len(report.Dialogs) == 0 {
		return nil
	}
	return report.Dialogs
}

func probeEditorStatusForDialogs(ctx context.Context, connection unityipc.Connection) (statusReport, error) {
	return probeEditorStatus(ctx, connection, defaultStatusCommandDeps())
}
