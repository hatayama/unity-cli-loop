//go:build !windows

package dispatcher

import (
	"os/exec"
	"syscall"
)

func configureDetachedUnityLaunchCommand(command *exec.Cmd) {
	command.SysProcAttr = &syscall.SysProcAttr{Setsid: true}
}
