package dispatcher

import (
	"context"
	"errors"
	"fmt"
	"io"
	"os/exec"
	"runtime"
	"strings"
)

const (
	installerScriptURL          = "https://raw.githubusercontent.com/hatayama/unity-cli-loop/main/scripts/install.sh"
	windowsInstallerScriptURL   = "https://raw.githubusercontent.com/hatayama/unity-cli-loop/main/scripts/install.ps1"
	updateCommandName           = "update"
	updateUnsupportedOSMessage  = "native update is only supported on macOS and Windows"
	updateUnsupportedArgMessage = "update does not accept options yet"
	windowsPowerShellCommand    = "powershell"
)

func tryHandleUpdateRequest(ctx context.Context, args []string, stdout io.Writer, stderr io.Writer) (bool, int) {
	if len(args) == 0 || args[0] != updateCommandName {
		return false, 0
	}
	if len(args) > 1 {
		writeError(stderr, argumentError(updateUnsupportedArgMessage, updateCommandName))
		return true, 1
	}

	commandName, commandArgs, err := updateCommandForOS(runtime.GOOS)
	if err != nil {
		writeError(stderr, argumentError(err.Error(), updateCommandName))
		return true, 1
	}

	writeLine(stdout, "Updating global uloop launcher...")
	command := exec.CommandContext(ctx, commandName, commandArgs...)
	command.Stdout = stdout
	command.Stderr = stderr
	if err := command.Run(); err != nil {
		updateError := internalError("Update failed: "+err.Error(), "")
		updateError.Retryable = true
		updateError.SafeToRetry = true
		updateError.Command = updateCommandName
		updateError.NextActions = []string{"Retry `uloop update` after checking network access to GitHub."}
		updateError.Details = map[string]any{"cause": err.Error()}
		writeError(stderr, updateError)
		return true, 1
	}
	writeLine(stdout, "uloop launcher update completed.")
	return true, 0
}

func updateCommandForOS(goos string) (string, []string, error) {
	switch goos {
	case "darwin":
		script := fmt.Sprintf(`tmp=$(mktemp) && curl -fSL %s -o "$tmp" && sh "$tmp"; ec=$?; rm -f "$tmp"; exit $ec`, shellQuote(installerScriptURL))
		return "sh", []string{"-c", script}, nil
	case "windows":
		return windowsPowerShellCommand, []string{
			"-NoProfile",
			"-ExecutionPolicy",
			"Bypass",
			"-Command",
			fmt.Sprintf("irm %s | iex", shellQuote(windowsInstallerScriptURL)),
		}, nil
	default:
		return "", nil, errors.New(updateUnsupportedOSMessage)
	}
}

func shellQuote(value string) string {
	return "'" + strings.ReplaceAll(value, "'", "'\"'\"'") + "'"
}
