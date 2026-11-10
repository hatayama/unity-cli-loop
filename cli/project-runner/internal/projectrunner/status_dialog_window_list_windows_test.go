//go:build windows

package projectrunner

import (
	"context"
	"errors"
	"os"
	"reflect"
	"runtime"
	"testing"
	"unsafe"

	"golang.org/x/sys/windows"
)

const (
	testWindowStylePopup    = 0x80000000 // WS_POPUP
	testWindowStyleChild    = 0x40000000 // WS_CHILD
	testWindowStyleVisible  = 0x10000000 // WS_VISIBLE
	testWindowStyleCaption  = 0x00C00000 // WS_CAPTION
	testButtonStyleDefault  = 0x00000001 // BS_DEFPUSHBUTTON
	testButtonStyleCheckBox = 0x00000003 // BS_AUTOCHECKBOX
	// WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE: no taskbar button, and the test does not take the focus.
	testWindowExStyleUnobtrusive = 0x00000080 | 0x08000000
	// Far outside every monitor, so the test windows are never seen.
	testWindowOffscreen = -32000
)

// testWindowFactory creates real windows on the calling thread through user32.dll.
type testWindowFactory struct {
	t             *testing.T
	createWindow  *windows.LazyProc
	destroyWindow *windows.LazyProc
	enableWindow  *windows.LazyProc
}

func newTestWindowFactory(t *testing.T) testWindowFactory {
	t.Helper()
	dll := windows.NewLazySystemDLL("user32.dll")
	return testWindowFactory{
		t:             t,
		createWindow:  dll.NewProc("CreateWindowExW"),
		destroyWindow: dll.NewProc("DestroyWindow"),
		enableWindow:  dll.NewProc("EnableWindow"),
	}
}

// create makes a window of a system class. For a popup, parent is its owner; for a child, parent
// is the window it sits in and controlID is its control ID.
func (factory testWindowFactory) create(
	className string,
	text string,
	style uint32,
	parent windows.HWND,
	controlID int,
) windows.HWND {
	factory.t.Helper()
	classNamePointer, err := windows.UTF16PtrFromString(className)
	if err != nil {
		factory.t.Fatalf("class name %q: %v", className, err)
	}
	textPointer, err := windows.UTF16PtrFromString(text)
	if err != nil {
		factory.t.Fatalf("window text %q: %v", text, err)
	}
	exStyle := uintptr(testWindowExStyleUnobtrusive)
	if style&testWindowStyleChild != 0 {
		exStyle = 0
	}
	position := int32(testWindowOffscreen)
	handle, _, callErr := factory.createWindow.Call(
		exStyle,
		uintptr(unsafe.Pointer(classNamePointer)),
		uintptr(unsafe.Pointer(textPointer)),
		uintptr(style),
		uintptr(position), uintptr(position), 200, 100,
		uintptr(parent),
		uintptr(controlID),
		0, 0,
	)
	if handle == 0 {
		factory.t.Fatalf("CreateWindowExW(%q, %q) failed: %v", className, text, callErr)
	}
	return windows.HWND(handle)
}

func (factory testWindowFactory) destroy(handle windows.HWND) {
	_, _, _ = factory.destroyWindow.Call(uintptr(handle))
}

func (factory testWindowFactory) setEnabled(handle windows.HWND, enabled bool) {
	flag := uintptr(0)
	if enabled {
		flag = 1
	}
	_, _, _ = factory.enableWindow.Call(uintptr(handle), flag)
}

// Verifies the Win32 listing reads a real dialog window of this process: it is not a dialog
// while its owner is enabled, and once the owner is disabled it is reported with its title, the
// text of its Edit control (which only a message returns), and its push button but not its check box.
func TestListUnityProcessWindowsReadsARealModalWindow(t *testing.T) {
	// A window belongs to the thread that created it, and the Edit answers the text message on
	// that thread, so creating and listing must stay on one thread.
	runtime.LockOSThread()
	defer runtime.UnlockOSThread()

	factory := newTestWindowFactory(t)
	owner := factory.create("Static", "uloop test owner", testWindowStylePopup|testWindowStyleVisible, 0, 0)
	defer factory.destroy(owner)
	dialog := factory.create(
		"#32770", "uloop test dialog", testWindowStylePopup|testWindowStyleVisible|testWindowStyleCaption, owner, 0)
	defer factory.destroy(dialog)
	factory.create("Edit", "Line one\r\nLine two", testWindowStyleChild|testWindowStyleVisible, dialog, 1019)
	factory.create("Button", "&Later", testWindowStyleChild|testWindowStyleVisible, dialog, 3)
	factory.create("Button", "&Remember", testWindowStyleChild|testWindowStyleVisible|testButtonStyleCheckBox, dialog, 4)
	factory.create("Button", "&Yes", testWindowStyleChild|testWindowStyleVisible|testButtonStyleDefault, dialog, 1)

	whileOwnerIsEnabled := listTestProcessDialogs(t)
	if len(whileOwnerIsEnabled) != 0 {
		t.Fatalf("dialogs while the owner is enabled = %#v, want none", whileOwnerIsEnabled)
	}

	factory.setEnabled(owner, false)
	whileOwnerIsDisabled := listTestProcessDialogs(t)

	want := []statusDialog{{Title: "uloop test dialog", Message: "Line one\nLine two", Buttons: []string{"Yes", "Later"}}}
	if !reflect.DeepEqual(whileOwnerIsDisabled, want) {
		t.Fatalf("dialogs while the owner is disabled = %#v, want %#v", whileOwnerIsDisabled, want)
	}
}

