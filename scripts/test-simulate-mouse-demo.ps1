<#
E2E verification for SimulateMouseDemoScene using the uloop CLI.

Usage:
  powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-simulate-mouse-demo.ps1
  powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-simulate-mouse-demo.ps1 -ProjectPath C:\path\to\project

Prerequisites:
  - Unity Editor is running
  - uloop is available on PATH
#>

[CmdletBinding()]
param(
    [string]$ProjectPath = "",
    [int]$UnityWaitAttempts = 15,
    [int]$PlayModeWaitSeconds = 20
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$ScenePath = "Assets/Scenes/SimulateMouseDemoScene.unity"

function Get-UloopArguments {
    param(
        [string[]]$CommandArguments
    )

    if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
        return $CommandArguments
    }

    return @($CommandArguments + @("--project-path", $ProjectPath))
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

function Invoke-UloopJson {
    param(
        [string[]]$CommandArguments
    )

    [pscustomobject]$result = Invoke-UloopCapture -CommandArguments $CommandArguments
    if ($result.ExitCode -ne 0) {
        if (-not [string]::IsNullOrWhiteSpace($result.Text)) {
            Write-Host $result.Text
        }

        [string]$commandText = "uloop " + ($result.Arguments -join " ")
        throw "$commandText failed with exit code $($result.ExitCode)"
    }

    return $result.Text | ConvertFrom-Json
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

function Wait-PlayMode {
    for ([int]$attempt = 0; $attempt -lt $PlayModeWaitSeconds; $attempt++) {
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
        if ($result.Success -eq $true -and $result.Result -eq "True") {
            return
        }

        Start-Sleep -Seconds 1
    }

    throw "Unity did not enter PlayMode within ${PlayModeWaitSeconds}s"
}

function Assert-EqualText {
    param(
        [string]$Actual,
        [string]$Expected,
        [string]$Context
    )

    if ($Actual -eq $Expected) {
        return
    }

    throw "${Context}: expected '$Expected', got '$Actual'"
}

function Assert-MouseResponse {
    param(
        [pscustomobject]$Response,
        [string]$ExpectedAction,
        [string]$ExpectedHit
    )

    if ($Response.Success -ne $true) {
        throw "$ExpectedAction failed: $($Response.Message)"
    }

    Assert-EqualText -Actual $Response.Action -Expected $ExpectedAction -Context "Action"

    if (-not [string]::IsNullOrWhiteSpace($ExpectedHit)) {
        Assert-EqualText -Actual $Response.HitGameObjectName -Expected $ExpectedHit -Context "HitGameObjectName"
    }
}

function Get-UiElement {
    param(
        [object[]]$Elements,
        [string]$Name
    )

    [object[]]$matches = @($Elements | Where-Object { $_.Name -eq $Name })
    if ($matches.Count -eq 1) {
        return $matches[0]
    }

    throw "Expected exactly one annotated element named '$Name', found $($matches.Count)"
}

function Invoke-MouseUi {
    param(
        [string]$Action,
        [double]$X,
        [double]$Y,
        [double]$FromX = [double]::NaN,
        [double]$FromY = [double]::NaN,
        [double]$Duration = [double]::NaN,
        [double]$DragSpeed = [double]::NaN,
        [string]$TargetPath = "",
        [string]$DropTargetPath = "",
        [string]$ExpectedHit = ""
    )

    [System.Collections.Generic.List[string]]$arguments = [System.Collections.Generic.List[string]]::new()
    $arguments.Add("simulate-mouse-ui")
    $arguments.Add("--action")
    $arguments.Add($Action)
    $arguments.Add("--x")
    $arguments.Add($X.ToString([Globalization.CultureInfo]::InvariantCulture))
    $arguments.Add("--y")
    $arguments.Add($Y.ToString([Globalization.CultureInfo]::InvariantCulture))

    if (-not [double]::IsNaN($FromX)) {
        $arguments.Add("--from-x")
        $arguments.Add($FromX.ToString([Globalization.CultureInfo]::InvariantCulture))
    }

    if (-not [double]::IsNaN($FromY)) {
        $arguments.Add("--from-y")
        $arguments.Add($FromY.ToString([Globalization.CultureInfo]::InvariantCulture))
    }

    if (-not [double]::IsNaN($Duration)) {
        $arguments.Add("--duration")
        $arguments.Add($Duration.ToString([Globalization.CultureInfo]::InvariantCulture))
    }

    if (-not [double]::IsNaN($DragSpeed)) {
        $arguments.Add("--drag-speed")
        $arguments.Add($DragSpeed.ToString([Globalization.CultureInfo]::InvariantCulture))
    }

    if (-not [string]::IsNullOrWhiteSpace($TargetPath)) {
        $arguments.Add("--bypass-raycast")
        $arguments.Add("--target-path")
        $arguments.Add($TargetPath)
    }

    if (-not [string]::IsNullOrWhiteSpace($DropTargetPath)) {
        $arguments.Add("--drop-target-path")
        $arguments.Add($DropTargetPath)
    }

    [pscustomobject]$response = Invoke-UloopJson -CommandArguments $arguments.ToArray()
    Assert-MouseResponse -Response $response -ExpectedAction $Action -ExpectedHit $ExpectedHit
    return $response
}

function Get-TextFromScene {
    param(
        [string]$ObjectName
    )

    [string]$code = @"
using UnityEngine;
using UnityEngine.UI;
GameObject target = GameObject.Find("$ObjectName");
if (target == null) { return "ERROR: $ObjectName not found"; }
Text text = target.GetComponent<Text>();
if (text == null) { return "ERROR: $ObjectName has no Text component"; }
return text.text;
"@

    [pscustomobject]$result = Invoke-UloopJson -CommandArguments @("execute-dynamic-code", "--code", $code)
    if ($result.Success -ne $true) {
        throw "Failed to read text from ${ObjectName}: $($result.ErrorMessage)"
    }

    return $result.Result
}

function Get-DropZoneStatus {
    [pscustomobject]$result = Invoke-UloopJson -CommandArguments @(
        "execute-dynamic-code",
        "--code",
        @'
using UnityEngine;
GameObject target = GameObject.Find("DropZone");
if (target == null) { return "ERROR: DropZone not found"; }
DropZone dropZone = target.GetComponent<DropZone>();
if (dropZone == null) { return "ERROR: DropZone component not found"; }
return dropZone.StatusMessage;
'@
    )

    if ($result.Success -ne $true) {
        throw "Failed to read DropZone status: $($result.ErrorMessage)"
    }

    return $result.Result
}

function Get-LongPressButtonText {
    [pscustomobject]$result = Invoke-UloopJson -CommandArguments @(
        "execute-dynamic-code",
        "--code",
        @'
using UnityEngine;
using UnityEngine.UI;
GameObject target = GameObject.Find("LongPressButton");
if (target == null) { return "ERROR: LongPressButton not found"; }
Text text = target.GetComponentInChildren<Text>();
if (text == null) { return "ERROR: LongPressButton label not found"; }
return text.text;
'@
    )

    if ($result.Success -ne $true) {
        throw "Failed to read LongPressButton text: $($result.ErrorMessage)"
    }

    return $result.Result
}

function Get-VirtualPadState {
    [pscustomobject]$result = Invoke-UloopJson -CommandArguments @(
        "execute-dynamic-code",
        "--code",
        @'
using UnityEngine;
GameObject target = GameObject.Find("VirtualPadBackground");
if (target == null) { return "ERROR: VirtualPadBackground not found"; }
DemoVirtualPad pad = target.GetComponent<DemoVirtualPad>();
if (pad == null) { return "ERROR: DemoVirtualPad component not found"; }
if (Mathf.Abs(pad.Direction.x) < 0.001f && Mathf.Abs(pad.Direction.y) < 0.001f) { return "Zero"; }
return pad.Direction.ToString("F3");
'@
    )

    if ($result.Success -ne $true) {
        throw "Failed to read VirtualPad state: $($result.ErrorMessage)"
    }

    return $result.Result
}

function Initialize-DemoScene {
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

    [pscustomobject]$result = Invoke-UloopJson -CommandArguments @("execute-dynamic-code", "--code", $code)
    if ($result.Success -ne $true) {
        throw "Failed to load ${ScenePath}: $($result.ErrorMessage)"
    }

    Assert-EqualText -Actual $result.Result -Expected $ScenePath -Context "Active scene"
}

function Get-AnnotatedElements {
    [pscustomobject]$response = Invoke-UloopJson -CommandArguments @(
        "screenshot",
        "--capture-mode",
        "rendering",
        "--annotate-elements",
        "--elements-only"
    )

    if ($response.ScreenshotCount -ne 1) {
        throw "Expected one screenshot response, got $($response.ScreenshotCount)"
    }

    [object[]]$elements = @($response.Screenshots[0].AnnotatedElements)
    if ($elements.Count -eq 0) {
        throw "No annotated UI elements were returned"
    }

    return ,$elements
}

Write-Host ""
Write-Host "========================================="
Write-Host "  SimulateMouse UI E2E Verification"
Write-Host "========================================="

Wait-UnityReady

try {
    Write-Host "[1/7] Loading SimulateMouse demo scene..."
    Initialize-DemoScene

    Write-Host "[2/7] Starting PlayMode..."
    Invoke-UloopJson -CommandArguments @("control-play-mode", "--action", "Play") | Out-Null
    Wait-PlayMode
    Start-Sleep -Seconds 1

    Write-Host "[3/7] Reading annotated UI coordinates..."
    [object[]]$elements = Get-AnnotatedElements
    [pscustomobject]$clickButton1 = Get-UiElement -Elements $elements -Name "ClickButton1"
    [pscustomobject]$clickButton2 = Get-UiElement -Elements $elements -Name "ClickButton2"
    [pscustomobject]$longPressButton = Get-UiElement -Elements $elements -Name "LongPressButton"
    [pscustomobject]$redBox = Get-UiElement -Elements $elements -Name "RedBox"
    [pscustomobject]$greenBox = Get-UiElement -Elements $elements -Name "GreenBox"
    [pscustomobject]$blueBox = Get-UiElement -Elements $elements -Name "BlueBox"
    [pscustomobject]$dropZone = Get-UiElement -Elements $elements -Name "DropZone"
    [pscustomobject]$virtualPad = Get-UiElement -Elements $elements -Name "VirtualPadBackground"

    Write-Host "[4/7] Clicking counter buttons..."
    Invoke-MouseUi -Action "Click" -X $clickButton1.SimX -Y $clickButton1.SimY -TargetPath $clickButton1.Path -ExpectedHit "ClickButton1" | Out-Null
    Invoke-MouseUi -Action "Click" -X $clickButton2.SimX -Y $clickButton2.SimY -TargetPath $clickButton2.Path -ExpectedHit "ClickButton2" | Out-Null
    Invoke-MouseUi -Action "Click" -X $clickButton1.SimX -Y $clickButton1.SimY -TargetPath $clickButton1.Path -ExpectedHit "ClickButton1" | Out-Null
    Invoke-MouseUi -Action "Click" -X $clickButton2.SimX -Y $clickButton2.SimY -TargetPath $clickButton2.Path -ExpectedHit "ClickButton2" | Out-Null
    [string]$counterText = Get-TextFromScene -ObjectName "CounterText"
    Assert-EqualText -Actual $counterText -Expected "Total Clicks: 4" -Context "CounterText"

    Write-Host "[5/7] Long-pressing the hold button..."
    Invoke-MouseUi -Action "LongPress" -X $longPressButton.SimX -Y $longPressButton.SimY -Duration 3.2 -TargetPath $longPressButton.Path -ExpectedHit "LongPressButton" | Out-Null
    [string]$longPressText = Get-LongPressButtonText
    Assert-EqualText -Actual $longPressText -Expected "Activated!" -Context "LongPressButton label"

    Write-Host "[6/7] Dragging boxes into the DropZone..."
    Invoke-MouseUi -Action "Drag" -FromX $redBox.SimX -FromY $redBox.SimY -X $dropZone.SimX -Y $dropZone.SimY -DragSpeed 900 -TargetPath $redBox.Path -DropTargetPath $dropZone.Path -ExpectedHit "RedBox" | Out-Null
    Assert-EqualText -Actual (Get-DropZoneStatus) -Expected "Dropped: RedBox" -Context "DropZone after RedBox"

    Invoke-MouseUi -Action "DragStart" -X $greenBox.SimX -Y $greenBox.SimY -DragSpeed 700 -TargetPath $greenBox.Path -ExpectedHit "GreenBox" | Out-Null
    Invoke-MouseUi -Action "DragMove" -X ($dropZone.SimX + 150) -Y ($greenBox.SimY - 50) -DragSpeed 500 -ExpectedHit "GreenBox" | Out-Null
    Invoke-MouseUi -Action "DragMove" -X ($dropZone.SimX - 150) -Y ($dropZone.SimY + 50) -DragSpeed 500 -ExpectedHit "GreenBox" | Out-Null
    Invoke-MouseUi -Action "DragEnd" -X $dropZone.SimX -Y $dropZone.SimY -DragSpeed 500 -DropTargetPath $dropZone.Path -ExpectedHit "GreenBox" | Out-Null
    Assert-EqualText -Actual (Get-DropZoneStatus) -Expected "Dropped: GreenBox" -Context "DropZone after GreenBox"

    Invoke-MouseUi -Action "Drag" -FromX $blueBox.SimX -FromY $blueBox.SimY -X $dropZone.SimX -Y $dropZone.SimY -DragSpeed 900 -TargetPath $blueBox.Path -DropTargetPath $dropZone.Path -ExpectedHit "BlueBox" | Out-Null
    Assert-EqualText -Actual (Get-DropZoneStatus) -Expected "Dropped: BlueBox" -Context "DropZone after BlueBox"

    Write-Host "[7/7] Exercising the virtual pad..."
    [double]$padWidth = $virtualPad.BoundsMaxX - $virtualPad.BoundsMinX
    [double]$padHeight = $virtualPad.BoundsMaxY - $virtualPad.BoundsMinY
    [double]$padOffset = [Math]::Min($padWidth, $padHeight) * 0.28
    Invoke-MouseUi -Action "DragStart" -X $virtualPad.SimX -Y $virtualPad.SimY -TargetPath $virtualPad.Path -ExpectedHit "VirtualPadBackground" | Out-Null
    Invoke-MouseUi -Action "DragMove" -X ($virtualPad.SimX + $padOffset) -Y ($virtualPad.SimY - $padOffset) -DragSpeed 400 -ExpectedHit "VirtualPadBackground" | Out-Null
    Invoke-MouseUi -Action "DragMove" -X ($virtualPad.SimX - $padOffset) -Y ($virtualPad.SimY + $padOffset) -DragSpeed 400 -ExpectedHit "VirtualPadBackground" | Out-Null
    Invoke-MouseUi -Action "DragMove" -X $virtualPad.SimX -Y ($virtualPad.SimY - $padOffset) -DragSpeed 400 -ExpectedHit "VirtualPadBackground" | Out-Null
    Invoke-MouseUi -Action "DragMove" -X ($virtualPad.SimX + $padOffset) -Y $virtualPad.SimY -DragSpeed 400 -ExpectedHit "VirtualPadBackground" | Out-Null
    Invoke-MouseUi -Action "DragEnd" -X $virtualPad.SimX -Y $virtualPad.SimY -DragSpeed 400 -ExpectedHit "VirtualPadBackground" | Out-Null
    Assert-EqualText -Actual (Get-VirtualPadState) -Expected "Zero" -Context "VirtualPad state"

    Write-Host ""
    Write-Host "========================================="
    Write-Host "  RESULT: PASS"
    Write-Host "========================================="
    exit 0
}
finally {
    Invoke-UloopCapture -CommandArguments @("control-play-mode", "--action", "Stop") | Out-Null
}
