<#
Verifies that Windows PowerShell native argument escaping preserves backslashes
while protecting embedded quotation marks for execute-dynamic-code requests.
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

[string[]]$ScriptPaths = @(
    "scripts/run-windows-e2e.ps1",
    "scripts/test-simulate-mouse-demo.ps1",
    "Assets/Tests/Demo/scripts/verify-replay-via-cli.ps1"
)

function Get-FunctionDefinition {
    param(
        [string]$ScriptPath,
        [string]$FunctionName
    )

    [string]$source = Get-Content -LiteralPath $ScriptPath -Raw -Encoding UTF8
    [string]$marker = "function $FunctionName"
    [int]$start = $source.IndexOf($marker, [System.StringComparison]::Ordinal)
    if ($start -lt 0) {
        throw "${ScriptPath} does not define ${FunctionName}."
    }

    [int]$openBrace = $source.IndexOf("{", $start, [System.StringComparison]::Ordinal)
    [int]$depth = 0
    for ([int]$index = $openBrace; $index -lt $source.Length; $index++) {
        if ($source[$index] -eq "{") {
            $depth++
        }
        elseif ($source[$index] -eq "}") {
            $depth--
            if ($depth -eq 0) {
                return $source.Substring($start, $index - $start + 1)
            }
        }
    }

    throw "${ScriptPath} has an unclosed ${FunctionName} definition."
}

function Assert-Equal {
    param(
        [string]$Actual,
        [string]$Expected,
        [string]$Context
    )

    if ($Actual -ne $Expected) {
        throw "${Context}: expected [$Expected], actual [$Actual]."
    }
}

[string[]]$definitions = @($ScriptPaths | ForEach-Object {
    Get-FunctionDefinition -ScriptPath $_ -FunctionName "ConvertTo-WindowsPowerShellNativeArgument"
})

foreach ($definition in @($definitions | Select-Object -Skip 1)) {
    Assert-Equal -Actual $definition -Expected $definitions[0] -Context "All E2E scripts must use the same native argument escaping implementation"
}

[string]$temporaryFunctionPath = Join-Path ([System.IO.Path]::GetTempPath()) "uloop-native-argument-escaping.ps1"
Set-Content -LiteralPath $temporaryFunctionPath -Value $definitions[0] -Encoding UTF8
. $temporaryFunctionPath
Remove-Item -LiteralPath $temporaryFunctionPath

Assert-Equal -Actual (ConvertTo-WindowsPowerShellNativeArgument -Argument '\"') -Expected '\\\"' -Context "Existing escaped quote"
Assert-Equal -Actual (ConvertTo-WindowsPowerShellNativeArgument -Argument 'C:\Users\Example\') -Expected 'C:\Users\Example\\' -Context "Trailing backslash"
Assert-Equal -Actual (ConvertTo-WindowsPowerShellNativeArgument -Argument '\\\"') -Expected '\\\\\\\"' -Context "Consecutive backslashes before quote"
Assert-Equal -Actual (ConvertTo-WindowsPowerShellNativeArgument -Argument 'C:\Users\Example\Project') -Expected 'C:\Users\Example\Project' -Context "Ordinary Windows path"
Assert-Equal -Actual (ConvertTo-WindowsPowerShellNativeArgument -Argument '日本語\パス"値') -Expected '日本語\パス\"値' -Context "Japanese text"

Write-Host "PowerShell native argument escaping tests passed."
