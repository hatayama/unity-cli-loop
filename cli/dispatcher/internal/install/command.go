package install

import (
	"encoding/base64"
	"errors"
	"strings"
	"unicode/utf16"

	"github.com/hatayama/unity-cli-loop/dispatcher/internal/nativepath"
)

const (
	UnsupportedOSMessage = "native install is only supported on macOS and Windows"
	PosixCommandName     = "uloop"
	WindowsCommandName   = "uloop.exe"
)

type Options struct {
	InstallDir string
}

type Command struct {
	Name         string
	Args         []string
	TargetPath   string
	InstallDir   string
	UpdatesPath  bool
	CleansLegacy bool
}

func CommandForOS(goos string, options Options) (Command, error) {
	if options.InstallDir == "" {
		return Command{}, errors.New("install directory is required")
	}

	switch goos {
	case "darwin":
		installDir := nativepath.TrimInstallDir(goos, options.InstallDir)
		targetPath := nativepath.CommandPath(goos, installDir, PosixCommandName, WindowsCommandName)
		return Command{
			Name:         "sh",
			Args:         posixInstallArgs(installDir, targetPath),
			TargetPath:   targetPath,
			InstallDir:   installDir,
			UpdatesPath:  true,
			CleansLegacy: true,
		}, nil
	case "windows":
		installDir := nativepath.TrimInstallDir(goos, options.InstallDir)
		targetPath := nativepath.CommandPath(goos, installDir, PosixCommandName, WindowsCommandName)
		return Command{
			Name:         "powershell",
			Args:         windowsInstallArgs(installDir, targetPath),
			TargetPath:   targetPath,
			InstallDir:   installDir,
			UpdatesPath:  true,
			CleansLegacy: true,
		}, nil
	default:
		return Command{}, errors.New(UnsupportedOSMessage)
	}
}

func windowsInstallArgs(installDir string, targetPath string) []string {
	return []string{
		"-NoProfile",
		"-ExecutionPolicy",
		"Bypass",
		"-EncodedCommand",
		encodePowerShellCommand(windowsInstallScript(installDir, targetPath)),
	}
}

func windowsInstallScript(installDir string, targetPath string) string {
	return strings.NewReplacer(
		"'{{INSTALL_DIR}}'", powerShellSingleQuote(installDir),
		"'{{EXPECTED_ULOOP_PATH}}'", powerShellSingleQuote(targetPath),
	).Replace(installScriptTemplate("scripts/install_windows.ps1"))
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
