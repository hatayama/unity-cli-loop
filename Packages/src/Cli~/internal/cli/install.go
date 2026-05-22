package cli

import (
	"context"
	"errors"
	"io"
	"os/exec"
	"runtime"

	"github.com/hatayama/unity-cli-loop/Packages/src/Cli/internal/install"
)

const (
	installCommandName          = "install"
	installDirFlagName          = "dir"
	installUnsupportedOSMessage = install.UnsupportedOSMessage
)

type installOptions struct {
	installDir string
}

func tryHandleInstallRequest(ctx context.Context, args []string, stdout io.Writer, stderr io.Writer) (bool, int) {
	if len(args) == 0 || args[0] != installCommandName {
		return false, 0
	}
	if containsHelpRequest(args[1:]) {
		printInstallHelp(stdout)
		return true, 0
	}
	options, err := parseInstallOptions(args[1:])
	if err != nil {
		writeClassifiedError(stderr, err, errorContext{command: installCommandName})
		return true, 1
	}

	installDir, err := resolveNativeInstallDir(runtime.GOOS, options.installDir)
	if err != nil {
		writeClassifiedError(stderr, err, errorContext{command: installCommandName})
		return true, 1
	}
	installCommand, err := install.CommandForOS(runtime.GOOS, install.Options{
		InstallDir: installDir,
	})
	if err != nil {
		writeClassifiedError(stderr, err, errorContext{command: installCommandName})
		return true, 1
	}

	writeLine(stdout, "Configuring global uloop launcher...")
	command := exec.CommandContext(ctx, installCommand.Name, installCommand.Args...)
	command.Stdout = stdout
	command.Stderr = stderr
	if err := command.Run(); err != nil {
		writeErrorEnvelope(stderr, cliError{
			ErrorCode:   errorCodeInternalError,
			Phase:       errorPhaseExecution,
			Message:     "Install setup failed: " + err.Error(),
			Retryable:   true,
			SafeToRetry: true,
			Command:     installCommandName,
			NextActions: []string{"Retry `uloop install --dir <install-dir>` after checking PATH permissions."},
			Details: map[string]any{
				"cause": err.Error(),
			},
		})
		return true, 1
	}

	writeInstallCompletion(stdout, runtime.GOOS)
	return true, 0
}

func parseInstallOptions(args []string) (installOptions, error) {
	options := installOptions{}
	for index := 0; index < len(args); index++ {
		arg := args[index]
		if arg == "-d" {
			if options.installDir != "" {
				return installOptions{}, duplicateInstallDirOptionError(arg)
			}
			if index+1 >= len(args) || isNextOptionToken(args[index+1]) {
				return installOptions{}, missingValueArgumentError(arg)
			}
			options.installDir = args[index+1]
			index++
			continue
		}

		name, value, consumedNext, err := parseFlagValue(arg, args, index)
		if err != nil {
			return installOptions{}, err
		}
		if name != installDirFlagName {
			return installOptions{}, &argumentError{
				message:     "Unknown install option: --" + name,
				option:      "--" + name,
				command:     installCommandName,
				nextActions: []string{"Run `uloop install` or `uloop install --dir <install-dir>`."},
			}
		}
		if options.installDir != "" {
			return installOptions{}, duplicateInstallDirOptionError("--" + installDirFlagName)
		}
		options.installDir = value
		if consumedNext {
			index++
		}
	}
	return options, nil
}

func duplicateInstallDirOptionError(option string) error {
	return &argumentError{
		message:     "Duplicate install option: " + option,
		option:      option,
		command:     installCommandName,
		nextActions: []string{"Pass the install directory only once."},
	}
}

func writeInstallCompletion(stdout io.Writer, goos string) {
	if goos == "windows" {
		writeLine(stdout, "The package-owned User PATH entry was configured.")
		writeLine(stdout, "Legacy npm uloop-cli launchers were cleaned up when detected.")
		return
	}

	writeLine(stdout, "Install setup completed.")
}

func printInstallHelp(stdout io.Writer) {
	writeLine(stdout, "Usage:")
	writeLine(stdout, "  uloop install [--dir <install-dir>]")
	writeLine(stdout, "")
	writeLine(stdout, "Configures the global uloop launcher after the installer places the binary.")
	writeLine(stdout, "Set ULOOP_INSTALL_DIR or pass --dir to choose the install directory.")
	writeLine(stdout, "On Windows, updates User PATH and removes legacy npm uloop-cli launchers.")
}

func resolveNativeInstallDir(goos string, explicitInstallDir string) (string, error) {
	if explicitInstallDir != "" {
		return explicitInstallDir, nil
	}
	if installDir := getenv(nativeInstallDirEnvName); installDir != "" {
		return installDir, nil
	}

	switch goos {
	case "darwin":
		home, err := nativeUserHomeDir()
		if err != nil {
			return "", err
		}
		return joinNativeInstallPath(goos, home, ".local", "bin"), nil
	case "windows":
		localAppData := getenv(nativeLocalAppDataEnvName)
		if localAppData == "" {
			return "", errors.New("LOCALAPPDATA is required to resolve the uloop install directory")
		}
		return joinNativeInstallPath(
			goos,
			localAppData,
			nativeWindowsProgramsDir,
			nativeInstallDirectoryName,
			nativeInstallBinDirName), nil
	default:
		return "", errors.New(installUnsupportedOSMessage)
	}
}
