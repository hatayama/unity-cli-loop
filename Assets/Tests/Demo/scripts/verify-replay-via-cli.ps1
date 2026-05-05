<#
E2E verification: human plays freely, then CLI replays and verifies.

Usage:
  powershell -NoProfile -ExecutionPolicy Bypass -File .\Assets\Tests\Demo\scripts\verify-replay-via-cli.ps1
  powershell -NoProfile -ExecutionPolicy Bypass -File .\Assets\Tests\Demo\scripts\verify-replay-via-cli.ps1 -ProjectPath C:\path\to\project
  powershell -NoProfile -ExecutionPolicy Bypass -File .\Assets\Tests\Demo\scripts\verify-replay-via-cli.ps1 -AutomatedInput

Prerequisites:
  - Unity Editor running with InputReplayVerificationScene loaded
  - PlayMode is not running because this script starts it
#>

[CmdletBinding()]
param(
    [string]$ProjectPath = "",
    [int]$UnityWaitAttempts = 15,
    [int]$ReplayTimeoutSeconds = 60,
    [switch]$AutomatedInput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$RecordingLogRelativePath = ".uloop/outputs/InputRecordings/recording-event-log.txt"
$ReplayLogRelativePath = ".uloop/outputs/InputRecordings/replay-event-log.txt"
$ScenePath = "Assets/Scenes/InputReplayVerificationScene.unity"

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

$RecordingLogPath = Resolve-ProjectRelativePath -RelativePath $RecordingLogRelativePath
$ReplayLogPath = Resolve-ProjectRelativePath -RelativePath $ReplayLogRelativePath

function Get-UloopArguments {
    param(
        [string[]]$CommandArguments
    )

    if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
        return $CommandArguments
    }

    return @($CommandArguments + @("--project-path", $ResolvedProjectPath))
}

function Invoke-UloopCapture {
    param(
        [string[]]$CommandArguments
    )

    [string[]]$arguments = Get-UloopArguments -CommandArguments $CommandArguments
    if ($PSVersionTable.PSVersion.Major -lt 6) {
        # Windows PowerShell strips embedded quote characters from native arguments unless they are escaped.
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
        ExitCode = $exitCode
        Text = $text
        Arguments = $arguments
    }
}

function Invoke-Uloop {
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

    [string]$text = Invoke-Uloop -CommandArguments $CommandArguments
    [pscustomobject]$json = $text | ConvertFrom-Json
    if ($json.PSObject.Properties.Name -contains "Success" -and $json.Success -ne $true) {
        throw "uloop $($CommandArguments -join " ") returned Success=false: $text"
    }

    return $json
}

function Assert-DynamicCodeResult {
    param(
        [pscustomobject]$Result,
        [string]$ExpectedResult,
        [string]$Context
    )

    [string]$actualResult = ""
    if ($null -ne $Result.Result) {
        $actualResult = $Result.Result.ToString()
    }

    if ($actualResult -eq $ExpectedResult) {
        return
    }

    throw "${Context} failed: $($Result | ConvertTo-Json -Depth 10)"
}

function Remove-EventLogIfExists {
    param(
        [string]$Path
    )

    if (Test-Path -LiteralPath $Path -PathType Leaf) {
        Remove-Item -LiteralPath $Path -Force
    }
}

function Wait-UnityReady {
    for ([int]$attempt = 0; $attempt -lt $UnityWaitAttempts; $attempt++) {
        [pscustomobject]$result = Invoke-UloopCapture -CommandArguments @("get-logs", "--max-count", "1")
        if ($result.ExitCode -eq 0) {
            return
        }

        Start-Sleep -Seconds 2
    }

    throw "Unity not responding"
}

function Initialize-ReplayScene {
    [pscustomobject]$stopResult = Invoke-UloopCapture -CommandArguments @("control-play-mode", "--action", "Stop")
    if ($stopResult.ExitCode -ne 0 -and -not [string]::IsNullOrWhiteSpace($stopResult.Text)) {
        Write-Host $stopResult.Text
    }

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

    [pscustomobject]$result = Invoke-UloopJsonChecked -CommandArguments @("execute-dynamic-code", "--code", $code)
    if ($result.Result -ne $ScenePath) {
        throw "Failed to load ${ScenePath}: $($result | ConvertTo-Json -Depth 10)"
    }
}

