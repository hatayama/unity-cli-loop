package dispatcher

import (
	"bytes"
	"context"
	"errors"
	"io"
	"os/exec"
	"runtime"
	"strings"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	"github.com/hatayama/unity-cli-loop/dispatcher/internal/install"
	"github.com/hatayama/unity-cli-loop/dispatcher/internal/nativepath"
)

const (
	installDirFlagName          = "dir"
	installUnsupportedOSMessage = install.UnsupportedOSMessage
)

type installOptions struct {
	installDir string
}

func tryHandleInstallRequest(ctx context.Context, args []string, stdout io.Writer, stderr io.Writer) (bool, int) {
	if len(args) == 0 || args[0] != clicore.InstallCommandName {
		return false, 0
	}
	if clicore.ContainsHelpRequest(args[1:]) {
		printInstallHelp(stdout)
		return true, 0
	}
	options, err := parseInstallOptions(args[1:])
	if err != nil {
		clierrors.WriteClassifiedError(stderr, err, clierrors.ErrorContext{Command: clicore.InstallCommandName})
		return true, 1
	}

	installDir, err := resolveNativeInstallDir(runtime.GOOS, options.installDir)
	if err != nil {
		clierrors.WriteClassifiedError(stderr, wrapUnsupportedPlatformError(err), clierrors.ErrorContext{Command: clicore.InstallCommandName})
		return true, 1
	}
	installCommand, err := install.CommandForOS(runtime.GOOS, install.Options{
		InstallDir: installDir,
	})
	if err != nil {
		clierrors.WriteClassifiedError(stderr, wrapUnsupportedPlatformError(err), clierrors.ErrorContext{Command: clicore.InstallCommandName})
		return true, 1
	}

	clicore.WriteLine(stdout, "Configuring global uloop dispatcher...")
	command := exec.CommandContext(ctx, installCommand.Name, installCommand.Args...)
	command.Stdout = stdout
	var installerStderr bytes.Buffer
	command.Stderr = &installerStderr
	if err := command.Run(); err != nil {
		clierrors.WriteErrorEnvelope(stderr, installSetupFailureError(err, installerStderr.String()))
		return true, 1
	}

	writeInstallCompletion(stdout, runtime.GOOS)
	return true, 0
}

func installSetupFailureError(err error, installerStderr string) clierrors.CLIError {
	details := map[string]any{
		"Cause": err.Error(),
	}
	if stderrText := strings.TrimSpace(installerStderr); stderrText != "" {
		details["InstallerStderr"] = stderrText
	}

	return clierrors.CLIError{
		ErrorCode:   clierrors.ErrorCodeInternalError,
		Phase:       clierrors.ErrorPhaseExecution,
		Message:     "Install setup failed: " + err.Error(),
		Retryable:   true,
		SafeToRetry: true,
		Command:     clicore.InstallCommandName,
		NextActions: []string{"Retry `uloop install --dir <install-dir>` after checking PATH permissions."},
		Details:     details,
	}
}

func parseInstallOptions(args []string) (installOptions, error) {
	options := installOptions{}
	for index := 0; index < len(args); index++ {
		arg := args[index]
		if arg == "-d" {
			if options.installDir != "" {
				return installOptions{}, duplicateInstallDirOptionError(arg)
			}
			if index+1 >= len(args) || clicore.IsNextOptionToken(args[index+1]) {
				return installOptions{}, clierrors.MissingValueArgumentError(arg)
			}
			options.installDir = args[index+1]
			index++
			continue
		}

		name, value, consumedNext, err := clicore.ParseFlagValue(arg, args, index)
		if err != nil {
			return installOptions{}, err
		}
		if name != installDirFlagName {
			return installOptions{}, &clierrors.ArgumentError{
				Message:     "Unknown install option: --" + name,
				Option:      "--" + name,
				Command:     clicore.InstallCommandName,
				NextActions: []string{"Run `uloop install` or `uloop install --dir <install-dir>`."},
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
	return &clierrors.ArgumentError{
		Message:     "Duplicate install option: " + option,
		Option:      option,
		Command:     clicore.InstallCommandName,
		NextActions: []string{"Pass the install directory only once."},
	}
}

func writeInstallCompletion(stdout io.Writer, goos string) {
	if goos == "windows" {
		clicore.WriteLine(stdout, "The package-owned User PATH entry was configured.")
		clicore.WriteLine(stdout, "Legacy npm uloop-cli launchers were cleaned up when detected.")
		return
	}
	if goos == "darwin" {
		clicore.WriteLine(stdout, "The package-owned shell PATH entry was configured.")
		clicore.WriteLine(stdout, "Legacy npm uloop-cli launchers were cleaned up when detected.")
		return
	}

	clicore.WriteLine(stdout, "Install setup completed.")
}

func printInstallHelp(stdout io.Writer) {
	clicore.WriteLine(stdout, "Usage:")
	clicore.WriteLine(stdout, "  uloop install [--dir <install-dir>]")
	clicore.WriteLine(stdout, "")
	clicore.WriteLine(stdout, "Configures the global uloop dispatcher after the installer places the binary.")
	clicore.WriteLine(stdout, "Set ULOOP_INSTALL_DIR or pass --dir to choose the install directory.")
	clicore.WriteLine(stdout, "On Windows, updates User PATH and removes legacy npm uloop-cli launchers.")
	clicore.WriteLine(stdout, "On macOS, updates shell PATH and removes legacy npm uloop-cli launchers.")
}

func resolveNativeInstallDir(goos string, explicitInstallDir string) (string, error) {
	installDir, err := nativepath.ResolveInstallDir(goos, explicitInstallDir, nativepath.DefaultEnvironment())
	if errors.Is(err, nativepath.ErrUnsupportedOS) {
		return "", errors.New(installUnsupportedOSMessage)
	}
	return installDir, err
}
