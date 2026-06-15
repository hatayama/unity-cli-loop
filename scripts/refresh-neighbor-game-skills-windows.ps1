<#
Development helper for refreshing generated uloop skill files in sibling Unity projects.
This is not an installed agent skill or a runtime command. It exists to support local
uloop development by resetting each target Git repository, quitting each target
Unity Editor, regenerating Claude/Agents skill copies, committing those generated
files locally, removing Library after Unity has stopped, relaunching each project,
and opening the sample scene.
#>

param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$Arguments = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$DryRun = $false
$UloopRoot = $env:ULOOP_ROOT
$Project = @()
$Help = $false
$ExpectedProjectCount = 3
$DefaultProjectNames = @("cli-loop-block-kuzushi", "cli-loop-minecraft", "cli-loop-tetris")
$SampleScenePath = "Assets/Scenes/SampleScene.unity"
$Projects = [System.Collections.Generic.List[string]]::new()
$UloopBin = ""

function Show-Usage {
    Write-Host @"
Usage:
  powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\refresh-neighbor-game-skills-windows.ps1 [-DryRun] [-UloopRoot PATH] [-Project PATH]...
  powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\refresh-neighbor-game-skills-windows.ps1 [--dry-run] [--uloop-root PATH] [--project PATH]...

Workflow for each target Unity project:
  0. Reset Git state to HEAD and remove untracked files
  1. Quit Unity if running
  2. Install uloop skills for Claude and Agents
  3. Commit generated skill changes without pushing
  4. Remove Library
  5. Launch Unity with the local uloop launch command
  6. Open Assets/Scenes/SampleScene.unity
"@
}

function Fail {
    param(
        [string]$Message
    )

    [Console]::Error.WriteLine("ERROR: $Message")
    exit 1
}

function Read-Arguments {
    param(
        [string[]]$RawArguments
    )

    [System.Collections.Generic.List[string]]$projectArguments = [System.Collections.Generic.List[string]]::new()
    [int]$index = 0
    while ($index -lt $RawArguments.Count) {
        [string]$argument = $RawArguments[$index]
        switch ($argument) {
            { $_ -in @("-DryRun", "-dry-run", "--dry-run") } {
                $script:DryRun = $true
                $index++
                continue
            }
            { $_ -in @("-Help", "-help", "-h", "--help") } {
                $script:Help = $true
                $index++
                continue
            }
            { $_ -in @("-UloopRoot", "-uloop-root", "--uloop-root") } {
                if ($index + 1 -ge $RawArguments.Count) {
                    Fail "--uloop-root requires a path"
                }

                $script:UloopRoot = $RawArguments[$index + 1]
                $index += 2
                continue
            }
            { $_ -in @("-Project", "-project", "--project") } {
                if ($index + 1 -ge $RawArguments.Count) {
                    Fail "--project requires a path"
                }

                [void]$projectArguments.Add($RawArguments[$index + 1])
                $index += 2
                continue
            }
            default {
                Fail "unknown argument: $argument"
            }
        }
    }

    $script:Project = $projectArguments.ToArray()
}

function Format-CommandArgument {
    param(
        [string]$Value
    )

    if ($Value -notmatch "[\s'`"]") {
        return $Value
    }

    [string]$escapedValue = $Value.Replace("'", "''")
    return "'$escapedValue'"
}

function Format-Command {
    param(
        [string]$FilePath,
        [string[]]$Arguments
    )

    [string[]]$tokens = @($FilePath) + $Arguments
    return ($tokens | ForEach-Object { Format-CommandArgument -Value $_ }) -join " "
}

