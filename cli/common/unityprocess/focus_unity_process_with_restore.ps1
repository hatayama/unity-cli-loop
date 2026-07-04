$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class Win32Interop {
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);
}
"@
$previous = [Win32Interop]::GetForegroundWindow()
try { $process = Get-Process -Id {{PID}} -ErrorAction Stop } catch { throw 'Unity process was not found: {{PID}}' }
$handle = $process.MainWindowHandle
if ($handle -eq 0) { throw 'Unity process has no main window handle: {{PID}}' }
$shown = [Win32Interop]::ShowWindowAsync($handle, 9)
if (-not $shown) { throw 'Failed to show Unity window' }
$focused = [Win32Interop]::SetForegroundWindow($handle)
if (-not $focused) {
  $shell = New-Object -ComObject WScript.Shell
  $focused = $shell.AppActivate({{PID}})
}
if (-not $focused) { throw 'Failed to focus Unity window' }
Write-Output $previous.ToInt64()
