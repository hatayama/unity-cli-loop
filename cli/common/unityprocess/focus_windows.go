//go:build windows

package unityprocess

import (
	"bytes"
	"context"
	"os/exec"
)

func FocusUnityProcess(ctx context.Context, pid int) error {
	commandContext, cancel := withCommandTimeout(ctx, FocusCommandTimeout)
	defer cancel()
	script := buildFocusUnityProcessWindowsScript(pid)
	stderr := bytes.Buffer{}
	command := exec.CommandContext(commandContext, windowsPowerShellCommand, "-NoProfile", "-Command", script)
	command.Stderr = &stderr
	if err := command.Run(); err != nil {
		return focusCommandError(commandContext.Err(), err, stderr.String())
	}
	return nil
}

func FocusUnityProcessWithRestore(ctx context.Context, pid int) (RestoreFocusFunc, error) {
	commandContext, cancel := withCommandTimeout(ctx, FocusCommandTimeout)
	defer cancel()
	script := buildFocusUnityProcessWindowsWithRestoreScript(pid)
	stderr := bytes.Buffer{}
	command := exec.CommandContext(commandContext, windowsPowerShellCommand, "-NoProfile", "-Command", script)
	command.Stderr = &stderr
	output, err := command.Output()
	if err != nil {
		return nil, focusCommandError(commandContext.Err(), err, stderr.String())
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
	commandContext, cancel := withCommandTimeout(ctx, FocusCommandTimeout)
	defer cancel()
	script := buildRestoreWindowsForegroundWindowScript(handle)
	stderr := bytes.Buffer{}
	command := exec.CommandContext(commandContext, windowsPowerShellCommand, "-NoProfile", "-Command", script)
	command.Stderr = &stderr
	if err := command.Run(); err != nil {
		return focusCommandError(commandContext.Err(), err, stderr.String())
	}
	return nil
}
