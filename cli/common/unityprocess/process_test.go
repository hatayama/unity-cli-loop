package unityprocess

import (
	"encoding/base64"
	"path/filepath"
	"runtime"
	"strings"
	"testing"
)

// Verifies macOS Unity process parsing extracts project paths and skips batchmode workers.
func TestParseMacUnityProcessesExtractsProjectPath(t *testing.T) {
	output := `123 /Applications/Unity/Hub/Editor/6000.0.0f1/Unity.app/Contents/MacOS/Unity -projectPath "/Users/<USER_NAME>/My Project" -useHub -hubIPC
456 /Applications/Unity/Hub/Editor/6000.0.0f1/Unity.app/Contents/MacOS/Unity -batchmode -projectPath "/Users/<USER_NAME>/Batch"
789 /Applications/Unity/Hub/Editor/6000.0.0f1/Unity.app/Contents/MacOS/Unity -projectPath /Users/<USER_NAME>/Other -logFile -
`

	processes := parseMacUnityProcesses(output)

	if len(processes) != 2 {
		t.Fatalf("process count mismatch: %#v", processes)
	}
	if processes[0].Pid != 123 || processes[0].projectPath != "/Users/<USER_NAME>/My Project" {
		t.Fatalf("first process mismatch: %#v", processes[0])
	}
	if processes[1].Pid != 789 || processes[1].projectPath != "/Users/<USER_NAME>/Other" {
		t.Fatalf("second process mismatch: %#v", processes[1])
	}
}

// Verifies Windows Unity process parsing decodes Base64 command lines, extracts project paths, and skips batchmode workers.
func TestParseWindowsUnityProcessesExtractsProjectPath(t *testing.T) {
	encode := func(commandLine string) string {
		return base64.StdEncoding.EncodeToString([]byte(commandLine))
	}
	output := "123|" + encode(`C:\Program Files\Unity\Hub\Editor\6000.0.0f1\Editor\Unity.exe -projectPath "C:\Users\<USER_NAME>\My Project" -useHub`) + "\r\n" +
		"456|" + encode(`C:\Program Files\Unity\Hub\Editor\6000.0.0f1\Editor\Unity.exe -batchmode -projectPath "C:\Users\<USER_NAME>\Batch"`) + "\r\n"

	processes := parseWindowsUnityProcesses(output)

	if len(processes) != 1 {
		t.Fatalf("process count mismatch: %#v", processes)
	}
	if processes[0].Pid != 123 || processes[0].projectPath != `C:\Users\<USER_NAME>\My Project` {
		t.Fatalf("process mismatch: %#v", processes[0])
	}
}

// Verifies non-ASCII project paths survive the PowerShell boundary because command lines travel as UTF-8 Base64.
func TestParseWindowsUnityProcessesPreservesNonASCIIProjectPath(t *testing.T) {
	projectPath := `C:\Users\<USER_NAME>\test[1] 検証用\proj`
	commandLine := `C:\Program Files\Unity\Hub\Editor\2022.3.62f3\Editor\Unity.exe -projectPath "` + projectPath + `" -useHub`
	output := "123|" + base64.StdEncoding.EncodeToString([]byte(commandLine)) + "\r\n"

	processes := parseWindowsUnityProcesses(output)

	if len(processes) != 1 {
		t.Fatalf("process count mismatch: %#v", processes)
	}
	if processes[0].projectPath != projectPath {
		t.Fatalf("project path mismatch: %q", processes[0].projectPath)
	}
}

// Verifies command fields that are not valid Base64 (e.g. legacy plain-text or OEM code page bytes) are skipped instead of mis-parsed.
func TestParseWindowsUnityProcessesSkipsNonBase64CommandLines(t *testing.T) {
	// 0x8C9F 0x8FD8 0x9770 is the measured CP932 byte sequence for "検証用"
	// that Windows PowerShell 5.1 emitted before the Base64 contract.
	cp932KenshouYou := string([]byte{0x8C, 0x9F, 0x8F, 0xD8, 0x97, 0x70})
	output := "123|" + `C:\Editor\Unity.exe -projectPath "C:\Users\<USER_NAME>\` + cp932KenshouYou + `\proj"` + "\r\n"

	processes := parseWindowsUnityProcesses(output)

	if len(processes) != 0 {
		t.Fatalf("non-Base64 command lines should be skipped: %#v", processes)
	}
}

