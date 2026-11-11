package projectrunner

import (
	"context"
	"fmt"
)

// pressWin32Button presses the button labeled exactly label in the frontmost modal window of the
// Unity process on Windows, and returns that dialog as it was read before the press.
// Why a failed listing is a press that did not start: nothing has been sent to the dialog yet.
// Why a failed press is not: the click may have reached the dialog even when its answer did not
// come back, so pressing the same label again could press a button of the dialog that opened next.
func (inspector dialogInspector) pressWin32Button(ctx context.Context, pid int, label string) (dialogPressResult, error) {
	pressCtx, cancel := context.WithTimeout(ctx, inspector.timeout)
	defer cancel()
	windows, err := inspector.listWindows(pressCtx, pid)
	// Why before err: a stopped listing returns the context's error, which hides why it stopped.
	if stopErr := inspector.stoppedLookError(ctx, pressCtx, "reading Unity's windows did not finish"); stopErr != nil {
		return dialogPressResult{}, dialogPressNotStartedError{err: stopErr}
	}
	if err != nil {
		return dialogPressResult{}, dialogPressNotStartedError{err: fmt.Errorf("could not read Unity's windows: %w", err)}
	}

	target := win32PressTarget(windows, label)
	result := dialogPressResult{DialogFound: target.DialogFound, Dialog: target.Dialog}
	if !target.ButtonFound {
		return result, nil
	}
	if err := inspector.pressWindowButton(pressCtx, target); err != nil {
		if stopErr := inspector.stoppedLookError(ctx, pressCtx, "the dialog did not answer the press"); stopErr != nil {
			return dialogPressResult{}, stopErr
		}
		return dialogPressResult{}, fmt.Errorf("could not press the button: %w", err)
	}
	result.Pressed = true
	return result, nil
}

// win32PressTargetResult is what choosing the button to press found in the Unity process's
// windows. DialogFound is false when no modal window is open; ButtonFound is false when the
// frontmost one has no button with the label. Dialog is the frontmost dialog as the listing shows
// it. The handles and the control ID say where the press goes; they are zero without a button.
type win32PressTargetResult struct {
	DialogFound  bool
	ButtonFound  bool
	Dialog       statusDialog
	DialogHandle uintptr
	ButtonHandle uintptr
	ButtonID     int
}

// win32PressTarget chooses the button of the frontmost modal window whose label equals label.
// Why only the first dialog: it is the one the listing shows first and the one that takes input,
// and a button with the same label in a dialog behind it answers a different question.
// Why the label must match exactly, without the mnemonic marker the listing also removes: a press
// cannot be undone, so a label that is only close must press nothing.
// Why the handles are returned: the press names the button by its handle, so a dialog that closed
// after this reading cannot turn the press into one of another window's buttons.
func win32PressTarget(windows []win32Window, label string) win32PressTargetResult {
	modalWindows := win32ModalWindows(windows)
	if len(modalWindows) == 0 {
		return win32PressTargetResult{}
	}

	dialog := modalWindows[0]
	target := win32PressTargetResult{DialogFound: true, Dialog: dialog.dialog(), DialogHandle: dialog.Handle}
	for _, button := range win32PushButtons(dialog.Controls) {
		if withoutMnemonicMarker(button.Text) != label {
			continue
		}
		target.ButtonFound = true
		target.ButtonHandle = button.Handle
		target.ButtonID = button.ID
		return target
	}
	return target
}
