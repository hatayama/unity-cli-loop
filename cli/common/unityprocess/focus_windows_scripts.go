package unityprocess

import (
	_ "embed"
	"strconv"
	"strings"
)

const (
	windowsFocusPIDPlaceholder    = "{{PID}}"
	windowsFocusHandlePlaceholder = "{{HANDLE}}"
)

//go:embed focus_unity_process.ps1
var focusUnityProcessWindowsTemplate string

//go:embed focus_unity_process_with_restore.ps1
var focusUnityProcessWithRestoreWindowsTemplate string

//go:embed restore_windows_foreground_window.ps1
var restoreWindowsForegroundWindowTemplate string

func buildFocusUnityProcessWindowsScript(pid int) string {
	return strings.ReplaceAll(focusUnityProcessWindowsTemplate, windowsFocusPIDPlaceholder, strconv.Itoa(pid))
}

func buildFocusUnityProcessWindowsWithRestoreScript(pid int) string {
	return strings.ReplaceAll(focusUnityProcessWithRestoreWindowsTemplate, windowsFocusPIDPlaceholder, strconv.Itoa(pid))
}

func parseWindowsForegroundHandle(output string) int64 {
	handle, err := strconv.ParseInt(strings.TrimSpace(output), 10, 64)
	if err != nil {
		return 0
	}
	return handle
}

func buildRestoreWindowsForegroundWindowScript(handle int64) string {
	return strings.ReplaceAll(restoreWindowsForegroundWindowTemplate, windowsFocusHandlePlaceholder, strconv.FormatInt(handle, 10))
}
