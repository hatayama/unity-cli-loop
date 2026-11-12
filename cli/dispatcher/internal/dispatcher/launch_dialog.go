package dispatcher

import (
	"context"
	"errors"
	"fmt"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
	"github.com/hatayama/unity-cli-loop/common/unityprocess"
	"github.com/hatayama/unity-cli-loop/common/unityprocess/unitydialog"
)

const launchDialogClosedNextAction = "After the dialog is closed, run uloop launch again to wait for the Editor."

// launchOldPackageNextAction follows the press guidance of launch only.
// Why launch needs it and the project runner's commands do not: launch runs in the dispatcher, which
// updates itself, while press-dialog-button comes from the project runner the project's Unity package
// pins, and a package from before the dialog commands pins a runner that does not have it. Why not
// decided from the pin: the first runner version with the dialog commands is not known before it is
// released.
const launchOldPackageNextAction = "If uloop answers press-dialog-button as an unknown command, this project's Unity " +
	"package predates the dialog commands: ask the user to press the button in the Unity window, and to update the package."

// launchDialogFinder is the dialog look a launch hands its readiness wait: it reads the windows of the
// Editor lookupProcess finds in projectRoot. A look that cannot find the Editor, or runs where uloop
// cannot read windows, fails, and the wait goes on as without it.
func launchDialogFinder(projectRoot string, lookupProcess unitydialog.ProcessLookup, deps launchDeps) clicore.DialogFinder {
	return func(ctx context.Context) ([]unitydialog.Dialog, error) {
		return deps.lookForDialogs(ctx, projectRoot, lookupProcess)
	}
}

// v3LaunchDialogFinder looks up the Editor by the project each time it looks.
// Why the definitive lookup: an Editor whose command line does not name its project may still be
// this project's, and the look would miss its dialog.
func v3LaunchDialogFinder(projectRoot string, deps launchDeps) clicore.DialogFinder {
	return launchDialogFinder(projectRoot, deps.findRunningUnityProcessDefinitively, deps)
}

// v2LaunchDialogLookout looks at the Editor whose process ID the V2 launch already knows.
func v2LaunchDialogLookout(projectRoot string, pid int, deps launchDeps) *clicore.DialogLookout {
	lookupProcess := func(context.Context, string) (*unityprocess.UnityProcess, error) {
		return &unityprocess.UnityProcess{Pid: pid}, nil
	}
	return clicore.NewDialogLookout(launchDialogFinder(projectRoot, lookupProcess, deps), deps.now)
}

// launchBlockedByDialogError reports that launch stopped waiting for an Editor that a modal dialog
// holds. The Editor goes on running.
type launchBlockedByDialogError struct {
	projectRoot string
	// dialogs is never empty, frontmost first.
	dialogs []unitydialog.Dialog
	// canPress is whether uloop press-dialog-button can answer the dialog for this project.
	canPress bool
	// restart is whether launch ran with --restart, which a second run repeats by quitting the Editor.
	restart bool
	// cause is the failure the wait saw last before it found the dialog; nil when it saw none.
	cause error
}

// asLaunchDialogError turns a wait that stopped at a dialog into launchBlockedByDialogError and
// returns any other err unchanged. canPress is false for a V2 project, whose commands go to the V2
// CLI, which has no dialog commands. restart is whether launch ran with --restart.
func asLaunchDialogError(err error, projectRoot string, canPress bool, restart bool) error {
	var blocked clicore.ToolReadinessBlockedByDialogError
	if !errors.As(err, &blocked) {
		return err
	}
	return launchBlockedByDialogError{
		projectRoot: projectRoot,
		dialogs:     blocked.Dialogs,
		canPress:    canPress,
		restart:     restart,
		cause:       blocked.Cause,
	}
}

// Error leaves out the dialog's text on purpose: the transport checks read words such as "EOF" in
// error text, so a dialog title would make this error read as a lost connection.
func (err launchBlockedByDialogError) Error() string {
	return "a modal dialog is open in Unity; uloop launch stopped waiting for the Editor"
}

// ToCLIError reports the dialogs the way uloop status does, with the last step pointing back at
// plain uloop launch.
// Why a restart is not safe to retry: running it again quits the Editor the dialog holds, and an
// Editor that cannot quit for the dialog is killed. A plain launch repeats nothing.
func (err launchBlockedByDialogError) ToCLIError(context clierrors.ErrorContext) clierrors.CLIError {
	details := map[string]any{"Dialogs": err.dialogs}
	if err.cause != nil {
		details["Cause"] = err.cause.Error()
	}
	return clierrors.CLIError{
		ErrorCode:   clierrors.ErrorCodeBlockedByDialog,
		Phase:       clierrors.ErrorPhaseConnection,
		Message:     launchBlockedByDialogMessage(err.dialogs),
		Retryable:   true,
		SafeToRetry: !err.restart,
		ProjectRoot: clicore.FirstNonEmpty(context.ProjectRoot, err.projectRoot),
		Command:     context.Command,
		NextActions: err.nextActions(),
		Details:     details,
	}
}

// launchBlockedByDialogMessage counts the dialogs as uloop status does: the number of them when
// several are open, and the title of a single one only when it has one.
func launchBlockedByDialogMessage(dialogs []unitydialog.Dialog) string {
	const stopped = " uloop launch stopped waiting; the Editor keeps running."
	if len(dialogs) > 1 {
		return fmt.Sprintf("Unity is running, but %d modal dialogs are open; Unity waits until they are closed.", len(dialogs)) + stopped
	}
	if dialogs[0].Title == "" {
		return "Unity is running, but a modal dialog is open; Unity waits until it is closed." + stopped
	}
	return fmt.Sprintf("Unity is running, but a modal dialog %q is open; Unity waits until it is closed.", dialogs[0].Title) + stopped
}

// nextActions are the steps uloop status gives for the dialogs, with the step for an old package
// right after the press guidance when there is one.
func (err launchBlockedByDialogError) nextActions() []string {
	steps := clicore.DialogAnswerNextActions(err.dialogs, err.canPress, launchDialogClosedNextAction)
	// Why the same condition DialogAnswerNextActions presses on: the step only makes sense after a press.
	if !err.canPress || len(err.dialogs[0].Buttons) == 0 {
		return steps
	}
	return append([]string{steps[0], launchOldPackageNextAction}, steps[1:]...)
}
