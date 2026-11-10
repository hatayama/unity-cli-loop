//go:build windows

package projectrunner

import (
	"context"
	"fmt"
	"runtime"
	"sync"
	"time"
	"unsafe"

	"golang.org/x/sys/windows"
)

const (
	win32GetWindowOwner       = 4      // GW_OWNER
	win32WindowLongStyle      = -16    // GWL_STYLE
	win32MessageGetText       = 0x000D // WM_GETTEXT
	win32MessageGetTextLength = 0x000E // WM_GETTEXTLENGTH
	win32SendAbortIfHung      = 0x0002 // SMTO_ABORTIFHUNG

	// The longest a window class name can be, plus the terminating zero.
	win32ClassNameCapacity = 257

	// How long one control may take to answer with its text. A modal dialog runs a message loop,
	// so it answers at once; the limit only bounds a window whose thread has stopped.
	win32ControlTextTimeout = time.Second
)

// win32WindowAPI wraps the user32.dll procs that golang.org/x/sys/windows does not expose.
type win32WindowAPI struct {
	getWindow            *windows.LazyProc
	getWindowLong        *windows.LazyProc
	getWindowText        *windows.LazyProc
	getWindowTextLength  *windows.LazyProc
	getDialogControlID   *windows.LazyProc
	isWindowEnabled      *windows.LazyProc
	sendMessageTimeout   *windows.LazyProc
	collectWindowHandles uintptr
}

// win32WindowSearch collects window handles during one EnumWindows or EnumChildWindows call.
// A zero processID collects every window the call visits.
type win32WindowSearch struct {
	processID uint32
	handles   []windows.HWND
}

// newPinnedWin32WindowSearch returns a search that stays where it is until release is called.
// Why pinned: Windows holds the search's address as a plain number while the callback runs Go
// code, and Go does not update such a number when it moves the goroutine's stack, which is where
// a search that is not pinned lives. A listing lost the windows collected after its stack grew.
func newPinnedWin32WindowSearch(processID uint32) (*win32WindowSearch, func()) {
	search := &win32WindowSearch{processID: processID}
	pinner := &runtime.Pinner{}
	pinner.Pin(search)
	return search, pinner.Unpin
}

var (
	win32CollectCallbackOnce sync.Once
	win32CollectCallback     uintptr
)

func newWin32WindowAPI() win32WindowAPI {
	// Why once: a callback made by NewCallback is never released, and their number is limited.
	win32CollectCallbackOnce.Do(func() {
		win32CollectCallback = windows.NewCallback(collectWin32Window)
	})
	dll := windows.NewLazySystemDLL("user32.dll")
	return win32WindowAPI{
		getWindow:            dll.NewProc("GetWindow"),
		getWindowLong:        dll.NewProc("GetWindowLongW"),
		getWindowText:        dll.NewProc("GetWindowTextW"),
		getWindowTextLength:  dll.NewProc("GetWindowTextLengthW"),
		getDialogControlID:   dll.NewProc("GetDlgCtrlID"),
		isWindowEnabled:      dll.NewProc("IsWindowEnabled"),
		sendMessageTimeout:   dll.NewProc("SendMessageTimeoutW"),
		collectWindowHandles: win32CollectCallback,
	}
}

// collectWin32Window is the EnumWindows and EnumChildWindows callback. It returns 1 to keep the
// enumeration going.
func collectWin32Window(handle windows.HWND, lparam unsafe.Pointer) uintptr {
	search := (*win32WindowSearch)(lparam)
	if search.processID != 0 {
		var processID uint32
		_, _ = windows.GetWindowThreadProcessId(handle, &processID)
		if processID != search.processID {
			return 1
		}
	}
	search.handles = append(search.handles, handle)
	return 1
}

// listUnityProcessWindows returns the top-level windows of the process, in the order Windows
// enumerates them (front to back). It reads the controls only of a window that may be modal, so
// the Editor's other windows are never sent a message.
func listUnityProcessWindows(ctx context.Context, pid int) ([]win32Window, error) {
	api := newWin32WindowAPI()
	search, release := newPinnedWin32WindowSearch(uint32(pid))
	defer release()
	err := windows.EnumWindows(api.collectWindowHandles, unsafe.Pointer(search))
	if err != nil {
		return nil, fmt.Errorf("EnumWindows failed: %w", err)
	}

	result := make([]win32Window, 0, len(search.handles))
	for _, handle := range search.handles {
		if err := ctx.Err(); err != nil {
			return nil, err
		}
		window := api.describeWindow(handle)
		if window.mayBeModal() {
			window.Controls = api.controls(ctx, handle)
		}
		result = append(result, window)
	}
	return result, nil
}

func (api win32WindowAPI) describeWindow(handle windows.HWND) win32Window {
	owner := api.owner(handle)
	window := win32Window{
		ClassName: win32ClassName(handle),
		Title:     api.windowText(handle),
		Visible:   windows.IsWindowVisible(handle),
		Enabled:   api.isEnabled(handle),
		HasOwner:  owner != 0,
	}
	if owner != 0 {
		window.OwnerEnabled = api.isEnabled(owner)
	}
	return window
}

