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
$AssetName = "uloop-dispatcher-windows-amd64.zip"

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

function Test-UloopNativeInstallSupported {
    param(
        [Parameter(Mandatory = $true)]
        [string]$UloopPath
    )

    $PreviousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = "Continue"
        & $UloopPath install --help > $null 2> $null
        return $LASTEXITCODE -eq 0
    }
    finally {
        $ErrorActionPreference = $PreviousErrorActionPreference
    }
}

function ConvertTo-NormalizedPath {
    param(
        [string]$Path
    )

    if (-not $Path) {
        return ""
    }

    return $Path.Trim().Trim('"').TrimEnd([char[]]@('\', '/')).Replace('/', '\')
}

function Get-NpmPrefixFromUloopPath {
    param(
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
        [string]$CommandPath
    )

    if (-not $CommandPath) {
        return $false
    }

    if ([string]::Equals([System.IO.Path]::GetExtension($CommandPath), ".exe", [System.StringComparison]::OrdinalIgnoreCase)) {
        return $false
    }

    if (-not (Test-Path $CommandPath -PathType Leaf)) {
        return $false
    }

    $CommandContent = Get-Content -Path $CommandPath -Raw -ErrorAction SilentlyContinue
    if (-not $CommandContent) {
        return $false
    }

    return $CommandContent.Contains("node_modules/uloop-cli") -or $CommandContent.Contains("node_modules\uloop-cli")
}

function Remove-LegacyNpmArtifacts {
    param(
        [string]$LegacyUloopPath,
        [string]$LegacyPrefix
    )

    if ($LegacyUloopPath -and (Test-Path $LegacyUloopPath -PathType Leaf)) {
        Remove-Item -Path $LegacyUloopPath -Force -ErrorAction SilentlyContinue
    }

    if (-not $LegacyPrefix) {
        return
    }

    foreach ($ShimName in @("uloop", "uloop.cmd", "uloop.ps1")) {
        Remove-Item -Path (Join-Path $LegacyPrefix $ShimName) -Force -ErrorAction SilentlyContinue
        Remove-Item -Path (Join-Path (Join-Path $LegacyPrefix "bin") $ShimName) -Force -ErrorAction SilentlyContinue
    }

    foreach ($PackagePath in @(
            (Join-Path (Join-Path $LegacyPrefix "node_modules") "uloop-cli"),
            (Join-Path (Join-Path (Join-Path $LegacyPrefix "lib") "node_modules") "uloop-cli"))) {
        Remove-Item -Path $PackagePath -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Test-LegacyNpmArtifactsExist {
    param(
        [string]$LegacyUloopPath,
        [string]$LegacyPrefix
    )

    if ($LegacyUloopPath -and (Test-Path $LegacyUloopPath -PathType Leaf)) {
        return $true
    }

    if (-not $LegacyPrefix) {
        return $false
    }

    foreach ($ShimName in @("uloop", "uloop.cmd", "uloop.ps1")) {
        if ((Test-Path (Join-Path $LegacyPrefix $ShimName) -PathType Leaf) -or (Test-Path (Join-Path (Join-Path $LegacyPrefix "bin") $ShimName) -PathType Leaf)) {
            return $true
        }
    }

    foreach ($PackagePath in @(
            (Join-Path (Join-Path $LegacyPrefix "node_modules") "uloop-cli"),
            (Join-Path (Join-Path (Join-Path $LegacyPrefix "lib") "node_modules") "uloop-cli"))) {
        if (Test-Path $PackagePath -PathType Container) {
            return $true
        }
    }

    return $false
}

function Invoke-LegacyNpmPackageRemoval {
    param(
        [string]$LegacyUloopPath,
        [string]$ExpectedUloopPath
    )

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
        $NpmArgs = @("uninstall", "-g", "--prefix", $LegacyPrefix, "uloop-cli")
        $null = & $NpmCommand.Source @NpmArgs
    }

    if ((-not $NpmCommand) -or $LASTEXITCODE -ne 0 -or (Test-LegacyNpmArtifactsExist -LegacyUloopPath $LegacyUloopPath -LegacyPrefix $LegacyPrefix)) {
        Remove-LegacyNpmArtifacts -LegacyUloopPath $LegacyUloopPath -LegacyPrefix $LegacyPrefix
    }

    if (Test-LegacyNpmArtifactsExist -LegacyUloopPath $LegacyUloopPath -LegacyPrefix $LegacyPrefix) {
        return $false
    }

    Write-Host "Removed legacy npm package: uloop-cli"
    return $true
}

function Invoke-DefaultLegacyNpmPackageRemoval {
    $NpmCommand = Get-Command npm -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $NpmCommand) {
        return
    }

    $null = & $NpmCommand.Source @("uninstall", "-g", "uloop-cli")
}

function Get-LegacyNpmUloopPathsFromPath {
    $PathValues = @(
        $env:Path,
        [Environment]::GetEnvironmentVariable("Path", "User")
    )
    $LegacyPaths = @()

    foreach ($PathValue in $PathValues) {
        if (-not $PathValue) {
            continue
        }

        foreach ($PathEntry in ($PathValue -split ";")) {
            if (-not $PathEntry) {
                continue
            }

            foreach ($ShimName in @("uloop", "uloop.cmd", "uloop.ps1")) {
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
    param(
        [string]$ExpectedUloopPath
    )

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

function Set-CurrentPathWithInstallDirectoryFirst {
    param(
        [string]$Directory
    )

    $CurrentPathEntries = @()
    if ($env:Path) {
        $CurrentPathEntries = $env:Path -split ";" | Where-Object {
            $_ -and -not [string]::Equals((ConvertTo-NormalizedPath -Path $_), (ConvertTo-NormalizedPath -Path $Directory), [System.StringComparison]::OrdinalIgnoreCase)
        }
    }

    $env:Path = [string]::Join(";", @($Directory) + $CurrentPathEntries)
}

function Set-UserPathWithInstallDirectoryFirst {
    param(
        [string]$Directory
    )

    $UserPath = [Environment]::GetEnvironmentVariable("Path", "User")
    $PathEntries = @()
    if ($UserPath) {
        $PathEntries = $UserPath -split ";" | Where-Object {
            $_ -and -not [string]::Equals((ConvertTo-NormalizedPath -Path $_), (ConvertTo-NormalizedPath -Path $Directory), [System.StringComparison]::OrdinalIgnoreCase)
        }
    }

    $NewUserPath = [string]::Join(";", @($Directory) + $PathEntries)
    if (-not [string]::Equals($UserPath, $NewUserPath, [System.StringComparison]::Ordinal)) {
        [Environment]::SetEnvironmentVariable("Path", $NewUserPath, "User")
        Write-Host "Added $Directory to User PATH. Open a new terminal to use it everywhere."
    }

    Set-CurrentPathWithInstallDirectoryFirst -Directory $Directory
}

function Get-FirstUloopCommandFromPath {
    param(
        [string]$PathValue
    )

    if (-not $PathValue) {
        return $null
    }

    foreach ($PathEntry in ($PathValue -split ";")) {
        if (-not $PathEntry) {
            continue
        }

        foreach ($ShimName in @("uloop.exe", "uloop.cmd", "uloop.ps1", "uloop")) {
            $CandidatePath = Join-Path $PathEntry $ShimName
            if (Test-Path $CandidatePath -PathType Leaf) {
                return $CandidatePath
            }
        }
    }

    return $null
}

function Report-PathShadowing {
    param(
        [string]$Directory,
        [string]$ExpectedUloopPath
    )

    $MachinePath = [Environment]::GetEnvironmentVariable("Path", "Machine")
    $UserPath = [Environment]::GetEnvironmentVariable("Path", "User")
    $ResolvedPath = Get-FirstUloopCommandFromPath -PathValue ([string]::Join(";", @($MachinePath, $UserPath)))
    if (-not $ResolvedPath) {
        return
    }

    if ([string]::Equals($ResolvedPath, $ExpectedUloopPath, [System.StringComparison]::OrdinalIgnoreCase)) {
        return
    }

    Write-Host "Installed uloop to $ExpectedUloopPath, but PATH resolves uloop to:"
    Write-Host "  $ResolvedPath"
    Write-Host "Move $Directory earlier in PATH, or remove the legacy installation if it owns that command."
}

function Invoke-CompatibilityWindowsInstall {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Directory,
        [Parameter(Mandatory = $true)]
        [string]$ExpectedUloopPath
    )

    Write-Host "Configuring global uloop launcher..."
    Set-UserPathWithInstallDirectoryFirst -Directory $Directory
    Invoke-AllLegacyNpmPackageRemoval -ExpectedUloopPath $ExpectedUloopPath | Out-Null

    Report-PathShadowing -Directory $Directory -ExpectedUloopPath $ExpectedUloopPath
    Write-Host "The package-owned User PATH entry was configured."
    Write-Host "Legacy npm uloop-cli launchers were cleaned up when detected."
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
    $NativeInstallSupported = Test-UloopNativeInstallSupported -UloopPath $StagedUloopPath

    $FinalUloopPath = Join-Path $InstallDir "uloop.exe"
    Copy-Item -Path $StagedUloopPath -Destination $FinalUloopPath -Force
    Remove-Item -Path $StagedUloopPath -Force
    $StagedUloopPath = $null

    if ($NativeInstallSupported) {
        Invoke-UloopNativeInstall -UloopPath $FinalUloopPath -Directory $InstallDir
        Set-CurrentPathWithInstallDirectoryFirst -Directory $InstallDir
    }
    else {
        Invoke-CompatibilityWindowsInstall -Directory $InstallDir -ExpectedUloopPath $FinalUloopPath
    }
    Assert-UloopVersionSucceeds -UloopPath $FinalUloopPath
}
finally {
    if ($StagedUloopPath -and (Test-Path $StagedUloopPath)) {
        Remove-Item -Path $StagedUloopPath -Force -ErrorAction SilentlyContinue
    }
    Remove-Item -Path $TempDir -Recurse -Force -ErrorAction SilentlyContinue
}
