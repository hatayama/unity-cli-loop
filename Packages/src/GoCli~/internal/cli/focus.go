package cli

import (
	"context"
	"encoding/json"
	"fmt"
	"io"
	"os/exec"
	"path/filepath"
	"regexp"
	"runtime"
	"strconv"
	"strings"
)

const windowsPowerShellCommand = "powershell"

var (
	macUnityExecutablePattern     = regexp.MustCompile(`(?i)Unity\.app/Contents/MacOS/Unity`)
	windowsUnityExecutablePattern = regexp.MustCompile(`(?i)Unity\.exe`)
	macProcessLinePattern         = regexp.MustCompile(`^\s*(\d+)\s+(.*)$`)
	projectPathFlagPattern        = regexp.MustCompile(`(?i)-projectpath(?:=|\s+)(.+)$`)
	nextUnityFlagPattern          = regexp.MustCompile(`\s-[A-Za-z][A-Za-z0-9-]*(?:=|\s|$)`)
)

type unityProcess struct {
	pid         int
	projectPath string
}

type focusResponse struct {
	Success bool   `json:"Success"`
	Message string `json:"Message"`
}

func runFocusWindow(ctx context.Context, projectRoot string, stdout io.Writer, stderr io.Writer) int {
	runningProcess, err := findRunningUnityProcess(ctx, projectRoot)
	if err != nil {
		writeFocusResponse(stderr, false, err.Error())
		return 1
	}
	if runningProcess == nil {
		writeFocusResponse(stderr, false, "No running Unity process found for this project")
		return 1
	}

	if err := focusUnityProcess(ctx, runningProcess.pid); err != nil {
		writeFocusResponse(stderr, false, fmt.Sprintf("Failed to focus Unity window: %s", err.Error()))
		return 1
	}

	writeFocusResponse(stdout, true, fmt.Sprintf("Unity Editor window focused (PID: %d)", runningProcess.pid))
	return 0
}

func writeFocusResponse(writer io.Writer, success bool, message string) {
	response := focusResponse{
		Success: success,
		Message: message,
	}
	encoder := json.NewEncoder(writer)
	encoder.SetIndent("", "  ")
	_ = encoder.Encode(response)
}

func findRunningUnityProcess(ctx context.Context, projectRoot string) (*unityProcess, error) {
	processes, err := listUnityProcesses(ctx)
	if err != nil {
		return nil, err
	}

	normalizedTarget, err := normalizeComparablePath(projectRoot)
	if err != nil {
		return nil, err
	}

	for _, candidate := range processes {
		normalizedCandidate, err := normalizeComparablePath(candidate.projectPath)
		if err != nil {
			continue
		}
		if normalizedCandidate == normalizedTarget {
			process := candidate
			return &process, nil
		}
	}
	return nil, nil
}

func listUnityProcesses(ctx context.Context) ([]unityProcess, error) {
	switch runtime.GOOS {
	case "darwin":
		return listUnityProcessesMac(ctx)
	case "windows":
		return listUnityProcessesWindows(ctx)
	default:
		return []unityProcess{}, nil
	}
}

func listUnityProcessesMac(ctx context.Context) ([]unityProcess, error) {
	output, err := exec.CommandContext(ctx, "ps", "-axo", "pid=,command=", "-ww").Output()
	if err != nil {
		return nil, fmt.Errorf("failed to retrieve Unity process list: %w", err)
	}
	return parseMacUnityProcesses(string(output)), nil
}

func listUnityProcessesWindows(ctx context.Context) ([]unityProcess, error) {
	scriptLines := []string{
		"$ErrorActionPreference = 'Stop'",
		"$processes = Get-CimInstance Win32_Process -Filter \"Name = 'Unity.exe'\" | Where-Object { $_.CommandLine }",
		"foreach ($process in $processes) {",
		"  $commandLine = $process.CommandLine -replace \"`r\", ' ' -replace \"`n\", ' '",
		"  Write-Output (\"{0}|{1}\" -f $process.ProcessId, $commandLine)",
		"}",
	}
	output, err := exec.CommandContext(ctx, windowsPowerShellCommand, "-NoProfile", "-Command", strings.Join(scriptLines, "\n")).Output()
	if err != nil {
		return nil, fmt.Errorf("failed to retrieve Unity process list on Windows: %w", err)
	}
	return parseWindowsUnityProcesses(string(output)), nil
}

func parseMacUnityProcesses(output string) []unityProcess {
	processes := []unityProcess{}
	for _, line := range strings.Split(output, "\n") {
		matches := macProcessLinePattern.FindStringSubmatch(line)
		if len(matches) != 3 {
			continue
		}

		pid, err := strconv.Atoi(matches[1])
		if err != nil {
			continue
		}

		command := matches[2]
		if !isUnityEditorCommand(command, macUnityExecutablePattern) {
			continue
		}
		projectPath := extractProjectPath(command)
		if projectPath == "" {
			continue
		}

		processes = append(processes, unityProcess{pid: pid, projectPath: projectPath})
	}
	return processes
}