function Invoke-NativeCapture {
    param(
        [string]$FilePath,
        [string[]]$Arguments = @()
    )

    [string]$previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        [object[]]$output = & $FilePath @Arguments 2>&1
        [int]$exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    [string]$text = (@($output) | Where-Object { $null -ne $_ } | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine
    return [pscustomobject]@{
        ExitCode = $exitCode
        Text = $text
    }
}

function Invoke-NativeChecked {
    param(
        [string]$FilePath,
        [string[]]$Arguments = @()
    )

    if ($DryRun) {
        Write-Host "[dry-run] $(Format-Command -FilePath $FilePath -Arguments $Arguments)"
        return ""
    }

    [pscustomobject]$result = Invoke-NativeCapture -FilePath $FilePath -Arguments $Arguments
    if ($result.ExitCode -eq 0) {
        return $result.Text
    }

    if (-not [string]::IsNullOrWhiteSpace($result.Text)) {
        Write-Host $result.Text
    }

    [string]$commandText = Format-Command -FilePath $FilePath -Arguments $Arguments
    Fail "$commandText failed with exit code $($result.ExitCode)"
}

function Get-GitText {
    param(
        [string]$RepositoryPath,
        [string[]]$Arguments
    )

    [pscustomobject]$result = Invoke-NativeCapture -FilePath "git" -Arguments (@("-C", $RepositoryPath) + $Arguments)
    if ($result.ExitCode -ne 0) {
        return ""
    }

    return $result.Text.Trim()
}

function Test-GitDiffQuiet {
    param(
        [string]$RepositoryPath,
        [string[]]$Pathspec
    )

    [string[]]$arguments = @("-C", $RepositoryPath, "diff", "--cached", "--quiet", "--") + $Pathspec
    [pscustomobject]$result = Invoke-NativeCapture -FilePath "git" -Arguments $arguments
    if ($result.ExitCode -eq 0) {
        return $true
    }

    if ($result.ExitCode -eq 1) {
        return $false
    }

    if (-not [string]::IsNullOrWhiteSpace($result.Text)) {
        Write-Host $result.Text
    }

    [string]$commandText = Format-Command -FilePath "git" -Arguments $arguments
    Fail "$commandText failed with exit code $($result.ExitCode)"
}

function Add-Project {
    param(
        [string]$ProjectPath
    )

    if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
        Fail "project path must not be empty"
    }
    if (-not (Test-Path -LiteralPath $ProjectPath -PathType Container)) {
        Fail "project path does not exist: $ProjectPath"
    }

    [string]$projectRoot = (Resolve-Path -LiteralPath $ProjectPath).Path
    if (-not (Test-Path -LiteralPath (Join-Path $projectRoot "ProjectSettings\ProjectVersion.txt") -PathType Leaf)) {
        Fail "not a Unity project: $projectRoot"
    }
    if (-not (Test-Path -LiteralPath (Join-Path $projectRoot "Packages\manifest.json") -PathType Leaf)) {
        Fail "not a Unity project: $projectRoot"
    }

    [pscustomobject]$gitRootResult = Invoke-NativeCapture -FilePath "git" -Arguments @("-C", $projectRoot, "rev-parse", "--show-toplevel")
    if ($gitRootResult.ExitCode -ne 0) {
        Fail "not a git repository: $projectRoot"
    }

    foreach ($existingProject in $Projects) {
        if ([string]::Equals($existingProject, $projectRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            Fail "duplicate project path: $projectRoot"
        }
    }

    [void]$Projects.Add($projectRoot)
}

function Get-UloopBinaryForRoot {
    param(
        [string]$Candidate
    )

    return Join-Path $Candidate "cli\dist\windows-amd64\uloop.exe"
}

function Test-LocalUloop {
    param(
        [string]$Candidate
    )

    return Test-Path -LiteralPath (Get-UloopBinaryForRoot -Candidate $Candidate) -PathType Leaf
}

function Resolve-UloopRoot {
    if (-not [string]::IsNullOrWhiteSpace($UloopRoot)) {
        if (-not (Test-Path -LiteralPath $UloopRoot -PathType Container)) {
            Fail "--uloop-root does not exist: $UloopRoot"
        }

        $script:UloopRoot = (Resolve-Path -LiteralPath $UloopRoot).Path
        if (-not (Test-LocalUloop -Candidate $script:UloopRoot)) {
            Fail "local uloop binary not found under --uloop-root"
        }
        return
    }

    [pscustomobject]$gitRootResult = Invoke-NativeCapture -FilePath "git" -Arguments @("rev-parse", "--show-toplevel")
    if ($gitRootResult.ExitCode -eq 0) {
        [string]$currentGitRoot = $gitRootResult.Text.Trim()
        if (-not [string]::IsNullOrWhiteSpace($currentGitRoot) -and (Test-LocalUloop -Candidate $currentGitRoot)) {
            $script:UloopRoot = $currentGitRoot
            return
        }
    }

    Fail "run from the uloop checkout or pass --uloop-root"
}

function Find-Projects {
    if ($Projects.Count -gt 0) {
        return
    }

    [string]$parentDirectory = Split-Path -Parent $UloopRoot
    foreach ($projectName in $DefaultProjectNames) {
        [string]$candidate = Join-Path $parentDirectory $projectName
        if (-not (Test-Path -LiteralPath $candidate -PathType Container)) {
            continue
        }
        if (-not (Test-Path -LiteralPath (Join-Path $candidate "ProjectSettings\ProjectVersion.txt") -PathType Leaf)) {
            continue
        }
        if (-not (Test-Path -LiteralPath (Join-Path $candidate "Packages\manifest.json") -PathType Leaf)) {
            continue
        }

        Add-Project -ProjectPath $candidate
    }
}

function Assert-ProjectCount {
    [int]$count = $Projects.Count
    if ($count -ne $ExpectedProjectCount) {
        Fail "expected $ExpectedProjectCount sibling Unity projects, found $count"
    }
}

function Invoke-UloopProjectCapture {
    param(
        [string]$ProjectRoot,
        [string[]]$CommandArguments
    )

    [string[]]$arguments = @("--project-path", $ProjectRoot) + $CommandArguments
    return Invoke-NativeCapture -FilePath $UloopBin -Arguments $arguments
}

function Invoke-UloopProjectChecked {
    param(
        [string]$ProjectRoot,
        [string[]]$CommandArguments
    )

    [string[]]$arguments = @("--project-path", $ProjectRoot) + $CommandArguments
    return Invoke-NativeChecked -FilePath $UloopBin -Arguments $arguments
}

function Invoke-ForProjects {
    param(
        [string]$WorkerName,
        [scriptblock]$Worker
    )

    foreach ($projectRoot in $Projects) {
        [string]$projectName = Split-Path -Leaf $projectRoot
        Write-Host "[$projectName] start: $WorkerName"
        & $Worker $projectRoot
        Write-Host "[$projectName] done: $WorkerName"
    }
}

function Invoke-ForProjectsParallel {
    param(
        [string]$WorkerName,
        [scriptblock]$Worker
    )

    [object[]]$jobs = @()
    foreach ($projectRoot in $Projects) {
        [string]$projectName = Split-Path -Leaf $projectRoot
        Write-Host "[$projectName] start: $WorkerName"
        $jobs += Start-Job -ScriptBlock $Worker -ArgumentList @(
            $projectRoot,
            $projectName,
            $UloopBin,
            $DryRun,
            $SampleScenePath
        )
    }

    [bool]$failed = $false
    foreach ($job in $jobs) {
        [object[]]$output = @(Receive-Job -Job $job -Wait -ErrorAction Continue)
        foreach ($line in $output) {
            if ($null -ne $line) {
                Write-Host $line.ToString()
            }
        }

        if ($job.State -ne "Completed") {
            $failed = $true
            foreach ($errorRecord in $job.ChildJobs[0].Error) {
                Write-Host $errorRecord.ToString()
            }
        }

        Remove-Job -Job $job -Force
    }

    if ($failed) {
        Fail "parallel phase failed: $WorkerName"
    }

    foreach ($projectRoot in $Projects) {
        [string]$projectName = Split-Path -Leaf $projectRoot
        Write-Host "[$projectName] done: $WorkerName"
    }
}

function Reset-GitState {
    param(
        [string]$ProjectRoot
    )

    Write-Host "Resetting Git state: $ProjectRoot"
    Invoke-NativeChecked -FilePath "git" -Arguments @("-C", $ProjectRoot, "reset", "--hard", "HEAD") | Out-Null
    Invoke-NativeChecked -FilePath "git" -Arguments @("-C", $ProjectRoot, "clean", "-fd") | Out-Null

    if ($DryRun) {
        Write-Host "[dry-run] assert Git state is clean for $ProjectRoot"
        return
    }

    [string]$dirty = Get-GitText -RepositoryPath $ProjectRoot -Arguments @("status", "--porcelain")
    if (-not [string]::IsNullOrWhiteSpace($dirty)) {
        Write-Host $dirty
        Fail "git state is not clean after reset: $ProjectRoot"
    }
}

function Assert-CleanSkillDirectories {
    param(
        [string]$ProjectRoot
    )

    if ($DryRun) {
        Write-Host "[dry-run] assert generated skill directories are clean for $ProjectRoot"
        return
    }

    [string]$existing = Get-GitText -RepositoryPath $ProjectRoot -Arguments @("status", "--porcelain", "--", ".claude/skills", ".agents/skills")
    if (-not [string]::IsNullOrWhiteSpace($existing)) {
        Write-Host $existing
        Fail "skill directories already have uncommitted changes: $ProjectRoot"
    }
}

function Quit-Unity {
    param(
        [string]$ProjectRoot
    )

    if ($DryRun) {
        Write-Host "[dry-run] quit Unity processes for $ProjectRoot"
        return
    }

    Write-Host "Quitting Unity with local uloop: $ProjectRoot"
    Invoke-UloopProjectChecked -ProjectRoot $ProjectRoot -CommandArguments @("launch", "--quit") | Out-Null
}

function Assert-UnityStopped {
    param(
        [string]$ProjectRoot
    )

    if ($DryRun) {
        Write-Host "[dry-run] assert Unity is stopped for $ProjectRoot"
        return
    }

    Invoke-UloopProjectChecked -ProjectRoot $ProjectRoot -CommandArguments @("launch", "--quit") | Out-Null
}

function Install-Skills {
    param(
        [string]$ProjectRoot
    )

    Invoke-UloopProjectChecked -ProjectRoot $ProjectRoot -CommandArguments @("skills", "install", "--claude", "--agents") | Out-Null
}

function Get-CommitIdentityValue {
    param(
        [string]$ProjectRoot,
        [string]$ConfigName,
        [string]$EnvironmentValue
    )

    if (-not [string]::IsNullOrWhiteSpace($EnvironmentValue)) {
        return $EnvironmentValue
    }

    [string]$projectValue = Get-GitText -RepositoryPath $ProjectRoot -Arguments @("config", $ConfigName)
    if (-not [string]::IsNullOrWhiteSpace($projectValue)) {
        return $projectValue
    }

    return Get-GitText -RepositoryPath $UloopRoot -Arguments @("config", $ConfigName)
}

function Commit-GeneratedSkillChanges {
    param(
        [string]$ProjectRoot
    )

    [string]$commitName = Get-CommitIdentityValue -ProjectRoot $ProjectRoot -ConfigName "user.name" -EnvironmentValue $env:GIT_AUTHOR_NAME
    [string]$commitEmail = Get-CommitIdentityValue -ProjectRoot $ProjectRoot -ConfigName "user.email" -EnvironmentValue $env:GIT_AUTHOR_EMAIL

    if ([string]::IsNullOrWhiteSpace($commitName)) {
        $commitName = "uLoop Automation"
    }
    if ([string]::IsNullOrWhiteSpace($commitEmail)) {
        $commitEmail = "uloop@example.invalid"
    }

    Invoke-NativeChecked -FilePath "git" -Arguments @(
        "-C",
        $ProjectRoot,
        "-c",
        "user.name=$commitName",
        "-c",
        "user.email=$commitEmail",
        "commit",
        "-m",
        "Update generated uloop skills"
    ) | Out-Null
}

function Commit-GeneratedSkills {
    param(
        [string]$ProjectRoot
    )

    if ($DryRun) {
        Invoke-NativeChecked -FilePath "git" -Arguments @("-C", $ProjectRoot, "add", "-A", "--", ".claude/skills", ".agents/skills") | Out-Null
        Invoke-NativeChecked -FilePath "git" -Arguments @("-C", $ProjectRoot, "diff", "--cached", "--quiet", "--", ".claude/skills", ".agents/skills") | Out-Null
        Write-Host "[dry-run] git -C $ProjectRoot commit -m 'Update generated uloop skills'"
        return
    }

    Invoke-NativeChecked -FilePath "git" -Arguments @("-C", $ProjectRoot, "add", "-A", "--", ".claude/skills", ".agents/skills") | Out-Null
    if (Test-GitDiffQuiet -RepositoryPath $ProjectRoot -Pathspec @(".claude/skills", ".agents/skills")) {
        Write-Host "No generated skill changes to commit: $ProjectRoot"
        return
    }

    Commit-GeneratedSkillChanges -ProjectRoot $ProjectRoot
}

function Remove-Library {
    param(
        [string]$ProjectRoot
    )

    if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
        Fail "project path must not be empty before removing Library"
    }

    [string]$fullProjectRoot = [System.IO.Path]::GetFullPath($ProjectRoot)
    [string]$driveRoot = [System.IO.Path]::GetPathRoot($fullProjectRoot)
    if ($fullProjectRoot.TrimEnd("\") -eq $driveRoot.TrimEnd("\")) {
        Fail "refusing to remove Library from a drive root"
    }
    if (-not (Test-Path -LiteralPath (Join-Path $fullProjectRoot "ProjectSettings\ProjectVersion.txt") -PathType Leaf)) {
        Fail "refusing to remove Library outside a Unity project"
    }

    Assert-UnityStopped -ProjectRoot $fullProjectRoot
    [string]$libraryPath = Join-Path $fullProjectRoot "Library"
    if ($DryRun) {
        Write-Host "[dry-run] Remove-Item -LiteralPath $(Format-CommandArgument -Value $libraryPath) -Recurse -Force"
        return
    }
    if (Test-Path -LiteralPath $libraryPath) {
        Remove-Item -LiteralPath $libraryPath -Recurse -Force
    }
}

Read-Arguments -RawArguments $Arguments

if ($Help) {
    Show-Usage
    exit 0
}

if ($env:OS -ne "Windows_NT") {
    Fail "refresh-neighbor-game-skills-windows.ps1 is only supported on Windows PowerShell."
}

foreach ($projectPath in $Project) {
    Add-Project -ProjectPath $projectPath
}

Resolve-UloopRoot
Find-Projects
Assert-ProjectCount

$UloopBin = Get-UloopBinaryForRoot -Candidate $UloopRoot
if (-not (Test-Path -LiteralPath $UloopBin -PathType Leaf)) {
    Fail "local uloop binary not found: $UloopBin"
}

Write-Host "Using uloop: $UloopBin"
Write-Host "Host OS: windows"
Write-Host "Target projects:"
foreach ($projectRoot in $Projects) {
    Write-Host "  $projectRoot"
}

Write-Host "Phase 0/6: reset Git state"
Invoke-ForProjects -WorkerName "reset_git_state" -Worker {
    param([string]$ProjectRoot)
    Reset-GitState -ProjectRoot $ProjectRoot
}

Invoke-ForProjects -WorkerName "assert_clean_skill_dirs" -Worker {
    param([string]$ProjectRoot)
    Assert-CleanSkillDirectories -ProjectRoot $ProjectRoot
}

Write-Host "Phase 1/6: quit Unity"
Invoke-ForProjects -WorkerName "quit_unity" -Worker {
    param([string]$ProjectRoot)
    Quit-Unity -ProjectRoot $ProjectRoot
}

Invoke-ForProjects -WorkerName "assert_unity_stopped" -Worker {
    param([string]$ProjectRoot)
    Assert-UnityStopped -ProjectRoot $ProjectRoot
}

Write-Host "Phase 2/6: install skills"
Invoke-ForProjects -WorkerName "install_skills" -Worker {
    param([string]$ProjectRoot)
    Install-Skills -ProjectRoot $ProjectRoot
}

Write-Host "Phase 3/6: commit generated skills"
Invoke-ForProjects -WorkerName "commit_generated_skills" -Worker {
    param([string]$ProjectRoot)
    Commit-GeneratedSkills -ProjectRoot $ProjectRoot
}

Write-Host "Phase 4/6: remove Library"
Invoke-ForProjects -WorkerName "remove_library" -Worker {
    param([string]$ProjectRoot)
    Remove-Library -ProjectRoot $ProjectRoot
}

Write-Host "Phase 5/6: launch Unity"
Invoke-ForProjectsParallel -WorkerName "launch_project" -Worker {
    param(
        [string]$ProjectRoot,
        [string]$ProjectName,
        [string]$UloopBin,
        [bool]$DryRun,
        [string]$SampleScenePath
    )

    if ($DryRun) {
        Write-Output "[dry-run] $UloopBin --project-path $ProjectRoot launch"
        return
    }

    [int]$attempt = 1
    [int]$maxAttempts = 3
    while ($attempt -le $maxAttempts) {
        [object[]]$output = & $UloopBin --project-path $ProjectRoot launch 2>&1
        [int]$exitCode = $LASTEXITCODE
        if ($exitCode -eq 0) {
            return
        }

        if ($attempt -eq $maxAttempts) {
            foreach ($line in $output) {
                if ($null -ne $line) {
                    Write-Output $line.ToString()
                }
            }

            throw "Unity did not launch after $maxAttempts attempts: $ProjectRoot"
        }

        Write-Output "[$ProjectName] Unity is not ready after launch attempt $attempt; retrying in 30s..."
        Start-Sleep -Seconds 30
        $attempt++
    }
}

Write-Host "Phase 6/6: open sample scene"
Invoke-ForProjectsParallel -WorkerName "open_sample_scene" -Worker {
    param(
        [string]$ProjectRoot,
        [string]$ProjectName,
        [string]$UloopBin,
        [bool]$DryRun,
        [string]$SampleScenePath
    )

    [string]$sceneFile = Join-Path $ProjectRoot $SampleScenePath
    if (-not (Test-Path -LiteralPath $sceneFile -PathType Leaf)) {
        throw "sample scene not found: $sceneFile"
    }

    if ($DryRun) {
        Write-Output "[dry-run] wait until Unity responds to uloop for $ProjectRoot"
        Write-Output "[dry-run] $UloopBin --project-path $ProjectRoot control-play-mode --action Stop"
        Write-Output "[dry-run] $UloopBin --project-path $ProjectRoot execute-dynamic-code --code-file <sample-scene-open-code-file>"
        return
    }

    [int]$elapsedSeconds = 0
    while ($elapsedSeconds -lt 300) {
        & $UloopBin --project-path $ProjectRoot get-logs --max-count 1 > $null 2>&1
        if ($LASTEXITCODE -eq 0) {
            break
        }

        Start-Sleep -Seconds 2
        $elapsedSeconds += 2
    }

    if ($elapsedSeconds -ge 300) {
        throw "Unity did not become ready after launch: $ProjectRoot"
    }

    [object[]]$stopPlayModeOutput = & $UloopBin --project-path $ProjectRoot control-play-mode --action Stop 2>&1
    [int]$stopPlayModeExitCode = $LASTEXITCODE
    if ($stopPlayModeExitCode -ne 0) {
        [string]$stopPlayModeText = (@($stopPlayModeOutput) | Where-Object { $null -ne $_ } | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine
        if (-not [string]::IsNullOrWhiteSpace($stopPlayModeText)) {
            Write-Output $stopPlayModeText
        }

        throw "failed to stop play mode before opening sample scene: $ProjectRoot"
    }
    Start-Sleep -Seconds 1

    [string]$code = @"
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

string scenePath = "$SampleScenePath";
if (SceneManager.GetActiveScene().path != scenePath)
{
    EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
}

return SceneManager.GetActiveScene().path;
"@

    [string]$codeFile = Join-Path ([System.IO.Path]::GetTempPath()) "refresh-neighbor-game-skills-$([System.Guid]::NewGuid().ToString("N")).cs"
    [System.IO.File]::WriteAllText($codeFile, $code)
    try {
        [object[]]$responseOutput = & $UloopBin --project-path $ProjectRoot execute-dynamic-code --code-file $codeFile 2>&1
        [int]$responseExitCode = $LASTEXITCODE
    }
    finally {
        if (Test-Path -LiteralPath $codeFile) {
            Remove-Item -LiteralPath $codeFile -Force
        }
    }

    [string]$responseText = (@($responseOutput) | Where-Object { $null -ne $_ } | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine
    if ($responseExitCode -ne 0) {
        if (-not [string]::IsNullOrWhiteSpace($responseText)) {
            Write-Output $responseText
        }

        throw "failed to execute sample scene open command: $ProjectRoot"
    }

    [pscustomobject]$json = $responseText | ConvertFrom-Json
    if ($json.Success -eq $true -and $json.Result -eq $SampleScenePath) {
        return
    }

    Write-Output $responseText
    throw "failed to open sample scene: $ProjectRoot"
}

Write-Host "Done."