function Invoke-ActivateForRecord {
    [pscustomobject]$result = Invoke-UloopJsonChecked -CommandArguments @(
        "execute-dynamic-code",
        "--code",
        @'
using UnityEngine;
GameObject cube = GameObject.Find("VerificationCube");
if (cube == null) return "ERROR: VerificationCube not found";
cube.SendMessage("ActivateForExternalControl");
return "OK: activated for recording";
'@
    )

    Assert-DynamicCodeResult -Result $result -ExpectedResult "OK: activated for recording" -Context "Activate recording controller"
}

function Invoke-ActivateForReplay {
    [pscustomobject]$result = Invoke-UloopJsonChecked -CommandArguments @(
        "execute-dynamic-code",
        "--code",
        @'
using UnityEngine;
GameObject cube = GameObject.Find("VerificationCube");
if (cube == null) return "ERROR: VerificationCube not found";
cube.SendMessage("ActivateForExternalReplay");
return "OK: activated for replay";
'@
    )

    Assert-DynamicCodeResult -Result $result -ExpectedResult "OK: activated for replay" -Context "Activate replay controller"
}

function Invoke-AutomatedInput {
    Invoke-UloopJsonChecked -CommandArguments @("simulate-mouse-input", "--action", "SmoothDelta", "--delta-x", "96", "--delta-y", "0", "--duration", "0.25") | Out-Null
    Start-Sleep -Milliseconds 300
    Invoke-UloopJsonChecked -CommandArguments @("simulate-mouse-input", "--action", "Click", "--x", "400", "--y", "300") | Out-Null
    Start-Sleep -Milliseconds 300
    Invoke-UloopJsonChecked -CommandArguments @("simulate-mouse-input", "--action", "Scroll", "--scroll-y", "120") | Out-Null
    Start-Sleep -Milliseconds 500
}

function Save-EventLog {
    param(
        [string]$Path
    )

    Remove-EventLogIfExists -Path $Path

    [string]$escapedPath = $Path.Replace("\", "\\").Replace('"', '\"')
    [string]$code = @"
using UnityEngine;
GameObject cube = GameObject.Find("VerificationCube");
if (cube == null) return "ERROR: VerificationCube not found";
cube.SendMessage("SaveLog", "$escapedPath");
return "OK: log saved";
"@

    [pscustomobject]$result = Invoke-UloopJsonChecked -CommandArguments @("execute-dynamic-code", "--code", $code)
    Assert-DynamicCodeResult -Result $result -ExpectedResult "OK: log saved" -Context "Save event log"

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Save event log did not create ${Path}"
    }
}

function Restart-PlayMode {
    Invoke-UloopJsonChecked -CommandArguments @("control-play-mode", "--action", "Stop") | Out-Null
    Start-Sleep -Seconds 3
    Invoke-UloopJsonChecked -CommandArguments @("control-play-mode", "--action", "Play") | Out-Null
    Write-Host "  Waiting for Unity..."
    Start-Sleep -Seconds 6
    Wait-UnityReady
}

function Invoke-ReplayToLog {
    param(
        [string]$LogPath,
        [string]$InputPath
    )

    Restart-PlayMode
    Invoke-ActivateForReplay
    Write-Host "  Starting replay..."

    [string[]]$replayStartArgs = @("replay-input", "--action", "Start")
    if (-not [string]::IsNullOrWhiteSpace($InputPath)) {
        $replayStartArgs += @("--input-path", $InputPath)
    }
    if ($AutomatedInput) {
        $replayStartArgs += @("--no-show-overlay")
    }

    [bool]$replayStartAccepted = Start-ReplayInput -ReplayStartArgs $replayStartArgs

    Write-Host "  Waiting for replay to finish..."
    Start-Sleep -Seconds 2
    Wait-ReplayCompleted -ReplayStartAccepted $replayStartAccepted
    Write-Host ""

    Start-Sleep -Seconds 1
    Save-EventLog -Path $LogPath
}

function Start-ReplayInput {
    param(
        [string[]]$ReplayStartArgs
    )

    [pscustomobject]$replayResult = Invoke-UloopCapture -CommandArguments $ReplayStartArgs
    Write-Host "  $($replayResult.Text)"
    if ($replayResult.ExitCode -ne 0) {
        return $false
    }

    [pscustomobject]$replayStartJson = $replayResult.Text | ConvertFrom-Json
    return $replayStartJson.Success -eq $true
}

