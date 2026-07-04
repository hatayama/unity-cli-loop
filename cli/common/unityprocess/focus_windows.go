//go:build windows

package unityprocess

import (
	"context"
	"os/exec"
)

func FocusUnityProcess(ctx context.Context, pid int) error {
	script := buildFocusUnityProcessWindowsScript(pid)
	return exec.CommandContext(ctx, windowsPowerShellCommand, "-NoProfile", "-Command", script).Run()
}

func FocusUnityProcessWithRestore(ctx context.Context, pid int) (RestoreFocusFunc, error) {
	script := buildFocusUnityProcessWindowsWithRestoreScript(pid)
	output, err := exec.CommandContext(ctx, windowsPowerShellCommand, "-NoProfile", "-Command", script).Output()
	if err != nil {
		return nil, err
	}
	previousHandle := parseWindowsForegroundHandle(string(output))
	if previousHandle == 0 {
		return nil, nil
	}
	return func(ctx context.Context) error {
		return restoreWindowsForegroundWindow(ctx, previousHandle)
	}, nil
}

func restoreWindowsForegroundWindow(ctx context.Context, handle int64) error {
	script := buildRestoreWindowsForegroundWindowScript(handle)
	return exec.CommandContext(ctx, windowsPowerShellCommand, "-NoProfile", "-Command", script).Run()
}