// controls returns the child windows of a window with their texts.
func (api win32WindowAPI) controls(ctx context.Context, parent windows.HWND) []win32Control {
	search, release := newPinnedWin32WindowSearch(0)
	defer release()
	windows.EnumChildWindows(parent, api.collectWindowHandles, unsafe.Pointer(search))

	controls := make([]win32Control, 0, len(search.handles))
	for _, handle := range search.handles {
		control := win32Control{
			ClassName: win32ClassName(handle),
			ID:        api.controlID(handle),
			Style:     api.style(handle),
			Visible:   windows.IsWindowVisible(handle),
		}
		control.Text = api.controlText(ctx, handle, control)
		controls = append(controls, control)
	}
	return controls
}

// controlText reads what a control shows.
// Why an Edit is asked with a message: GetWindowText does not read the text of an edit control in
// another process, and Unity's message box keeps its message in one. It returned the unrelated
// word "Static" there in a real Editor.
// Why the others are not: GetWindowText reads a label or a button from the window itself, without
// waiting for the other process.
func (api win32WindowAPI) controlText(ctx context.Context, handle windows.HWND, control win32Control) string {
	if !control.hasClass(win32EditClassName) {
		return api.windowText(handle)
	}
	return api.messageText(ctx, handle)
}

func win32ClassName(handle windows.HWND) string {
	buffer := make([]uint16, win32ClassNameCapacity)
	length, err := windows.GetClassName(handle, &buffer[0], int32(len(buffer)))
	if err != nil {
		return ""
	}
	return windows.UTF16ToString(buffer[:length])
}

func (api win32WindowAPI) windowText(handle windows.HWND) string {
	length, _, _ := api.getWindowTextLength.Call(uintptr(handle))
	if length == 0 {
		return ""
	}
	buffer := make([]uint16, length+1)
	copied, _, _ := api.getWindowText.Call(uintptr(handle), uintptr(unsafe.Pointer(&buffer[0])), uintptr(len(buffer)))
	return windows.UTF16ToString(buffer[:copied])
}

// messageText asks the control itself for its text and gives up, with an empty text, when the
// control does not answer in time or the look has run out of time.
func (api win32WindowAPI) messageText(ctx context.Context, handle windows.HWND) string {
	length, answered := api.sendMessage(ctx, handle, win32MessageGetTextLength, 0, nil)
	if !answered || length == 0 {
		return ""
	}
	buffer := make([]uint16, length+1)
	copied, answered := api.sendMessage(
		ctx, handle, win32MessageGetText, uintptr(len(buffer)), unsafe.Pointer(&buffer[0]))
	if !answered || copied > uintptr(len(buffer)) {
		return ""
	}
	return windows.UTF16ToString(buffer[:copied])
}

// sendMessage sends one message and waits no longer than the look has left. The second result is
// false when the window did not answer.
// Why lparam is a pointer and not a uintptr: the buffer must stay a pointer until the call itself,
// or the runtime may move it first.
func (api win32WindowAPI) sendMessage(
	ctx context.Context,
	handle windows.HWND,
	message uint32,
	wparam uintptr,
	lparam unsafe.Pointer,
) (uintptr, bool) {
	timeout := win32ControlTextTimeout
	if deadline, hasDeadline := ctx.Deadline(); hasDeadline && time.Until(deadline) < timeout {
		timeout = time.Until(deadline)
	}
	if ctx.Err() != nil || timeout < time.Millisecond {
		return 0, false
	}
	var result uintptr
	sent, _, _ := api.sendMessageTimeout.Call(
		uintptr(handle),
		uintptr(message),
		wparam,
		uintptr(lparam),
		win32SendAbortIfHung,
		uintptr(timeout.Milliseconds()),
		uintptr(unsafe.Pointer(&result)),
	)
	return result, sent != 0
}

func (api win32WindowAPI) owner(handle windows.HWND) windows.HWND {
	owner, _, _ := api.getWindow.Call(uintptr(handle), win32GetWindowOwner)
	return windows.HWND(owner)
}

func (api win32WindowAPI) isEnabled(handle windows.HWND) bool {
	enabled, _, _ := api.isWindowEnabled.Call(uintptr(handle))
	return enabled != 0
}

func (api win32WindowAPI) controlID(handle windows.HWND) int {
	id, _, _ := api.getDialogControlID.Call(uintptr(handle))
	return int(int32(id))
}

func (api win32WindowAPI) style(handle windows.HWND) uint32 {
	// Why the cast through int32: the index is negative, and a negative constant does not convert to uintptr.
	index := int32(win32WindowLongStyle)
	style, _, _ := api.getWindowLong.Call(uintptr(handle), uintptr(index))
	return uint32(style)
}