// Verifies the Windows process list script transports command lines as UTF-8 Base64 so the OEM console code page cannot corrupt non-ASCII paths.
func TestWindowsUnityProcessListScriptEncodesCommandLineAsUTF8Base64(t *testing.T) {
	script := windowsUnityProcessListScript()

	for _, expected := range []string{
		"[System.Text.Encoding]::UTF8.GetBytes($commandLine)",
		"[Convert]::ToBase64String(",
	} {
		if !strings.Contains(script, expected) {
			t.Fatalf("script missing %q: %s", expected, script)
		}
	}
}

// Verifies Unity -projectPath extraction supports quoted, unquoted, and equals forms.
func TestExtractProjectPathSupportsEqualsAndSpaces(t *testing.T) {
	cases := map[string]string{
		`Unity -projectPath="/Users/<USER_NAME>/My Project" -useHub`:                                                             "/Users/<USER_NAME>/My Project",
		`Unity -projectpath '/Users/<USER_NAME>/Other Project' -flag`:                                                            "/Users/<USER_NAME>/Other Project",
		`Unity -projectPath /Users/<USER_NAME>/Plain -flag`:                                                                      "/Users/<USER_NAME>/Plain",
		`Unity Hub -- --silent -- -projectPath /Users/<USER_NAME>/SampleWorkspace/SampleUnityProject -cacheServerEnableUpload`:   "/Users/<USER_NAME>/SampleWorkspace/SampleUnityProject",
		`Unity -projectPath /Users/<USER_NAME>/SampleWorkspace/SampleUnityProject -acceptSoftwareTermsForThisRunOnly -useHub`:    "/Users/<USER_NAME>/SampleWorkspace/SampleUnityProject",
		`Unity -projectPath /Users/<USER_NAME>/SampleWorkspace/SampleUnityProject -cacheServerEnableDownload=false -useHub`:      "/Users/<USER_NAME>/SampleWorkspace/SampleUnityProject",
		`Unity -projectPath /Users/<USER_NAME>/SampleWorkspace/SampleUnityProject -hubSessionId 715810a5-220d-411e-a7d2-28cf46f`: "/Users/<USER_NAME>/SampleWorkspace/SampleUnityProject",
	}

	for command, expected := range cases {
		actual := extractProjectPath(command)
		if actual != expected {
			t.Fatalf("project path mismatch for %q: %q", command, actual)
		}
	}
}

// Verifies the embedded Windows focus script verifies the foreground result and throws instead of trusting API return values.
func TestBuildFocusUnityProcessWindowsScriptVerifiesForegroundAndThrowsOnFailures(t *testing.T) {
	script := buildFocusUnityProcessWindowsScript(123)

	assertWindowsFocusScriptContract(t, script)
}

// Verifies the embedded Windows focus-with-restore script captures the previous foreground window and shares the focus contract.
func TestBuildFocusUnityProcessWindowsWithRestoreScriptCapturesForegroundWindow(t *testing.T) {
	script := buildFocusUnityProcessWindowsWithRestoreScript(123)

	assertWindowsFocusScriptContract(t, script)
	for _, expected := range []string{
		"$previous = [Win32Interop]::GetForegroundWindow()",
		"Write-Output $previous.ToInt64()",
	} {
		if !strings.Contains(script, expected) {
			t.Fatalf("script missing %q: %s", expected, script)
		}
	}
}

