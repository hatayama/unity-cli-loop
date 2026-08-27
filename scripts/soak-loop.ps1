<#
Endurance (soak) harness for the uloop CLI against a real Unity project.
Windows/PowerShell port of scripts/soak-loop.sh - same iteration plan, same
CSV schema. Full guide (expected failures, safety, reading results):
docs/soak-testing.md

Each iteration rewrites a scratch editor script inside the target project to
force a genuine script compilation + domain reload, then exercises the
commands that must survive hundreds of reload crossings: compile, get-logs,
get-hierarchy, screenshot, execute-dynamic-code. On a cadence it also runs
Unity tests, full editor restarts, forced recompiles, and a PlayMode
pause-point cycle against a generated minimal scene. Every command's exit
code, duration, and payload size is appended to a CSV so latency drift and
failure rate over time can be graphed afterwards. Editor working set and
leftover project-runner processes are sampled per iteration as leak signals.

Usage:
  powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\soak-loop.ps1 -ProjectPath C:\path\to\unity-project
  pwsh -NoProfile -File .\scripts\soak-loop.ps1 -ProjectPath C:\path\to\unity-project -Iterations 20 -TestAssembly YourProject.Tests.Editor

Environment:
  ULOOP_BIN  uloop binary to use (default: uloop from PATH - the realistic
             release configuration; point at a dist\windows-amd64\uloop.exe
             to soak unreleased CLI code)

Prerequisites:
  - uloop package installed in the target project
  - Unity Editor may or may not be running: a busy editor is waited on, and
    a missing one is launched via `uloop launch`

All generated files live under Assets/UloopSoak/ in the target project (the
recompile scratch script, the PlayMode ticker script, and the pause-point
scene) and are left behind after the run - delete the folder and its .meta
manually when done. When -PauseEvery > 0, the harness opens its generated
scene during the run and releases it before the cleanup reminder, so save
your own scene changes before running.
#>