function Get-ReplayStatus {
    [pscustomobject]$result = Invoke-UloopCapture -CommandArguments @("replay-input", "--action", "Status")
    if ($result.ExitCode -ne 0) {
        return [pscustomobject]@{
            IsReplaying = $null
            Progress = $null
            RawText = $result.Text
        }
    }

    [pscustomobject]$status = $result.Text | ConvertFrom-Json
    if ($status.PSObject.Properties.Name -contains "Success" -and $status.Success -ne $true) {
        return [pscustomobject]@{
            IsReplaying = $null
            Progress = $null
            RawText = $result.Text
        }
    }

    return [pscustomobject]@{
        IsReplaying = $status.IsReplaying
        Progress = $status.Progress
        RawText = $result.Text
    }
}

function Wait-ReplayCompleted {
    param(
        [bool]$ReplayStartAccepted
    )

    [string]$lastStatusText = ""
    [bool]$observedReplay = $ReplayStartAccepted

    for ([int]$waitedSeconds = 0; $waitedSeconds -lt $ReplayTimeoutSeconds; $waitedSeconds++) {
        [pscustomobject]$status = Get-ReplayStatus
        $lastStatusText = $status.RawText

        if ($status.IsReplaying -eq $true) {
            $observedReplay = $true
        }

        if ($status.IsReplaying -eq $false) {
            if (-not $observedReplay) {
                Write-Host "ERROR: Replay did not start"
                Write-Host "  Last status: $lastStatusText"
                throw "Replay did not start"
            }

            Write-Host "  Replay completed."
            return
        }

        if (($waitedSeconds % 5) -eq 0) {
            [string]$progressText = "..."
            if ($null -ne $status.Progress) {
                $progressText = $status.Progress.ToString()
            }

            Write-Host "  Progress: $progressText"
        }

        Start-Sleep -Seconds 1
    }

    Write-Host "ERROR: Replay did not complete within ${ReplayTimeoutSeconds}s"
    Write-Host "  Last status: $lastStatusText"
    throw "Replay timeout"
}

function Get-NormalizedFrames {
    param(
        [string]$Path
    )

    [string[]]$lines = @(Get-Content -LiteralPath $Path)
    if ($lines.Count -eq 0) {
        throw "Log file is empty: $Path"
    }

    [System.Text.RegularExpressions.Match]$baseMatch = [regex]::Match($lines[0], "^Frame ([0-9]+): (.*)$")
    if (-not $baseMatch.Success) {
        throw "Log line does not start with a frame prefix: $($lines[0])"
    }

    [int]$baseFrame = [int]$baseMatch.Groups[1].Value
    [System.Collections.Generic.List[string]]$normalizedLines = [System.Collections.Generic.List[string]]::new()

    foreach ($line in $lines) {
        [System.Text.RegularExpressions.Match]$lineMatch = [regex]::Match($line, "^Frame ([0-9]+): (.*)$")
        if (-not $lineMatch.Success) {
            throw "Log line does not start with a frame prefix: $line"
        }

        [int]$frame = [int]$lineMatch.Groups[1].Value
        [string]$rest = $lineMatch.Groups[2].Value
        $normalizedLines.Add("Frame $($frame - $baseFrame): $rest")
    }

    return ,$normalizedLines.ToArray()
}

function Compare-NormalizedLogs {
    param(
        [string[]]$RecordingLines,
        [string[]]$ReplayLines
    )

    [int]$maxCount = [Math]::Max($RecordingLines.Count, $ReplayLines.Count)
    [System.Collections.Generic.List[string]]$diffLines = [System.Collections.Generic.List[string]]::new()

    for ([int]$index = 0; $index -lt $maxCount; $index++) {
        [string]$recordingLine = ""
        if ($index -lt $RecordingLines.Count) {
            $recordingLine = $RecordingLines[$index]
        }

        [string]$replayLine = ""
        if ($index -lt $ReplayLines.Count) {
            $replayLine = $ReplayLines[$index]
        }

        if ($recordingLine -eq $replayLine) {
            continue
        }

        if ($index -lt $RecordingLines.Count) {
            $diffLines.Add("< $recordingLine")
        }

        if ($index -lt $ReplayLines.Count) {
            $diffLines.Add("> $replayLine")
        }
    }

    return ,$diffLines.ToArray()
}

Write-Host ""
Write-Host "========================================="
Write-Host "  Input Record/Replay E2E Verification"
Write-Host "========================================="

