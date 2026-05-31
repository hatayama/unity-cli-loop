//go:build !windows

package cli

import (
	"os/exec"
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