[CmdletBinding()]
param(
    # Target Unity project root.
    [Parameter(Mandatory = $true)]
    [string]$ProjectPath,
    # Total iterations.
    [int]$Iterations = 100,
    # Run `uloop launch -r` every N iterations (0 = never).
    [int]$RestartEvery = 25,
    # Use compile --force-recompile every N iterations (0 = never).
    [int]$ForceEvery = 10,
    # Run a PlayMode cycle (UI click + pause-point) every N iterations (0 = never).
    [int]$PauseEvery = 5,
    # Run `uloop run-tests` every N iterations (0 = never).
    [int]$TestsEvery = 10,
    # Test assembly passed to run-tests --filter-type assembly (required when
    # -TestsEvery > 0; never run the full suite of a large project).
    [string]$TestAssembly = "",
    # Leave the editor's code optimization mode alone. Without this the
    # PlayMode cycle switches a Release editor to Debug for the duration of the
    # run, because pause points cannot be armed by file and line otherwise.
    [switch]$KeepCodeOptimization,
    # Kill bound for a single uloop call. Raise it for a large project, and
    # especially for parallel soaks: a full recompile of one large project took
    # over 10 minutes with three editors compiling at once.
    [int]$CommandTimeoutSeconds = 600,
    # Passed to every compile as --timeout-seconds, or --compile-wait-timeout-seconds
    # when the pinned runner predates the rename (0 = leave the runner's own default
    # alone). Raise it on a project whose forced recompile outlives the runner's
    # 10-minute wait; above 1200 the runner warns that Unity may drop the retained result.
    [int]$CompileWaitTimeoutSeconds = 0,
    # Pause between iterations.
    [int]$SleepSeconds = 0,
    # Results directory (default: .\uloop-soak-results\<timestamp>).
    [string]$OutDir = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

[string]$UloopBin = "uloop"
if (-not [string]::IsNullOrWhiteSpace($env:ULOOP_BIN)) {
    $UloopBin = $env:ULOOP_BIN
}

if (-not (Test-Path -LiteralPath $ProjectPath)) {
    throw "Error: $ProjectPath does not exist"
}
[string]$ResolvedProjectPath = (Resolve-Path -LiteralPath $ProjectPath).Path
if (-not (Test-Path -LiteralPath (Join-Path $ResolvedProjectPath "ProjectSettings\ProjectVersion.txt"))) {
    throw "Error: $ResolvedProjectPath does not look like a Unity project (no ProjectSettings/ProjectVersion.txt)"
}
if ($TestsEvery -gt 0 -and [string]::IsNullOrWhiteSpace($TestAssembly)) {
    throw "Error: -TestAssembly is required when tests are enabled (pass -TestsEvery 0 to skip tests; running the full suite of a large project is not a safe default)"
}
# A kill watchdog at or below the compile wait would end the command before the
# CLI could report COMPILE_WAIT_TIMEOUT, turning a diagnosable timeout into a
# bare exit 124, so the watchdog is lifted over it instead of failing the run.
if ($CompileWaitTimeoutSeconds -gt 0 -and $CommandTimeoutSeconds -le $CompileWaitTimeoutSeconds) {
    $CommandTimeoutSeconds = $CompileWaitTimeoutSeconds + 120
}

# Start-Process needs a real executable path, and resolving once keeps every
# iteration from re-searching PATH.
[string]$ResolvedUloopBin = $UloopBin
if (Test-Path -LiteralPath $UloopBin) {
    $ResolvedUloopBin = (Resolve-Path -LiteralPath $UloopBin).Path
}
else {
    $ResolvedUloopBin = (Get-Command $UloopBin -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
}

if ([string]::IsNullOrWhiteSpace($OutDir)) {
    [string]$stamp = (Get-Date).ToString("yyyyMMdd-HHmmss")
    $OutDir = Join-Path (Get-Location).Path "uloop-soak-results\$stamp"
}
$null = New-Item -ItemType Directory -Path $OutDir -Force
[string]$ResolvedOutDir = (Resolve-Path -LiteralPath $OutDir).Path

[string]$CommandsCsv = Join-Path $ResolvedOutDir "commands.csv"
[string]$MetricsCsv = Join-Path $ResolvedOutDir "metrics.csv"
[string]$RunLog = Join-Path $ResolvedOutDir "run.log"
[string]$StdOutFile = Join-Path $ResolvedOutDir ".last-stdout"
[string]$StdErrFile = Join-Path $ResolvedOutDir ".last-stderr"
[string]$SceneSetupFile = Join-Path $ResolvedOutDir "setup-scene.cs"
[string]$SceneSetupForceFile = Join-Path $ResolvedOutDir "setup-scene-force.cs"
[string]$FailuresDir = Join-Path $ResolvedOutDir "failures"

[string]$SoakAssetsDir = Join-Path $ResolvedProjectPath "Assets\UloopSoak"
[string]$ScratchDir = Join-Path $SoakAssetsDir "Editor"
[string]$ScratchFile = Join-Path $ScratchDir "UloopSoakScratch.cs"
[string]$TickerRelativePath = "Assets/UloopSoak/UloopSoakTicker.cs"
[string]$ProbeRelativePath = "Assets/UloopSoak/UloopSoakButtonProbe.cs"
[string]$SceneRelativePath = "Assets/UloopSoak/UloopSoak.unity"

# Unity reads .cs files as UTF-8; writing without a BOM keeps the generated
# sources byte-identical between Windows PowerShell 5.1 and PowerShell 7,
# whose Set-Content defaults disagree about the BOM.
[System.Text.UTF8Encoding]$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)

function Write-TextFile {
    param(
        [string]$Path,
        [string]$Content
    )

    [System.IO.File]::WriteAllText($Path, $Content, $Utf8NoBom)
}

# Add-Content is avoided for appends: Windows PowerShell 5.1 stamps a BOM onto
# an empty file, which would corrupt the first CSV row for readers that do not
# strip it.
function Add-TextLine {
    param(
        [string]$Path,
        [string]$Line
    )

    [System.IO.File]::AppendAllText($Path, $Line + "`r`n", $Utf8NoBom)
}

Write-TextFile -Path $CommandsCsv -Content "epoch_ms,iteration,command,exit_code,duration_ms,payload_bytes`r`n"
Write-TextFile -Path $MetricsCsv -Content "epoch_ms,iteration,unity_rss_kb,project_runner_procs,outputs_dir_kb`r`n"
# Created up front so the first append has a file to write into.
Write-TextFile -Path $RunLog -Content ""

function Write-SoakLog {
    param(
        [string]$Message
    )

    [string]$line = "{0} [soak] {1}" -f (Get-Date).ToString("HH:mm:ss"), $Message
    Write-Host $line
    Add-TextLine -Path $RunLog -Line $line
}

function Get-EpochMilliseconds {
    return [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
}

function ConvertTo-NativeCommandLineArgument {
    param(
        [string]$Argument
    )

    # Start-Process takes a raw command line, so each token is quoted here
    # instead of by PowerShell. Windows argv rules treat backslashes specially
    # only in front of a quote or the closing quote of a quoted token: ordinary
    # paths such as C:\Users must stay unchanged, while a run of backslashes
    # that reaches a quote has to be doubled.
    [string]$escapedArgument = [regex]::Replace($Argument, '(\\*)"', {
        param($match)

        [string]$backslashes = $match.Groups[1].Value
        return $backslashes + $backslashes + '\"'
    })

    if ($Argument.Length -gt 0 -and $Argument -notmatch '[\s"]') {
        return $escapedArgument
    }

    [string]$quotedArgument = [regex]::Replace($escapedArgument, '(\\*)\z', {
        param($match)

        [string]$backslashes = $match.Groups[1].Value
        return $backslashes + $backslashes
    })

    return '"' + $quotedArgument + '"'
}

function Get-CapturedText {
    param(
        [string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return ""
    }

    [string]$text = Get-Content -LiteralPath $Path -Raw -Encoding UTF8
    if ($null -eq $text) {
        return ""
    }

    return $text
}

function Get-FileByteLength {
    param(
        [string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return 0
    }

    return [int64](Get-Item -LiteralPath $Path).Length
}

# Every uloop call runs under a kill watchdog: a hung IPC call - e.g. a frozen
# editor that accepted the connection but never answers - must surface as one
# failed, finite sample so the consecutive-failure recovery can fire, instead
# of blocking the unattended soak forever. The bound has to stay above the
# slowest legitimate command, which is project- and load-dependent, so
# -CommandTimeoutSeconds exists: a forced recompile of a large project takes
# ~8 minutes on its own and longer while other editors compile in parallel.
#
# Reported for a killed command; matches the exit code GNU timeout(1) uses, so
# a timeout is distinguishable from any exit code uloop itself returns.
[int]$TimeoutExitCode = 124

function Invoke-Uloop {
    param(
        [string[]]$CommandArguments
    )

    [string[]]$allArguments = @($CommandArguments) + @("--project-path", $ResolvedProjectPath)
    [string]$commandLine = (@($allArguments | ForEach-Object { ConvertTo-NativeCommandLineArgument -Argument $_ })) -join " "

    Remove-Item -LiteralPath $StdOutFile, $StdErrFile -Force -ErrorAction SilentlyContinue
    [System.Diagnostics.Process]$process = Start-Process -FilePath $ResolvedUloopBin `
        -ArgumentList $commandLine `
        -NoNewWindow `
        -PassThru `
        -RedirectStandardOutput $StdOutFile `
        -RedirectStandardError $StdErrFile
    # Touching Handle caches the process handle: without it Windows PowerShell
    # can report a null ExitCode for a -PassThru process it did not -Wait on.
    $null = $process.Handle

    [int]$exitCode = 0
    if ($process.WaitForExit($CommandTimeoutSeconds * 1000)) {
        $exitCode = $process.ExitCode
    }
    else {
        try {
            $process.Kill()
        }
        catch {
            # Already gone between the timeout and the kill.
        }
        $process.WaitForExit()
        $exitCode = $TimeoutExitCode
    }

    [string]$text = (Get-CapturedText -Path $StdOutFile) + (Get-CapturedText -Path $StdErrFile)
    [int64]$bytes = (Get-FileByteLength -Path $StdOutFile) + (Get-FileByteLength -Path $StdErrFile)

    return [pscustomobject]@{
        Arguments = $allArguments
        ExitCode = $exitCode
        Text = $text
        Bytes = $bytes
    }
}

# Known-benign outcomes are counted per label so the summary can report them
# apart from real failures. The raw exit code still reaches commands.csv: the
# CSV stays the unfiltered record, the log and the summary stay signal.
[hashtable]$ToleratedCounts = @{}

# uloop is single-flight: while Unity runs one tool, every other command is
# refused at dispatch with UNITY_SERVER_BUSY. That is back-pressure from work
# the harness itself started - a compile that outlived the runner's own wait
# keeps the editor busy for minutes - not a defect, and counting it would fail
# a whole iteration's worth of commands over one slow compile. Such attempts
# are waited out instead, and only the decisive attempt is recorded, so
# commands.csv keeps one row per intended invocation.
#
# The wait is driven by uloop's own SafeToRetry classification rather than by
# matching one hardcoded error code. Busy is not the only transient state a
# soak crosses: an editor whose domain reload has torn the IPC pipe down
# answers UNITY_NOT_REACHABLE (Phase "connection") for as long as the reload
# runs, which on a large project is minutes. Matching only UNITY_SERVER_BUSY
# reported those as defects - measured 3 of 3 post-restart scene restores
# against a large project, all of them false positives.
#
# SafeToRetry, not Retryable, is the right axis. Both are set by
# cli/common/errors: Retryable says the condition is transient, while
# SafeToRetry says re-issuing cannot double-apply the command. Unity may
# already have received a request that failed after dispatch, and this harness
# re-issues commands that mutate project and scene state, so anything uloop
# marks SafeToRetry:false is reported as a failure instead of being retried.
[regex]$SafeToRetryPattern = [regex]::new('"SafeToRetry"\s*:\s*true', [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
[regex]$ErrorCodePattern = [regex]::new('"ErrorCode"\s*:\s*"([^"]+)"')
[int]$BusyRetryLimit = 20
[int]$BusyRetryDelaySeconds = 30

# SafeToRetry only classifies errors the CLI itself raises. A tool that ran
# inside Unity answers with its own response envelope, which carries no such
# field, so a transient reported there has to be recognised by its message.
# Waiting out the IPC outage is not enough on its own: once the pipe is back,
# execute-dynamic-code can still answer that its runtime was disposed by the
# same domain reload (ExecuteDynamicCodeUseCase maps ObjectDisposedException
# at the UseCase boundary). The code never ran, and the tool's own guidance is
# to retry shortly, so this is waited out the same way. Keyed by the message
# with the constant's name as the value, so the run log can name the wait.
[hashtable]$ToolTransientReasons = @{
    "Dynamic-code runtime was disposed during a server reset or domain reload" = "DYNAMIC_CODE_RUNTIME_RESTARTING"
}

# Returns a short name for the transient state a payload reports, or an empty
# string when the payload is not something this harness may re-issue.
function Get-TransientRetryReason {
    param(
        [string]$Text
    )

    if ($SafeToRetryPattern.IsMatch($Text)) {
        return (Get-PayloadErrorCode -Text $Text)
    }

    foreach ($message in $ToolTransientReasons.Keys) {
        if ($Text.Contains($message)) {
            return $ToolTransientReasons[$message]
        }
    }

    return ""
}

# The error code is only pulled out to name the wait in the run log: a soak
# that stalls for ten minutes must say which transient state it is waiting on.
function Get-PayloadErrorCode {
    param(
        [string]$Text
    )

    [System.Text.RegularExpressions.Match]$match = $ErrorCodePattern.Match($Text)
    if (-not $match.Success) {
        return "unknown"
    }

    return $match.Groups[1].Value
}

# Documented outcomes that are expected to fail without being a defect.
# CompileCollisionPattern is the only one worth retrying - the others describe
# a finished command whose result is simply not a clean success.
[string]$CompileCollisionPattern = "Compilation is already in progress"
[string]$ForcedUnknownResultPattern = "definitive result"

# A failing command's payload is the only thing that identifies which of the
# several exit-1 paths a command actually took, and the run log deliberately
# keeps just a 200-character excerpt so the log stays readable. The full text
# used to survive only in .last-stdout/.last-stderr, which the very next
# command overwrites - so by the time a soak finished, the evidence for every
# failure but the last was gone. Each failure is written to its own file here
# instead, which is what makes an unattended overnight soak diagnosable.
[int]$FailurePayloadCount = 0

function Save-FailurePayload {
    param(
        [int]$Iteration,
        [string]$Label,
        [string]$Kind,
        [pscustomobject]$Result
    )

    $script:FailurePayloadCount = $script:FailurePayloadCount + 1
    # Zero-padded so the files sort in the order the failures happened, which
    # is the order they have to be read in to follow a cascade.
    [string]$sequence = "{0:d4}" -f $FailurePayloadCount
    [string]$safeLabel = ($Label -replace '[^A-Za-z0-9._-]', '-')
    [string]$path = Join-Path $FailuresDir "$sequence-iter$Iteration-$safeLabel-exit$($Result.ExitCode).txt"

    try {
        $null = New-Item -ItemType Directory -Path $FailuresDir -Force
        [string[]]$lines = @(
            "kind: $Kind",
            "iteration: $Iteration",
            "label: $Label",
            "exit_code: $($Result.ExitCode)",
            "payload_bytes: $($Result.Bytes)",
            "utc: $([DateTimeOffset]::UtcNow.ToString('o'))",
            "command: $(($Result.Arguments) -join ' ')",
            "",
            "----- payload (stdout + stderr) -----",
            $Result.Text
        )
        Write-TextFile -Path $path -Content ($lines -join "`r`n")
    }
    catch {
        # Never let diagnostics collection end a soak that is otherwise healthy.
        Write-SoakLog -Message "could not save failure payload for iter=$Iteration $Label : $($_.Exception.Message)"
    }
}

# Runs one uloop command, appends a CSV row, and returns the command result.
# ToleratedPatterns names the documented outcomes for this command that are
# expected to fail without being a defect (see docs/soak-testing.md); a
# non-zero exit whose payload contains one is reported as TOLERATED, and the
# returned Tolerated flag tells the caller not to fail the iteration.
# ToleratedPattern reports which one matched, so a caller can react to a
# specific outcome rather than to tolerance in general.
function Invoke-TimedUloop {
    param(
        [int]$Iteration,
        [string]$Label,
        [string[]]$CommandArguments,
        [string[]]$ToleratedPatterns = @()
    )

    [int64]$start = 0
    [int64]$end = 0
    [pscustomobject]$result = $null
    [bool]$waitedForEditor = $false
    [string]$lastRetryReason = ""
    for ([int]$attempt = 1; $attempt -le $BusyRetryLimit; $attempt++) {
        $start = Get-EpochMilliseconds
        $result = Invoke-Uloop -CommandArguments $CommandArguments
        $end = Get-EpochMilliseconds

        [string]$retryReason = ""
        if ($result.ExitCode -ne 0) {
            $retryReason = Get-TransientRetryReason -Text $result.Text
        }

        if ($result.ExitCode -eq 0 -or [string]::IsNullOrEmpty($retryReason) -or $attempt -eq $BusyRetryLimit) {
            break
        }

        # Logged again whenever the reason changes, not just on the first wait:
        # one restart can walk through several transients in a row (busy, then
        # an unreachable pipe, then a disposed dynamic-code runtime), and a log
        # naming only the first hides how the editor actually recovered.
        if ($retryReason -ne $lastRetryReason) {
            $waitedForEditor = $true
            $lastRetryReason = $retryReason
            Write-SoakLog -Message "iter=$Iteration $Label deferred on $retryReason (uloop reports it safe to retry), retrying every ${BusyRetryDelaySeconds}s"
        }
        Start-Sleep -Seconds $BusyRetryDelaySeconds
    }

    if ($waitedForEditor -and $result.ExitCode -eq 0) {
        Write-SoakLog -Message "iter=$Iteration $Label ran once the editor was free"
    }

    Add-TextLine -Path $CommandsCsv -Line ("{0},{1},{2},{3},{4},{5}" -f $start, $Iteration, $Label, $result.ExitCode, ($end - $start), $result.Bytes)

    [bool]$tolerated = $false
    [string]$matchedPattern = ""
    if ($result.ExitCode -ne 0) {
        foreach ($pattern in $ToleratedPatterns) {
            if ((-not [string]::IsNullOrEmpty($pattern)) -and $result.Text.Contains($pattern)) {
                $tolerated = $true
                $matchedPattern = $pattern
                break
            }
        }

        [string]$excerpt = $result.Text
        if ($excerpt.Length -gt 200) {
            $excerpt = $excerpt.Substring(0, 200)
        }
        $excerpt = ($excerpt -replace '\r?\n', ' ')

        if ($tolerated) {
            if (-not $ToleratedCounts.ContainsKey($Label)) {
                $ToleratedCounts[$Label] = 0
            }
            $ToleratedCounts[$Label] = $ToleratedCounts[$Label] + 1
            Write-SoakLog -Message "TOLERATED iter=$Iteration $Label exit=$($result.ExitCode) ($excerpt)"
            # Tolerated outcomes are saved too: a pattern match only proves the
            # payload contains a known-benign string, not that nothing else
            # went wrong alongside it.
            Save-FailurePayload -Iteration $Iteration -Label $Label -Kind "TOLERATED" -Result $result
        }
        else {
            Write-SoakLog -Message "FAIL iter=$Iteration $Label exit=$($result.ExitCode) ($excerpt)"
            Save-FailurePayload -Iteration $Iteration -Label $Label -Kind "FAIL" -Result $result
        }
    }

    return [pscustomobject]@{
        Arguments = $result.Arguments
        ExitCode = $result.ExitCode
        Text = $result.Text
        Bytes = $result.Bytes
        Tolerated = $tolerated
        ToleratedPattern = $matchedPattern
    }
}

# An explicit compile can collide with a compilation Unity started on its own
# ("Compilation is already in progress", Retryable: true) - after an editor
# restart, and after the setup switches code optimization, which recompiles
# every assembly. Retrying absorbs that race; any other failure is returned to
# the caller untouched. Exhausting the attempts is a real failure, so the last
# collision is un-tolerated before returning.
function Invoke-CompileWithRetry {
    param(
        [int]$Iteration,
        [string]$Label,
        [string[]]$CompileArguments,
        [int]$MaxAttempts = 3
    )

    [pscustomobject]$result = $null
    [string]$attemptLabel = $Label
    for ([int]$attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
        $attemptLabel = $Label
        if ($attempt -gt 1) {
            $attemptLabel = "$Label-retry"
        }

        # A plain compile issued after a forced one that outlived the runner's
        # wait attaches to it and returns the forced compile's own unknown
        # result. That is the recovery working, not a collision, so it is
        # tolerated but must not be retried.
        $result = Invoke-TimedUloop -Iteration $Iteration -Label $attemptLabel -CommandArguments $CompileArguments -ToleratedPatterns @($CompileCollisionPattern, $ForcedUnknownResultPattern)
        if ($result.ExitCode -eq 0 -or $result.ToleratedPattern -ne $CompileCollisionPattern) {
            return $result
        }

        Start-Sleep -Seconds 10
    }

    if ($ToleratedCounts.ContainsKey($attemptLabel)) {
        $ToleratedCounts[$attemptLabel] = $ToleratedCounts[$attemptLabel] - 1
    }
    Write-SoakLog -Message "FAIL iter=$Iteration $Label still collided with an in-progress compilation after $MaxAttempts attempts"

    return [pscustomobject]@{
        Arguments = $result.Arguments
        ExitCode = $result.ExitCode
        Text = $result.Text
        Bytes = $result.Bytes
        Tolerated = $false
    }
}

# Forces a script recompile by rewriting the scratch file with a new constant.
function Write-ScratchScript {
    param(
        [int]$Iteration
    )

    $null = New-Item -ItemType Directory -Path $ScratchDir -Force
    Write-TextFile -Path $ScratchFile -Content @"
// Auto-generated by soak-loop.ps1 - safe to delete.
// Rewritten every iteration to force a script recompile and domain reload.
public static class UloopSoakScratch
{
    public const int Iteration = $Iteration;
}
"@
}

[string]$TickerSource = @'
// Auto-generated by soak-loop.ps1 - safe to delete.
// PlayMode ticker whose Update line is the soak's pause-point target.
using UnityEngine;

public class UloopSoakTicker : MonoBehaviour
{
    private int tickCount;

    private void Update()
    {
        tickCount++;
    }
}
'@

# The pause point must be armed on the line that actually executes every
# frame. Deriving it from the source above removes the hand-maintained
# constant the shell harness has to keep in sync with its heredoc.
function Get-TickerPauseLine {
    [string[]]$lines = $TickerSource -split "`r?`n"
    for ([int]$index = 0; $index -lt $lines.Length; $index++) {
        if ($lines[$index] -match 'tickCount\+\+;') {
            return $index + 1
        }
    }

    throw "The generated ticker source no longer contains its pause-point line."
}

# The uloop relative paths use forward slashes; joining them onto a Windows
# root needs the separators normalized first.
function Resolve-ProjectRelativePath {
    param(
        [string]$RelativePath
    )

    return Join-Path $ResolvedProjectPath ($RelativePath -replace '/', '\')
}

function Write-TickerScripts {
    $null = New-Item -ItemType Directory -Path $SoakAssetsDir -Force
    Write-TextFile -Path (Resolve-ProjectRelativePath -RelativePath $TickerRelativePath) -Content $TickerSource
    # The probe must live in its own file: Unity only serializes a
    # MonoBehaviour into a scene when its class name matches the file name,
    # and a second class in a shared file saves as a missing-script reference.
    Write-TextFile -Path (Resolve-ProjectRelativePath -RelativePath $ProbeRelativePath) -Content @'
// Auto-generated by soak-loop.ps1 - safe to delete.
// Counts EventSystem clicks so the soak can verify simulate-mouse-ui delivery.
using UnityEngine;

public class UloopSoakButtonProbe : MonoBehaviour, UnityEngine.EventSystems.IPointerClickHandler
{
    public int ClickCount { get; private set; }

    public void OnPointerClick(UnityEngine.EventSystems.PointerEventData eventData)
    {
        ClickCount++;
        Debug.Log("UloopSoakButtonClicked count=" + ClickCount);
    }
}
'@
}

# Discards any previous soak scene and rebuilds it from scratch: the
# pause-point ticker plus a clickable button wired for the UI simulation
# check. Recreating every time keeps stale generated state from leaking
# between cycles. Two variants are written: the guarded one refuses to
# discard unsaved USER scene changes (returns DIRTY_SCENE), while the force
# variant skips that guard - it is used only right after a harness-initiated
# editor restart, when any dirt in the reopened startup scene was produced
# by project tooling during startup and cannot be user work.
function Write-SceneSetupSnippets {
    [string]$forceSource = @"
string scenePath = "$SceneRelativePath";
UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
if (System.IO.File.Exists(scenePath))
{
    UnityEditor.AssetDatabase.DeleteAsset(scenePath);
}
UnityEngine.GameObject tickerGo = new UnityEngine.GameObject("SoakTicker");
tickerGo.AddComponent<UloopSoakTicker>();
UnityEngine.GameObject canvasGo = new UnityEngine.GameObject("SoakCanvas");
UnityEngine.Canvas canvas = canvasGo.AddComponent<UnityEngine.Canvas>();
canvas.renderMode = UnityEngine.RenderMode.ScreenSpaceOverlay;
canvasGo.AddComponent<UnityEngine.UI.CanvasScaler>();
canvasGo.AddComponent<UnityEngine.UI.GraphicRaycaster>();
UnityEngine.GameObject buttonGo = new UnityEngine.GameObject("SoakButton");
buttonGo.transform.SetParent(canvasGo.transform, false);
buttonGo.AddComponent<UnityEngine.UI.Image>();
buttonGo.AddComponent<UloopSoakButtonProbe>();
UnityEngine.RectTransform rect = buttonGo.GetComponent<UnityEngine.RectTransform>();
rect.sizeDelta = new UnityEngine.Vector2(320f, 120f);
UnityEngine.GameObject eventSystemGo = new UnityEngine.GameObject("SoakEventSystem");
eventSystemGo.AddComponent<UnityEngine.EventSystems.EventSystem>();
eventSystemGo.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
UnityEditor.SceneManagement.EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), scenePath);
return "recreated";
"@

    [string]$guard = @"
UnityEngine.SceneManagement.Scene guardActive = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
if (guardActive.path != "$SceneRelativePath" && guardActive.isDirty)
{
    return "DIRTY_SCENE";
}
"@

    Write-TextFile -Path $SceneSetupForceFile -Content $forceSource
    Write-TextFile -Path $SceneSetupFile -Content ($guard + "`n" + $forceSource)
}

# Runs before every pause cycle, not just at setup: an editor restart mid-soak
# reopens the project's own last scene, which would silently leave the ticker
# out of PlayMode and expire every subsequent pause-point.
function Invoke-EnsureSoakScene {
    param(
        [int]$Iteration
    )

    return Invoke-TimedUloop -Iteration $Iteration -Label "scene-ensure" -CommandArguments @("execute-dynamic-code", "--code-file", $SceneSetupFile)
}

function Get-UnityProcessForProject {
    [object[]]$processes = @(Get-CimInstance Win32_Process -Filter "Name = 'Unity.exe'" -ErrorAction SilentlyContinue | Where-Object { $_.CommandLine })
    foreach ($process in $processes) {
        if ($process.CommandLine.IndexOf($ResolvedProjectPath, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
            return $process
        }
    }

    return $null
}

function Get-OutputsDirectoryKb {
    [string]$outputsPath = Join-Path $ResolvedProjectPath ".uloop\outputs"
    if (-not (Test-Path -LiteralPath $outputsPath)) {
        return 0
    }

    [object]$sum = (Get-ChildItem -LiteralPath $outputsPath -Recurse -File -Force -ErrorAction SilentlyContinue | Measure-Object -Property Length -Sum).Sum
    if ($null -eq $sum) {
        return 0
    }

    return [int64]($sum / 1KB)
}

function Write-Metrics {
    param(
        [int]$Iteration
    )

    [string]$workingSetKb = ""
    [object]$unityProcess = Get-UnityProcessForProject
    if ($null -ne $unityProcess) {
        $workingSetKb = [string][int64]([int64]$unityProcess.WorkingSetSize / 1KB)
    }

    [int]$runnerCount = @(Get-Process -Name "uloop-project-runner" -ErrorAction SilentlyContinue).Count
    Add-TextLine -Path $MetricsCsv -Line ("{0},{1},{2},{3},{4}" -f (Get-EpochMilliseconds), $Iteration, $workingSetKb, $runnerCount, (Get-OutputsDirectoryKb))
}

# Polls get-logs until the editor answers again (used after restarts and recovery).
function Wait-Editor {
    [datetime]$deadline = (Get-Date).AddSeconds(900)
    while ((Get-Date) -lt $deadline) {
        [pscustomobject]$result = Invoke-Uloop -CommandArguments @("get-logs", "--max-count", "1")
        if ($result.ExitCode -eq 0) {
            return $true
        }

        Start-Sleep -Seconds 20
    }

    return $false
}

# enable-pause-point resolves a file and line through information Unity only
# keeps under Debug code optimization: against a Release editor every arm call
# fails on that precondition, so the whole PlayMode cycle would soak a
# guaranteed failure instead of the paths it is meant to exercise. The original
# mode is remembered here and restored when the run ends.
[string]$PreviousCodeOptimization = ""

# Re-applied after every editor restart, not just at setup: the mode does not
# survive `uloop launch -r`. An editor that comes back on the project's own
# Release setting cannot arm pause points at all, which silently turned every
# post-restart pause cycle into a failure until this was found.
function Set-DebugCodeOptimization {
    param(
        [int]$Iteration
    )

    if ($KeepCodeOptimization -or $PauseEvery -le 0) {
        return
    }

    [pscustomobject]$result = Invoke-TimedUloop -Iteration $Iteration -Label "code-optimization" -CommandArguments @(
        "execute-dynamic-code",
        "--code",
        'UnityEditor.Compilation.CodeOptimization previous = UnityEditor.Compilation.CompilationPipeline.codeOptimization; if (previous != UnityEditor.Compilation.CodeOptimization.Debug) { UnityEditor.Compilation.CompilationPipeline.codeOptimization = UnityEditor.Compilation.CodeOptimization.Debug; } return previous.ToString();'
    )
    if ($result.ExitCode -ne 0) {
        Write-SoakLog -Message "iter=$Iteration could not set Debug code optimization - pause points will fail while the editor stays on Release."
        return $null
    }

    return $result
}

function Initialize-CodeOptimization {
    if ($KeepCodeOptimization) {
        return
    }

    [pscustomobject]$result = Set-DebugCodeOptimization -Iteration 0
    if ($null -eq $result) {
        return
    }

    [object]$json = $null
    try {
        $json = $result.Text | ConvertFrom-Json
    }
    catch {
        return
    }

    if ($null -eq $json -or ($json.PSObject.Properties.Name -notcontains "Result") -or ($json.Result -eq "Debug")) {
        return
    }

    $script:PreviousCodeOptimization = [string]$json.Result
    Write-SoakLog -Message "code optimization: switched $($json.Result) -> Debug so pause points can be armed (restored when the run ends)"
}

# Teardown runs right after whatever ended the soak, so the editor is often
# still busy (a killed compile keeps running inside Unity) and rejects the
# command. Leaving a project on Debug because one attempt bounced is a state
# leak the operator has no reason to expect, so this retries and only claims
# success once the editor reports the restored mode back.
function Restore-CodeOptimization {
    if ([string]::IsNullOrEmpty($PreviousCodeOptimization)) {
        return
    }

    [string]$previous = $PreviousCodeOptimization
    # Cleared first so a failing restore is not retried on every later call.
    $script:PreviousCodeOptimization = ""

    for ([int]$attempt = 1; $attempt -le 10; $attempt++) {
        [pscustomobject]$result = Invoke-Uloop -CommandArguments @(
            "execute-dynamic-code",
            "--code",
            "UnityEditor.Compilation.CompilationPipeline.codeOptimization = UnityEditor.Compilation.CodeOptimization.$previous; return UnityEditor.Compilation.CompilationPipeline.codeOptimization.ToString();"
        )
        if ($result.ExitCode -eq 0 -and $result.Text.Contains('"Result": "' + $previous + '"')) {
            Write-SoakLog -Message "code optimization: restored $previous (the editor recompiles once more in the background)"
            return
        }

        Start-Sleep -Seconds 30
    }

    Write-SoakLog -Message "WARNING: could not restore code optimization to $previous - the editor is still on Debug, switch it back from the bug icon in the main toolbar."
}

# A soak aborted mid-pause-cycle would leave the editor paused in PlayMode;
# always hand the editor back in a usable state.
function Reset-EditorState {
    if ($PauseEvery -le 0) {
        return
    }

    try {
        $null = Invoke-Uloop -CommandArguments @("clear-pause-point", "--all")
        $null = Invoke-Uloop -CommandArguments @("control-play-mode", "--action", "Stop")
        # Only after PlayMode has stopped: Unity refuses to recompile while playing.
        Restore-CodeOptimization
        [string]$releaseSoakSceneCode = 'string activeScenePath = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path; if (!System.String.Equals(activeScenePath, "Assets/UloopSoak/UloopSoak.unity", System.StringComparison.Ordinal)) { return "not-soak-scene"; } UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single); return "released";'
        $null = Invoke-Uloop -CommandArguments @("execute-dynamic-code", "--code", $releaseSoakSceneCode)
    }
    catch {
        Write-SoakLog -Message "Editor state cleanup failed: $($_.Exception.Message)"
    }
}

function Write-Summary {
    Reset-EditorState
    Write-SoakLog -Message "Results: $ResolvedOutDir"
    if ($FailurePayloadCount -gt 0) {
        Write-SoakLog -Message "Full payloads for $FailurePayloadCount failed/tolerated commands: $FailuresDir"
    }

    [object[]]$rows = @(Import-Csv -LiteralPath $CommandsCsv)
    [string]$header = "{0,-20} {1,8} {2,8} {3,10} {4,10}" -f "command", "runs", "fails", "tolerated", "avg_ms"
    Write-Host $header
    Add-TextLine -Path $RunLog -Line $header
    foreach ($group in ($rows | Group-Object -Property command)) {
        [int]$runs = $group.Count
        [int]$nonZero = @($group.Group | Where-Object { $_.exit_code -ne "0" }).Count
        [int]$tolerated = 0
        if ($ToleratedCounts.ContainsKey($group.Name)) {
            $tolerated = $ToleratedCounts[$group.Name]
        }
        [int]$fails = $nonZero - $tolerated
        [int64]$totalMs = 0
        foreach ($row in $group.Group) {
            $totalMs += [int64]$row.duration_ms
        }

        [string]$line = "{0,-20} {1,8} {2,8} {3,10} {4,10}" -f $group.Name, $runs, $fails, $tolerated, [int64]($totalMs / $runs)
        Write-Host $line
        Add-TextLine -Path $RunLog -Line $line
    }

    Write-SoakLog -Message "Reminder: the harness released the soak scene if it was still active; delete $SoakAssetsDir (and its .meta) from the target project when finished."
}

# Reads the SoakButton's simulation coordinates out of an annotated screenshot
# response. Returns $null when the element is missing or the payload is not
# the expected JSON, which the caller reports as a failed iteration.
function Get-SoakButtonCoordinates {
    param(
        [string]$Text
    )

    [object]$json = $null
    try {
        $json = $Text | ConvertFrom-Json
    }
    catch {
        return $null
    }

    if ($null -eq $json -or ($json.PSObject.Properties.Name -notcontains "Screenshots")) {
        return $null
    }

    [object[]]$screenshots = @($json.Screenshots)
    if ($screenshots.Count -eq 0 -or ($screenshots[0].PSObject.Properties.Name -notcontains "AnnotatedElements")) {
        return $null
    }

    foreach ($element in @($screenshots[0].AnnotatedElements)) {
        [string[]]$elementProperties = @($element.PSObject.Properties.Name)
        if (($elementProperties -notcontains "Path") -or ($elementProperties -notcontains "SimX") -or ($elementProperties -notcontains "SimY")) {
            continue
        }

        if ($element.Path -ne "SoakCanvas/SoakButton") {
            continue
        }

        return [pscustomobject]@{
            X = ([double]$element.SimX).ToString([Globalization.CultureInfo]::InvariantCulture)
            Y = ([double]$element.SimY).ToString([Globalization.CultureInfo]::InvariantCulture)
        }
    }

    return $null
}

function Invoke-PauseCycle {
    param(
        [int]$Iteration,
        [int]$TickerLine
    )

    [bool]$failed = $false
    [pscustomobject]$sceneResult = Invoke-EnsureSoakScene -Iteration $Iteration
    # Told apart deliberately: the command failing is a defect signal whose
    # payload the FAIL line above already carries, while DIRTY_SCENE is the
    # guard refusing to discard someone's unsaved work.
    if ($sceneResult.ExitCode -ne 0) {
        Write-SoakLog -Message "iter=$Iteration soak scene could not be rebuilt - pause cycle failed"
        return $true
    }
    if ($sceneResult.Text.Contains("DIRTY_SCENE")) {
        Write-SoakLog -Message "iter=$Iteration the active scene has unsaved changes - pause cycle skipped to protect them"
        return $true
    }

    [pscustomobject]$playResult = Invoke-TimedUloop -Iteration $Iteration -Label "play-start" -CommandArguments @("control-play-mode", "--action", "Play")
    if ($playResult.ExitCode -ne 0) {
        return $true
    }

    # UI simulation runs before the pause-point so EventSystem click
    # processing happens while the game is still un-paused. Per the
    # simulate-mouse-ui contract, clicks are coordinate-driven - the canonical
    # flow is reading the button's SimX/SimY from the screenshot element
    # annotation, which this also soaks.
    [object]$button = $null
    [pscustomobject]$annotateResult = Invoke-TimedUloop -Iteration $Iteration -Label "ui-annotate" -CommandArguments @("screenshot", "--capture-mode", "rendering", "--annotate-elements", "--elements-only")
    if ($annotateResult.ExitCode -eq 0) {
        $button = Get-SoakButtonCoordinates -Text $annotateResult.Text
    }

    if ($null -eq $button) {
        Write-SoakLog -Message "iter=$Iteration SoakButton missing from annotated elements"
        $failed = $true
    }
    else {
        [pscustomobject]$clickResult = Invoke-TimedUloop -Iteration $Iteration -Label "ui-click" -CommandArguments @("simulate-mouse-ui", "--action", "Click", "--x", $button.X, "--y", $button.Y)
        if ($clickResult.ExitCode -ne 0) {
            $failed = $true
        }
        elseif (-not $clickResult.Text.Contains('"HitGameObjectName": "SoakButton"')) {
            Write-SoakLog -Message "iter=$Iteration click did not hit SoakButton"
            $failed = $true
        }

        [pscustomobject]$verifyResult = Invoke-TimedUloop -Iteration $Iteration -Label "ui-verify" -CommandArguments @("execute-dynamic-code", "--code", 'UloopSoakButtonProbe probe = UnityEngine.Object.FindFirstObjectByType<UloopSoakButtonProbe>(); return probe == null ? "probe-missing" : probe.ClickCount.ToString();')
        if ($verifyResult.ExitCode -ne 0) {
            $failed = $true
        }
        elseif (-not $verifyResult.Text.Contains('"Result": "1"')) {
            Write-SoakLog -Message "iter=$Iteration button click was not registered by the probe"
            $failed = $true
        }
    }

    [pscustomobject]$armResult = Invoke-TimedUloop -Iteration $Iteration -Label "pause-arm" -CommandArguments @("enable-pause-point", "--file", $TickerRelativePath, "--line", "$TickerLine", "--timeout-seconds", "60")
    if ($armResult.ExitCode -ne 0) {
        $failed = $true
    }

    [pscustomobject]$awaitResult = Invoke-TimedUloop -Iteration $Iteration -Label "pause-await" -CommandArguments @("await-pause-point", "--id", "${TickerRelativePath}:${TickerLine}", "--timeout-seconds", "60")
    if ($awaitResult.ExitCode -ne 0) {
        $failed = $true
    }
    else {
        if (-not $awaitResult.Text.Contains('"Hit"')) {
            Write-SoakLog -Message "iter=$Iteration pause-point await returned without a Hit"
            $failed = $true
        }
        # A Hit whose CapturedVariables lacks the ticker's field means the
        # variable-capture pipeline broke even though pausing works.
        if (-not $awaitResult.Text.Contains('"tickCount"')) {
            Write-SoakLog -Message "iter=$Iteration pause-point hit but tickCount was not captured"
            $failed = $true
        }
    }

    $null = Invoke-Uloop -CommandArguments @("clear-pause-point", "--all")
    [pscustomobject]$stopResult = Invoke-TimedUloop -Iteration $Iteration -Label "play-stop" -CommandArguments @("control-play-mode", "--action", "Stop")
    if ($stopResult.ExitCode -ne 0) {
        $failed = $true
    }

    return $failed
}

try {
    Write-SoakLog -Message "Soak start: $Iterations iterations against $ResolvedProjectPath (uloop: $ResolvedUloopBin)"
    Write-SoakLog -Message "restart-every=$RestartEvery force-every=$ForceEvery pause-every=$PauseEvery tests-every=$TestsEvery sleep=${SleepSeconds}s command-timeout=${CommandTimeoutSeconds}s"

    # A freshly launched editor can be busy importing/compiling for a long time
    # (especially on large projects), so the preflight polls instead of
    # one-shotting. No response can mean either a busy editor or no editor at
    # all: launching over a busy editor would be wrong, and waiting 15 minutes
    # for an editor that was never started would be wasted, so the two cases
    # are told apart by whether a Unity process has this project open.
    if ((Invoke-Uloop -CommandArguments @("get-logs", "--max-count", "1")).ExitCode -ne 0) {
        if ($null -ne (Get-UnityProcessForProject)) {
            Write-SoakLog -Message "Editor busy - waiting up to 15 minutes for it to answer"
        }
        else {
            Write-SoakLog -Message "Editor not running - launching it with uloop launch"
            if ((Invoke-TimedUloop -Iteration 0 -Label "launch-start" -CommandArguments @("launch")).ExitCode -ne 0) {
                Write-SoakLog -Message "Preflight failed: could not launch the editor."
                exit 1
            }
        }

        if (-not (Wait-Editor)) {
            Write-SoakLog -Message "Preflight failed: uloop cannot reach the editor."
            exit 1
        }
    }

    # Runner generations flipped this flag's polarity: newer runners need an
    # explicit --wait-for-domain-reload (off by default), older ones wait by
    # default and only expose --no-wait-for-domain-reload. Detect which flavor
    # the pinned runner speaks.
    [string]$compileHelp = (Invoke-Uloop -CommandArguments @("compile", "--help")).Text
    [string[]]$CompileWaitArguments = @()
    if ($compileHelp -match '(?m)^\s*--wait-for-domain-reload') {
        $CompileWaitArguments = @("--wait-for-domain-reload")
    }
    if ($CompileWaitArguments.Count -gt 0) {
        Write-SoakLog -Message "compile wait flag: $($CompileWaitArguments[0])"
    }
    else {
        Write-SoakLog -Message "compile wait flag: (runner waits by default)"
    }

    # Runners predating the configurable wait reject the flag outright, and the
    # soak has to keep working against whichever runner the project pins. The
    # flag name also changed across runner generations, so probe help for the
    # current name first and fall back to the pre-rename one.
    if ($CompileWaitTimeoutSeconds -gt 0) {
        if ($compileHelp -match '(?m)^\s*--timeout-seconds') {
            $CompileWaitArguments += @("--timeout-seconds", "$CompileWaitTimeoutSeconds")
            Write-SoakLog -Message "compile wait timeout: ${CompileWaitTimeoutSeconds}s (watchdog ${CommandTimeoutSeconds}s)"
        }
        elseif ($compileHelp -match '(?m)^\s*--compile-wait-timeout-seconds') {
            $CompileWaitArguments += @("--compile-wait-timeout-seconds", "$CompileWaitTimeoutSeconds")
            Write-SoakLog -Message "compile wait timeout: ${CompileWaitTimeoutSeconds}s (watchdog ${CommandTimeoutSeconds}s)"
        }
        else {
            Write-SoakLog -Message "compile wait timeout: ignored - the pinned runner has no configurable compile wait flag"
        }
    }

    [int]$TickerLine = 0
    if ($PauseEvery -gt 0) {
        Write-TickerScripts
        $TickerLine = Get-TickerPauseLine
        # Before the setup compile: switching the mode recompiles everything
        # anyway, so the compile below doubles as the confirmation.
        Initialize-CodeOptimization
        if ((Invoke-CompileWithRetry -Iteration 0 -Label "setup-compile" -CompileArguments (@("compile") + $CompileWaitArguments)).ExitCode -ne 0) {
            Write-SoakLog -Message "Setup compile for the pause-point ticker failed - aborting."
            exit 1
        }

        Write-SceneSetupSnippets
        [pscustomobject]$setupSceneResult = Invoke-EnsureSoakScene -Iteration 0
        if ($setupSceneResult.ExitCode -ne 0) {
            Write-SoakLog -Message "Pause-point scene setup failed - aborting."
            exit 1
        }
        if ($setupSceneResult.Text.Contains("DIRTY_SCENE")) {
            Write-SoakLog -Message "The active scene has unsaved changes - save or discard them, then rerun."
            exit 1
        }

        Write-SoakLog -Message "pause-point scene ready: $SceneRelativePath"
    }

    [int]$consecutiveFails = 0
    [bool]$testsDeferred = $false
    for ([int]$i = 1; $i -le $Iterations; $i++) {
        Write-ScratchScript -Iteration $i

        [bool]$iterationFailed = $false
        [bool]$forcedThisIteration = $ForceEvery -gt 0 -and ($i % $ForceEvery) -eq 0
        # Forced recompiles rebuild every assembly - a heavier reload path
        # worth soaking, but far too slow to run on every iteration of a large
        # project. Unity may legitimately report an unknown forced result after
        # the domain reload (ForceCompileUnknownResult); the tool's own
        # guidance is to follow up with a plain compile, so only that follow-up
        # counts against the iteration and any other forced failure still does.
        if ($forcedThisIteration) {
            [pscustomobject]$forcedResult = Invoke-TimedUloop -Iteration $i -Label "compile-forced" -CommandArguments (@("compile") + $CompileWaitArguments + @("--force-recompile")) -ToleratedPatterns @($ForcedUnknownResultPattern)
            if ($forcedResult.ExitCode -ne 0 -and -not $forcedResult.Tolerated) {
                $iterationFailed = $true
            }
        }

        [pscustomobject]$compileResult = Invoke-CompileWithRetry -Iteration $i -Label "compile" -CompileArguments (@("compile") + $CompileWaitArguments)
        if ($compileResult.ExitCode -ne 0) {
            $iterationFailed = $true
        }

        if ((Invoke-TimedUloop -Iteration $i -Label "get-logs" -CommandArguments @("get-logs", "--max-count", "200")).ExitCode -ne 0) {
            $iterationFailed = $true
        }
        if ((Invoke-TimedUloop -Iteration $i -Label "get-hierarchy" -CommandArguments @("get-hierarchy", "--max-depth", "5")).ExitCode -ne 0) {
            $iterationFailed = $true
        }
        if ((Invoke-TimedUloop -Iteration $i -Label "screenshot" -CommandArguments @("screenshot", "--window-name", "Game", "--resolution-scale", "0.5")).ExitCode -ne 0) {
            $iterationFailed = $true
        }
        if ((Invoke-TimedUloop -Iteration $i -Label "dynamic-code" -CommandArguments @("execute-dynamic-code", "--code", "int iteration = $i; return iteration + UnityEngine.SceneManagement.SceneManager.sceneCount;")).ExitCode -ne 0) {
            $iterationFailed = $true
        }

        if ($PauseEvery -gt 0 -and ($i % $PauseEvery) -eq 0) {
            if (Invoke-PauseCycle -Iteration $i -TickerLine $TickerLine) {
                $iterationFailed = $true
            }
        }

        # The default cadences share their multiples, so tests used to land on
        # the same iteration as the forced recompile. Stacking them serialises
        # a full project rebuild in front of the test run, and while that
        # rebuild is still running inside Unity the test run cannot start at
        # all. Testing one iteration later keeps both measurable on their own.
        # The last iteration is the exception: deferring there would drop the
        # test run entirely, which is worse than running it stacked.
        [bool]$testsDue = $TestsEvery -gt 0 -and ((($i % $TestsEvery) -eq 0) -or $testsDeferred)
        if ($testsDue -and $forcedThisIteration -and $i -lt $Iterations) {
            $testsDeferred = $true
            $testsDue = $false
            Write-SoakLog -Message "iter=$i tests deferred one iteration to keep them apart from the forced recompile"
        }

        if ($testsDue) {
            $testsDeferred = $false
            # Red project tests are not a soak failure - the harness measures
            # whether uloop transported and completed the run, so only a
            # missing test report (no TestCount in the response) counts against
            # the iteration.
            [pscustomobject]$testsResult = Invoke-TimedUloop -Iteration $i -Label "run-tests" -CommandArguments @("run-tests", "--test-mode", "EditMode", "--filter-type", "assembly", "--filter-value", $TestAssembly) -ToleratedPatterns @('"TestCount"')
            if ($testsResult.ExitCode -ne 0 -and -not $testsResult.Tolerated) {
                $iterationFailed = $true
            }
        }

        if ($RestartEvery -gt 0 -and ($i % $RestartEvery) -eq 0 -and $i -lt $Iterations) {
            Write-SoakLog -Message "iter=$i scheduled editor restart"
            $null = Invoke-TimedUloop -Iteration $i -Label "launch-restart" -CommandArguments @("launch", "-r")
            if (-not (Wait-Editor)) {
                Write-SoakLog -Message "Editor did not come back within 15 minutes after scheduled restart - aborting."
                exit 1
            }

            # The editor comes back on the project's own code optimization mode.
            $null = Set-DebugCodeOptimization -Iteration $i

            # The reopened startup scene may be auto-dirtied by project
            # tooling, which would trip the DIRTY_SCENE guard on the next pause
            # cycle. Right after our own restart that dirt cannot be user work,
            # so rebuild the soak scene unconditionally.
            if ($PauseEvery -gt 0) {
                if ((Invoke-TimedUloop -Iteration $i -Label "scene-restore" -CommandArguments @("execute-dynamic-code", "--code-file", $SceneSetupForceFile)).ExitCode -ne 0) {
                    Write-SoakLog -Message "iter=$i post-restart scene restore failed"
                }
            }
        }

        Write-Metrics -Iteration $i

        if ($iterationFailed) {
            $consecutiveFails++
        }
        else {
            $consecutiveFails = 0
        }

        if ($consecutiveFails -ge 3) {
            Write-SoakLog -Message "3 consecutive failing iterations - attempting one recovery restart."
            # Not routed through Invoke-TimedUloop on purpose (the recovery is
            # not one of the measured samples), but its payload is still the
            # record of why a soak had to recover, so it is kept when it fails.
            [pscustomobject]$recoveryResult = Invoke-Uloop -CommandArguments @("launch", "-r")
            if ($recoveryResult.ExitCode -ne 0) {
                Save-FailurePayload -Iteration $i -Label "recovery-restart" -Kind "FAIL" -Result $recoveryResult
            }
            if (-not (Wait-Editor)) {
                Write-SoakLog -Message "Recovery restart failed - aborting soak."
                exit 1
            }

            # Same post-restart repair as the scheduled restart path.
            $null = Set-DebugCodeOptimization -Iteration $i
            if ($PauseEvery -gt 0) {
                if ((Invoke-TimedUloop -Iteration $i -Label "scene-restore" -CommandArguments @("execute-dynamic-code", "--code-file", $SceneSetupForceFile)).ExitCode -ne 0) {
                    Write-SoakLog -Message "iter=$i post-recovery scene restore failed"
                }
            }

            $consecutiveFails = 0
            Write-SoakLog -Message "Recovery succeeded, continuing."
        }

        if ($SleepSeconds -gt 0) {
            Start-Sleep -Seconds $SleepSeconds
        }
        if (($i % 10) -eq 0) {
            Write-SoakLog -Message "progress: $i/$Iterations iterations done"
        }
    }

    Write-SoakLog -Message "Soak completed: $Iterations iterations."
}
finally {
    Write-Summary
}
