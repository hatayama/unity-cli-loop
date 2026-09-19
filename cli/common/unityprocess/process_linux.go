//go:build linux

package unityprocess

import (
	"context"
	"fmt"
	"os"
	"path/filepath"
	"strconv"
)

// listUnityProcessesLinux enumerates Unity Editor processes by reading
// /proc/<pid>/cmdline directly instead of exec'ing ps, for the same reason the
// macOS implementation reads sysctl: it keeps working inside sandboxes that forbid
// exec'ing external binaries.
func listUnityProcessesLinux(ctx context.Context) ([]UnityProcess, error) {
	if err := ctx.Err(); err != nil {
		return nil, err
	}
	entries, err := os.ReadDir("/proc")
	if err != nil {
		return nil, fmt.Errorf("failed to retrieve Unity process list: %w", err)
	}

	processes := []UnityProcess{}
	for _, entry := range entries {
		// Non-process entries such as "meminfo" or "self" are not positive integers.
		pid, err := strconv.Atoi(entry.Name())
		if err != nil || pid <= 0 {
			continue
		}
		// A process that exited between ReadDir and this read, or whose cmdline is
		// not readable by this user, cannot be the caller's Editor, so skip it
		// rather than failing the whole listing.
		buf, err := os.ReadFile(filepath.Join("/proc", entry.Name(), "cmdline"))
		if err != nil {
			continue
		}
		process, matched := matchLinuxUnityProcess(pid, parseLinuxProcCmdline(buf))
		if matched {
			processes = append(processes, process)
		}
	}
	return processes, nil
}