func parseWindowsUnityProcesses(output string) []unityProcess {
	processes := []unityProcess{}
	for _, line := range strings.Split(output, "\n") {
		trimmed := strings.TrimSpace(line)
		if trimmed == "" {
			continue
		}

		delimiterIndex := strings.Index(trimmed, "|")
		if delimiterIndex < 0 {
			continue
		}

		pid, err := strconv.Atoi(strings.TrimSpace(trimmed[:delimiterIndex]))
		if err != nil {
			continue
		}

		command := strings.TrimSpace(trimmed[delimiterIndex+1:])
		if !isUnityEditorCommand(command, windowsUnityExecutablePattern) {
			continue
		}
		projectPath := extractProjectPath(command)
		if projectPath == "" {
			continue
		}

		processes = append(processes, unityProcess{pid: pid, projectPath: projectPath})
	}
	return processes
}

func isUnityEditorCommand(command string, executablePattern *regexp.Regexp) bool {
	lowerCommand := strings.ToLower(command)
	if strings.Contains(lowerCommand, "-batchmode") || strings.Contains(lowerCommand, "assetimportworker") {
		return false
	}
	return executablePattern.MatchString(command)
}

func extractProjectPath(command string) string {
	matches := projectPathFlagPattern.FindStringSubmatch(command)
	if len(matches) != 2 {
		return ""
	}

	value := strings.TrimSpace(matches[1])
	if value == "" {
		return ""
	}

	if strings.HasPrefix(value, `"`) || strings.HasPrefix(value, `'`) {
		return extractQuotedProjectPath(value)
	}

	nextFlagIndex := nextUnityFlagPattern.FindStringIndex(value)
	if nextFlagIndex != nil {
		value = strings.TrimSpace(value[:nextFlagIndex[0]])
	}
	return strings.Trim(value, `"'`)
}

func extractQuotedProjectPath(value string) string {
	quote := value[0]
	endIndex := strings.IndexByte(value[1:], quote)
	if endIndex < 0 {
		return ""
	}
	return value[1 : endIndex+1]
}

func normalizeComparablePath(path string) (string, error) {
	absolutePath, err := filepath.Abs(path)
	if err != nil {
		return "", err
	}
	return strings.ToLower(filepath.ToSlash(filepath.Clean(absolutePath))), nil
}

func focusUnityProcess(ctx context.Context, pid int) error {
	switch runtime.GOOS {
	case "darwin":
		return focusUnityProcessMac(ctx, pid)
	case "windows":
		return focusUnityProcessWindows(ctx, pid)
	default:
		return fmt.Errorf("focus-window is not supported on %s", runtime.GOOS)
	}
}

func focusUnityProcessMac(ctx context.Context, pid int) error {
	script := fmt.Sprintf(`tell application "System Events" to set frontmost of (first process whose unix id is %d) to true`, pid)
	return exec.CommandContext(ctx, "osascript", "-e", script).Run()
}

func focusUnityProcessWindows(ctx context.Context, pid int) error {
	script := buildFocusUnityProcessWindowsScript(pid)
	return exec.CommandContext(ctx, windowsPowerShellCommand, "-NoProfile", "-Command", script).Run()
}

func buildFocusUnityProcessWindowsScript(pid int) string {
	addTypeLines := []string{
		"Add-Type -TypeDefinition @\"",
		"using System;",
		"using System.Runtime.InteropServices;",
		"public static class Win32Interop {",
		"  [DllImport(\"user32.dll\")] public static extern bool SetForegroundWindow(IntPtr hWnd);",
		"  [DllImport(\"user32.dll\")] public static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);",
		"}",
		"\"@",
	}
	scriptLines := []string{
		"$ErrorActionPreference = 'Stop'",
	}
	scriptLines = append(scriptLines, addTypeLines...)
	scriptLines = append(scriptLines,
		fmt.Sprintf("try { $process = Get-Process -Id %d -ErrorAction Stop } catch { throw 'Unity process was not found: %d' }", pid, pid),
		"$handle = $process.MainWindowHandle",
		fmt.Sprintf("if ($handle -eq 0) { throw 'Unity process has no main window handle: %d' }", pid),
		"$shown = [Win32Interop]::ShowWindowAsync($handle, 9)",
		"if (-not $shown) { throw 'Failed to show Unity window' }",
		"$focused = [Win32Interop]::SetForegroundWindow($handle)",
		"if (-not $focused) { throw 'Failed to focus Unity window' }",
	)
	return strings.Join(scriptLines, "\n")
}
