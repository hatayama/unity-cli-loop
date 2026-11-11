//go:build windows

package projectrunner

import (
	"context"
	"errors"
	"unsafe"

	"golang.org/x/sys/windows"
)

const (
	win32MessageCommand      = 0x0111 // WM_COMMAND
	win32NotificationClicked = 0      // BN_CLICKED
)

// pressWin32WindowButton tells a dialog that its button was clicked, which is what the button
// itself tells the dialog when the user clicks it.
// Why a message to the dialog and not a click on the button: the dialog acts on the message
// without being the active window. A real Editor closed its message box this way, and returned
// the pressed button to the script, while another application was in front.
// Why the dialog is checked first: it may have closed since its windows were read, and a handle
// that no longer names a visible window must not be sent a press.
// Why a dialog that does not answer is an error: the click may or may not have arrived, and the
// caller must not report a press it cannot vouch for.
func pressWin32WindowButton(ctx context.Context, target win32PressTargetResult) error {
	api := newWin32WindowAPI()
	dialog := windows.HWND(target.DialogHandle)
	if !api.isWindow(dialog) || !windows.IsWindowVisible(dialog) {
		return errors.New("the dialog closed before its button was pressed")
	}
	if !api.sendButtonClicked(ctx, dialog, target.ButtonID, windows.HWND(target.ButtonHandle)) {
		return errors.New("the dialog did not answer the click, so the button may or may not have been pressed")
	}
	return nil
}

// sendButtonClicked sends the dialog the notification a clicked button sends, and waits no longer
// than the press has left. It returns false when the dialog did not answer.
// Why not sendMessage: its last argument is a buffer that must stay a pointer, and this one is a
// window handle, which is a number that must not become a pointer.
func (api win32WindowAPI) sendButtonClicked(
	ctx context.Context,
	dialog windows.HWND,
	buttonID int,
	button windows.HWND,
) bool {
	timeout, hasTime := win32SendTimeout(ctx)
	if !hasTime {
		return false
	}
	// The control ID is the low word and the notification the high word of the first argument.
	wparam := uintptr(buttonID&0xFFFF) | uintptr(win32NotificationClicked)<<16
	var result uintptr
	sent, _, _ := api.sendMessageTimeout.Call(
		uintptr(dialog),
		win32MessageCommand,
		wparam,
		uintptr(button),
		win32SendAbortIfHung,
		uintptr(timeout.Milliseconds()),
		uintptr(unsafe.Pointer(&result)),
	)
	return sent != 0
}

func (api win32WindowAPI) isWindow(handle windows.HWND) bool {
	exists, _, _ := api.isWindowProc.Call(uintptr(handle))
	return exists != 0
}
