param(
    [string] $Root = "."
)

$excludedDirectories = @(
    ".git",
    "Library",
    "Temp",
    "node_modules",
    ".agents",
    ".claude",
    ".codex",
    ".cursor",
    ".gemini",
    ".windsurf",
    ".agent"
)

$targetExtensions = @(".md", ".sh", ".bash", ".zsh", ".ps1", ".psm1")
$outputFields = "Success|Message|ErrorMessage|ErrorCount|WarningCount|TotalCount|DisplayedCount|LogType|StackTrace|XmlPath|TestCount|PassedCount|FailedCount|SkippedCount|CompletedAt|ScreenshotCount|Screenshots|CompilationErrors|ErrorCode|UpdatedCode|DiagnosticsSummary|OutputPath|InputPath|TotalFrames|DurationSeconds|CurrentFrame|IsReplaying|KeyName|PositionX|PositionY|EndPositionX|EndPositionY|HitGameObjectName|IsPlaying|IsPaused|ClearedLogCount|ClearedCounts"

function Test-IsExcludedPath {
    param([string] $Path)

    $parts = $Path -split [System.IO.Path]::DirectorySeparatorChar
    foreach ($part in $parts) {
        if ($excludedDirectories -contains $part) {
            return $true
        }
    }

    return $false
}

function Write-Candidate {
    param(
        [string] $Kind,
        [string] $Path,
        [int] $LineNumber,
        [string] $Line
    )

    Write-Output "$Kind ${Path}:${LineNumber}: $Line"
}

Get-ChildItem -LiteralPath $Root -Recurse -File |
    Where-Object {
        -not (Test-IsExcludedPath -Path $_.FullName) -and
        ($_.Name -eq "SKILL.md" -or $targetExtensions -contains $_.Extension)
    } |
    ForEach-Object {
        $path = $_.FullName
        $lines = Get-Content -LiteralPath $path
        for ($index = 0; $index -lt $lines.Count; $index++) {
            $line = $lines[$index]
            $lineNumber = $index + 1
            if ($line -match "uloop\s+[^#|;&]*--[A-Za-z0-9][A-Za-z0-9-]*(\s+|=)(true|false)(\s|$|[|;&])") {
                Write-Candidate -Kind "ARG_BOOL" -Path $path -LineNumber $lineNumber -Line $line
            }
            if ($line -match "uloop\s+compile\s+[^#|;&]*--(wait-for-domain-reload|reload-external-scene-changes|force-recompile)(\s|=|$)") {
                Write-Candidate -Kind "FIRST_PARTY_OPTION" -Path $path -LineNumber $lineNumber -Line $line
            }
            if ($line -match "uloop\s+run-tests\s+[^#|;&]*--save-before-run(\s|=|$)") {
                Write-Candidate -Kind "FIRST_PARTY_OPTION" -Path $path -LineNumber $lineNumber -Line $line
            }
            if ($line -match "uloop\s+(record-input|replay-input)\s+[^#|;&]*--show-overlay(\s|=|$)") {
                Write-Candidate -Kind "FIRST_PARTY_OPTION" -Path $path -LineNumber $lineNumber -Line $line
            }
            if ($line -match "uloop\s+get-hierarchy\s+[^#|;&]*--include-(components|inactive)(\s|=|$)") {
                Write-Candidate -Kind "FIRST_PARTY_OPTION" -Path $path -LineNumber $lineNumber -Line $line
            }
            if ($line -match "uloop\s+execute-dynamic-code\s+[^#|;&]*--compile-only(\s|=|$)") {
                Write-Candidate -Kind "FIRST_PARTY_OPTION" -Path $path -LineNumber $lineNumber -Line $line
            }
            if ($line -match "\.($outputFields)([^A-Za-z0-9_]|$)") {
                Write-Candidate -Kind "OUTPUT_FIELD" -Path $path -LineNumber $lineNumber -Line $line
            }
        }
    }
