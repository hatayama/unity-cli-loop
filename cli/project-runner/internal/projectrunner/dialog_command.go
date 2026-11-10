package projectrunner

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"slices"
	"strings"
	"time"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

const dialogAnswerOption = "--answer"

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

// dialogPressResult is what one press attempt saw. DialogFound is false when no modal dialog was
// open; Pressed is false when Dialog has no button with the label. Dialog is read before the press.
type dialogPressResult struct {
	DialogFound bool
	Pressed     bool
	Dialog      statusDialog
}

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
	answerFailedHint := ""
	if inspector.goos == "darwin" {
		answerFailedHint = inspector.checkFailedNextAction()
	}
	return dialogCommandDeps{
		canPress:              inspector.canPress(),
		look:                  inspector.inspect,
		press:                 inspector.press,
		recheckDelay:          dialogRecheckDelay,
		recheckCount:          dialogRecheckCount,
		checkFailedNextAction: inspector.checkFailedNextAction(),
		answerFailedHint:      answerFailedHint,
	}
}

func runDialogCommand(
	ctx context.Context,
	connection unityipc.Connection,
	args []string,
	stdout io.Writer,
	stderr io.Writer,
) int {
	return runDialogCommandWithDeps(ctx, connection, args, stdout, stderr, defaultDialogCommandDeps())
}

// runDialogCommandWithDeps lists the modal dialogs open in Unity, or with --answer presses a button
// of the frontmost one. It talks only to the operating system, never to the Editor's server, which
// cannot answer while a dialog holds the main thread.
func runDialogCommandWithDeps(
	ctx context.Context,
	connection unityipc.Connection,
	args []string,
	stdout io.Writer,
	stderr io.Writer,
	deps dialogCommandDeps,
) int {
	errorContext := clierrors.ErrorContext{ProjectRoot: connection.ProjectRoot, Command: clicore.DialogCommandName}
	label, hasAnswer, argErr := parseDialogArgs(args)
	if argErr != nil {
		clierrors.WriteClassifiedError(stderr, argErr, errorContext)
		return 1
	}
	if !hasAnswer {
		return listDialogs(ctx, connection.ProjectRoot, stdout, stderr, deps)
	}
	return answerDialog(ctx, connection.ProjectRoot, label, stdout, stderr, deps)
}

// parseDialogArgs accepts --answer <label> or --answer=<label> at most once, with a non-empty label.
func parseDialogArgs(args []string) (string, bool, *clierrors.ArgumentError) {
	label := ""
	hasAnswer := false
	for index := 0; index < len(args); index++ {
		arg := args[index]
		value := ""
		switch {
		case arg == dialogAnswerOption:
			if index+1 >= len(args) {
				return "", false, dialogArgumentError(dialogAnswerOption+" requires a button label", arg)
			}
			index++
			value = args[index]
		case strings.HasPrefix(arg, dialogAnswerOption+"="):
			value = strings.TrimPrefix(arg, dialogAnswerOption+"=")
		default:
			return "", false, dialogArgumentError("dialog does not accept "+arg, arg)
		}
		if value == "" {
			return "", false, dialogArgumentError(dialogAnswerOption+" requires a button label", dialogAnswerOption)
		}
		// Why reject instead of taking the last: pressing a button cannot be undone, so an ambiguous
		// command must not pick one of its labels.
		if hasAnswer {
			return "", false, dialogArgumentError(dialogAnswerOption+" can be given only once", dialogAnswerOption)
		}
		label = value
		hasAnswer = true
	}
	return label, hasAnswer, nil
}

func dialogArgumentError(message string, option string) *clierrors.ArgumentError {
	return &clierrors.ArgumentError{
		Message: message,
		Option:  option,
		NextActions: []string{
			"Run `uloop dialog` to list the open dialogs, or `uloop dialog --answer \"<button>\"` to press one.",
		},
	}
}

func listDialogs(ctx context.Context, projectRoot string, stdout io.Writer, stderr io.Writer, deps dialogCommandDeps) int {
	dialogs, err := deps.look(ctx, projectRoot)
	if errors.Is(err, errDialogInspectionUnsupported) {
		writeDialogError(stderr, projectRoot, notAnswerableError(err.Error()))
		return 1
	}
	if err != nil {
		writeDialogError(stderr, projectRoot, clierrors.CLIError{
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
	return writeDialogOutput(stdout, stderr, projectRoot, dialogListOutput{Success: true, Dialogs: dialogs})
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
		writeDialogError(stderr, projectRoot, notAnswerableError("uloop can press a dialog button only on macOS."))
		return 1
	}
	result, err := deps.press(ctx, projectRoot, label)
	if err != nil {
		writeDialogError(stderr, projectRoot, answerFailedError("uloop could not press the button: "+err.Error(), deps))
		return 1
	}
	if failure, failed := unpressedDialogError(result, label); failed {
		writeDialogError(stderr, projectRoot, failure)
		return 1
	}
	next, closed := waitUntilDialogCloses(ctx, deps, projectRoot, result.Dialog)
	if !closed {
		writeDialogError(stderr, projectRoot, answerFailedError(
			fmt.Sprintf("uloop pressed %q, but the dialog %q is still open.", label, result.Dialog.Title), deps))
		return 1
	}
	return writeDialogOutput(stdout, stderr, projectRoot, dialogAnswerOutput{
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
			"The dialog %q has no button uloop can read, so uloop cannot press one.", result.Dialog.Title)), true
	}
	return clierrors.CLIError{
		ErrorCode:   clierrors.ErrorCodeDialogButtonNotFound,
		Message:     fmt.Sprintf("The dialog %q has no button labeled %q.", result.Dialog.Title, label),
		SafeToRetry: true,
		NextActions: []string{"Run uloop dialog --answer with one of the listed Buttons, spelled exactly."},
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

// answerFailedError is not safe to retry: the press may have landed, and pressing the same label
// again could press a button of the dialog that opened next.
func answerFailedError(message string, deps dialogCommandDeps) clierrors.CLIError {
	nextActions := []string{
		"Run uloop dialog to see whether the dialog is still open before pressing again.",
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

func writeDialogError(stderr io.Writer, projectRoot string, err clierrors.CLIError) {
	err.Phase = clierrors.ErrorPhaseExecution
	err.ProjectRoot = projectRoot
	err.Command = clicore.DialogCommandName
	clierrors.WriteErrorEnvelope(stderr, err)
}

func writeDialogOutput(stdout io.Writer, stderr io.Writer, projectRoot string, output any) int {
	payload, err := json.Marshal(output)
	if err != nil {
		clierrors.WriteClassifiedError(stderr, err,
			clierrors.ErrorContext{ProjectRoot: projectRoot, Command: clicore.DialogCommandName})
		return 1
	}
	clicore.WriteJSON(stdout, payload)
	return 0
}
