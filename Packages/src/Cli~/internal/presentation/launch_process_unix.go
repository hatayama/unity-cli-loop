//go:build !windows

package presentation

import (
	"os/exec"
	"syscall"
)

func configureDetachedUnityLaunchCommand(command *exec.Cmd) {
	command.SysProcAttr = &syscall.SysProcAttr{Setsid: true}
}
