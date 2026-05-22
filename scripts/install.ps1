$ErrorActionPreference = "Stop"

$Repository = "hatayama/unity-cli-loop"
$Version = if ($env:ULOOP_VERSION) { $env:ULOOP_VERSION } else { "latest" }
$LatestVersion = "latest"
$LatestBetaVersion = "latest-beta"
$InstallDir = if ($env:ULOOP_INSTALL_DIR) {
    $env:ULOOP_INSTALL_DIR
} else {
    Join-Path $env:LOCALAPPDATA "Programs\uloop\bin"
}
$AssetName = "uloop-windows-amd64.zip"

function Find-LatestAssetUrl {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ReleaseChannel
    )

    $Page = 1

    while ($true) {
        $Releases = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repository/releases?per_page=100&page=$Page"
        foreach ($Release in $Releases) {
            if ($Release.draft) {
                continue
            }

            if ($ReleaseChannel -eq "stable" -and $Release.prerelease) {
                continue
            }

            if ($ReleaseChannel -eq "beta" `
                -and (-not $Release.prerelease -or -not $Release.tag_name.ToLowerInvariant().Contains("-beta."))) {
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

if ($Version -eq $LatestVersion -or $Version -eq $LatestBetaVersion) {
    $ReleaseChannel = if ($Version -eq $LatestBetaVersion) { "beta" } else { "stable" }
    $DownloadUrl = Find-LatestAssetUrl -ReleaseChannel $ReleaseChannel
    if (-not $DownloadUrl) {
        throw "Could not find a $Version release asset named $AssetName. Set ULOOP_VERSION to a release tag that provides this asset."
    }
} else {
    $DownloadUrl = "https://github.com/$Repository/releases/download/$Version/$AssetName"
}
$ChecksumUrl = "$DownloadUrl.sha256"

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

function Get-UloopSha256Hash {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $Sha256 = [System.Security.Cryptography.SHA256]::Create()
    $Stream = [System.IO.File]::OpenRead($Path)
    try {
        $HashBytes = $Sha256.ComputeHash($Stream)
    }
    finally {
        $Stream.Dispose()
        $Sha256.Dispose()
    }

    return ([System.BitConverter]::ToString($HashBytes) -replace "-", "").ToLowerInvariant()
}

function Expand-UloopArchive {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ArchivePath,
        [Parameter(Mandatory = $true)]
        [string]$DestinationPath
    )

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::ExtractToDirectory($ArchivePath, $DestinationPath)
}

function Invoke-UloopNativeInstall {
    param(
        [Parameter(Mandatory = $true)]
        [string]$UloopPath,
        [Parameter(Mandatory = $true)]
        [string]$Directory
    )

    $NativeInstallArgs = @("install", "--dir", $Directory)
    & $UloopPath @NativeInstallArgs
    if ($LASTEXITCODE -ne 0) {
        throw "uloop native install setup failed for $UloopPath"
    }
}

$TempDir = Join-Path ([System.IO.Path]::GetTempPath()) ("uloop-stage-" + [System.Guid]::NewGuid().ToString("N"))
$StagedUloopPath = $null
New-Item -ItemType Directory -Path $TempDir | Out-Null

try {
    $ArchivePath = Join-Path $TempDir $AssetName
    $ChecksumPath = Join-Path $TempDir "$AssetName.sha256"
    Invoke-WebRequest -Uri $DownloadUrl -OutFile $ArchivePath
    Invoke-WebRequest -Uri $ChecksumUrl -OutFile $ChecksumPath
    $ExpectedHash = ((Get-Content -Path $ChecksumPath -Raw) -split "\s+")[0].ToLowerInvariant()
    $ActualHash = Get-UloopSha256Hash -Path $ArchivePath
    if ($ExpectedHash -ne $ActualHash) {
        throw "Checksum mismatch for $AssetName"
    }

    Expand-UloopArchive -ArchivePath $ArchivePath -DestinationPath $TempDir

    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
    $StagedUloopPath = Join-Path $InstallDir ("uloop-staged-" + [System.Guid]::NewGuid().ToString("N") + ".exe")
    Copy-Item -Path (Join-Path $TempDir "uloop.exe") -Destination $StagedUloopPath -Force
    Assert-UloopVersionSucceeds -UloopPath $StagedUloopPath -Quiet

    $FinalUloopPath = Join-Path $InstallDir "uloop.exe"
    Copy-Item -Path $StagedUloopPath -Destination $FinalUloopPath -Force
    Remove-Item -Path $StagedUloopPath -Force
    $StagedUloopPath = $null

    Invoke-UloopNativeInstall -UloopPath $FinalUloopPath -Directory $InstallDir
    Assert-UloopVersionSucceeds -UloopPath $FinalUloopPath
}
finally {
    if ($StagedUloopPath -and (Test-Path $StagedUloopPath)) {
        Remove-Item -Path $StagedUloopPath -Force -ErrorAction SilentlyContinue
    }
    Remove-Item -Path $TempDir -Recurse -Force -ErrorAction SilentlyContinue
}
