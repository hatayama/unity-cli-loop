//go:build windows

package unityprocess

import "golang.org/x/sys/windows"

const (
	SW_RESTORE      = 9
	GW_OWNER        = 4
	VK_MENU         = 0x12
	KEYEVENTF_KEYUP = 2
)

// user32API wraps user32.dll procs that golang.org/x/sys/windows does not expose.
type user32API struct {
	setForegroundWindow *windows.LazyProc
	showWindowAsync     *windows.LazyProc
	isIconic            *windows.LazyProc
	bringWindowToTop    *windows.LazyProc
	attachThreadInput   *windows.LazyProc
	getWindow           *windows.LazyProc
	keybdEvent          *windows.LazyProc
}

func newUser32API() *user32API {
	dll := windows.NewLazySystemDLL("user32.dll")
	return &user32API{
		setForegroundWindow: dll.NewProc("SetForegroundWindow"),
		showWindowAsync:     dll.NewProc("ShowWindowAsync"),
		isIconic:            dll.NewProc("IsIconic"),
		bringWindowToTop:    dll.NewProc("BringWindowToTop"),
		attachThreadInput:   dll.NewProc("AttachThreadInput"),
		getWindow:           dll.NewProc("GetWindow"),
		keybdEvent:          dll.NewProc("keybd_event"),
	}
}

var user32 = newUser32API()

func (api *user32API) SetForegroundWindow(hwnd windows.HWND) bool {
	ret, _, _ := api.setForegroundWindow.Call(uintptr(hwnd))
	return ret != 0
}

func (api *user32API) ShowWindowAsync(hwnd windows.HWND, cmdShow int) bool {
	ret, _, _ := api.showWindowAsync.Call(uintptr(hwnd), uintptr(cmdShow))
	return ret != 0
}

func (api *user32API) IsIconic(hwnd windows.HWND) bool {
	ret, _, _ := api.isIconic.Call(uintptr(hwnd))
	return ret != 0
}

func (api *user32API) BringWindowToTop(hwnd windows.HWND) bool {
	ret, _, _ := api.bringWindowToTop.Call(uintptr(hwnd))
	return ret != 0
}

func (api *user32API) AttachThreadInput(idAttach uint32, idAttachTo uint32, attach bool) bool {
	attachFlag := uintptr(0)
	if attach {
		attachFlag = 1
	}
	ret, _, _ := api.attachThreadInput.Call(uintptr(idAttach), uintptr(idAttachTo), attachFlag)
	return ret != 0
}

func (api *user32API) GetWindow(hwnd windows.HWND, cmd uint32) windows.HWND {
	ret, _, _ := api.getWindow.Call(uintptr(hwnd), uintptr(cmd))
	return windows.HWND(ret)
}

func (api *user32API) KeybdEvent(virtualKey byte, scanCode byte, flags uint32) {
	_, _, _ = api.keybdEvent.Call(uintptr(virtualKey), uintptr(scanCode), uintptr(flags), 0)
}
