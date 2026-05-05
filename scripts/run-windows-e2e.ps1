<#
Runs the terminal-driven E2E coverage from Windows PowerShell.

Usage:
  powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run-windows-e2e.ps1
  powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run-windows-e2e.ps1 -ProjectPath C:\path\to\project
#>

param(
    [string]$ProjectPath = "",
    [int]$StressRounds = 3,
    [switch]$SkipLaunchSmoke
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$DiscoveryScenePath = "Assets/Scenes/SimulateMouseDemoScene.unity"
$KeyboardScenePath = "Assets/Scenes/SimulateKeyboardDemoScene.unity"
$RunTestsFilter = "io.github.hatayama.UnityCliLoop.Tests.Editor.CliVersionComparerTests.IsVersionGreaterThanOrEqual_WhenVersionIsInvalid_ReturnsFalse"

function Get-ResolvedProjectPath {
    if (-not [string]::IsNullOrWhiteSpace($ProjectPath)) {
        return (Resolve-Path -LiteralPath $ProjectPath).Path
    }

    return (Resolve-Path -LiteralPath ".").Path
}

$ResolvedProjectPath = Get-ResolvedProjectPath

function Resolve-ProjectRelativePath {
    param(
        [string]$RelativePath
    )

    if ([System.IO.Path]::IsPathRooted($RelativePath)) {
        return [System.IO.Path]::GetFullPath($RelativePath)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $ResolvedProjectPath $RelativePath))
}

function Get-UloopArguments {
    param(
        [string[]]$CommandArguments
    )

    [string[]]$arguments = @($CommandArguments)
    if (-not [string]::IsNullOrWhiteSpace($ResolvedProjectPath)) {
        $arguments += @("--project-path", $ResolvedProjectPath)
    }

    return $arguments
}

