//go:build !windows

package dispatcher

import (
	"os"
	"os/exec"
	"syscall"
)

func configureDetachedUnityLaunchCommand(command *exec.Cmd) {
	command.SysProcAttr = &syscall.SysProcAttr{Setsid: true}
}

// killUnityProcess stops the Unity Editor. On Unix the Editor's helper processes are in the
// session created by configureDetachedUnityLaunchCommand and exit on their own.
func killUnityProcess(pid int) error {
	process, err := os.FindProcess(pid)
	if err != nil {
		return err
	}
	return process.Kill()
}