// Asserts the shared Windows focus contract: escalation techniques, foreground verification, and no trust in AppActivate.
func assertWindowsFocusScriptContract(t *testing.T, script string) {
	t.Helper()
	for _, expected := range []string{
		"throw 'Unity process was not found: 123'",
		"throw 'Unity process has no main window handle: 123'",
		"if ([Win32Interop]::IsIconic($handle)) {",
		"throw 'Failed to show Unity window'",
		"function Test-TargetForeground",
		"[Win32Interop]::GetWindowProcessId([Win32Interop]::GetForegroundWindow()) -eq 123",
		"AttachThreadInput($currentThreadId, $foregroundThreadId, $true)",
		"AttachThreadInput($currentThreadId, $targetThreadId, $true)",
		"AttachThreadInput($currentThreadId, $targetThreadId, $false)",
		"AttachThreadInput($currentThreadId, $foregroundThreadId, $false)",
		"BringWindowToTop",
		"keybd_event(0x12, 0, 0, [UIntPtr]::Zero)",
		"keybd_event(0x12, 0, 2, [UIntPtr]::Zero)",
		"throw 'Windows refused to bring the Unity window (PID: 123) to the foreground (foreground lock). Click the Unity window or its taskbar icon to focus it manually.'",
	} {
		if !strings.Contains(script, expected) {
			t.Fatalf("script missing %q: %s", expected, script)
		}
	}
	if strings.Contains(script, "AppActivate") {
		t.Fatalf("script must not trust AppActivate return values: %s", script)
	}
	if strings.Contains(script, "catch { return }") || strings.Contains(script, "{ return }") {
		t.Fatalf("script should not silently return: %s", script)
	}
}

// Verifies the embedded Windows restore script fails when the saved foreground window cannot be restored.
func TestBuildRestoreWindowsForegroundWindowScriptThrowsOnRestoreFailure(t *testing.T) {
	script := buildRestoreWindowsForegroundWindowScript(123)

	for _, expected := range []string{
		"$handle = [IntPtr]::new(123)",
		"if ($handle -eq [IntPtr]::Zero) { throw 'Saved foreground window handle is invalid' }",
		"GetWindowThreadProcessId",
		"GetCurrentThreadId",
		"AttachThreadInput",
		"BringWindowToTop",
		"try {",
		"} finally {",
		"AttachThreadInput($foregroundThreadId, $targetThreadId, $false)",
		"AttachThreadInput($currentThreadId, $targetThreadId, $false)",
		"$restored = [Win32Interop]::SetForegroundWindow($handle)",
		"if (-not $restored) { throw 'Failed to restore previous foreground window' }",
	} {
		if !strings.Contains(script, expected) {
			t.Fatalf("script missing %q: %s", expected, script)
		}
	}
}

// Verifies the embedded Windows restore script avoids resizing a saved maximized foreground window.
func TestBuildRestoreWindowsForegroundWindowScriptRestoresOnlyMinimizedWindow(t *testing.T) {
	script := buildRestoreWindowsForegroundWindowScript(123)

	for _, expected := range []string{
		"IsIconic",
		"$isMinimized = [Win32Interop]::IsIconic($handle)",
		"if ($isMinimized) {",
		"    $shown = [Win32Interop]::ShowWindowAsync($handle, 9)",
	} {
		if !strings.Contains(script, expected) {
			t.Fatalf("script missing %q: %s", expected, script)
		}
	}
	if strings.Contains(script, "\n  $shown = [Win32Interop]::ShowWindowAsync($handle, 9)") {
		t.Fatalf("restore script should not unconditionally restore the previous window: %s", script)
	}
}

// Verifies Windows foreground handle parsing ignores invalid saved state output.
func TestParseWindowsForegroundHandle(t *testing.T) {
	if parseWindowsForegroundHandle("123\r\n") != 123 {
		t.Fatal("expected numeric handle to parse")
	}
	if parseWindowsForegroundHandle("not-a-handle") != 0 {
		t.Fatal("expected invalid handle to be ignored")
	}
}

// Verifies comparable project paths preserve case on case-sensitive-capable platforms.
func TestNormalizeComparablePathPreservesCaseOutsideWindows(t *testing.T) {
	if runtime.GOOS == "windows" {
		t.Skip("Windows project matching is intentionally case-insensitive.")
	}

	path := filepath.Join(t.TempDir(), "CaseSensitiveProject")
	normalizedPath, err := normalizeComparablePath(path)
	if err != nil {
		t.Fatalf("normalizeComparablePath failed: %v", err)
	}
	if !strings.Contains(normalizedPath, "CaseSensitiveProject") {
		t.Fatalf("expected normalized path to preserve case, got %q", normalizedPath)
	}
}