Write-Host ""
Write-Host "[0/8] Loading replay verification scene..."
Initialize-ReplayScene

Write-Host "[1/8] Starting PlayMode..."
Invoke-UloopJsonChecked -CommandArguments @("control-play-mode", "--action", "Play") | Out-Null
Write-Host "  Waiting for Unity..."
Start-Sleep -Seconds 6
Wait-UnityReady

Write-Host "[2/8] Activating controller..."
Invoke-ActivateForRecord

Write-Host "[3/8] Starting recording via CLI..."
[string[]]$recordStartArgs = @("record-input", "--action", "Start")
if ($AutomatedInput) {
    $recordStartArgs += @("--delay-seconds", "0", "--no-show-overlay")
}

Invoke-UloopJsonChecked -CommandArguments $recordStartArgs | Out-Null

if ($AutomatedInput) {
    Write-Host "  Running automated input sequence..."
    Start-Sleep -Milliseconds 500
    Invoke-AutomatedInput
} else {
    Write-Host ""
    Write-Host "========================================="
    Write-Host "  Recording is active!"
    Write-Host "  Go to the Unity Game View and play."
    Write-Host ""
    Write-Host "  WASD: move | Mouse: rotate"
    Write-Host "  Left click: red | Right click: blue"
    Write-Host "  Scroll: scale"
    Write-Host ""
    Write-Host "  Press ENTER here when done."
    Write-Host "========================================="
    Write-Host ""
    Read-Host | Out-Null
}

Write-Host "[4/8] Stopping recording via CLI..."
[pscustomobject]$recordStopResult = Invoke-UloopCapture -CommandArguments @("record-input", "--action", "Stop")
Write-Host "  $($recordStopResult.Text)"
if ($recordStopResult.ExitCode -ne 0) {
    throw "record-input Stop failed"
}

[pscustomobject]$recordStopJson = $recordStopResult.Text | ConvertFrom-Json
if ($recordStopJson.Success -ne $true) {
    throw "record-input Stop reported failure: $($recordStopResult.Text)"
}

[string]$recordingInputPath = ""
if ($null -ne $recordStopJson.OutputPath) {
    $recordingInputPath = $recordStopJson.OutputPath.ToString()
}

if ($AutomatedInput) {
    Write-Host "[5/8] Saving recording event log..."
    Save-EventLog -Path $RecordingLogPath

    Write-Host "[6/8] Replaying recorded input via CLI..."
    Invoke-ReplayToLog -LogPath $ReplayLogPath -InputPath $recordingInputPath
} else {
    Write-Host "[5/8] Saving recording event log..."
    Save-EventLog -Path $RecordingLogPath

    Write-Host "[5/8] Restarting PlayMode..."
    Restart-PlayMode

    Write-Host "[6/8] Activating controller + starting replay via CLI..."
    Invoke-ActivateForReplay
    Write-Host "  Starting replay..."
    [bool]$replayStartAccepted = Start-ReplayInput -ReplayStartArgs @("replay-input", "--action", "Start")

    Write-Host "  Waiting for replay to finish..."
    Start-Sleep -Seconds 2
    Wait-ReplayCompleted -ReplayStartAccepted $replayStartAccepted
    Write-Host ""

    Start-Sleep -Seconds 1

    Write-Host "[7/8] Saving replay event log..."
    Save-EventLog -Path $ReplayLogPath
}

Write-Host ""
Write-Host "[8/8] Comparing logs..."
Write-Host ""

[string[]]$recordingNormalized = Get-NormalizedFrames -Path $RecordingLogPath
[string[]]$replayNormalized = Get-NormalizedFrames -Path $ReplayLogPath
[string[]]$diffLines = Compare-NormalizedLogs -RecordingLines $recordingNormalized -ReplayLines $replayNormalized

if ($diffLines.Count -eq 0) {
    Write-Host "========================================="
    Write-Host "  RESULT: MATCH ($($recordingNormalized.Count) events identical)"
    Write-Host "  Relative frame timing verified."
    Write-Host "========================================="
    Write-Host ""
    exit 0
}

Write-Host "========================================="
Write-Host "  RESULT: MISMATCH ($($diffLines.Count) differences)"
Write-Host "========================================="
Write-Host ""
$diffLines | Select-Object -First 20 | ForEach-Object { Write-Host $_ }
Write-Host ""
exit 1
