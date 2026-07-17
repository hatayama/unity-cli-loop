package unityprocess

import (
	"context"
	"encoding/base64"
	"fmt"
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

type UnityProcess struct {
	Pid         int
	projectPath string
}

type RestoreFocusFunc func(context.Context) error

func FindRunningUnityProcess(ctx context.Context, projectRoot string) (*UnityProcess, error) {
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

func listUnityProcesses(ctx context.Context) ([]UnityProcess, error) {
	switch runtime.GOOS {
	case "darwin":
		return listUnityProcessesMac(ctx)
	case "windows":
		return listUnityProcessesWindows(ctx)
	default:
		return []UnityProcess{}, nil
	}
}

func listUnityProcessesMac(ctx context.Context) ([]UnityProcess, error) {
	commandContext, cancel := withCommandTimeout(ctx, ProcessListCommandTimeout)
	defer cancel()
	output, err := exec.CommandContext(commandContext, "ps", "-axo", "pid=,command=", "-ww").Output()
	if err != nil {
		return nil, fmt.Errorf("failed to retrieve Unity process list: %w", err)
	}
	return parseMacUnityProcesses(string(output)), nil
}

func listUnityProcessesWindows(ctx context.Context) ([]UnityProcess, error) {
	commandContext, cancel := withCommandTimeout(ctx, ProcessListCommandTimeout)
	defer cancel()
	output, err := exec.CommandContext(commandContext, windowsPowerShellCommand, "-NoProfile", "-Command", windowsUnityProcessListScript()).Output()
	if err != nil {
		return nil, fmt.Errorf("failed to retrieve Unity process list on Windows: %w", err)
	}
	return parseWindowsUnityProcesses(string(output)), nil
}

// windowsUnityProcessListScript builds the PowerShell script that lists Unity
// processes as "pid|base64(UTF-8 command line)" lines.
// why: Windows PowerShell 5.1 encodes redirected stdout with the OEM code page
// (e.g. CP932 on Japanese Windows), so non-ASCII project paths in the command
// line get corrupted when Go reads the output as UTF-8. Base64 over UTF-8
// bytes keeps the stream ASCII-only regardless of the console code page.
func windowsUnityProcessListScript() string {
	scriptLines := []string{
		"$ErrorActionPreference = 'Stop'",
		"$processes = Get-CimInstance Win32_Process -Filter \"Name = 'Unity.exe'\" | Where-Object { $_.CommandLine }",
		"foreach ($process in $processes) {",
		"  $commandLine = $process.CommandLine -replace \"`r\", ' ' -replace \"`n\", ' '",
		"  $encodedCommandLine = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($commandLine))",
		"  Write-Output (\"{0}|{1}\" -f $process.ProcessId, $encodedCommandLine)",
		"}",
	}
	return strings.Join(scriptLines, "\n")
}

func parseMacUnityProcesses(output string) []UnityProcess {
	processes := []UnityProcess{}
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

		processes = append(processes, UnityProcess{Pid: pid, projectPath: projectPath})
	}
	return processes
}

func parseWindowsUnityProcesses(output string) []UnityProcess {
	processes := []UnityProcess{}
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

		decodedCommand, err := base64.StdEncoding.DecodeString(strings.TrimSpace(trimmed[delimiterIndex+1:]))
		if err != nil {
			continue
		}

		command := strings.TrimSpace(string(decodedCommand))
		if !isUnityEditorCommand(command, windowsUnityExecutablePattern) {
			continue
		}
		projectPath := extractProjectPath(command)
		if projectPath == "" {
			continue
		}

		processes = append(processes, UnityProcess{Pid: pid, projectPath: projectPath})
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
	normalizedPath := filepath.ToSlash(filepath.Clean(absolutePath))
	if runtime.GOOS == "windows" {
		return strings.ToLower(normalizedPath), nil
	}
	return normalizedPath, nil
}
