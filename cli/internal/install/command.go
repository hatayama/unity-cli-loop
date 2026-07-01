package install

import (
	"encoding/base64"
	"errors"
	"fmt"
	"path"
	"strings"
	"unicode/utf16"
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
		installDir := trimPosixInstallDir(options.InstallDir)
		targetPath := path.Join(installDir, PosixCommandName)
		return Command{
			Name:         "sh",
			Args:         posixInstallArgs(installDir, targetPath),
			TargetPath:   targetPath,
			InstallDir:   installDir,
			UpdatesPath:  true,
			CleansLegacy: true,
		}, nil
	case "windows":
		installDir := strings.TrimRight(options.InstallDir, `\/`)
		targetPath := installDir + `\` + WindowsCommandName
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

func trimPosixInstallDir(installDir string) string {
	trimmedInstallDir := strings.TrimRight(installDir, `/`)
	if trimmedInstallDir == "" {
		return `/`
	}
	return trimmedInstallDir
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
	return fmt.Sprintf(
		`$InstallDir = %s
$ExpectedUloopPath = %s
$NormalizePath = {
    param([string]$Path)
    if (-not $Path) {
        return ''
    }
    return $Path.Trim().Trim('"').TrimEnd([char[]]@('\','/')).Replace('/','\')
}
function Get-NpmPrefixFromUloopPath {
    param([string]$CommandPath)
    if (-not $CommandPath) {
        return $null
    }
    $CommandDirectory = Split-Path -Parent $CommandPath
    if (-not $CommandDirectory) {
        return $null
    }
    $DirectoryName = Split-Path -Leaf $CommandDirectory
    if ([string]::Equals($DirectoryName, 'bin', [System.StringComparison]::OrdinalIgnoreCase)) {
        return Split-Path -Parent $CommandDirectory
    }
    return $CommandDirectory
}
function Test-LegacyNpmUloopPath {
    param([string]$CommandPath)
    if (-not $CommandPath) {
        return $false
    }
    if ([string]::Equals([System.IO.Path]::GetExtension($CommandPath), '.exe', [System.StringComparison]::OrdinalIgnoreCase)) {
        return $false
    }
    if (-not (Test-Path $CommandPath -PathType Leaf)) {
        return $false
    }
    $CommandContent = Get-Content -Path $CommandPath -Raw
    return $CommandContent.Contains('node_modules/uloop-cli') -or $CommandContent.Contains('node_modules\uloop-cli')
}
function Write-LegacyNpmMultilineArgumentWarning {
    Write-Host 'Legacy npm shims can alter multiline PowerShell arguments before the native CLI receives them.'
}
function Remove-LegacyNpmArtifacts {
    param([string]$LegacyUloopPath, [string]$LegacyPrefix)
    if ($LegacyUloopPath -and (Test-Path $LegacyUloopPath -PathType Leaf)) {
        Remove-Item -Path $LegacyUloopPath -Force -ErrorAction SilentlyContinue
    }
    if (-not $LegacyPrefix) {
        return
    }
    foreach ($ShimName in @('uloop', 'uloop.cmd', 'uloop.ps1')) {
        Remove-Item -Path (Join-Path $LegacyPrefix $ShimName) -Force -ErrorAction SilentlyContinue
        Remove-Item -Path (Join-Path (Join-Path $LegacyPrefix 'bin') $ShimName) -Force -ErrorAction SilentlyContinue
    }
    foreach ($PackagePath in @(
            (Join-Path (Join-Path $LegacyPrefix 'node_modules') 'uloop-cli'),
            (Join-Path (Join-Path (Join-Path $LegacyPrefix 'lib') 'node_modules') 'uloop-cli'))) {
        Remove-Item -Path $PackagePath -Recurse -Force -ErrorAction SilentlyContinue
    }
}
function Test-LegacyNpmArtifactsExist {
    param([string]$LegacyUloopPath, [string]$LegacyPrefix)
    if ($LegacyUloopPath -and (Test-Path $LegacyUloopPath -PathType Leaf)) {
        return $true
    }
    if (-not $LegacyPrefix) {
        return $false
    }
    foreach ($ShimName in @('uloop', 'uloop.cmd', 'uloop.ps1')) {
        if ((Test-Path (Join-Path $LegacyPrefix $ShimName) -PathType Leaf) -or (Test-Path (Join-Path (Join-Path $LegacyPrefix 'bin') $ShimName) -PathType Leaf)) {
            return $true
        }
    }
    foreach ($PackagePath in @(
            (Join-Path (Join-Path $LegacyPrefix 'node_modules') 'uloop-cli'),
            (Join-Path (Join-Path (Join-Path $LegacyPrefix 'lib') 'node_modules') 'uloop-cli'))) {
        if (Test-Path $PackagePath -PathType Container) {
            return $true
        }
    }
    return $false
}
function Invoke-LegacyNpmPackageRemoval {
    param([string]$LegacyUloopPath, [string]$ExpectedUloopPath)
    if ($LegacyUloopPath -and [string]::Equals($LegacyUloopPath, $ExpectedUloopPath, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $true
    }
    if ($LegacyUloopPath -and -not (Test-Path $LegacyUloopPath -PathType Leaf)) {
        return $true
    }
    $NpmCommand = Get-Command npm -ErrorAction SilentlyContinue | Select-Object -First 1
    $LegacyPrefix = $null
    $LegacyCommandShadowsNative = $LegacyUloopPath -and -not [string]::Equals($LegacyUloopPath, $ExpectedUloopPath, [System.StringComparison]::OrdinalIgnoreCase)
    $LegacyCommandIsNpmShim = $LegacyCommandShadowsNative -and (Test-LegacyNpmUloopPath -CommandPath $LegacyUloopPath)
    if ($LegacyCommandIsNpmShim) {
        $LegacyPrefix = Get-NpmPrefixFromUloopPath -CommandPath $LegacyUloopPath
    }
    if (-not $LegacyPrefix) {
        return $false
    }
    if ($NpmCommand) {
        $NpmArgs = @('uninstall', '-g', '--prefix', $LegacyPrefix, 'uloop-cli')
        $null = & $NpmCommand.Source @NpmArgs
    }
    if ((-not $NpmCommand) -or $LASTEXITCODE -ne 0 -or (Test-LegacyNpmArtifactsExist -LegacyUloopPath $LegacyUloopPath -LegacyPrefix $LegacyPrefix)) {
        Remove-LegacyNpmArtifacts -LegacyUloopPath $LegacyUloopPath -LegacyPrefix $LegacyPrefix
    }
    if (Test-LegacyNpmArtifactsExist -LegacyUloopPath $LegacyUloopPath -LegacyPrefix $LegacyPrefix) {
        return $false
    }
    Write-Host 'Removed legacy npm package: uloop-cli'
    return $true
}
function Invoke-DefaultLegacyNpmPackageRemoval {
    $NpmCommand = Get-Command npm -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $NpmCommand) {
        return
    }
    $null = & $NpmCommand.Source @('uninstall', '-g', 'uloop-cli')
}
function Get-LegacyNpmUloopPathsFromPath {
    $PathValues = @(
        $env:Path,
        [Environment]::GetEnvironmentVariable('Path', 'User')
    )
    $LegacyPaths = @()
    foreach ($PathValue in $PathValues) {
        if (-not $PathValue) {
            continue
        }
        foreach ($PathEntry in ($PathValue -split ';')) {
            if (-not $PathEntry) {
                continue
            }
            foreach ($ShimName in @('uloop', 'uloop.cmd', 'uloop.ps1')) {
                $CandidatePath = Join-Path $PathEntry $ShimName
                if (-not (Test-LegacyNpmUloopPath -CommandPath $CandidatePath)) {
                    continue
                }
                $AlreadyAdded = $false
                foreach ($LegacyPath in $LegacyPaths) {
                    if ([string]::Equals($LegacyPath, $CandidatePath, [System.StringComparison]::OrdinalIgnoreCase)) {
                        $AlreadyAdded = $true
                        break
                    }
                }
                if (-not $AlreadyAdded) {
                    $LegacyPaths += $CandidatePath
                }
            }
        }
    }
    return $LegacyPaths
}
function Invoke-AllLegacyNpmPackageRemoval {
    param([string]$ExpectedUloopPath)
    $RemovedAll = $true
    foreach ($LegacyUloopPath in (Get-LegacyNpmUloopPathsFromPath)) {
        if (-not (Invoke-LegacyNpmPackageRemoval -LegacyUloopPath $LegacyUloopPath -ExpectedUloopPath $ExpectedUloopPath)) {
            $RemovedAll = $false
        }
    }
    Invoke-DefaultLegacyNpmPackageRemoval
    foreach ($LegacyUloopPath in (Get-LegacyNpmUloopPathsFromPath)) {
        if (-not [string]::Equals($LegacyUloopPath, $ExpectedUloopPath, [System.StringComparison]::OrdinalIgnoreCase)) {
            $RemovedAll = $false
        }
    }
    return $RemovedAll
}
function Set-UserPathWithInstallDirectoryFirst {
    param([string]$Directory)
    $UserPath = [Environment]::GetEnvironmentVariable('Path', 'User')
    $PathEntries = @()
    if ($UserPath) {
        $PathEntries = $UserPath -split ';' | Where-Object {
            $_ -and -not [string]::Equals((& $NormalizePath $_), (& $NormalizePath $Directory), [System.StringComparison]::OrdinalIgnoreCase)
        }
    }
    $NewUserPath = [string]::Join(';', @($Directory) + $PathEntries)
    if (-not [string]::Equals($UserPath, $NewUserPath, [System.StringComparison]::Ordinal)) {
        [Environment]::SetEnvironmentVariable('Path', $NewUserPath, 'User')
        Write-Host "Added $Directory to User PATH. Open a new terminal to use it everywhere."
    }
    $CurrentPathEntries = @()
    if ($env:Path) {
        $CurrentPathEntries = $env:Path -split ';' | Where-Object {
            $_ -and -not [string]::Equals((& $NormalizePath $_), (& $NormalizePath $Directory), [System.StringComparison]::OrdinalIgnoreCase)
        }
    }
    $env:Path = [string]::Join(';', @($Directory) + $CurrentPathEntries)
}
function Get-FirstUloopCommandFromPath {
    param([string]$PathValue)
    if (-not $PathValue) {
        return $null
    }
    foreach ($PathEntry in ($PathValue -split ';')) {
        if (-not $PathEntry) {
            continue
        }
        foreach ($ShimName in @('uloop.exe', 'uloop.cmd', 'uloop.ps1', 'uloop')) {
            $CandidatePath = Join-Path $PathEntry $ShimName
            if (Test-Path $CandidatePath -PathType Leaf) {
                return $CandidatePath
            }
        }
    }
    return $null
}
function Report-PathShadowing {
    $MachinePath = [Environment]::GetEnvironmentVariable('Path', 'Machine')
    $UserPath = [Environment]::GetEnvironmentVariable('Path', 'User')
    $ResolvedPath = Get-FirstUloopCommandFromPath -PathValue ([string]::Join(';', @($MachinePath, $UserPath)))
    if (-not $ResolvedPath) {
        return
    }
    if ([string]::Equals($ResolvedPath, $ExpectedUloopPath, [System.StringComparison]::OrdinalIgnoreCase)) {
        return
    }
    Write-Host "Installed uloop to $ExpectedUloopPath, but PATH resolves uloop to:"
    Write-Host "  $ResolvedPath"
    if (Test-LegacyNpmUloopPath -CommandPath $ResolvedPath) {
        Write-LegacyNpmMultilineArgumentWarning
    }
    Write-Host "Move $InstallDir earlier in PATH, or remove the legacy installation if it owns that command."
}
Set-UserPathWithInstallDirectoryFirst -Directory $InstallDir
Invoke-AllLegacyNpmPackageRemoval -ExpectedUloopPath $ExpectedUloopPath | Out-Null
Report-PathShadowing
`,
		powerShellSingleQuote(installDir),
		powerShellSingleQuote(targetPath),
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