function Invoke-UloopCapture {
    param(
        [string[]]$CommandArguments
    )

    [string[]]$arguments = Get-UloopArguments -CommandArguments $CommandArguments
    if ($PSVersionTable.PSVersion.Major -lt 6) {
        # Windows PowerShell strips embedded quotes before native processes see them.
        $arguments = @($arguments | ForEach-Object { $_.Replace('"', '\"') })
    }

    [string]$previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        [object[]]$output = & uloop @arguments 2>&1
        [int]$exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    [string]$text = ($output | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine
    return [pscustomobject]@{
        Arguments = $arguments
        ExitCode = $exitCode
        Text = $text
    }
}

function Invoke-UloopChecked {
    param(
        [string[]]$CommandArguments
    )

    [pscustomobject]$result = Invoke-UloopCapture -CommandArguments $CommandArguments
    if ($result.ExitCode -eq 0) {
        return $result.Text
    }

    if (-not [string]::IsNullOrWhiteSpace($result.Text)) {
        Write-Host $result.Text
    }

    [string]$commandText = "uloop " + ($result.Arguments -join " ")
    throw "$commandText failed with exit code $($result.ExitCode)"
}

function Invoke-UloopJsonChecked {
    param(
        [string[]]$CommandArguments
    )

    [string]$text = Invoke-UloopChecked -CommandArguments $CommandArguments
    [pscustomobject]$json = $text | ConvertFrom-Json
    if ($json.PSObject.Properties.Name -contains "Success" -and $json.Success -ne $true) {
        throw "uloop $($CommandArguments -join " ") returned Success=false: $text"
    }

    return $json
}

function Wait-UnityReady {
    for ([int]$attempt = 0; $attempt -lt 30; $attempt++) {
        [pscustomobject]$result = Invoke-UloopCapture -CommandArguments @("get-logs", "--max-count", "1")
        if ($result.ExitCode -eq 0) {
            return
        }

        Start-Sleep -Seconds 2
    }

    throw "Unity did not become ready"
}

function Get-UnityProcessIdsForProject {
    [int[]]$processIds = @()
    [object[]]$processes = @(Get-CimInstance Win32_Process -Filter "Name = 'Unity.exe'" | Where-Object { $_.CommandLine })
    foreach ($process in $processes) {
        [string]$commandLine = $process.CommandLine
        if ($commandLine.IndexOf($ResolvedProjectPath, [System.StringComparison]::OrdinalIgnoreCase) -lt 0) {
            continue
        }

        $processIds += [int]$process.ProcessId
    }

    return $processIds
}

function Wait-UnityStopped {
    for ([int]$attempt = 0; $attempt -lt 60; $attempt++) {
        [int[]]$processIds = @(Get-UnityProcessIdsForProject)
        if ($processIds.Count -eq 0) {
            return
        }

        Start-Sleep -Seconds 1
    }

    [string]$remainingProcessIds = (@(Get-UnityProcessIdsForProject)) -join ", "
    throw "Unity did not stop for ${ResolvedProjectPath}: PID $remainingProcessIds"
}

function Wait-PlayMode {
    for ([int]$attempt = 0; $attempt -lt 30; $attempt++) {
        [pscustomobject]$probe = Invoke-UloopCapture -CommandArguments @(
            "execute-dynamic-code",
            "--code",
            "using UnityEngine; return Application.isPlaying;"
        )

        if ($probe.ExitCode -ne 0) {
            Start-Sleep -Seconds 1
            continue
        }

        [pscustomobject]$result = $probe.Text | ConvertFrom-Json
        [string]$isPlaying = ""
        if ($null -ne $result.Result) {
            $isPlaying = $result.Result.ToString()
        }

        if ($result.Success -eq $true -and $isPlaying -eq "True") {
            return
        }

        Start-Sleep -Seconds 1
    }

    throw "Unity did not enter PlayMode"
}

function Ensure-UnityReady {
    [pscustomobject]$result = Invoke-UloopCapture -CommandArguments @("get-logs", "--max-count", "1")
    if ($result.ExitCode -eq 0) {
        return
    }

    Invoke-UloopChecked -CommandArguments @("launch") | Out-Host
    Wait-UnityReady
}

function Invoke-DynamicCode {
    param(
        [string]$Code
    )

    return Invoke-UloopJsonChecked -CommandArguments @("execute-dynamic-code", "--code", $Code)
}

function Open-Scene {
    param(
        [string]$ScenePath
    )

    [string]$code = @"
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
string scenePath = "$ScenePath";
if (SceneManager.GetActiveScene().path != scenePath)
{
    EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
}
return SceneManager.GetActiveScene().path;
"@

    [pscustomobject]$result = Invoke-DynamicCode -Code $code
    if ($result.Result -ne $ScenePath) {
        throw "Failed to open scene: $ScenePath"
    }
}

function Start-PlayMode {
    Invoke-UloopJsonChecked -CommandArguments @("control-play-mode", "--action", "Play") | Out-Null
    Wait-PlayMode
}

function Stop-PlayMode {
    [pscustomobject]$result = Invoke-UloopCapture -CommandArguments @("control-play-mode", "--action", "Stop")
    if ($result.ExitCode -ne 0 -and -not [string]::IsNullOrWhiteSpace($result.Text)) {
        Write-Host $result.Text
    }
    Start-Sleep -Seconds 1
}

function Invoke-ScriptChecked {
    param(
        [string]$ScriptPath,
        [string[]]$Arguments = @()
    )

    [string[]]$scriptArguments = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $ScriptPath) + $Arguments
    [string]$previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        [object[]]$output = & powershell @scriptArguments 2>&1
        [int]$exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    [string]$text = ($output | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine
    Write-Host $text
    if ($exitCode -ne 0) {
        throw "$ScriptPath failed with exit code $exitCode"
    }
}

function Invoke-Step {
    param(
        [string]$Name,
        [scriptblock]$Body
    )

    Write-Host ""
    Write-Host "=== $Name ==="
    & $Body
}

function Invoke-LaunchSmoke {
    if ($SkipLaunchSmoke) {
        Write-Host "Skipping launch smoke."
        return
    }

    Invoke-UloopChecked -CommandArguments @("launch", "-q") | Out-Host
    Wait-UnityStopped
    Invoke-UloopChecked -CommandArguments @("launch") | Out-Host
    Wait-UnityReady
    [pscustomobject]$result = Invoke-DynamicCode -Code 'return "windows-launch-smoke";'
    if ($result.Result -ne "windows-launch-smoke") {
        throw "launch smoke dynamic-code readiness check failed"
    }
}

function Invoke-CoreToolSmoke {
    Ensure-UnityReady
    Invoke-UloopChecked -CommandArguments @("list") | Out-Null
    Invoke-UloopChecked -CommandArguments @("sync") | Out-Null
    Invoke-UloopJsonChecked -CommandArguments @("hello-world") | Out-Null
    Invoke-UloopJsonChecked -CommandArguments @("focus-window") | Out-Null
    Invoke-UloopJsonChecked -CommandArguments @("clear-console") | Out-Null
    Invoke-UloopJsonChecked -CommandArguments @("compile", "--wait-for-domain-reload") | Out-Null
    Wait-UnityReady
    Invoke-UloopJsonChecked -CommandArguments @("get-logs", "--log-type", "All", "--max-count", "1") | Out-Null
}

function Invoke-DiscoverySmoke {
    Open-Scene -ScenePath $DiscoveryScenePath
    Start-PlayMode
    Invoke-UloopJsonChecked -CommandArguments @("find-game-objects", "--search-mode", "Contains", "--name-pattern", "Canvas", "--max-results", "10") | Out-Null
    Invoke-UloopJsonChecked -CommandArguments @("get-hierarchy", "--max-depth", "3") | Out-Null
    Invoke-UloopJsonChecked -CommandArguments @("screenshot", "--capture-mode", "rendering", "--annotate-elements", "--elements-only") | Out-Null
    Stop-PlayMode
}

function Invoke-RunTestsSmoke {
    Stop-PlayMode
    Invoke-UloopJsonChecked -CommandArguments @(
        "run-tests",
        "--test-mode",
        "EditMode",
        "--filter-type",
        "exact",
        "--filter-value",
        $RunTestsFilter
    ) | Out-Null
}

function Invoke-CompileGetLogsStress {
    for ([int]$round = 1; $round -le $StressRounds; $round++) {
        Write-Host "stress round $round/$StressRounds"
        Invoke-UloopJsonChecked -CommandArguments @("compile", "--wait-for-domain-reload") | Out-Null
        Wait-UnityReady
        Invoke-UloopJsonChecked -CommandArguments @("get-logs", "--max-count", "1") | Out-Null
        Start-Sleep -Seconds 1
    }
}

function Get-KeyboardCubeZ {
    [pscustomobject]$result = Invoke-DynamicCode -Code @'
using UnityEngine;
GameObject cube = GameObject.Find("KeyboardInputCube");
if (cube == null) return -9999f;
return cube.transform.position.z;
'@

    return [double]$result.Result
}

try {
    Invoke-Step -Name "Launch Smoke" -Body { Invoke-LaunchSmoke }
    Invoke-Step -Name "Core CLI Tool Smoke" -Body { Invoke-CoreToolSmoke }
    Invoke-Step -Name "Discovery and Screenshot Smoke" -Body { Invoke-DiscoverySmoke }
    Invoke-Step -Name "Run Tests Smoke" -Body { Invoke-RunTestsSmoke }
    Invoke-Step -Name "Mouse Input Smoke" -Body {
        Open-Scene -ScenePath $DiscoveryScenePath
        Start-PlayMode
        Invoke-UloopJsonChecked -CommandArguments @("simulate-mouse-input", "--action", "SmoothDelta", "--delta-x", "120", "--delta-y", "0", "--duration", "0.5") | Out-Null
        Invoke-UloopJsonChecked -CommandArguments @("simulate-mouse-input", "--action", "Click", "--x", "400", "--y", "300") | Out-Null
        Invoke-UloopJsonChecked -CommandArguments @("simulate-mouse-input", "--action", "Scroll", "--scroll-y", "120") | Out-Null
        Stop-PlayMode
    }
    Invoke-Step -Name "Keyboard Cube E2E" -Body {
        Open-Scene -ScenePath $KeyboardScenePath
        Start-PlayMode
        [double]$beforeZ = Get-KeyboardCubeZ
        Invoke-UloopJsonChecked -CommandArguments @("simulate-keyboard", "--action", "KeyDown", "--key", "W") | Out-Null
        Start-Sleep -Seconds 1
        Invoke-UloopJsonChecked -CommandArguments @("simulate-keyboard", "--action", "KeyUp", "--key", "W") | Out-Null
        [double]$afterZ = Get-KeyboardCubeZ
        if ($afterZ -le ($beforeZ + 0.1)) {
            throw "KeyboardInputCube did not move forward: beforeZ=$beforeZ afterZ=$afterZ"
        }
        Stop-PlayMode
    }
    Invoke-Step -Name "Input Record Replay E2E" -Body {
        Invoke-ScriptChecked -ScriptPath (Resolve-ProjectRelativePath -RelativePath "Assets\Tests\Demo\scripts\verify-replay-via-cli.ps1") -Arguments @(
            "-ProjectPath",
            $ResolvedProjectPath,
            "-AutomatedInput"
        )
    }
    Invoke-Step -Name "Simulate Mouse UI E2E" -Body {
        Invoke-ScriptChecked -ScriptPath (Resolve-ProjectRelativePath -RelativePath "scripts\test-simulate-mouse-demo.ps1") -Arguments @(
            "-ProjectPath",
            $ResolvedProjectPath
        )
    }
    Invoke-Step -Name "Compile Get Logs Stress" -Body { Invoke-CompileGetLogsStress }
    Invoke-Step -Name "Final Console Check" -Body {
        [pscustomobject]$logs = Invoke-UloopJsonChecked -CommandArguments @("get-logs", "--log-type", "All", "--max-count", "200")
        [int]$badCount = 0
        foreach ($log in $logs.Logs) {
            if ($log.Type -eq "Error" -or $log.Type -eq "Warning") {
                $badCount++
            }
        }
        if ($badCount -ne 0) {
            throw "Unity Console contains $badCount errors or warnings"
        }
    }
}
finally {
    Stop-PlayMode
}

Write-Host ""
Write-Host "All Windows terminal-driven E2E checks passed."
