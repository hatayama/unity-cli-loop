package dispatcher

import (
	"os"
	"os/exec"
	"runtime"
	"strings"
)

func detectShell() string {
	return detectShellForPlatform(runtime.GOOS, os.Getenv("SHELL"), exec.LookPath)
}

func detectShellForPlatform(goos string, shellPath string, lookPath func(string) (string, error)) string {
	shellPath = strings.ToLower(shellPath)
	if strings.Contains(shellPath, "pwsh") {
		return "pwsh"
	}
	if strings.Contains(shellPath, "powershell") {
		return "powershell"
	}
	if strings.Contains(shellPath, "zsh") {
		return "zsh"
	}
	if strings.Contains(shellPath, "bash") {
		return "bash"
	}
	if goos == "windows" {
		if _, err := lookPath("pwsh"); err == nil {
			return "pwsh"
		}
		if _, err := lookPath("powershell"); err == nil {
			return "powershell"
		}
		return ""
	}
	return ""
}
