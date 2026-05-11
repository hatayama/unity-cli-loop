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
	sharedversion "github.com/hatayama/unity-cli-loop/Packages/src/Cli/Shared/version"
)

const (
	updateUnsupportedOSMessage = "native update is only supported on macOS and Windows"
	updateToVersionFlagName    = "to-version"
)

type updateOptions struct {
	targetVersion string
}

func tryHandleUpdateRequest(ctx context.Context, args []string, stdout io.Writer, stderr io.Writer) (bool, int) {
	if len(args) == 0 || args[0] != "update" {
		return false, 0
	}
	options, err := parseUpdateOptions(args[1:])
	if err != nil {
		writeClassifiedError(stderr, err, errorContext{command: "update"})
		return true, 1
	}

	commandName, commandArgs, err := updateCommandForOSWithOptions(runtime.GOOS, options)
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
	return updateCommandForOSWithOptions(goos, updateOptions{})
}

func updateCommandForOSWithOptions(goos string, options updateOptions) (string, []string, error) {
	version := updateScriptVersion(options)
	updateSelector := updateSelector(options)
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

func parseUpdateOptions(args []string) (updateOptions, error) {
	options := updateOptions{}
	for index := 0; index < len(args); index++ {
		arg := args[index]
		name, value, consumedNext, err := parseFlagValue(arg, args, index)
		if err != nil {
			return updateOptions{}, err
		}
		if name != updateToVersionFlagName {
			return updateOptions{}, &argumentError{
				message:     "Unknown update option: --" + name,
				option:      "--" + name,
				command:     "update",
				nextActions: []string{"Run `uloop update` or `uloop update --to-version <version>`."},
			}
		}
		if options.targetVersion != "" {
			return updateOptions{}, &argumentError{
				message:     "Duplicate update option: --" + updateToVersionFlagName,
				option:      "--" + updateToVersionFlagName,
				command:     "update",
				nextActions: []string{"Pass `--to-version` only once."},
			}
		}
		if !isValidUpdateTargetVersion(value) {
			return updateOptions{}, &argumentError{
				message:      "Invalid CLI version for --" + updateToVersionFlagName + ": " + value,
				option:       "--" + updateToVersionFlagName,
				received:     value,
				expectedType: "semantic version",
				command:      "update",
				nextActions:  []string{"Pass a semantic version such as `3.0.0-beta.6`."},
			}
		}
		options.targetVersion = value
		if consumedNext {
			index++
		}
	}
	return options, nil
}

func isValidUpdateTargetVersion(value string) bool {
	_, ok := sharedversion.Compare(value, value)
	return ok
}

func updateScriptVersion(options updateOptions) string {
	if options.targetVersion != "" {
		return options.targetVersion
	}
	return corecontract.Current.CliVersion
}

func updateSelector(options updateOptions) string {
	if options.targetVersion != "" {
		return installer.ReleaseTag(options.targetVersion)
	}
	return installer.UpdateSelectorForVersion(corecontract.Current.CliVersion)
}

func shellQuote(value string) string {
	return "'" + strings.ReplaceAll(value, "'", "'\"'\"'") + "'"
}
