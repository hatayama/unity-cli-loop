package uninstall

import (
	"encoding/base64"
	"errors"
	"strconv"
	"strings"
	"unicode/utf16"

	"github.com/hatayama/unity-cli-loop/dispatcher/internal/nativepath"
)

const (
	UnsupportedOSMessage = "native uninstall is only supported on macOS and Windows"
	PosixCommandName     = "uloop"
	WindowsCommandName   = "uloop.exe"
)

type Options struct {
	InstallDir string
	CurrentPID int
}

type Command struct {
	Name       string
	Args       []string
	TargetPath string
	Deferred   bool
}

func CommandForOS(goos string, options Options) (Command, error) {
	if options.InstallDir == "" {
		return Command{}, errors.New("install directory is required")
	}

	switch goos {
	case "darwin":
		targetPath := nativepath.CommandPath(goos, options.InstallDir, PosixCommandName, WindowsCommandName)
		return Command{
			Name:       "sh",
			Args:       posixUninstallArgs(targetPath),
			TargetPath: targetPath,
		}, nil
	case "windows":
		if options.CurrentPID <= 0 {
			return Command{}, errors.New("current process id is required")
		}
		targetPath := nativepath.CommandPath(goos, options.InstallDir, PosixCommandName, WindowsCommandName)
		return Command{
			Name:       "powershell",
			Args:       windowsUninstallArgs(targetPath, options.CurrentPID),
			TargetPath: targetPath,
			Deferred:   true,
		}, nil
	default:
		return Command{}, errors.New(UnsupportedOSMessage)
	}
}

func posixUninstallArgs(targetPath string) []string {
	return []string{
		"-c",
		posixUninstallScript(targetPath),
	}
}

func posixUninstallScript(targetPath string) string {
	return strings.NewReplacer(
		"'{{TARGET_PATH}}'", shellQuote(targetPath),
	).Replace(uninstallScriptTemplate("scripts/uninstall_darwin.sh"))
}

func windowsUninstallArgs(targetPath string, currentPID int) []string {
	deleteScript := windowsDeletionScript(targetPath, currentPID)
	launchScript := windowsLaunchScript(encodePowerShellCommand(deleteScript))
	return []string{
		"-NoProfile",
		"-ExecutionPolicy",
		"Bypass",
		"-EncodedCommand",
		encodePowerShellCommand(launchScript),
	}
}

func windowsLaunchScript(encodedDeletionScript string) string {
	return strings.NewReplacer(
		"'{{ENCODED_DELETION}}'", powerShellSingleQuote(encodedDeletionScript),
	).Replace(uninstallScriptTemplate("scripts/uninstall_windows_launch.ps1"))
}

func windowsDeletionScript(targetPath string, currentPID int) string {
	return strings.NewReplacer(
		"'{{TARGET_PATH}}'", powerShellSingleQuote(targetPath),
		"{{CURRENT_PID}}", strconv.Itoa(currentPID),
	).Replace(uninstallScriptTemplate("scripts/uninstall_windows_delete.ps1"))
}

func encodePowerShellCommand(script string) string {
	encoded := utf16.Encode([]rune(script))
	bytes := make([]byte, 0, len(encoded)*2)
	for _, value := range encoded {
		bytes = append(bytes, byte(value), byte(value>>8))
	}
	return base64.StdEncoding.EncodeToString(bytes)
}

func powerShellSingleQuote(value string) string {
	return "'" + strings.ReplaceAll(value, "'", "''") + "'"
}

func shellQuote(value string) string {
	return "'" + strings.ReplaceAll(value, "'", "'\"'\"'") + "'"
}
