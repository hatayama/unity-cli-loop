//go:build !windows

package dispatcher

import (
	"errors"
	"os/exec"
	"syscall"
	"testing"
)

func TestConfigureDetachedUnityLaunchCommandStartsNewSession(t *testing.T) {
	command := exec.Command("/bin/echo", "hello")

	configureDetachedUnityLaunchCommand(command)

	if command.SysProcAttr == nil {
		t.Fatal("expected Unity launch command to configure process attributes")
	}
	if !command.SysProcAttr.Setsid {
		t.Fatal("Unity launch command must start a new session so terminal interrupts do not close Unity")
	}
}

func TestKillUnityProcessStopsTheProcess(t *testing.T) {
	// Verifies the Unix kill stops the given process, using a child this test started and owns.
	command := exec.Command("/bin/sh", "-c", "read line")
	stdin, err := command.StdinPipe()
	if err != nil {
		t.Fatalf("failed to open stdin: %v", err)
	}
	t.Cleanup(func() { _ = stdin.Close() })
	if err := command.Start(); err != nil {
		t.Fatalf("failed to start child: %v", err)
	}

	if err := killUnityProcess(command.Process.Pid); err != nil {
		t.Fatalf("killUnityProcess failed: %v", err)
	}

	// Closing stdin lets a surviving child exit normally, so a kill that did nothing fails fast instead of hanging.
	_ = stdin.Close()
	err = command.Wait()
	var exitErr *exec.ExitError
	if !errors.As(err, &exitErr) || exitErr.Sys().(syscall.WaitStatus).Signal() != syscall.SIGKILL {
		t.Fatalf("expected the child to be killed, got %v", err)
	}
}
