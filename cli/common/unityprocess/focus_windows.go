//go:build windows

package unityprocess

import (
	"bytes"
	"context"
	"os/exec"
)

func FocusUnityProcess(ctx context.Context, pid int) error {
	script := buildFocusUnityProcessWindowsScript(pid)
	stderr := bytes.Buffer{}
	command := exec.CommandContext(ctx, windowsPowerShellCommand, "-NoProfile", "-Command", script)
	command.Stderr = &stderr
	if err := command.Run(); err != nil {
		return commandErrorWithStderr(err, stderr.String())
	}
	return nil
}

func FocusUnityProcessWithRestore(ctx context.Context, pid int) (RestoreFocusFunc, error) {
	script := buildFocusUnityProcessWindowsWithRestoreScript(pid)
	stderr := bytes.Buffer{}
	command := exec.CommandContext(ctx, windowsPowerShellCommand, "-NoProfile", "-Command", script)
	command.Stderr = &stderr
	output, err := command.Output()
	if err != nil {
		return nil, commandErrorWithStderr(err, stderr.String())
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
	stderr := bytes.Buffer{}
	command := exec.CommandContext(ctx, windowsPowerShellCommand, "-NoProfile", "-Command", script)
	command.Stderr = &stderr
	if err := command.Run(); err != nil {
		return commandErrorWithStderr(err, stderr.String())
	}
	return nil
}
