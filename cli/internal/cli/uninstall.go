package cli

import (
	"context"
	"errors"
	"io"
	"os"
	"os/exec"
	"path/filepath"
	"runtime"

	"github.com/hatayama/unity-cli-loop/cli/internal/uninstall"
)

const (
	uninstallInstallDirEnvName    = "ULOOP_INSTALL_DIR"
	uninstallLocalAppDataEnvName  = "LOCALAPPDATA"
	uninstallUnsupportedOSMessage = uninstall.UnsupportedOSMessage
)

func tryHandleUninstallRequest(ctx context.Context, args []string, stdout io.Writer, stderr io.Writer) (bool, int) {
	if len(args) == 0 || args[0] != uninstallCommandName {
		return false, 0
	}
	if containsHelpRequest(args[1:]) {
		printUninstallHelp(stdout)
		return true, 0
	}
	if len(args) > 1 {
		writeClassifiedError(stderr, &argumentError{
			message:     "Unknown uninstall option: " + args[1],
			option:      args[1],
			command:     uninstallCommandName,
			nextActions: []string{"Run `uloop uninstall` without options."},
		}, errorContext{command: uninstallCommandName})
		return true, 1
	}

	installDir, err := resolveUninstallInstallDir(runtime.GOOS)
	if err != nil {
		writeClassifiedError(stderr, wrapUnsupportedPlatformError(err), errorContext{command: uninstallCommandName})
		return true, 1
	}
	uninstallCommand, err := uninstall.CommandForOS(runtime.GOOS, uninstall.Options{
		InstallDir: installDir,
		CurrentPID: os.Getpid(),
	})
	if err != nil {
		writeClassifiedError(stderr, wrapUnsupportedPlatformError(err), errorContext{command: uninstallCommandName})
		return true, 1
	}

	writeLine(stdout, "Uninstalling global uloop launcher...")
	command := exec.CommandContext(ctx, uninstallCommand.Name, uninstallCommand.Args...)
	command.Stdout = stdout
	command.Stderr = stderr
	if err := command.Run(); err != nil {
		writeErrorEnvelope(stderr, cliError{
			ErrorCode:   errorCodeInternalError,
			Phase:       errorPhaseExecution,
			Message:     "Uninstall failed: " + err.Error(),
			Retryable:   true,
			SafeToRetry: true,
			Command:     uninstallCommandName,
			NextActions: []string{"Retry `uloop uninstall` after checking file permissions."},
			Details: map[string]any{
				"Cause": err.Error(),
			},
		})
		return true, 1
	}

	if uninstallCommand.Deferred {
		writeFormat(stdout, "Scheduled uloop launcher removal: %s\n", uninstallCommand.TargetPath)
	} else {
		writeFormat(stdout, "Removed uloop launcher: %s\n", uninstallCommand.TargetPath)
	}
	writeUninstallPathCompletion(stdout, runtime.GOOS)
	return true, 0
}

func writeUninstallPathCompletion(stdout io.Writer, goos string) {
	if goos == "windows" {
		writeLine(stdout, "The package-owned User PATH entry will be removed after this process exits.")
		return
	}

	writeLine(stdout, "PATH settings were not changed. Remove the install directory from PATH manually if it is no longer needed.")
}

func printUninstallHelp(stdout io.Writer) {
	writeLine(stdout, "Usage:")
	writeLine(stdout, "  uloop uninstall")
	writeLine(stdout, "")
	writeLine(stdout, "Removes the global uloop launcher binary from the install directory.")
	writeLine(stdout, "Set ULOOP_INSTALL_DIR to uninstall from a custom install directory.")
	writeLine(stdout, "On Windows, also removes the package-owned install directory from User PATH.")
	writeLine(stdout, "On macOS, PATH settings are not changed automatically.")
}

func resolveUninstallInstallDir(goos string) (string, error) {
	if installDir := os.Getenv(uninstallInstallDirEnvName); installDir != "" {
		return installDir, nil
	}

	switch goos {
	case "darwin":
		home, err := os.UserHomeDir()
		if err != nil {
			return "", err
		}
		return filepath.Join(home, ".local", "bin"), nil
	case "windows":
		localAppData := os.Getenv(uninstallLocalAppDataEnvName)
		if localAppData == "" {
			return "", errors.New("LOCALAPPDATA is required to resolve the uloop install directory")
		}
		return filepath.Join(localAppData, "Programs", "uloop", "bin"), nil
	default:
		return "", errors.New(uninstallUnsupportedOSMessage)
	}
}