// Verifies a listing whose context is already cancelled stops with the context's error instead
// of returning windows.
func TestListUnityProcessWindowsStopsWhenCancelled(t *testing.T) {
	runtime.LockOSThread()
	defer runtime.UnlockOSThread()

	factory := newTestWindowFactory(t)
	window := factory.create("Static", "uloop test window", testWindowStylePopup|testWindowStyleVisible, 0, 0)
	defer factory.destroy(window)
	ctx, cancel := context.WithCancel(context.Background())
	cancel()

	windowsListed, err := listUnityProcessWindows(ctx, os.Getpid())

	if !errors.Is(err, context.Canceled) || windowsListed != nil {
		t.Fatalf("listUnityProcessWindows = %#v, %v; want context.Canceled and no windows", windowsListed, err)
	}
}

func listTestProcessDialogs(t *testing.T) []statusDialog {
	t.Helper()
	listed, err := listUnityProcessWindows(context.Background(), os.Getpid())
	if err != nil {
		t.Fatalf("listUnityProcessWindows returned error: %v", err)
	}
	return dialogsFromWin32Windows(listed)
}

// loadProgressBarClass makes the progress bar window class available to this process. The class
// lives in comctl32.dll, which registers it when it is loaded.
func (factory testWindowFactory) loadProgressBarClass() {
	factory.t.Helper()
	initCommonControls := windows.NewLazySystemDLL("comctl32.dll").NewProc("InitCommonControls")
	if err := initCommonControls.Find(); err != nil {
		factory.t.Fatalf("comctl32.dll could not be loaded: %v", err)
	}
	_, _, _ = initCommonControls.Call()
}

// Verifies the Win32 listing reports a real dialog window that has no owner while it holds a push
// button, as a prompt shown while the Editor starts does, and stops reporting it once it also
// holds a progress bar, as a progress window with a Cancel button does.
func TestListUnityProcessWindowsTellsAMessageBoxWithoutAnOwnerFromAProgressWindow(t *testing.T) {
	runtime.LockOSThread()
	defer runtime.UnlockOSThread()

	factory := newTestWindowFactory(t)
	dialog := factory.create(
		"#32770", "uloop test prompt", testWindowStylePopup|testWindowStyleVisible|testWindowStyleCaption, 0, 0)
	defer factory.destroy(dialog)
	factory.create("Edit", "Prompt body", testWindowStyleChild|testWindowStyleVisible, dialog, 1019)
	factory.create("Button", "&Quit", testWindowStyleChild|testWindowStyleVisible, dialog, 2)

	asMessageBox := listTestProcessDialogs(t)

	want := []statusDialog{{Title: "uloop test prompt", Message: "Prompt body", Buttons: []string{"Quit"}}}
	if !reflect.DeepEqual(asMessageBox, want) {
		t.Fatalf("dialogs without a progress bar = %#v, want %#v", asMessageBox, want)
	}

	factory.loadProgressBarClass()
	factory.create("msctls_progress32", "", testWindowStyleChild|testWindowStyleVisible, dialog, 1031)
	asProgressWindow := listTestProcessDialogs(t)

	if len(asProgressWindow) != 0 {
		t.Fatalf("dialogs with a progress bar = %#v, want none", asProgressWindow)
	}
}

// Verifies the first listing a goroutine makes returns every window the process has. The
// goroutine's stack grows while Windows calls back into Go, and a listing that handed Windows a
// pointer into that stack lost the windows collected after the stack moved.
func TestListUnityProcessWindowsKeepsItsWindowsWhenTheStackGrows(t *testing.T) {
	runtime.LockOSThread()
	defer runtime.UnlockOSThread()

	const windowCount = 64
	factory := newTestWindowFactory(t)
	for index := 0; index < windowCount; index++ {
		window := factory.create("Static", "uloop test window", testWindowStylePopup, 0, 0)
		defer factory.destroy(window)
	}

	listed, err := listUnityProcessWindows(context.Background(), os.Getpid())

	if err != nil || len(listed) < windowCount {
		t.Fatalf("listed %d windows, %v; want at least the %d created and no error", len(listed), err, windowCount)
	}
}
