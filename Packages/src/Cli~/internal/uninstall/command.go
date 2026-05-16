package uninstall

import (
	"encoding/base64"
	"errors"
	"fmt"
	"path"
	"strings"
	"unicode/utf16"
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
		targetPath := path.Join(options.InstallDir, PosixCommandName)
		script := "rm -f " + shellQuote(targetPath)
		return Command{
			Name:       "sh",
			Args:       []string{"-c", script},
			TargetPath: targetPath,
		}, nil
	case "windows":
		if options.CurrentPID <= 0 {
			return Command{}, errors.New("current process id is required")
		}
		targetPath := windowsTargetPath(options.InstallDir)
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

func windowsUninstallArgs(targetPath string, currentPID int) []string {
	deleteScript := windowsDeletionScript(targetPath, currentPID)
	launchScript := fmt.Sprintf(
		"$EncodedDeletion = '%s'\nStart-Process -FilePath 'powershell' -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-EncodedCommand',$EncodedDeletion) -WindowStyle Hidden\n",
		encodePowerShellCommand(deleteScript),
	)
	return []string{
		"-NoProfile",
		"-ExecutionPolicy",
		"Bypass",
		"-EncodedCommand",
		encodePowerShellCommand(launchScript),
	}
}

func windowsDeletionScript(targetPath string, currentPID int) string {
	return fmt.Sprintf(
		"$Target = %s\n$ParentPid = %d\nWait-Process -Id $ParentPid -ErrorAction SilentlyContinue\nif (Test-Path -LiteralPath $Target) {\n    Remove-Item -LiteralPath $Target -Force -ErrorAction SilentlyContinue\n}\n",
		powerShellSingleQuote(targetPath),
		currentPID,
	)
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

func windowsTargetPath(installDir string) string {
	trimmed := strings.TrimRight(installDir, `\/`)
	return trimmed + `\` + WindowsCommandName
}

func shellQuote(value string) string {
	return "'" + strings.ReplaceAll(value, "'", "'\"'\"'") + "'"
}
