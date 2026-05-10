$ErrorActionPreference = "Stop"

$Repository = "hatayama/unity-cli-loop"
$Version = if ($env:ULOOP_VERSION) { $env:ULOOP_VERSION } else { "latest" }
$InstallDir = if ($env:ULOOP_INSTALL_DIR) {
    $env:ULOOP_INSTALL_DIR
} else {
    Join-Path $env:LOCALAPPDATA "Programs\uloop\bin"
}
$AssetName = "uloop-windows-amd64.zip"

function Find-LatestAssetUrl {
    $Page = 1

    while ($true) {
        $Releases = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repository/releases?per_page=100&page=$Page"
        foreach ($Release in $Releases) {
            if ($Release.draft -or $Release.prerelease) {
                continue
            }

            foreach ($Asset in $Release.assets) {
                if ([string]::Equals($Asset.name, $AssetName, [System.StringComparison]::Ordinal)) {
                    return $Asset.browser_download_url
                }
            }
        }

        if ($Releases.Count -lt 100) {
            return $null
        }

        $Page += 1
    }
}

if ($Version -eq "latest") {
    $DownloadUrl = Find-LatestAssetUrl
    if (-not $DownloadUrl) {
        throw "Could not find a latest release asset named $AssetName. Set ULOOP_VERSION to a release tag that provides this asset."
    }
} else {
    $DownloadUrl = "https://github.com/$Repository/releases/download/$Version/$AssetName"
}
$ChecksumUrl = "$DownloadUrl.sha256"

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

function Get-NpmPrefixFromUloopPath {
    param(
        [AllowNull()]
        [AllowEmptyString()]
        [string]$CommandPath
    )

    if (-not $CommandPath) {
        return $null
    }

    $CommandDirectory = Split-Path -Parent $CommandPath
    if (-not $CommandDirectory) {
        return $null
    }

    $DirectoryName = Split-Path -Leaf $CommandDirectory
    if ([string]::Equals($DirectoryName, "bin", [System.StringComparison]::OrdinalIgnoreCase)) {
        return Split-Path -Parent $CommandDirectory
    }

    return $CommandDirectory
}

function Test-LegacyNpmUloopPath {
    param(
        [AllowNull()]
        [AllowEmptyString()]
        [string]$CommandPath
    )

    if (-not $CommandPath) {
        return $false
    }

    if (-not (Test-Path $CommandPath -PathType Leaf)) {
        return $false
    }

    $CommandContent = Get-Content -Path $CommandPath -Raw
    return $CommandContent.Contains("node_modules/uloop-cli") `
        -or $CommandContent.Contains("node_modules\uloop-cli")
}

function Write-LegacyNpmManualRemoval {
    param(
        [AllowNull()]
        [AllowEmptyString()]
        [string]$LegacyUloopPath,
        [AllowNull()]
        [AllowEmptyString()]
        [string]$LegacyPrefix
    )

    Write-Host "Could not remove the legacy npm package automatically."
    if ($LegacyUloopPath) {
        Write-Host "Legacy uloop command: $LegacyUloopPath"
    }

    if ($LegacyPrefix) {
        Write-Host "Run this manually if that command still shadows the native dispatcher:"
        Write-Host "  npm uninstall -g --prefix `"$LegacyPrefix`" uloop-cli"
        return
    }

    Write-Host "Run this manually if the old npm command still shadows the native dispatcher:"
    Write-Host "  npm uninstall -g uloop-cli"
}

function Invoke-LegacyNpmPackageRemoval {
    param(
        [AllowNull()]
        [AllowEmptyString()]
        [string]$LegacyUloopPath,
        [Parameter(Mandatory = $true)]
        [string]$ExpectedUloopPath
    )

    if ($LegacyUloopPath `
        -and [string]::Equals($LegacyUloopPath, $ExpectedUloopPath, [System.StringComparison]::OrdinalIgnoreCase)) {
        return
    }

    $NpmCommand = Get-Command npm -ErrorAction SilentlyContinue | Select-Object -First 1
    $LegacyPrefix = $null
    $LegacyCommandShadowsNative = $LegacyUloopPath `
        -and -not [string]::Equals($LegacyUloopPath, $ExpectedUloopPath, [System.StringComparison]::OrdinalIgnoreCase)
    if ($LegacyCommandShadowsNative) {
        $LegacyPrefix = Get-NpmPrefixFromUloopPath -CommandPath $LegacyUloopPath
    }

    if (-not $NpmCommand) {
        Write-LegacyNpmManualRemoval -LegacyUloopPath $LegacyUloopPath -LegacyPrefix $LegacyPrefix
        return
    }

    $NpmArgs = @("uninstall", "-g", "uloop-cli")
    if ($LegacyPrefix) {
        $NpmArgs = @("uninstall", "-g", "--prefix", $LegacyPrefix, "uloop-cli")
    }

    & $NpmCommand.Source @NpmArgs
    if ($LASTEXITCODE -ne 0) {
        Write-LegacyNpmManualRemoval -LegacyUloopPath $LegacyUloopPath -LegacyPrefix $LegacyPrefix
        return
    }

    if ($LegacyCommandShadowsNative -and (Test-Path $LegacyUloopPath -PathType Leaf)) {
        Write-LegacyNpmManualRemoval -LegacyUloopPath $LegacyUloopPath -LegacyPrefix $LegacyPrefix
        return
    }

    Write-Host "Removed legacy npm package: uloop-cli"
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
$LegacyUloopBeforeInstallCommand = Get-Command uloop -ErrorAction SilentlyContinue | Select-Object -First 1
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
    $FinalUloopPath = Join-Path $InstallDir "uloop.exe"
    $LegacyUloopBeforeInstallPath = $LegacyUloopBeforeInstallCommand.Source
    $LegacyNpmRemovedBeforeInstall = $false
    if ($LegacyUloopBeforeInstallPath `
        -and [string]::Equals($LegacyUloopBeforeInstallPath, $FinalUloopPath, [System.StringComparison]::OrdinalIgnoreCase)) {
        if (Test-LegacyNpmUloopPath -CommandPath $LegacyUloopBeforeInstallPath) {
            Invoke-LegacyNpmPackageRemoval -LegacyUloopPath $LegacyUloopBeforeInstallPath -ExpectedUloopPath ""
            $LegacyNpmRemovedBeforeInstall = $true
        }

        $LegacyUloopBeforeInstallPath = $null
    }
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
    if (-not $LegacyNpmRemovedBeforeInstall) {
        Invoke-LegacyNpmPackageRemoval -LegacyUloopPath $LegacyUloopBeforeInstallPath -ExpectedUloopPath $FinalUloopPath
    }

    Report-PathShadowing
}
finally {
    if ($StagedUloopPath -and (Test-Path $StagedUloopPath)) {
        Remove-Item -Path $StagedUloopPath -Force -ErrorAction SilentlyContinue
    }
    Remove-Item -Path $TempDir -Recurse -Force -ErrorAction SilentlyContinue
}
