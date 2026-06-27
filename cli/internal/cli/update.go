package cli

import (
	"context"
	"io"
	"os/exec"
	"runtime"

	clicontract "github.com/hatayama/unity-cli-loop/cli"
	"github.com/hatayama/unity-cli-loop/cli/internal/update"
)

const (
	updateCommandName          = "update"
	updateUnsupportedOSMessage = update.UnsupportedOSMessage
	updateToVersionFlagName    = "to-version"
)

type updateOptions struct {
	targetVersion string
}

func tryHandleUpdateRequest(ctx context.Context, args []string, stdout io.Writer, stderr io.Writer) (bool, int) {
	if len(args) == 0 || args[0] != updateCommandName {
		return false, 0
	}
	if containsHelpRequest(args[1:]) {
		printUpdateHelp(stdout)
		return true, 0
	}
	options, err := parseUpdateOptions(args[1:])
	if err != nil {
		writeClassifiedError(stderr, err, errorContext{command: updateCommandName})
		return true, 1
	}

	updateCommand, err := update.CommandForOS(runtime.GOOS, update.Options{
		CurrentVersion: clicontract.DispatcherCurrent.DispatcherVersion,
		TargetVersion:  options.targetVersion,
	})
	if err != nil {
		writeClassifiedError(stderr, err, errorContext{command: updateCommandName})
		return true, 1
	}

	writeLine(stdout, "Updating global uloop launcher...")
	command := exec.CommandContext(ctx, updateCommand.Name, updateCommand.Args...)
	command.Stdout = stdout
	command.Stderr = stderr
	if err := command.Run(); err != nil {
		writeErrorEnvelope(stderr, cliError{
			ErrorCode:   errorCodeInternalError,
			Phase:       errorPhaseExecution,
			Message:     "Update failed: " + err.Error(),
			Retryable:   true,
			SafeToRetry: true,
			Command:     updateCommandName,
			NextActions: []string{"Retry `uloop update` after checking network access to GitHub."},
			Details: map[string]any{
				"Cause": err.Error(),
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
	command, err := update.CommandForOS(goos, update.Options{
		CurrentVersion: clicontract.DispatcherCurrent.DispatcherVersion,
		TargetVersion:  options.targetVersion,
	})
	if err != nil {
		return "", nil, err
	}
	return command.Name, command.Args, nil
}

func printUpdateHelp(stdout io.Writer) {
	writeLine(stdout, "Usage:")
	writeLine(stdout, "  uloop update [--to-version <version>]")
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
		normalizedValue := update.NormalizeTargetVersion(value)
		if !update.IsValidTargetVersion(normalizedValue) {
			return updateOptions{}, &argumentError{
				message:      "Invalid CLI version for --" + updateToVersionFlagName + ": " + value,
				option:       "--" + updateToVersionFlagName,
				received:     value,
				expectedType: "semantic version",
				command:      "update",
				nextActions:  []string{"Pass a semantic version such as `3.0.0-beta.6`."},
			}
		}
		options.targetVersion = normalizedValue
		if consumedNext {
			index++
		}
	}
	return options, nil
}
