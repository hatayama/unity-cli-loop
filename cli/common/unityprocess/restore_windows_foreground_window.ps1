$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class Win32Interop {
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);
  [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr lpdwProcessId);
  [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
  [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
  [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
}
"@
$handle = [IntPtr]::new({{HANDLE}})
if ($handle -eq [IntPtr]::Zero) { throw 'Saved foreground window handle is invalid' }
$targetThreadId = [Win32Interop]::GetWindowThreadProcessId($handle, [IntPtr]::Zero)
if ($targetThreadId -eq 0) { throw 'Saved foreground window thread is invalid' }
$foreground = [Win32Interop]::GetForegroundWindow()
$foregroundThreadId = [Win32Interop]::GetWindowThreadProcessId($foreground, [IntPtr]::Zero)
$currentThreadId = [Win32Interop]::GetCurrentThreadId()
$attachedCurrent = $false
$attachedForeground = $false
try {
  if ($targetThreadId -ne $currentThreadId) {
    $attachedCurrent = [Win32Interop]::AttachThreadInput($currentThreadId, $targetThreadId, $true)
  }
  if ($foregroundThreadId -ne 0 -and $foregroundThreadId -ne $targetThreadId) {
    $attachedForeground = [Win32Interop]::AttachThreadInput($foregroundThreadId, $targetThreadId, $true)
  }
  $isMinimized = [Win32Interop]::IsIconic($handle)
  if ($isMinimized) {
    $shown = [Win32Interop]::ShowWindowAsync($handle, 9)
    if (-not $shown) { throw 'Failed to show previous foreground window' }
  }
  [void][Win32Interop]::BringWindowToTop($handle)
  $restored = [Win32Interop]::SetForegroundWindow($handle)
} finally {
  if ($attachedForeground) { [void][Win32Interop]::AttachThreadInput($foregroundThreadId, $targetThreadId, $false) }
  if ($attachedCurrent) { [void][Win32Interop]::AttachThreadInput($currentThreadId, $targetThreadId, $false) }
}
if (-not $restored) { throw 'Failed to restore previous foreground window' }
