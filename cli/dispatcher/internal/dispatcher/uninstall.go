package dispatcher

import (
	"context"
	"errors"
	"io"
	"os"
	"os/exec"
	"runtime"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	"github.com/hatayama/unity-cli-loop/dispatcher/internal/nativepath"
	"github.com/hatayama/unity-cli-loop/dispatcher/internal/uninstall"
)

const (
	uninstallUnsupportedOSMessage = uninstall.UnsupportedOSMessage
)

func tryHandleUninstallRequest(ctx context.Context, args []string, stdout io.Writer, stderr io.Writer) (bool, int) {
	if len(args) == 0 || args[0] != clicore.UninstallCommandName {
		return false, 0
	}
	if clicore.ContainsHelpRequest(args[1:]) {
		printUninstallHelp(stdout)
		return true, 0
	}
	if len(args) > 1 {
		clierrors.WriteClassifiedError(stderr, &clierrors.ArgumentError{
			Message:     "Unknown uninstall option: " + args[1],
			Option:      args[1],
			Command:     clicore.UninstallCommandName,
			NextActions: []string{"Run `uloop uninstall` without options."},
		}, clierrors.ErrorContext{Command: clicore.UninstallCommandName})
		return true, 1
	}

	installDir, err := resolveUninstallInstallDir(runtime.GOOS)
	if err != nil {
		clierrors.WriteClassifiedError(stderr, wrapUnsupportedPlatformError(err), clierrors.ErrorContext{Command: clicore.UninstallCommandName})
		return true, 1
	}
	uninstallCommand, err := uninstall.CommandForOS(runtime.GOOS, uninstall.Options{
		InstallDir: installDir,
		CurrentPID: os.Getpid(),
	})
	if err != nil {
		clierrors.WriteClassifiedError(stderr, wrapUnsupportedPlatformError(err), clierrors.ErrorContext{Command: clicore.UninstallCommandName})
		return true, 1
	}

	clicore.WriteLine(stdout, "Uninstalling global uloop dispatcher...")
	command := exec.CommandContext(ctx, uninstallCommand.Name, uninstallCommand.Args...)
	command.Stdout = stdout
	command.Stderr = stderr
	if err := command.Run(); err != nil {
		clierrors.WriteErrorEnvelope(stderr, clierrors.CLIError{
			ErrorCode:   clierrors.ErrorCodeInternalError,
			Phase:       clierrors.ErrorPhaseExecution,
			Message:     "Uninstall failed: " + err.Error(),
			Retryable:   true,
			SafeToRetry: true,
			Command:     clicore.UninstallCommandName,
			NextActions: []string{"Retry `uloop uninstall` after checking file permissions."},
			Details: map[string]any{
				"Cause": err.Error(),
			},
		})
		return true, 1
	}

	if uninstallCommand.Deferred {
		clicore.WriteFormat(stdout, "Scheduled uloop dispatcher removal: %s\n", uninstallCommand.TargetPath)
	} else {
		clicore.WriteFormat(stdout, "Removed uloop dispatcher: %s\n", uninstallCommand.TargetPath)
	}
	writeUninstallPathCompletion(stdout, runtime.GOOS)
	return true, 0
}

func writeUninstallPathCompletion(stdout io.Writer, goos string) {
	if goos == "windows" {
		clicore.WriteLine(stdout, "The package-owned User PATH entry will be removed after this process exits.")
		return
	}

	clicore.WriteLine(stdout, "PATH settings were not changed. Remove the install directory from PATH manually if it is no longer needed.")
}

func printUninstallHelp(stdout io.Writer) {
	clicore.WriteLine(stdout, "Usage:")
	clicore.WriteLine(stdout, "  uloop uninstall")
	clicore.WriteLine(stdout, "")
	clicore.WriteLine(stdout, "Removes the global uloop dispatcher binary from the install directory.")
	clicore.WriteLine(stdout, "Set ULOOP_INSTALL_DIR to uninstall from a custom install directory.")
	clicore.WriteLine(stdout, "On Windows, also removes the package-owned install directory from User PATH.")
	clicore.WriteLine(stdout, "On macOS, PATH settings are not changed automatically.")
}

func resolveUninstallInstallDir(goos string) (string, error) {
	installDir, err := nativepath.ResolveInstallDir(goos, "", nativepath.DefaultEnvironment())
	if errors.Is(err, nativepath.ErrUnsupportedOS) {
		return "", errors.New(uninstallUnsupportedOSMessage)
	}
	return installDir, err
}
