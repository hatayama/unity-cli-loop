package cli

import (
	"context"
	"fmt"
	"io"
	"os/exec"
	"runtime"
	"strings"
)

const (
	installerScriptURL          = "https://raw.githubusercontent.com/hatayama/unity-cli-loop/main/scripts/install.sh"
	windowsInstallerScriptURL   = "https://raw.githubusercontent.com/hatayama/unity-cli-loop/main/scripts/install.ps1"
	updateUnsupportedOSMessage  = "native update is only supported on macOS and Windows"
	updateUnsupportedArgMessage = "update does not accept options yet"
)

func tryHandleUpdateRequest(ctx context.Context, args []string, stdout io.Writer, stderr io.Writer) (bool, int) {
	if len(args) == 0 || args[0] != "update" {
		return false, 0
	}
	if len(args) > 1 {
		fmt.Fprintln(stderr, updateUnsupportedArgMessage)
		return true, 1
	}

	commandName, commandArgs, err := updateCommandForOS(runtime.GOOS)
	if err != nil {
		fmt.Fprintln(stderr, err.Error())
		return true, 1
	}

	fmt.Fprintln(stdout, "Updating global uloop launcher...")
	command := exec.CommandContext(ctx, commandName, commandArgs...)
	command.Stdout = stdout
	command.Stderr = stderr
	if err := command.Run(); err != nil {
		fmt.Fprintf(stderr, "Update failed: %s\n", err.Error())
		return true, 1
	}
	fmt.Fprintln(stdout, "uloop launcher update completed.")
	return true, 0
}

func updateCommandForOS(goos string) (string, []string, error) {
	switch goos {
	case "darwin":
		return "sh", []string{"-c", fmt.Sprintf("curl -fsSL %s | sh", shellQuote(installerScriptURL))}, nil
	case "windows":
		return windowsPowerShellCommand, []string{
			"-NoProfile",
			"-ExecutionPolicy",
			"Bypass",
			"-Command",
			fmt.Sprintf("irm %s | iex", shellQuote(windowsInstallerScriptURL)),
		}, nil
	default:
		return "", nil, fmt.Errorf(updateUnsupportedOSMessage)
	}
}

func shellQuote(value string) string {
	return "'" + strings.ReplaceAll(value, "'", "'\"'\"'") + "'"
}
