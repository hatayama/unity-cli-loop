$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class Win32Interop {
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);
  [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr processIdPointer);
  [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
  [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
  [DllImport("user32.dll")] public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
  public static uint GetWindowProcessId(IntPtr hWnd) {
    IntPtr buffer = Marshal.AllocHGlobal(4);
    try {
      GetWindowThreadProcessId(hWnd, buffer);
      return (uint)Marshal.ReadInt32(buffer);
    } finally {
      Marshal.FreeHGlobal(buffer);
    }
  }
}
"@
# Why: SetForegroundWindow can report the switch before the shell finishes it, so poll briefly instead of a single read.
function Test-TargetForeground {
  for ($attempt = 0; $attempt -lt 10; $attempt++) {
    if ([Win32Interop]::GetWindowProcessId([Win32Interop]::GetForegroundWindow()) -eq {{PID}}) { return $true }
    Start-Sleep -Milliseconds 50
  }
  return $false
}
$previous = [Win32Interop]::GetForegroundWindow()
try { $process = Get-Process -Id {{PID}} -ErrorAction Stop } catch { throw 'Unity process was not found: {{PID}}' }
$handle = $process.MainWindowHandle
if ($handle -eq 0) { throw 'Unity process has no main window handle: {{PID}}' }
# Why: SW_RESTORE on a non-minimized window would shrink a maximized Unity window, so restore only when minimized.
if ([Win32Interop]::IsIconic($handle)) {
  $shown = [Win32Interop]::ShowWindowAsync($handle, 9)
  if (-not $shown) { throw 'Failed to show Unity window' }
}
[void][Win32Interop]::SetForegroundWindow($handle)
if (-not (Test-TargetForeground)) {
  # Why: the Windows foreground lock rejects SetForegroundWindow from background processes; sharing the
  # foreground thread's input queue via AttachThreadInput lifts that restriction.
  $currentThreadId = [Win32Interop]::GetCurrentThreadId()
  $foreground = [Win32Interop]::GetForegroundWindow()
  $foregroundThreadId = [Win32Interop]::GetWindowThreadProcessId($foreground, [IntPtr]::Zero)
  $targetThreadId = [Win32Interop]::GetWindowThreadProcessId($handle, [IntPtr]::Zero)
  $attachedForeground = $false
  $attachedTarget = $false
  try {
    if ($foregroundThreadId -ne 0 -and $foregroundThreadId -ne $currentThreadId) {
      $attachedForeground = [Win32Interop]::AttachThreadInput($currentThreadId, $foregroundThreadId, $true)
    }
    if ($targetThreadId -ne 0 -and $targetThreadId -ne $currentThreadId) {
      $attachedTarget = [Win32Interop]::AttachThreadInput($currentThreadId, $targetThreadId, $true)
    }
    [void][Win32Interop]::BringWindowToTop($handle)
    [void][Win32Interop]::SetForegroundWindow($handle)
  } finally {
    if ($attachedTarget) { [void][Win32Interop]::AttachThreadInput($currentThreadId, $targetThreadId, $false) }
    if ($attachedForeground) { [void][Win32Interop]::AttachThreadInput($currentThreadId, $foregroundThreadId, $false) }
  }
}
if (-not (Test-TargetForeground)) {
  # Why: a transient Alt keypress makes this process the last input source, a documented workaround
  # that unlocks SetForegroundWindow when AttachThreadInput alone is not enough.
  [Win32Interop]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero)
  [void][Win32Interop]::SetForegroundWindow($handle)
  [Win32Interop]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)
}
if (-not (Test-TargetForeground)) {
  throw 'Windows refused to bring the Unity window (PID: {{PID}}) to the foreground (foreground lock). Click the Unity window or its taskbar icon to focus it manually.'
}
Write-Output $previous.ToInt64()
