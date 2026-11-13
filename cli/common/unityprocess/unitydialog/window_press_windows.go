//go:build windows

package unitydialog

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
// that no longer names a shown window must not be sent a press; a dialog hidden only with its
// minimized owner counts as shown, as it does in the listing. It may also have been disabled by a
// nested modal dialog opened over it, and WM_COMMAND would still make it act.
// Why the button is checked as well: it may have been closed, hidden, or disabled since the
// windows were read, and WM_COMMAND makes the dialog act even for a button that is disabled.
// Why a failure before the click is sent is a press that did not start: nothing reached the
// dialog, so the caller may press again.
func pressWin32WindowButton(ctx context.Context, target win32PressTargetResult) error {
	api := newWin32WindowAPI()
	dialog := windows.HWND(target.DialogHandle)
	if !api.isWindow(dialog) || !api.describeWindow(dialog).isShown() || !api.isEnabled(dialog) {
		return PressNotStartedError{
			Err: errors.New("the dialog closed, was hidden, or was disabled before its button was pressed"),
		}
	}
	button := windows.HWND(target.ButtonHandle)
	if !api.isWindow(button) || !isWin32StyleVisible(api.style(button)) || !api.isEnabled(button) {
		return PressNotStartedError{
			Err: errors.New("the button was closed, hidden, or disabled before it was pressed"),
		}
	}
	return api.sendButtonClicked(ctx, dialog, target.ButtonID, button)
}

// sendButtonClicked sends the dialog the notification a clicked button sends, and waits no longer
// than the press has left.
// Why not sendMessage: its last argument is a buffer that must stay a pointer, and this one is a
// window handle, which is a number that must not become a pointer.
func (api win32WindowAPI) sendButtonClicked(
	ctx context.Context,
	dialog windows.HWND,
	buttonID int,
	button windows.HWND,
) error {
	timeout, hasTime := win32SendTimeout(ctx)
	if !hasTime {
		return PressNotStartedError{Err: errors.New("no time was left to press the button")}
	}
	// The control ID is the low word and the notification the high word of the first argument.
	wparam := uintptr(buttonID&0xFFFF) | uintptr(win32NotificationClicked)<<16
	var result uintptr
	sent, _, callErr := api.sendMessageTimeout.Call(
		uintptr(dialog),
		win32MessageCommand,
		wparam,
		uintptr(button),
		win32SendAbortIfHung,
		uintptr(timeout.Milliseconds()),
		uintptr(unsafe.Pointer(&result)),
	)
	return win32ClickError(sent, callErr)
}

// win32ClickError says what the answer to a sent click means. sent is what SendMessageTimeout
// returned, and callErr the error Windows left for that call.
// Why access denied is a press that did not start: Windows does not deliver a message to a
// process that runs with higher privileges than the sender, for example an Editor started as
// administrator. A press sent from a lower integrity level to a real Editor returned at once
// with this error and left the dialog open.
// Why an invalid window handle is a press that did not start: the dialog closed before the click
// was sent, and Windows delivers no message to a window that no longer exists.
// Why any other unanswered click is not: the click may or may not have arrived, and the caller
// must not report a press, or the lack of one, that it cannot vouch for.
// Why callErr can be read as the result of this call alone: Go clears the thread's last error
// right before the call and reads it right after, so an older access denied cannot show up here.
func win32ClickError(sent uintptr, callErr error) error {
	if sent != 0 {
		return nil
	}
	if errors.Is(callErr, windows.ERROR_ACCESS_DENIED) {
		return PressNotStartedError{Err: errors.New(
			"the click was not delivered: Windows denied access, which happens when Unity runs with higher privileges than uloop",
		)}
	}
	if errors.Is(callErr, windows.ERROR_INVALID_WINDOW_HANDLE) {
		return PressNotStartedError{Err: errors.New("the dialog closed before its button was pressed")}
	}
	return errors.New("the dialog did not answer the click, so the button may or may not have been pressed")
}

func (api win32WindowAPI) isWindow(handle windows.HWND) bool {
	exists, _, _ := api.isWindowProc.Call(uintptr(handle))
	return exists != 0
}
