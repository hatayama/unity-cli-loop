param(
    [Parameter(Mandatory = $true)]
    [string]$Version,
    [string]$Repository = "hatayama/unity-cli-loop",
    [string]$RawBaseUrl = "https://raw.githubusercontent.com"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$InstallerScripts = @(
    "install.ps1",
    "install.sh"
)

function Get-UloopCliReleaseTag {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Version
    )

    if ($Version.StartsWith("cli-v", [System.StringComparison]::Ordinal)) {
        return $Version
    }

    if ($Version.StartsWith("v", [System.StringComparison]::Ordinal)) {
        return "cli-$Version"
    }

    return "cli-v$Version"
}

function Get-ReleaseInstallerScriptUrl {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ReleaseTag,
        [Parameter(Mandatory = $true)]
        [string]$ScriptName
    )

    $NormalizedRawBaseUrl = $RawBaseUrl -replace "/+$", ""
    return "$NormalizedRawBaseUrl/$Repository/$ReleaseTag/scripts/$ScriptName"
}

function Assert-RemoteInstallerScriptExists {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Uri
    )

    $Response = Invoke-WebRequest -Uri $Uri -UseBasicParsing
    $Content = [string]$Response.Content

    if ([string]::IsNullOrWhiteSpace($Content)) {
        throw "Release installer script is empty: $Uri"
    }

    Write-Host "Verified release installer script: $Uri"
}

$ReleaseTag = Get-UloopCliReleaseTag -Version $Version

foreach ($InstallerScript in $InstallerScripts) {
    $Uri = Get-ReleaseInstallerScriptUrl -ReleaseTag $ReleaseTag -ScriptName $InstallerScript
    Assert-RemoteInstallerScriptExists -Uri $Uri
}

Write-Host "Release installer scripts are reachable for $ReleaseTag."
