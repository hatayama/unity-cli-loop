//go:build darwin

package unityprocess

import (
	"context"
	"fmt"
	"os/exec"
)

const (
	lsappinfoCommand = "lsappinfo"
	openCommand      = "open"
	osascriptCommand = "osascript"
)

func FocusUnityProcess(ctx context.Context, pid int) error {
	return setFrontmostProcessMac(ctx, pid)
}

func FocusUnityProcessWithRestore(ctx context.Context, pid int) (RestoreFocusFunc, error) {
	previousPID := readFrontmostProcessIDMac(ctx)
	if err := setFrontmostProcessMac(ctx, pid); err != nil {
		return nil, err
	}
	if previousPID <= 0 {
		return nil, nil
	}
	return func(ctx context.Context) error {
		return setFrontmostProcessMac(ctx, previousPID)
	}, nil
}

func readFrontmostProcessIDMac(ctx context.Context) int {
	output, err := runFocusCommand(ctx, lsappinfoCommand, "front")
	if err != nil {
		return 0
	}
	asn := parseLsappinfoFrontASN(string(output))
	if asn == "" {
		return 0
	}
	pidOutput, err := runFocusCommand(ctx, lsappinfoCommand, "info", "-only", "pid", asn)
	if err != nil {
		return 0
	}
	return parseLsappinfoPID(string(pidOutput))
}

func bundlePathForPIDMac(ctx context.Context, pid int) string {
	output, err := runFocusCommand(ctx, lsappinfoCommand, "find", fmt.Sprintf("pid=%d", pid))
	if err != nil {
		return ""
	}
	asn := parseLsappinfoFindASN(string(output))
	if asn == "" {
		return ""
	}
	pathOutput, err := runFocusCommand(ctx, lsappinfoCommand, "info", "-only", "LSBundlePath", asn)
	if err != nil {
		return ""
	}
	return parseLsappinfoBundlePath(string(pathOutput))
}

func countProcessesWithBundlePathMac(ctx context.Context, bundlePath string) int {
	output, err := runFocusCommand(ctx, lsappinfoCommand, "list")
	if err != nil {
		return 0
	}
	return countLsappinfoBundlePath(string(output), bundlePath)
}

func setFrontmostProcessMac(ctx context.Context, pid int) error {
	bundlePath := bundlePathForPIDMac(ctx, pid)
	if bundlePath == "" {
		return setFrontmostProcessViaOsascriptMac(ctx, pid)
	}
	if countProcessesWithBundlePathMac(ctx, bundlePath) >= 2 {
		return setFrontmostProcessViaOsascriptMac(ctx, pid)
	}
	return activateAppViaOpenMac(ctx, bundlePath)
}

// setFrontmostProcessViaOsascriptMac activates a process by PID.
// open -a can only name a bundle path, so it cannot choose one instance when
// the same Unity version has two projects open.
func setFrontmostProcessViaOsascriptMac(ctx context.Context, pid int) error {
	script := fmt.Sprintf(`tell application "System Events" to set frontmost of (first process whose unix id is %d) to true`, pid)
	return runFocusCommandNoOutput(ctx, osascriptCommand, "-e", script)
}

func activateAppViaOpenMac(ctx context.Context, bundlePath string) error {
	return runFocusCommandNoOutput(ctx, openCommand, "-a", bundlePath)
}

func runFocusCommand(ctx context.Context, name string, args ...string) ([]byte, error) {
	commandContext, cancel := withCommandTimeout(ctx, FocusCommandTimeout)
	defer cancel()
	return exec.CommandContext(commandContext, name, args...).Output()
}

func runFocusCommandNoOutput(ctx context.Context, name string, args ...string) error {
	commandContext, cancel := withCommandTimeout(ctx, FocusCommandTimeout)
	defer cancel()
	return exec.CommandContext(commandContext, name, args...).Run()
}
