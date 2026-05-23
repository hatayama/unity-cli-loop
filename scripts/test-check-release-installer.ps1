$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$ScriptPath = Join-Path $PSScriptRoot "check-release-installer.ps1"
$script:RequestedUris = @()

function Invoke-WebRequest {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Uri,
        [switch]$UseBasicParsing
    )

    $script:RequestedUris += $Uri
    return [pscustomobject]@{
        Content = "mock installer script"
    }
}

function Assert-Contains {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Values,
        [Parameter(Mandatory = $true)]
        [string]$Expected
    )

    if ($Values -notcontains $Expected) {
        throw "Expected value was not requested: $Expected`nActual values:`n$($Values -join "`n")"
    }
}

# Verifies plain CLI versions are normalized to release tags before checking raw installer scripts.
. $ScriptPath -Version "3.0.0-beta.99" -Repository "owner/repo" -RawBaseUrl "https://raw.example.test/"
Assert-Contains -Values $script:RequestedUris -Expected "https://raw.example.test/owner/repo/cli-v3.0.0-beta.99/scripts/install.ps1"
Assert-Contains -Values $script:RequestedUris -Expected "https://raw.example.test/owner/repo/cli-v3.0.0-beta.99/scripts/install.sh"

$script:RequestedUris = @()

# Verifies explicit CLI release tags are not prefixed twice.
. $ScriptPath -Version "cli-v3.0.0" -Repository "owner/repo" -RawBaseUrl "https://raw.example.test/"
Assert-Contains -Values $script:RequestedUris -Expected "https://raw.example.test/owner/repo/cli-v3.0.0/scripts/install.ps1"
Assert-Contains -Values $script:RequestedUris -Expected "https://raw.example.test/owner/repo/cli-v3.0.0/scripts/install.sh"

Write-Host "check-release-installer tests passed."
