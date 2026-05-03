$ErrorActionPreference = "Stop"

$Repository = "hatayama/unity-cli-loop"
$LegacyNpmPackage = "uloop-cli"
$Version = if ($env:ULOOP_VERSION) { $env:ULOOP_VERSION } else { "latest" }
$InstallDir = if ($env:ULOOP_INSTALL_DIR) {
    $env:ULOOP_INSTALL_DIR
} else {
    Join-Path $env:LOCALAPPDATA "Programs\uloop\bin"
}
$AssetName = "uloop-windows-amd64.zip"
$LegacyCleanupFailed = $false

if ($Version -eq "latest") {
    $DownloadUrl = "https://github.com/$Repository/releases/latest/download/$AssetName"
} else {
    $DownloadUrl = "https://github.com/$Repository/releases/download/$Version/$AssetName"
}
$ChecksumUrl = "$DownloadUrl.sha256"

function Test-RemoveLegacyEnabled {
    if (-not $env:ULOOP_REMOVE_LEGACY) {
        return $false
    }

    $EnabledValues = @("1", "true", "yes")
    return $EnabledValues -contains $env:ULOOP_REMOVE_LEGACY.ToLowerInvariant()
}

function Get-NpmCommand {
    $CommandNames = @("npm.cmd", "npm.exe", "npm")
    foreach ($CommandName in $CommandNames) {
        $Command = Get-Command $CommandName -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($Command) {
            return $Command.Source
        }
    }

    return $null
}

function Test-LegacyNpmInstalled {
    $NpmCommand = Get-NpmCommand
    if (-not $NpmCommand) {
        return $false
    }

    & $NpmCommand list -g $LegacyNpmPackage --depth=0 > $null 2> $null
    return $LASTEXITCODE -eq 0
}

function Remove-LegacyNpmIfEnabled {
    if (-not (Test-LegacyNpmInstalled)) {
        return
    }

    if (Test-RemoveLegacyEnabled) {
        $NpmCommand = Get-NpmCommand
        Write-Host "Removing legacy npm installation: $LegacyNpmPackage"
        & $NpmCommand uninstall -g $LegacyNpmPackage
        if ($LASTEXITCODE -ne 0) {
            $script:LegacyCleanupFailed = $true
            Write-Warning "Could not remove legacy npm installation: $LegacyNpmPackage"
            Write-Host "To remove it manually, run:"
            Write-Host "  npm uninstall -g $LegacyNpmPackage"
        }
    }
}

function Confirm-ActiveUloopAfterLegacyCleanup {
    if (-not $script:LegacyCleanupFailed) {
        return
    }

    $ResolvedCommand = Get-Command uloop -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $ResolvedCommand) {
        return
    }

    $ExpectedUloop = Join-Path $InstallDir "uloop.exe"
    if ([string]::Equals($ResolvedCommand.Source, $ExpectedUloop, [System.StringComparison]::OrdinalIgnoreCase)) {
        return
    }

    throw "Failed to remove legacy npm installation, and PATH still resolves uloop to $($ResolvedCommand.Source). The native dispatcher was installed to $ExpectedUloop, but running uloop may still use the legacy command. Remove the legacy package manually, or move $InstallDir earlier in PATH."
}

function Write-LegacyNpmWarningIfPresent {
    if ((Test-RemoveLegacyEnabled) -or (-not (Test-LegacyNpmInstalled))) {
        return
    }

    Write-Host "Legacy npm installation detected: $LegacyNpmPackage"
    Write-Host "The native dispatcher was installed, but the npm package may still provide an older uloop command."
    Write-Host "To remove it, run:"
    Write-Host "  npm uninstall -g $LegacyNpmPackage"
    Write-Host "Or rerun this installer with:"
    Write-Host "  `$env:ULOOP_REMOVE_LEGACY = `"1`""
}

function Report-PathShadowing {
    $ResolvedCommand = Get-Command uloop -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $ResolvedCommand) {
        return
    }

    $ExpectedUloop = Join-Path $InstallDir "uloop.exe"
    if ([string]::Equals($ResolvedCommand.Source, $ExpectedUloop, [System.StringComparison]::OrdinalIgnoreCase)) {
        return
    }

    Write-Host "Installed uloop to $ExpectedUloop, but PATH resolves uloop to:"
    Write-Host "  $($ResolvedCommand.Source)"
    Write-Host "Move $InstallDir earlier in PATH, or remove the legacy installation if it owns that command."
}

function Assert-UloopVersionSucceeds {
    param(
        [Parameter(Mandatory = $true)]
        [string]$UloopPath,
        [switch]$Quiet
    )

    if ($Quiet) {
        & $UloopPath --version > $null
    }
    else {
        & $UloopPath --version
    }

    if ($LASTEXITCODE -ne 0) {
        throw "uloop binary verification failed for $UloopPath"
    }
}

$TempDir = Join-Path ([System.IO.Path]::GetTempPath()) ("uloop-install-" + [System.Guid]::NewGuid().ToString("N"))
$StagedUloopPath = $null
New-Item -ItemType Directory -Path $TempDir | Out-Null

try {
    $ArchivePath = Join-Path $TempDir $AssetName
    $ChecksumPath = Join-Path $TempDir "$AssetName.sha256"
    Invoke-WebRequest -Uri $DownloadUrl -OutFile $ArchivePath
    Invoke-WebRequest -Uri $ChecksumUrl -OutFile $ChecksumPath
    $ExpectedHash = ((Get-Content -Path $ChecksumPath -Raw) -split "\s+")[0].ToLowerInvariant()
    $ActualHash = (Get-FileHash -Path $ArchivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($ExpectedHash -ne $ActualHash) {
        throw "Checksum mismatch for $AssetName"
    }

    Expand-Archive -Path $ArchivePath -DestinationPath $TempDir -Force

    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
    $StagedUloopPath = Join-Path $InstallDir ("uloop-install-" + [System.Guid]::NewGuid().ToString("N") + ".exe")
    Copy-Item -Path (Join-Path $TempDir "uloop.exe") -Destination $StagedUloopPath -Force
    Assert-UloopVersionSucceeds -UloopPath $StagedUloopPath -Quiet
    Remove-LegacyNpmIfEnabled
    $FinalUloopPath = Join-Path $InstallDir "uloop.exe"
    Copy-Item -Path $StagedUloopPath -Destination $FinalUloopPath -Force
    Remove-Item -Path $StagedUloopPath -Force
    $StagedUloopPath = $null

    $UserPath = [Environment]::GetEnvironmentVariable("Path", "User")
    $PathEntries = @()
    if ($UserPath) {
        $PathEntries = $UserPath -split ";"
    }

    if ($PathEntries -notcontains $InstallDir) {
        $NewUserPath = if ($UserPath) { "$UserPath;$InstallDir" } else { $InstallDir }
        [Environment]::SetEnvironmentVariable("Path", $NewUserPath, "User")
        $env:Path = "$env:Path;$InstallDir"
        Write-Host "Added $InstallDir to User PATH. Open a new terminal to use it everywhere."
    }

    Assert-UloopVersionSucceeds -UloopPath $FinalUloopPath
    Confirm-ActiveUloopAfterLegacyCleanup
    Write-LegacyNpmWarningIfPresent
    Report-PathShadowing
}
finally {
    if ($StagedUloopPath -and (Test-Path $StagedUloopPath)) {
        Remove-Item -Path $StagedUloopPath -Force -ErrorAction SilentlyContinue
    }
    Remove-Item -Path $TempDir -Recurse -Force -ErrorAction SilentlyContinue
}
