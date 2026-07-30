<#
Verifies install.ps1 pin-manifest helpers via AST extraction: document parse,
manifest format (including uppercase hex), ref format, and Resolve-UloopManifestFromPin
with Get-UloopPinJson shadowed for success / fetch-failure / version-mismatch.
Windows PowerShell 5.1 compatible — no ternary, null-coalescing, or ForEach -Parallel.
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$InstallScriptPath = Join-Path $PSScriptRoot "install.ps1"
$Hex64Lower = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
$Hex64Upper = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"
$Hex63 = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"

function Get-InstallScriptFunction {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FunctionName
    )

    $Tokens = $null
    $Errors = $null
    $Ast = [System.Management.Automation.Language.Parser]::ParseFile(
        $InstallScriptPath,
        [ref]$Tokens,
        [ref]$Errors
    )
    if ($Errors -and $Errors.Count -gt 0) {
        throw "Failed to parse install.ps1: $($Errors[0].Message)"
    }

    $FunctionAst = $Ast.FindAll(
        {
            param($Node)
            return (
                $Node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
                $Node.Name -eq $FunctionName
            )
        },
        $true
    ) | Select-Object -First 1

    if ($null -eq $FunctionAst) {
        throw "install.ps1 does not define function $FunctionName"
    }

    return $FunctionAst.Extent.Text
}

function Assert-True {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Label,
        [Parameter(Mandatory = $true)]
        [scriptblock]$Script
    )

    & $Script
    if (-not $?) {
        throw "FAIL: $Label — expected success"
    }
}

function Assert-Throws {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Label,
        [Parameter(Mandatory = $true)]
        [scriptblock]$Script
    )

    $Threw = $false
    try {
        & $Script
    } catch {
        $Threw = $true
    }
    if (-not $Threw) {
        throw "FAIL: $Label — expected failure"
    }
}

# Script-scope values the extracted helpers close over / read.
$Repository = "hatayama/unity-cli-loop"
$LatestVersion = "latest"
$LatestBetaVersion = "latest-beta"
$DefaultPinRef = "main"
$PinRef = $DefaultPinRef
$Version = "latest"
$ResolvedArchiveManifest = $null

. ([scriptblock]::Create((Get-InstallScriptFunction -FunctionName "Test-UloopVersionFormat")))
. ([scriptblock]::Create((Get-InstallScriptFunction -FunctionName "Test-UloopRefFormat")))
. ([scriptblock]::Create((Get-InstallScriptFunction -FunctionName "ConvertFrom-UloopPinDocument")))
. ([scriptblock]::Create((Get-InstallScriptFunction -FunctionName "Test-UloopPinManifestFormat")))
. ([scriptblock]::Create((Get-InstallScriptFunction -FunctionName "Resolve-UloopManifestFromPin")))

$PinJson = @"
{
  "dispatcherArchiveManifest": "$Hex64Lower  uloop-dispatcher-windows-amd64.zip\n$Hex64Lower  install.ps1",
  "dispatcherReleaseTag": "dispatcher-v3.1.0-beta.16",
  "minimumDispatcherVersion": "3.0.0-beta.19",
  "projectRunnerVersion": "3.0.0-beta.64"
}
"@

$Document = ConvertFrom-UloopPinDocument -JsonText $PinJson
if ($Document.dispatcherReleaseTag -ne "dispatcher-v3.1.0-beta.16") {
    throw "FAIL: unexpected dispatcherReleaseTag: $($Document.dispatcherReleaseTag)"
}
$Expanded = [string]$Document.dispatcherArchiveManifest
$Lines = @($Expanded -split "`n" | Where-Object { -not [string]::IsNullOrEmpty($_) })
if ($Lines.Count -ne 2) {
    throw "FAIL: expected 2 expanded manifest lines, got $($Lines.Count)"
}
if ($Lines[0] -ne "$Hex64Lower  uloop-dispatcher-windows-amd64.zip") {
    throw "FAIL: unexpected first expanded line: $($Lines[0])"
}

Assert-Throws "missing dispatcherArchiveManifest" {
    $Missing = '{"dispatcherReleaseTag":"dispatcher-v3.1.0-beta.16"}'
    $Parsed = ConvertFrom-UloopPinDocument -JsonText $Missing
    if ([string]::IsNullOrEmpty([string]$Parsed.dispatcherArchiveManifest)) {
        throw "missing dispatcherArchiveManifest"
    }
}

Assert-Throws "reject 63-digit digest" {
    Test-UloopPinManifestFormat -Manifest "$Hex63  install.ps1"
}
Assert-Throws "reject single-space separator" {
    Test-UloopPinManifestFormat -Manifest "$Hex64Lower install.ps1"
}
Assert-True "accept uppercase hex digest" {
    Test-UloopPinManifestFormat -Manifest "$Hex64Upper  install.ps1"
}
Assert-Throws "reject duplicate filenames" {
    Test-UloopPinManifestFormat -Manifest "$Hex64Lower  install.ps1`n$Hex64Upper  install.ps1"
}
Assert-Throws "reject empty manifest" {
    Test-UloopPinManifestFormat -Manifest ""
}
Assert-Throws "reject CR in expanded manifest" {
    Test-UloopPinManifestFormat -Manifest ("$Hex64Lower  install.ps1" + "`r")
}

Assert-True "accept v3-beta ref" { Test-UloopRefFormat -Candidate "v3-beta" }
Assert-True "accept main ref" { Test-UloopRefFormat -Candidate "main" }
Assert-True "accept release/1.0 ref" { Test-UloopRefFormat -Candidate "release/1.0" }
Assert-Throws "reject empty ref" { Test-UloopRefFormat -Candidate "" }
Assert-Throws "reject path traversal ref" { Test-UloopRefFormat -Candidate "../evil" }
Assert-Throws "reject space in ref" { Test-UloopRefFormat -Candidate "a b" }

# Shadow Get-UloopPinJson for Resolve-UloopManifestFromPin cases.
function Get-UloopPinJson {
    if ($script:MockPinFetchFail) {
        throw "mock pin fetch failure"
    }
    return $script:MockPinJsonText
}

$env:ULOOP_ARCHIVE_MANIFEST = $null
Remove-Item Env:ULOOP_ARCHIVE_MANIFEST -ErrorAction SilentlyContinue

# Success: latest → pin tag, ResolvedArchiveManifest populated.
$script:MockPinFetchFail = $false
$script:MockPinJsonText = $PinJson
$Version = "latest"
$ResolvedArchiveManifest = $null
$PinRef = "main"
Resolve-UloopManifestFromPin
if ($Version -ne "dispatcher-v3.1.0-beta.16") {
    throw "FAIL: expected Version to become pin tag, got $Version"
}
if ([string]::IsNullOrEmpty($ResolvedArchiveManifest)) {
    throw "FAIL: expected ResolvedArchiveManifest to be set"
}

# Fetch failure mentions ULOOP_ARCHIVE_MANIFEST.
$script:MockPinFetchFail = $true
$Version = "latest"
$ResolvedArchiveManifest = $null
Assert-Throws "pin fetch failure" {
    Resolve-UloopManifestFromPin
}

# Version mismatch against pin tag.
$script:MockPinFetchFail = $false
$Version = "dispatcher-v9.9.9"
$ResolvedArchiveManifest = $null
Assert-Throws "pin version mismatch" {
    Resolve-UloopManifestFromPin
}

Write-Host "OK"
