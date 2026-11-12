package projectrunner

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"runtime"
	"slices"
	"strings"
	"time"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
	"github.com/hatayama/unity-cli-loop/common/unityprocess/unitydialog"
)

const dialogLabelOption = "--label"

// Long enough for a dialog to finish closing after the press, short enough that five rechecks
// still answer within a couple of seconds.
const dialogRecheckDelay = 300 * time.Millisecond

const dialogRecheckCount = 5

// dialogCommandDeps holds what tests replace.
type dialogCommandDeps struct {
	// Whether uloop can press a dialog button on this operating system.
	canPress bool
	// Lists the modal dialogs open in this project's Editor.
	look func(ctx context.Context, projectRoot string) ([]statusDialog, error)
	// Reads the frontmost modal dialog and presses its button with exactly this label.
	press func(ctx context.Context, projectRoot string, label string) (dialogPressResult, error)
	// How long to wait before each look that checks the pressed dialog closed, and how many looks.
	recheckDelay time.Duration
	recheckCount int
	// The hint for a failed look; it differs by operating system.
	checkFailedNextAction string
	// Added to a failed press where a missing permission can be the cause; empty elsewhere.
	answerFailedHint string
}

// dialogPressResult is what one press attempt saw.
type dialogPressResult = unitydialog.PressResult

type dialogListOutput struct {
	Success bool           `json:"Success"`
	Dialogs []statusDialog `json:"Dialogs"`
}

type dialogAnswerOutput struct {
	Success     bool           `json:"Success"`
	Pressed     string         `json:"Pressed"`
	Title       string         `json:"Title"`
	Message     string         `json:"Message"`
	Buttons     []string       `json:"Buttons"`
	NextDialogs []statusDialog `json:"NextDialogs,omitempty"`
}

func defaultDialogCommandDeps() dialogCommandDeps {
	inspector := defaultDialogInspector()
	return dialogCommandDeps{
		canPress:              inspector.CanPress(),
		look:                  inspector.Inspect,
		press:                 inspector.Press,
		recheckDelay:          dialogRecheckDelay,
		recheckCount:          dialogRecheckCount,
		checkFailedNextAction: dialogCheckFailedNextActionFor(runtime.GOOS),
		answerFailedHint:      dialogAnswerFailedHintFor(runtime.GOOS),
	}
}

func runGetDialogsCommand(
	ctx context.Context,
	connection unityipc.Connection,
	args []string,
	stdout io.Writer,
	stderr io.Writer,
) int {
	return runGetDialogsCommandWithDeps(ctx, connection, args, stdout, stderr, defaultDialogCommandDeps())
}

// runGetDialogsCommandWithDeps lists the modal dialogs open in Unity. It talks only to the
// operating system, never to the Editor's server, which cannot answer while a dialog holds the
// main thread.
func runGetDialogsCommandWithDeps(
	ctx context.Context,
	connection unityipc.Connection,
	args []string,
	stdout io.Writer,
	stderr io.Writer,
	deps dialogCommandDeps,
) int {
	if len(args) > 0 {
		clierrors.WriteClassifiedError(stderr, &clierrors.ArgumentError{
			Message:     clicore.GetDialogsCommandName + " takes no options: " + args[0],
			Option:      args[0],
			NextActions: []string{"Run `uloop get-dialogs` with no options other than --project-path."},
		}, clierrors.ErrorContext{ProjectRoot: connection.ProjectRoot, Command: clicore.GetDialogsCommandName})
		return 1
	}
	return listDialogs(ctx, connection.ProjectRoot, stdout, stderr, deps)
}

func runPressDialogButtonCommand(
	ctx context.Context,
	connection unityipc.Connection,
	args []string,
	stdout io.Writer,
	stderr io.Writer,
) int {
	return runPressDialogButtonCommandWithDeps(ctx, connection, args, stdout, stderr, defaultDialogCommandDeps())
}

// runPressDialogButtonCommandWithDeps presses the button with the --label text in the frontmost
// modal dialog open in Unity. Like get-dialogs, it talks only to the operating system.
func runPressDialogButtonCommandWithDeps(
	ctx context.Context,
	connection unityipc.Connection,
	args []string,
	stdout io.Writer,
	stderr io.Writer,
	deps dialogCommandDeps,
) int {
	label, argErr := parsePressDialogButtonArgs(args)
	if argErr != nil {
		clierrors.WriteClassifiedError(stderr, argErr,
			clierrors.ErrorContext{ProjectRoot: connection.ProjectRoot, Command: clicore.PressDialogButtonCommandName})
		return 1
	}
	return answerDialog(ctx, connection.ProjectRoot, label, stdout, stderr, deps)
}

