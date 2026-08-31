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

function Get-UloopDispatcherReleaseTag {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Version
    )

    if ($Version.StartsWith("dispatcher-v", [System.StringComparison]::Ordinal)) {
        return $Version
    }

    if ($Version.StartsWith("v", [System.StringComparison]::Ordinal)) {
        return "dispatcher-$Version"
    }

    return "dispatcher-v$Version"
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

$ReleaseTag = Get-UloopDispatcherReleaseTag -Version $Version

foreach ($InstallerScript in $InstallerScripts) {
    $Uri = Get-ReleaseInstallerScriptUrl -ReleaseTag $ReleaseTag -ScriptName $InstallerScript
    Assert-RemoteInstallerScriptExists -Uri $Uri
}

Write-Host "Release installer scripts are reachable for $ReleaseTag."
