//go:build darwin

package unityprocess

import (
	"context"
	"fmt"
	"os/exec"
	"strconv"
	"strings"
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
	output, err := exec.CommandContext(ctx, "osascript", "-e", `tell application "System Events" to get unix id of first process whose frontmost is true`).Output()
	if err != nil {
		return 0
	}
	pid, err := strconv.Atoi(strings.TrimSpace(string(output)))
	if err != nil {
		return 0
	}
	return pid
}

func setFrontmostProcessMac(ctx context.Context, pid int) error {
	script := fmt.Sprintf(`tell application "System Events" to set frontmost of (first process whose unix id is %d) to true`, pid)
	return exec.CommandContext(ctx, "osascript", "-e", script).Run()
}
