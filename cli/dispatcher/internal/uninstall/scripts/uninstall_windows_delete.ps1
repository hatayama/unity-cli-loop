$Target = '{{TARGET_PATH}}'
$ParentPid = [int]'{{CURRENT_PID}}'
$InstallDir = Split-Path -Parent $Target
$NormalizePath = {
    param([string]$Path)
    if (-not $Path) {
        return ''
    }
    return $Path.Trim().Trim('"').TrimEnd([char[]]@('\','/')).Replace('/','\')
}
$ParentProcess = Get-Process -Id $ParentPid -ErrorAction SilentlyContinue
if ($ParentProcess) {
    $ParentProcess | Wait-Process -ErrorAction SilentlyContinue
}
$UserPath = [Environment]::GetEnvironmentVariable('Path', 'User')
if ($UserPath) {
    $NormalizedInstallDir = & $NormalizePath $InstallDir
    $PathEntries = $UserPath -split ';' | Where-Object { $_ -and -not [string]::Equals((& $NormalizePath $_), $NormalizedInstallDir, [System.StringComparison]::OrdinalIgnoreCase) }
    $NewUserPath = [string]::Join(';', $PathEntries)
    if (-not [string]::Equals($UserPath, $NewUserPath, [System.StringComparison]::Ordinal)) {
        [Environment]::SetEnvironmentVariable('Path', $NewUserPath, 'User')
    }
}
if (Test-Path -LiteralPath $Target) {
    Remove-Item -LiteralPath $Target -Force -ErrorAction SilentlyContinue
}
