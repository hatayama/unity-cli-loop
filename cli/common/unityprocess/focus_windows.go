//go:build windows

package unityprocess

import (
	"context"
	"fmt"
	"runtime"
	"sync"
	"time"

	"golang.org/x/sys/windows"
)

const (
	foregroundPollAttempts = 10
	foregroundPollInterval = 50 * time.Millisecond
)

func FocusUnityProcess(ctx context.Context, pid int) error {
	_, err := FocusUnityProcessWithRestore(ctx, pid)
	return err
}

func FocusUnityProcessWithRestore(ctx context.Context, pid int) (RestoreFocusFunc, error) {
	commandContext, cancel := withCommandTimeout(ctx, FocusCommandTimeout)
	defer cancel()

	previous := windows.GetForegroundWindow()
	if err := focusUnityWindow(commandContext, pid); err != nil {
		return nil, focusCommandError(commandContext.Err(), err, "")
	}
	if previous == 0 {
		return nil, nil
	}
	return func(restoreCtx context.Context) error {
		return restoreWindowsForegroundWindow(restoreCtx, previous)
	}, nil
}

func focusUnityWindow(ctx context.Context, pid int) error {
	if !unityProcessExists(pid) {
		return fmt.Errorf("Unity process was not found: %d", pid)
	}
	processID := uint32(pid)
	handle, err := findMainWindowHandle(processID)
	if err != nil {
		return err
	}
	if err := restoreUnityWindowIfMinimized(handle); err != nil {
		return err
	}
	focusViaSetForegroundWindow(handle)
	if waitForForegroundPID(ctx, processID) {
		return nil
	}
	focusViaAttachThreadInput(handle)
	if waitForForegroundPID(ctx, processID) {
		return nil
	}
	focusViaAltKey(handle)
	if waitForForegroundPID(ctx, processID) {
		return nil
	}
	return fmt.Errorf("Windows refused to bring the Unity window (PID: %d) to the foreground (foreground lock). Click the Unity window or its taskbar icon to focus it manually.", pid)
}

func unityProcessExists(pid int) bool {
	if pid <= 0 {
		return false
	}
	handle, err := windows.OpenProcess(windows.PROCESS_QUERY_LIMITED_INFORMATION, false, uint32(pid))
	if err != nil {
		return false
	}
	_ = windows.CloseHandle(handle)
	return true
}

var (
	mainWindowEnumOnce     sync.Once
	mainWindowEnumCallback uintptr
	mainWindowSearchMu     sync.Mutex
	mainWindowSearchPID    uint32
	mainWindowSearchFound  windows.HWND
)

func findMainWindowHandle(pid uint32) (windows.HWND, error) {
	mainWindowEnumOnce.Do(func() {
		mainWindowEnumCallback = windows.NewCallback(enumMainWindow)
	})
	mainWindowSearchMu.Lock()
	defer mainWindowSearchMu.Unlock()
	mainWindowSearchPID = pid
	mainWindowSearchFound = 0
	_ = windows.EnumWindows(mainWindowEnumCallback, nil)
	if mainWindowSearchFound == 0 {
		return 0, fmt.Errorf("Unity process has no main window handle: %d", pid)
	}
	return mainWindowSearchFound, nil
}

func enumMainWindow(hwnd windows.HWND, _ uintptr) uintptr {
	var windowPID uint32
	_, _ = windows.GetWindowThreadProcessId(hwnd, &windowPID)
	if windowPID != mainWindowSearchPID {
		return 1
	}
	if !windows.IsWindowVisible(hwnd) {
		return 1
	}
	if user32.GetWindow(hwnd, GW_OWNER) != 0 {
		return 1
	}
	mainWindowSearchFound = hwnd
	return 0
}

func restoreUnityWindowIfMinimized(handle windows.HWND) error {
	if !user32.IsIconic(handle) {
		return nil
	}
	if !user32.ShowWindowAsync(handle, SW_RESTORE) {
		return fmt.Errorf("Failed to show Unity window")
	}
	return nil
}

func waitForForegroundPID(ctx context.Context, pid uint32) bool {
	for attempt := 0; attempt < foregroundPollAttempts; attempt++ {
		if ctx.Err() != nil {
			return false
		}
		if readForegroundPID() == pid {
			return true
		}
		timer := time.NewTimer(foregroundPollInterval)
		select {
		case <-ctx.Done():
			timer.Stop()
			return false
		case <-timer.C:
		}
	}
	return false
}

