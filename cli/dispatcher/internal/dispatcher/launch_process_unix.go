//go:build !windows

package dispatcher

import (
	"os/exec"
	"syscall"
)

func configureDetachedUnityLaunchCommand(command *exec.Cmd) {
	command.SysProcAttr = &syscall.SysProcAttr{Setsid: true}
}

// killUnityProcess stops the Unity Editor. Unix needs no process-tree kill: an open file can be
// unlinked here, so a helper process that outlives the Editor cannot block the Temp cleanup.
func killUnityProcess(pid int) error {
	return killProcessById(pid)
}
