//go:build windows

package projectrunner

import (
	"context"
	"reflect"
	"runtime"
	"testing"

	"golang.org/x/sys/windows"
)

const testWindowLongProcedure = -4 // GWLP_WNDPROC

// testCommandMessage is one WM_COMMAND a test window was sent.
type testCommandMessage struct {
	wparam uintptr
	lparam uintptr
}

// recordCommands replaces the window's procedure with one that records every WM_COMMAND the
// window is sent and hands every other message to the procedure it had.
func (factory testWindowFactory) recordCommands(window windows.HWND) *[]testCommandMessage {
	factory.t.Helper()
	dll := windows.NewLazySystemDLL("user32.dll")
	setWindowLong := dll.NewProc("SetWindowLongPtrW")
	callWindowProcedure := dll.NewProc("CallWindowProcW")

	messages := &[]testCommandMessage{}
	var original uintptr
	procedure := windows.NewCallback(func(handle uintptr, message uintptr, wparam uintptr, lparam uintptr) uintptr {
		if message == win32MessageCommand {
			*messages = append(*messages, testCommandMessage{wparam: wparam, lparam: lparam})
			return 0
		}
		result, _, _ := callWindowProcedure.Call(original, handle, message, wparam, lparam)
		return result
	})
	// Why the cast through int32: the index is negative, and a negative constant does not convert to uintptr.
	index := int32(testWindowLongProcedure)
	original, _, callErr := setWindowLong.Call(uintptr(window), uintptr(index), procedure)
	if original == 0 {
		factory.t.Fatalf("SetWindowLongPtrW failed: %v", callErr)
	}
	return messages
}

// createDialogWithButton makes a dialog window that records its WM_COMMAND messages and one
// button in it, and returns the press target that names them. The caller destroys the dialog on
// the thread that created it.
func (factory testWindowFactory) createDialogWithButton(
	dialogStyle uint32,
	buttonID int,
) (win32PressTargetResult, *[]testCommandMessage) {
	factory.t.Helper()
	dialog := factory.create("#32770", "uloop test dialog", dialogStyle, 0, 0)
	button := factory.create("Button", "&Later", testWindowStyleChild|testWindowStyleVisible, dialog, buttonID)
	target := win32PressTargetResult{
		DialogFound:  true,
		ButtonFound:  true,
		DialogHandle: uintptr(dialog),
		ButtonHandle: uintptr(button),
		ButtonID:     buttonID,
	}
	return target, factory.recordCommands(dialog)
}

const testShownDialogStyle = testWindowStylePopup | testWindowStyleVisible | testWindowStyleCaption

// Verifies the press sends a real dialog window exactly one WM_COMMAND that carries the button's
// control ID with the clicked notification and the button's handle, as a clicked button does.
func TestPressWin32WindowButtonSendsTheClickToTheDialog(t *testing.T) {
	// A window answers a message on the thread that created it, so creating and pressing must stay
	// on one thread.
	runtime.LockOSThread()
	defer runtime.UnlockOSThread()

	factory := newTestWindowFactory(t)
	target, messages := factory.createDialogWithButton(testShownDialogStyle, 3)
	defer factory.destroy(windows.HWND(target.DialogHandle))

	err := pressWin32WindowButton(context.Background(), target)
	if err != nil {
		t.Fatalf("pressWin32WindowButton returned error: %v", err)
	}
	want := []testCommandMessage{{wparam: 3, lparam: target.ButtonHandle}}
	if !reflect.DeepEqual(*messages, want) {
		t.Fatalf("messages = %#v, want %#v", *messages, want)
	}
}

// Verifies a dialog that is no longer shown is sent nothing, and the failure says no click was sent.
func TestPressWin32WindowButtonPressesNothingInAHiddenDialog(t *testing.T) {
	runtime.LockOSThread()
	defer runtime.UnlockOSThread()

	factory := newTestWindowFactory(t)
	target, messages := factory.createDialogWithButton(testWindowStylePopup|testWindowStyleCaption, 3)
	defer factory.destroy(windows.HWND(target.DialogHandle))

	err := pressWin32WindowButton(context.Background(), target)

	assertPressNotStarted(t, err, true)
	if len(*messages) != 0 {
		t.Fatalf("messages = %#v, want none", *messages)
	}
}

// Verifies a dialog whose window no longer exists makes the press fail as one that sent no click,
// instead of reporting a press.
func TestPressWin32WindowButtonFailsForADestroyedDialog(t *testing.T) {
	runtime.LockOSThread()
	defer runtime.UnlockOSThread()

	factory := newTestWindowFactory(t)
	target, _ := factory.createDialogWithButton(testShownDialogStyle, 3)
	factory.destroy(windows.HWND(target.DialogHandle))

	err := pressWin32WindowButton(context.Background(), target)

	assertPressNotStarted(t, err, true)
}

// Verifies a press whose context has already ended sends the dialog nothing and fails as one that
// sent no click, instead of reporting a press.
func TestPressWin32WindowButtonPressesNothingWhenItHasNoTimeLeft(t *testing.T) {
	runtime.LockOSThread()
	defer runtime.UnlockOSThread()

	factory := newTestWindowFactory(t)
	target, messages := factory.createDialogWithButton(testShownDialogStyle, 3)
	defer factory.destroy(windows.HWND(target.DialogHandle))
	ctx, cancel := context.WithCancel(context.Background())
	cancel()

	err := pressWin32WindowButton(ctx, target)

	assertPressNotStarted(t, err, true)
	if len(*messages) != 0 {
		t.Fatalf("messages = %#v, want none", *messages)
	}
}

// Verifies what the answer to a click means: an answered click is a press, a click Windows refused
// to deliver was never sent, and any other unanswered click may have reached the dialog.
func TestWin32ClickErrorTellsAnUnsentClickFromAnUnansweredOne(t *testing.T) {
	if err := win32ClickError(1, windows.ERROR_SUCCESS); err != nil {
		t.Fatalf("answered click returned error: %v", err)
	}
	assertPressNotStarted(t, win32ClickError(0, windows.ERROR_ACCESS_DENIED), true)
	assertPressNotStarted(t, win32ClickError(0, windows.ERROR_TIMEOUT), false)
	assertPressNotStarted(t, win32ClickError(0, windows.ERROR_SUCCESS), false)
}