// parsePressDialogButtonArgs accepts exactly one --label <label> or --label=<label>, with a
// non-empty label, and nothing else.
func parsePressDialogButtonArgs(args []string) (string, *clierrors.ArgumentError) {
	label := ""
	hasLabel := false
	for index := 0; index < len(args); index++ {
		arg := args[index]
		value := ""
		switch {
		case arg == dialogLabelOption:
			if index+1 >= len(args) {
				return "", dialogArgumentError(dialogLabelOption+" requires a button label", arg)
			}
			index++
			value = args[index]
		case strings.HasPrefix(arg, dialogLabelOption+"="):
			value = strings.TrimPrefix(arg, dialogLabelOption+"=")
		default:
			return "", dialogArgumentError(clicore.PressDialogButtonCommandName+" does not accept "+arg, arg)
		}
		if value == "" {
			return "", dialogArgumentError(dialogLabelOption+" requires a button label", dialogLabelOption)
		}
		// Why reject instead of taking the last: pressing a button cannot be undone, so an ambiguous
		// command must not pick one of its labels.
		if hasLabel {
			return "", dialogArgumentError(dialogLabelOption+" can be given only once", dialogLabelOption)
		}
		label = value
		hasLabel = true
	}
	if !hasLabel {
		return "", dialogArgumentError(
			clicore.PressDialogButtonCommandName+" requires "+dialogLabelOption+" \"<button>\"", dialogLabelOption)
	}
	return label, nil
}

func dialogArgumentError(message string, option string) *clierrors.ArgumentError {
	return &clierrors.ArgumentError{
		Message: message,
		Option:  option,
		NextActions: []string{
			"Run `uloop get-dialogs` to list the open dialogs and their Buttons, then `uloop press-dialog-button --label \"<button>\"`.",
		},
	}
}

func listDialogs(ctx context.Context, projectRoot string, stdout io.Writer, stderr io.Writer, deps dialogCommandDeps) int {
	dialogs, err := deps.look(ctx, projectRoot)
	if errors.Is(err, unitydialog.ErrInspectionUnsupported) {
		writeDialogError(stderr, clicore.GetDialogsCommandName, projectRoot, notAnswerableError(err.Error()))
		return 1
	}
	if err != nil {
		writeDialogError(stderr, clicore.GetDialogsCommandName, projectRoot, clierrors.CLIError{
			ErrorCode:   clierrors.ErrorCodeDialogCheckFailed,
			Message:     "uloop could not look for dialogs in Unity's windows: " + err.Error(),
			Retryable:   true,
			SafeToRetry: true,
			NextActions: []string{deps.checkFailedNextAction},
		})
		return 1
	}
	if dialogs == nil {
		dialogs = []statusDialog{}
	}
	return writeDialogOutput(stdout, stderr, clicore.GetDialogsCommandName, projectRoot, dialogListOutput{Success: true, Dialogs: dialogs})
}

func answerDialog(
	ctx context.Context,
	projectRoot string,
	label string,
	stdout io.Writer,
	stderr io.Writer,
	deps dialogCommandDeps,
) int {
	if !deps.canPress {
		writeDialogError(stderr, clicore.PressDialogButtonCommandName, projectRoot, notAnswerableError("uloop can press a dialog button only on macOS and Windows."))
		return 1
	}
	result, err := deps.press(ctx, projectRoot, label)
	if err != nil {
		failure := answerFailedError("uloop could not press the button: "+err.Error(), deps)
		var notStarted unitydialog.PressNotStartedError
		// Why only here: once osascript started, the click may have landed even when it failed.
		failure.SafeToRetry = errors.As(err, &notStarted)
		writeDialogError(stderr, clicore.PressDialogButtonCommandName, projectRoot, failure)
		return 1
	}
	if failure, failed := unpressedDialogError(result, label); failed {
		writeDialogError(stderr, clicore.PressDialogButtonCommandName, projectRoot, failure)
		return 1
	}
	next, closed := waitUntilDialogCloses(ctx, deps, projectRoot, result.Dialog)
	if !closed {
		writeDialogError(stderr, clicore.PressDialogButtonCommandName, projectRoot, answerFailedError(
			fmt.Sprintf("uloop pressed %q, but the dialog %q is still open.", label, result.Dialog.Title), deps))
		return 1
	}
	return writeDialogOutput(stdout, stderr, clicore.PressDialogButtonCommandName, projectRoot, dialogAnswerOutput{
		Success:     true,
		Pressed:     label,
		Title:       result.Dialog.Title,
		Message:     result.Dialog.Message,
		Buttons:     result.Dialog.Buttons,
		NextDialogs: next,
	})
}

