//go:build windows

package dispatcher

import (
	"os"
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
	if err := killUnityProcessTree(pid); err == nil {
		return nil
	}
	// taskkill lives in System32, but a PATH that no longer resolves it must not make a
	// restart impossible: killing the Editor alone is still better than failing outright.
	process, err := os.FindProcess(pid)
	if err != nil {
		return err
	}
	return process.Kill()
}

func killUnityProcessTree(pid int) error {
	command := exec.Command("taskkill", "/PID", strconv.Itoa(pid), "/T", "/F")
	command.SysProcAttr = &syscall.SysProcAttr{HideWindow: true}
	return command.Run()
}
