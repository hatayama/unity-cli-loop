package dispatcher

import (
	"context"
	"io"
	"os"
	"os/exec"
	"path/filepath"
	"runtime"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	"github.com/hatayama/unity-cli-loop/dispatcher/internal/update"
)

const (
	updateUnsupportedOSMessage = update.UnsupportedOSMessage
	updateToVersionFlagName    = "to-version"
)

var updateRunCommand = runUpdateCommand

type updateOptions struct {
	targetVersion string
}

func tryHandleUpdateRequest(ctx context.Context, args []string, stdout io.Writer, stderr io.Writer) (bool, int) {
	if len(args) == 0 || args[0] != clicore.UpdateCommandName {
		return false, 0
	}
	if clicore.ContainsHelpRequest(args[1:]) {
		printUpdateHelp(stdout)
		return true, 0
	}
	options, err := parseUpdateOptions(args[1:])
	if err != nil {
		clierrors.WriteClassifiedError(stderr, err, clierrors.ErrorContext{Command: clicore.UpdateCommandName})
		return true, 1
	}

	resolvedOptions, err := resolveUpdateTargetVersionFunc(ctx, update.Options{
		CurrentVersion: dispatcherVersion,
		TargetVersion:  options.targetVersion,
	})
	if err != nil {
		clierrors.WriteClassifiedError(stderr, err, clierrors.ErrorContext{Command: clicore.UpdateCommandName})
		return true, 1
	}
	updateCommand, err := update.CommandForOS(runtime.GOOS, resolvedOptions)
	if err != nil {
		clierrors.WriteClassifiedError(stderr, wrapUnsupportedPlatformError(err), clierrors.ErrorContext{Command: clicore.UpdateCommandName})
		return true, 1
	}

	clicore.WriteLine(stdout, "Updating global uloop dispatcher...")
	if err := updateRunCommand(ctx, updateCommand, stdout, stderr); err != nil {
		clierrors.WriteErrorEnvelope(stderr, clierrors.CLIError{
			ErrorCode:   clierrors.ErrorCodeInternalError,
			Phase:       clierrors.ErrorPhaseExecution,
			Message:     "Update failed: " + err.Error(),
			Retryable:   true,
			SafeToRetry: true,
			Command:     clicore.UpdateCommandName,
			NextActions: []string{"Retry `uloop update` after checking network access to GitHub."},
			Details: map[string]any{
				"Cause": err.Error(),
			},
		})
		return true, 1
	}
	writeManualDispatcherUpdateCompletion(stdout, dispatcherVersion, dispatcherInstalledVersionOrEmpty(ctx))
	return true, 0
}

func updateCommandForOS(goos string) (string, []string, error) {
	return updateCommandForOSWithOptions(goos, updateOptions{})
}

func updateCommandForOSWithOptions(goos string, options updateOptions) (string, []string, error) {
	command, err := update.CommandForOS(goos, update.Options{
		CurrentVersion: dispatcherVersion,
		TargetVersion:  options.targetVersion,
	})
	if err != nil {
		return "", nil, err
	}
	return command.Name, command.Args, nil
}

func runUpdateCommand(ctx context.Context, updateCommand update.Command, stdout io.Writer, stderr io.Writer) error {
	tempDir, err := os.MkdirTemp("", "uloop-update-")
	if err != nil {
		return err
	}
	defer func() {
		_ = os.RemoveAll(tempDir)
	}()

	installerPath, err := downloadVerifiedUpdateInstaller(ctx, updateCommand, tempDir)
	if err != nil {
		return err
	}

	manifest, err := fetchAttestationSubjectManifestFunc(ctx, updateCommand.ReleaseTag)
	if err != nil {
		return err
	}

	args := updateExecutionArgs(updateCommand, installerPath)
	command := exec.CommandContext(ctx, updateCommand.Name, args...)
	command.Stdout = stdout
	command.Stderr = stderr
	env := append(os.Environ(), updateCommand.Env...)
	// Why: installer scripts now fall back to the repository pin when this env
	// var is absent; self-update MUST keep passing the Sigstore-derived
	// manifest explicitly so the update installs the requested release tag
	// rather than the pinned one.
	env = append(env, updateArchiveManifestEnvName+"="+manifest)
	command.Env = env
	return command.Run()
}

func downloadVerifiedUpdateInstaller(ctx context.Context, updateCommand update.Command, tempDir string) (string, error) {
	installerPath := filepath.Join(tempDir, updateCommand.InstallerName)
	checksumPath := installerPath + ".sha256"
	if err := downloadDispatcherFile(ctx, updateCommand.InstallerURL, installerPath); err != nil {
		return "", err
	}
	if err := downloadDispatcherFile(ctx, updateCommand.InstallerChecksumURL, checksumPath); err != nil {
		return "", err
	}
	if err := verifyDispatcherChecksum(installerPath, checksumPath); err != nil {
		return "", err
	}
	if err := verifyReleaseAssetAttestation(ctx, updateCommand.ReleaseTag, updateCommand.InstallerURL, installerPath, attestationDispatcherPublishWorkflowPath); err != nil {
		return "", err
	}
	return installerPath, nil
}

func updateExecutionArgs(updateCommand update.Command, installerPath string) []string {
	if updateCommand.Name == "powershell" {
		return []string{"-NoProfile", "-ExecutionPolicy", "Bypass", "-File", installerPath}
	}
	return append(updateCommand.Args, installerPath)
}

func printUpdateHelp(stdout io.Writer) {
	clicore.WriteLine(stdout, "Usage:")
	clicore.WriteLine(stdout, "  uloop update [--to-version <version>]")
}

func parseUpdateOptions(args []string) (updateOptions, error) {
	options := updateOptions{}
	for index := 0; index < len(args); index++ {
		arg := args[index]
		name, value, consumedNext, err := clicore.ParseFlagValue(arg, args, index)
		if err != nil {
			return updateOptions{}, err
		}
		if name != updateToVersionFlagName {
			return updateOptions{}, &clierrors.ArgumentError{
				Message:     "Unknown update option: --" + name,
				Option:      "--" + name,
				Command:     "update",
				NextActions: []string{"Run `uloop update` or `uloop update --to-version <version>`."},
			}
		}
		if options.targetVersion != "" {
			return updateOptions{}, &clierrors.ArgumentError{
				Message:     "Duplicate update option: --" + updateToVersionFlagName,
				Option:      "--" + updateToVersionFlagName,
				Command:     "update",
				NextActions: []string{"Pass `--to-version` only once."},
			}
		}
		normalizedValue := update.NormalizeTargetVersion(value)
		if !update.IsValidTargetVersion(normalizedValue) {
			return updateOptions{}, &clierrors.ArgumentError{
				Message:      "Invalid CLI version for --" + updateToVersionFlagName + ": " + value,
				Option:       "--" + updateToVersionFlagName,
				Received:     value,
				ExpectedType: "semantic version",
				Command:      "update",
				NextActions:  []string{"Pass a semantic version such as `3.0.0-beta.6`."},
			}
		}
		options.targetVersion = normalizedValue
		if consumedNext {
			index++
		}
	}
	return options, nil
}
