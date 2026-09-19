//go:build linux

package unityprocess

import (
	"context"
	"fmt"
	"os"
	"path/filepath"
	"strconv"
	"syscall"
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
		// /proc exposes other users' cmdline too, so limit matches to processes
		// owned by this user; otherwise launch -q/-r could target another user's
		// Editor for the same path. macOS covers the same set implicitly, because
		// procargs2 fails for processes of another UID.
		if !isOwnedByCurrentUser(entry.Name()) {
			continue
		}
		// A process that exited between ReadDir and this read cannot be the
		// caller's Editor, so skip it rather than failing the whole listing.
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

// isOwnedByCurrentUser reports whether /proc/<pid> belongs to the effective UID.
// Any failure to read the owner counts as not owned, so an unverifiable process is
// never treated as the caller's Editor.
func isOwnedByCurrentUser(pidEntry string) bool {
	info, err := os.Stat(filepath.Join("/proc", pidEntry))
	if err != nil {
		return false
	}
	stat, ok := info.Sys().(*syscall.Stat_t)
	if !ok {
		return false
	}
	return stat.Uid == uint32(os.Geteuid())
}