func readForegroundPID() uint32 {
	var pid uint32
	_, _ = windows.GetWindowThreadProcessId(windows.GetForegroundWindow(), &pid)
	return pid
}

func focusViaSetForegroundWindow(handle windows.HWND) {
	user32.SetForegroundWindow(handle)
}

func focusViaAttachThreadInput(handle windows.HWND) {
	// AttachThreadInput is bound to the calling OS thread. A goroutine can
	// migrate, so lock from GetCurrentThreadId through detach; otherwise
	// attach and detach can run on different threads and leave input queues joined.
	runtime.LockOSThread()
	defer runtime.UnlockOSThread()

	currentThreadID := windows.GetCurrentThreadId()
	foregroundThreadID := windowThreadID(windows.GetForegroundWindow())
	targetThreadID := windowThreadID(handle)
	attachedForeground := false
	attachedTarget := false
	if foregroundThreadID != 0 && foregroundThreadID != currentThreadID {
		attachedForeground = user32.AttachThreadInput(currentThreadID, foregroundThreadID, true)
	}
	if targetThreadID != 0 && targetThreadID != currentThreadID {
		attachedTarget = user32.AttachThreadInput(currentThreadID, targetThreadID, true)
	}
	defer detachFocusThreads(currentThreadID, foregroundThreadID, targetThreadID, attachedForeground, attachedTarget)

	user32.BringWindowToTop(handle)
	user32.SetForegroundWindow(handle)
}

func detachFocusThreads(currentThreadID uint32, foregroundThreadID uint32, targetThreadID uint32, attachedForeground bool, attachedTarget bool) {
	if attachedTarget {
		user32.AttachThreadInput(currentThreadID, targetThreadID, false)
	}
	if attachedForeground {
		user32.AttachThreadInput(currentThreadID, foregroundThreadID, false)
	}
}

func focusViaAltKey(handle windows.HWND) {
	user32.KeybdEvent(VK_MENU, 0, 0)
	user32.SetForegroundWindow(handle)
	user32.KeybdEvent(VK_MENU, 0, KEYEVENTF_KEYUP)
}

func windowThreadID(hwnd windows.HWND) uint32 {
	threadID, _ := windows.GetWindowThreadProcessId(hwnd, nil)
	return threadID
}

func restoreWindowsForegroundWindow(ctx context.Context, handle windows.HWND) error {
	commandContext, cancel := withCommandTimeout(ctx, FocusCommandTimeout)
	defer cancel()
	if err := restoreForegroundWindow(handle); err != nil {
		return focusCommandError(commandContext.Err(), err, "")
	}
	return nil
}

func restoreForegroundWindow(handle windows.HWND) error {
	if handle == 0 {
		return fmt.Errorf("Saved foreground window handle is invalid")
	}
	runtime.LockOSThread()
	defer runtime.UnlockOSThread()

	targetThreadID := windowThreadID(handle)
	if targetThreadID == 0 {
		return fmt.Errorf("Saved foreground window thread is invalid")
	}
	currentThreadID := windows.GetCurrentThreadId()
	foregroundThreadID := windowThreadID(windows.GetForegroundWindow())
	detach := attachRestoreThreads(currentThreadID, foregroundThreadID, targetThreadID)
	defer detach()

	if user32.IsIconic(handle) {
		if !user32.ShowWindowAsync(handle, SW_RESTORE) {
			return fmt.Errorf("Failed to show previous foreground window")
		}
	}
	user32.BringWindowToTop(handle)
	if !user32.SetForegroundWindow(handle) {
		return fmt.Errorf("Failed to restore previous foreground window")
	}
	return nil
}

func attachRestoreThreads(currentThreadID uint32, foregroundThreadID uint32, targetThreadID uint32) func() {
	attachedCurrent := false
	attachedForeground := false
	if targetThreadID != currentThreadID {
		attachedCurrent = user32.AttachThreadInput(currentThreadID, targetThreadID, true)
	}
	if foregroundThreadID != 0 && foregroundThreadID != targetThreadID {
		attachedForeground = user32.AttachThreadInput(foregroundThreadID, targetThreadID, true)
	}
	return func() {
		if attachedForeground {
			user32.AttachThreadInput(foregroundThreadID, targetThreadID, false)
		}
		if attachedCurrent {
			user32.AttachThreadInput(currentThreadID, targetThreadID, false)
		}
	}
}
