//go:build darwin

package unityprocess

import (
	"context"
	"fmt"
	"strings"

	"golang.org/x/sys/unix"
)

// listUnityProcessesMac enumerates Unity Editor processes via the kern.proc.all and
// kern.procargs2 sysctl nodes instead of exec'ing /bin/ps, so Unity process discovery
// keeps working inside sandboxes that deny exec of external binaries (ps included) but
// still permit process-info syscalls.
func listUnityProcessesMac(ctx context.Context) ([]UnityProcess, error) {
	if err := ctx.Err(); err != nil {
		return nil, err
	}

	kinfoProcs, err := unix.SysctlKinfoProcSlice("kern.proc.all")
	if err != nil {
		return nil, fmt.Errorf("failed to retrieve Unity process list: %w", err)
	}

	processes := []UnityProcess{}
	for _, kinfoProc := range kinfoProcs {
		pid := int(kinfoProc.Proc.P_pid)
		if pid <= 0 {
			continue
		}

		args, err := macProcessArgs(pid)
		if err != nil || len(args) == 0 {
			// Reading another user's process args fails (EPERM). Non-root ps could
			// not read those processes' arguments either, so they never matched
			// before; skipping them preserves prior behavior.
			continue
		}

		process, matched := matchMacUnityProcess(pid, strings.Join(args, " "))
		if matched {
			processes = append(processes, process)
		}
	}
	return processes, nil
}

// macProcessArgs reads a process's argv via the kern.procargs2 sysctl node.
func macProcessArgs(pid int) ([]string, error) {
	buf, err := unix.SysctlRaw("kern.procargs2", pid)
	if err != nil {
		return nil, err
	}
	return parseMacProcArgs2(buf)
}
