$ErrorActionPreference = "Stop"

$Repository = "hatayama/unity-cli-loop"
$Version = if ($env:ULOOP_VERSION) { $env:ULOOP_VERSION } else { "latest" }
$InstallDir = if ($env:ULOOP_INSTALL_DIR) {
    $env:ULOOP_INSTALL_DIR
} else {
    Join-Path $env:LOCALAPPDATA "Programs\uloop\bin"
}
$AssetName = "uloop-windows-amd64.zip"

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

function Test-LegacyTypeScriptLauncherShimContent {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Content
    )

    return $Content.IndexOf("node_modules/uloop-cli", [System.StringComparison]::OrdinalIgnoreCase) -ge 0 `
        -or $Content.IndexOf("node_modules\uloop-cli", [System.StringComparison]::OrdinalIgnoreCase) -ge 0
}

function Test-NativeUloopShimContent {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Content,
        [Parameter(Mandatory = $true)]
        [string]$NativeUloopPath
    )

    return $Content.IndexOf($NativeUloopPath, [System.StringComparison]::OrdinalIgnoreCase) -ge 0 `
        -or $Content.IndexOf("Programs\uloop\bin\uloop.exe", [System.StringComparison]::OrdinalIgnoreCase) -ge 0 `
        -or $Content.IndexOf("Programs/uloop/bin/uloop.exe", [System.StringComparison]::OrdinalIgnoreCase) -ge 0 `
        -or (
            ($Content.IndexOf("Cli~\Dispatcher~\dist", [System.StringComparison]::OrdinalIgnoreCase) -ge 0 `
                -or $Content.IndexOf("Cli~/Dispatcher~/dist", [System.StringComparison]::OrdinalIgnoreCase) -ge 0 `
                -or $Content.IndexOf("GoCli~\dist", [System.StringComparison]::OrdinalIgnoreCase) -ge 0 `
                -or $Content.IndexOf("GoCli~/dist", [System.StringComparison]::OrdinalIgnoreCase) -ge 0) `
            -and (
                $Content.IndexOf("uloop-dispatcher.exe", [System.StringComparison]::OrdinalIgnoreCase) -ge 0 `
                    -or $Content.IndexOf("uloop-dispatcher", [System.StringComparison]::OrdinalIgnoreCase) -ge 0
            )
        )
}

function Test-PackageOwnedUloopShimContent {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Content,
        [Parameter(Mandatory = $true)]
        [string]$NativeUloopPath
    )

    return (Test-LegacyTypeScriptLauncherShimContent -Content $Content) `
        -or (Test-NativeUloopShimContent -Content $Content -NativeUloopPath $NativeUloopPath)
}

function Remove-PathEntry {
    param(
        [AllowNull()]
        [AllowEmptyString()]
        [string]$PathValue,
        [Parameter(Mandatory = $true)]
        [string]$EntryToRemove
    )

    if (-not $PathValue) {
        return ""
    }

    $FilteredEntries = @()
    foreach ($PathEntry in ($PathValue -split ";")) {
        if (-not $PathEntry) {
            continue
        }

        if ([string]::Equals($PathEntry, $EntryToRemove, [System.StringComparison]::OrdinalIgnoreCase)) {
            continue
        }

        $FilteredEntries += $PathEntry
    }

    return ($FilteredEntries -join ";")
}

function Remove-LegacyUloopShims {
    param(
        [Parameter(Mandatory = $true)]
        [string]$NativeUloopPath
    )

    if (-not $env:APPDATA) {
        return
    }

    $LegacyBinDir = Join-Path $env:APPDATA "npm"
    if (-not (Test-Path $LegacyBinDir -PathType Container)) {
        return
    }

    $ShimNames = @("uloop", "uloop.cmd", "uloop.ps1")
    foreach ($ShimName in $ShimNames) {
        $ShimPath = Join-Path $LegacyBinDir $ShimName
        if (-not (Test-Path $ShimPath -PathType Leaf)) {
            continue
        }

        $ShimContent = Get-Content -LiteralPath $ShimPath -Raw -ErrorAction SilentlyContinue
        if (-not $ShimContent) {
            continue
        }

        if (-not (Test-PackageOwnedUloopShimContent -Content $ShimContent -NativeUloopPath $NativeUloopPath)) {
            continue
        }

        Remove-Item -LiteralPath $ShimPath -Force -ErrorAction SilentlyContinue
        if (-not (Test-Path $ShimPath -PathType Leaf)) {
            Write-Host "Removed legacy uloop shim: $ShimPath"
        }
    }
}

function Test-LegacyBinContainsCommandEntries {
    param(
        [Parameter(Mandatory = $true)]
        [string]$LegacyBinDir
    )

    if (-not (Test-Path $LegacyBinDir -PathType Container)) {
        return $false
    }

    foreach ($Entry in (Get-ChildItem -LiteralPath $LegacyBinDir -Force)) {
        if ($Entry.PSIsContainer -and [string]::Equals($Entry.Name, "node_modules", [System.StringComparison]::OrdinalIgnoreCase)) {
            continue
        }

        return $true
    }

    return $false
}

function Remove-UnusedLegacyBinPath {
    if (-not $env:APPDATA) {
        return
    }

    $LegacyBinDir = Join-Path $env:APPDATA "npm"
    if (Test-LegacyBinContainsCommandEntries -LegacyBinDir $LegacyBinDir) {
        return
    }

    $UserPath = [Environment]::GetEnvironmentVariable("Path", "User")
    $UpdatedUserPath = Remove-PathEntry -PathValue $UserPath -EntryToRemove $LegacyBinDir
    if ($UserPath -and (-not [string]::Equals($UserPath, $UpdatedUserPath, [System.StringComparison]::OrdinalIgnoreCase))) {
        [Environment]::SetEnvironmentVariable("Path", $UpdatedUserPath, "User")
        Write-Host "Removed unused legacy command bin directory from User PATH: $LegacyBinDir"
    }

    $UpdatedProcessPath = Remove-PathEntry -PathValue $env:Path -EntryToRemove $LegacyBinDir
    if ($env:Path -and (-not [string]::Equals($env:Path, $UpdatedProcessPath, [System.StringComparison]::OrdinalIgnoreCase))) {
        $env:Path = $UpdatedProcessPath
    }
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
    if (Test-RemoveLegacyEnabled) {
        Remove-LegacyUloopShims -NativeUloopPath $FinalUloopPath
        Remove-UnusedLegacyBinPath
    }
    Report-PathShadowing
}
finally {
    if ($StagedUloopPath -and (Test-Path $StagedUloopPath)) {
        Remove-Item -Path $StagedUloopPath -Force -ErrorAction SilentlyContinue
    }
    Remove-Item -Path $TempDir -Recurse -Force -ErrorAction SilentlyContinue
}
