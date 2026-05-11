package cli

import (
	"context"
	"errors"
	"fmt"
	"io"
	"os/exec"
	"runtime"
	"strings"

	corecontract "github.com/hatayama/unity-cli-loop/Packages/src/Cli/Core"
	"github.com/hatayama/unity-cli-loop/Packages/src/Cli/Shared/adapters/installer"
)

const (
	updateUnsupportedOSMessage  = "native update is only supported on macOS and Windows"
	updateUnsupportedArgMessage = "update does not accept options yet"
)

func tryHandleUpdateRequest(ctx context.Context, args []string, stdout io.Writer, stderr io.Writer) (bool, int) {
	if len(args) == 0 || args[0] != "update" {
		return false, 0
	}
	if len(args) > 1 {
		writeErrorEnvelope(stderr, (&argumentError{
			message:     updateUnsupportedArgMessage,
			command:     "update",
			nextActions: []string{"Run `uloop update` without options."},
		}).toCLIError(errorContext{command: "update"}))
		return true, 1
	}

	commandName, commandArgs, err := updateCommandForOS(runtime.GOOS)
	if err != nil {
		writeClassifiedError(stderr, err, errorContext{command: "update"})
		return true, 1
	}

	writeLine(stdout, "Updating global uloop launcher...")
	command := exec.CommandContext(ctx, commandName, commandArgs...)
	command.Stdout = stdout
	command.Stderr = stderr
	if err := command.Run(); err != nil {
		writeErrorEnvelope(stderr, cliError{
			ErrorCode:   errorCodeInternalError,
			Phase:       errorPhaseExecution,
			Message:     "Update failed: " + err.Error(),
			Retryable:   true,
			SafeToRetry: true,
			Command:     "update",
			NextActions: []string{"Retry `uloop update` after checking network access to GitHub."},
			Details: map[string]any{
				"cause": err.Error(),
			},
		})
		return true, 1
	}
	writeLine(stdout, "uloop launcher update completed.")
	return true, 0
}

func updateCommandForOS(goos string) (string, []string, error) {
	version := corecontract.Current.CliVersion
	updateSelector := installer.UpdateSelectorForVersion(version)
	switch goos {
	case "darwin":
		scriptURL := installer.ScriptURL(version, installer.PosixScriptName)
		script := fmt.Sprintf(`tmp=$(mktemp) && curl -fSL %s -o "$tmp" && ULOOP_VERSION=%s sh "$tmp"; ec=$?; rm -f "$tmp"; exit $ec`, shellQuote(scriptURL), shellQuote(updateSelector))
		return "sh", []string{"-c", script}, nil
	case "windows":
		scriptURL := installer.ScriptURL(version, installer.WindowsScriptName)
		return windowsPowerShellCommand, []string{
			"-NoProfile",
			"-ExecutionPolicy",
			"Bypass",
			"-Command",
			fmt.Sprintf("$env:ULOOP_VERSION=%s; irm %s | iex", shellQuote(updateSelector), shellQuote(scriptURL)),
		}, nil
	default:
		return "", nil, errors.New(updateUnsupportedOSMessage)
	}
}

func shellQuote(value string) string {
	return "'" + strings.ReplaceAll(value, "'", "'\"'\"'") + "'"
}
