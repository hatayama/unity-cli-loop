# Verifies Windows CLI binaries embed VERSIONINFO matching the stamped release versions.
$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

[string]$RepoRoot = Split-Path -Parent $PSScriptRoot
[string]$DispatcherContractPath = Join-Path $RepoRoot "cli/dispatcher/dispatchercontract/dispatcher-contract.json"
[string]$ProjectRunnerContractPath = Join-Path $RepoRoot "cli/common/clicontract/contract.json"
[string]$DispatcherExePath = Join-Path $RepoRoot "dist/windows-amd64/uloop.exe"
[string]$ProjectRunnerExePath = Join-Path $RepoRoot "dist/windows-amd64/uloop-project-runner.exe"

function Read-JsonObject {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    [string]$JsonText = Get-Content -LiteralPath $Path -Raw -Encoding UTF8
    return $JsonText | ConvertFrom-Json
}

function Assert-WindowsVersionInfo {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ExePath,
        [Parameter(Mandatory = $true)]
        [string]$ExpectedProductVersion
    )

    if (-not (Test-Path -LiteralPath $ExePath)) {
        Write-Error "Windows binary is missing: $ExePath"
        exit 1
    }

    $VersionInfo = (Get-Item -LiteralPath $ExePath).VersionInfo
    [string]$ProductVersion = [string]$VersionInfo.ProductVersion
    [string]$ProductName = [string]$VersionInfo.ProductName
    [string]$CompanyName = [string]$VersionInfo.CompanyName
    [string]$FileDescription = [string]$VersionInfo.FileDescription

    if ($ProductVersion -ne $ExpectedProductVersion) {
        Write-Error "ProductVersion mismatch for $ExePath. Expected '$ExpectedProductVersion', got '$ProductVersion'."
        exit 1
    }

    if ($ProductName -ne "uloop") {
        Write-Error "ProductName mismatch for $ExePath. Expected 'uloop', got '$ProductName'."
        exit 1
    }

    if ($CompanyName -ne "unity-cli-loop") {
        Write-Error "CompanyName mismatch for $ExePath. Expected 'unity-cli-loop', got '$CompanyName'."
        exit 1
    }

    if ([string]::IsNullOrWhiteSpace($FileDescription)) {
        Write-Error "FileDescription is empty for $ExePath."
        exit 1
    }

    Write-Host "VERSIONINFO ok: $ExePath ProductVersion=$ProductVersion ProductName=$ProductName CompanyName=$CompanyName FileDescription=$FileDescription"
}

$DispatcherContract = Read-JsonObject -Path $DispatcherContractPath
$ProjectRunnerContract = Read-JsonObject -Path $ProjectRunnerContractPath
[string]$ExpectedDispatcherVersion = [string]$DispatcherContract.dispatcherVersion
[string]$ExpectedProjectRunnerVersion = [string]$ProjectRunnerContract.projectRunnerVersion

if ([string]::IsNullOrWhiteSpace($ExpectedDispatcherVersion) -or $ExpectedDispatcherVersion -eq "null") {
    Write-Error "Could not resolve dispatcherVersion from $DispatcherContractPath."
    exit 1
}

if ([string]::IsNullOrWhiteSpace($ExpectedProjectRunnerVersion) -or $ExpectedProjectRunnerVersion -eq "null") {
    Write-Error "Could not resolve projectRunnerVersion from $ProjectRunnerContractPath."
    exit 1
}

Assert-WindowsVersionInfo -ExePath $DispatcherExePath -ExpectedProductVersion $ExpectedDispatcherVersion
Assert-WindowsVersionInfo -ExePath $ProjectRunnerExePath -ExpectedProductVersion $ExpectedProjectRunnerVersion
