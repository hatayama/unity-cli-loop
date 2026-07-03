package uninstall

import (
	"encoding/base64"
	"errors"
	"fmt"
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
		`$Target = %s
$ParentPid = %d
$InstallDir = Split-Path -Parent $Target
$NormalizePath = {
    param([string]$Path)
    if (-not $Path) {
        return ''
    }
    return $Path.Trim().Trim('"').TrimEnd([char[]]@('\','/')).Replace('/','\')
}
$ParentProcess = Get-Process -Id $ParentPid -ErrorAction SilentlyContinue
if ($ParentProcess) {
    $ParentProcess | Wait-Process -ErrorAction SilentlyContinue
}
$UserPath = [Environment]::GetEnvironmentVariable('Path', 'User')
if ($UserPath) {
    $NormalizedInstallDir = & $NormalizePath $InstallDir
    $PathEntries = $UserPath -split ';' | Where-Object { $_ -and -not [string]::Equals((& $NormalizePath $_), $NormalizedInstallDir, [System.StringComparison]::OrdinalIgnoreCase) }
    $NewUserPath = [string]::Join(';', $PathEntries)
    if (-not [string]::Equals($UserPath, $NewUserPath, [System.StringComparison]::Ordinal)) {
        [Environment]::SetEnvironmentVariable('Path', $NewUserPath, 'User')
    }
}
if (Test-Path -LiteralPath $Target) {
    Remove-Item -LiteralPath $Target -Force -ErrorAction SilentlyContinue
}
`,
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

func shellQuote(value string) string {
	return "'" + strings.ReplaceAll(value, "'", "'\"'\"'") + "'"
}
