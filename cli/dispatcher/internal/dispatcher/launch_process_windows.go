//go:build windows

package dispatcher

import (
	"os/exec"
	"strconv"
	"syscall"
)

func configureDetachedUnityLaunchCommand(command *exec.Cmd) {
	command.SysProcAttr = &syscall.SysProcAttr{CreationFlags: syscall.CREATE_NEW_PROCESS_GROUP}
}

// killUnityProcess stops the Unity Editor together with its process tree. Killing only the
// Editor leaves helpers such as the build backend and the shader compiler running, and those
// keep holding files under Temp that the next launch has to delete.
func killUnityProcess(pid int) error {
	return killUnityProcessWithFallback(pid, killUnityProcessTree, killProcessById)
}

// killUnityProcessTree kills the Editor and everything it spawned. taskkill lives in System32,
// but a PATH that no longer resolves it must not make a restart impossible, so the caller falls
// back to killing the Editor alone.
func killUnityProcessTree(pid int) error {
	command := exec.Command("taskkill", "/PID", strconv.Itoa(pid), "/T", "/F")
	command.SysProcAttr = &syscall.SysProcAttr{HideWindow: true}
	return command.Run()
}