// unpressedDialogError explains why a press attempt pressed nothing; failed is false after a press.
func unpressedDialogError(result dialogPressResult, label string) (clierrors.CLIError, bool) {
	if !result.DialogFound {
		return clierrors.CLIError{
			ErrorCode:   clierrors.ErrorCodeDialogNotOpen,
			Message:     "No modal dialog is open in Unity.",
			SafeToRetry: true,
			NextActions: []string{"Run uloop status to see what Unity is doing."},
		}, true
	}
	if result.Pressed {
		return clierrors.CLIError{}, false
	}
	if len(result.Dialog.Buttons) == 0 {
		return notAnswerableError(fmt.Sprintf(
			"The dialog %q has no button uloop can press.", result.Dialog.Title)), true
	}
	return clierrors.CLIError{
		ErrorCode:   clierrors.ErrorCodeDialogButtonNotFound,
		Message:     fmt.Sprintf("The dialog %q has no button labeled %q.", result.Dialog.Title, label),
		SafeToRetry: true,
		NextActions: []string{"Run uloop press-dialog-button --label with one of the listed Buttons, spelled exactly."},
		Details:     map[string]any{"Buttons": result.Dialog.Buttons},
	}, true
}

// waitUntilDialogCloses looks again until the pressed dialog is gone. closed is false only when
// every look still showed it; a look that fails or is cancelled counts as closed, because the press
// itself has already happened. next holds the dialogs open after it closed, if any.
func waitUntilDialogCloses(
	ctx context.Context,
	deps dialogCommandDeps,
	projectRoot string,
	pressed statusDialog,
) ([]statusDialog, bool) {
	for attempt := 0; attempt < deps.recheckCount; attempt++ {
		if !waitForRecheck(ctx, deps.recheckDelay) {
			return nil, true
		}
		dialogs, err := deps.look(ctx, projectRoot)
		if err != nil {
			return nil, true
		}
		if len(dialogs) > 0 && sameDialog(dialogs[0], pressed) {
			continue
		}
		return dialogs, true
	}
	return nil, false
}

// waitForRecheck returns false when ctx ends before the delay passes.
func waitForRecheck(ctx context.Context, delay time.Duration) bool {
	timer := time.NewTimer(delay)
	defer timer.Stop()
	select {
	case <-ctx.Done():
		return false
	case <-timer.C:
		return true
	}
}

func sameDialog(left statusDialog, right statusDialog) bool {
	return left.Title == right.Title && left.Message == right.Message && slices.Equal(left.Buttons, right.Buttons)
}

func notAnswerableError(message string) clierrors.CLIError {
	return clierrors.CLIError{
		ErrorCode:   clierrors.ErrorCodeDialogNotAnswerable,
		Message:     message,
		SafeToRetry: true,
		NextActions: []string{"Ask the user to answer the dialog in Unity."},
	}
}

// answerFailedError is not safe to retry unless the caller knows no click happened: the press may
// have landed, and pressing the same label again could press a button of the dialog that opened next.
func answerFailedError(message string, deps dialogCommandDeps) clierrors.CLIError {
	nextActions := []string{
		"Run uloop get-dialogs to see whether the dialog is still open before pressing again.",
		"If it stays open, ask the user to answer it in Unity.",
	}
	if deps.answerFailedHint != "" {
		nextActions = append(nextActions, deps.answerFailedHint)
	}
	return clierrors.CLIError{
		ErrorCode:   clierrors.ErrorCodeDialogAnswerFailed,
		Message:     message,
		NextActions: nextActions,
	}
}

// writeDialogError writes err as the failure of command, the dialog command that ran.
func writeDialogError(stderr io.Writer, command string, projectRoot string, err clierrors.CLIError) {
	err.Phase = clierrors.ErrorPhaseExecution
	err.ProjectRoot = projectRoot
	err.Command = command
	clierrors.WriteErrorEnvelope(stderr, err)
}

func writeDialogOutput(stdout io.Writer, stderr io.Writer, command string, projectRoot string, output any) int {
	payload, err := json.Marshal(output)
	if err != nil {
		clierrors.WriteClassifiedError(stderr, err,
			clierrors.ErrorContext{ProjectRoot: projectRoot, Command: command})
		return 1
	}
	clicore.WriteJSON(stdout, payload)
	return 0
}
