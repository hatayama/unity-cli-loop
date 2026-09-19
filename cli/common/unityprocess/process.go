package unityprocess

import (
	"bytes"
	"context"
	"encoding/base64"
	"encoding/binary"
	"fmt"
	"os/exec"
	"path"
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
	case "linux":
		return listUnityProcessesLinux(ctx)
	default:
		return []UnityProcess{}, nil
	}
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

// matchMacUnityProcess checks a single process's (pid, space-joined argv) pair against
// the same Unity-editor-command and project-path rules used for the ps-based command
// text this replaced, so callers built from either a text source or a raw argv slice
// share one matching implementation.
func matchMacUnityProcess(pid int, command string) (UnityProcess, bool) {
	if !isUnityEditorCommand(command, macUnityExecutablePattern) {
		return UnityProcess{}, false
	}
	projectPath := extractProjectPath(command)
	if projectPath == "" {
		return UnityProcess{}, false
	}
	return UnityProcess{Pid: pid, projectPath: projectPath}, true
}

// parseMacProcArgs2 decodes the kern.procargs2 sysctl buffer layout: a leading int32
// argc, followed by the exec path (NUL terminated), NUL padding, then argc consecutive
// NUL-terminated argv strings. Darwin's supported architectures (amd64, arm64) are both
// little-endian, so argc is read with binary.LittleEndian. This parser has no
// darwin-specific dependency, only the sysctl call that supplies its input does, so it
// stays in the shared, cross-platform-buildable file for CI coverage.
func parseMacProcArgs2(buf []byte) ([]string, error) {
	if len(buf) < 4 {
		return nil, fmt.Errorf("procargs2 buffer too short: %d bytes", len(buf))
	}

	argc := int(binary.LittleEndian.Uint32(buf[:4]))
	rest := buf[4:]

	execPathEnd := bytes.IndexByte(rest, 0)
	if execPathEnd < 0 {
		return nil, fmt.Errorf("procargs2 missing exec path terminator")
	}
	rest = rest[execPathEnd:]
	for len(rest) > 0 && rest[0] == 0 {
		rest = rest[1:]
	}

	args := make([]string, 0, argc)
	for len(rest) > 0 && len(args) < argc {
		argEnd := bytes.IndexByte(rest, 0)
		if argEnd < 0 {
			break
		}
		args = append(args, string(rest[:argEnd]))
		rest = rest[argEnd+1:]
	}
	return args, nil
}

// parseLinuxProcCmdline splits a /proc/<pid>/cmdline buffer into argv. The kernel
// stores argv as NUL-terminated strings, so splitting on NUL keeps argument
// boundaries (and spaces inside arguments) exactly as the process received them.
// Like parseMacProcArgs2, it has no Linux-specific dependency, so it stays in the
// shared file and runs in every OS's CI.
func parseLinuxProcCmdline(buf []byte) []string {
	trimmed := bytes.TrimRight(buf, "\x00")
	if len(trimmed) == 0 {
		return []string{}
	}
	return strings.Split(string(trimmed), "\x00")
}

// matchLinuxUnityProcess decides from a raw argv slice whether a process is an
// interactive Unity Editor and, if so, which project it opened. Unlike the macOS
// matcher it does not join argv and cut the project path out with a regex: argv
// boundaries are preserved in /proc, so a project path containing spaces is read
// without guessing where it ends. path.Base (not filepath.Base) keeps the result
// identical when the tests run on Windows, since Linux paths always use '/'.
func matchLinuxUnityProcess(pid int, args []string) (UnityProcess, bool) {
	if len(args) == 0 {
		return UnityProcess{}, false
	}
	if path.Base(args[0]) != "Unity" {
		return UnityProcess{}, false
	}
	lowerCommand := strings.ToLower(strings.Join(args, " "))
	if strings.Contains(lowerCommand, "-batchmode") || strings.Contains(lowerCommand, "assetimportworker") {
		return UnityProcess{}, false
	}
	projectPath := linuxProjectPathFromArgs(args)
	if projectPath == "" {
		return UnityProcess{}, false
	}
	return UnityProcess{Pid: pid, projectPath: projectPath}, true
}

// linuxProjectPathFromArgs returns the value of Unity's -projectPath flag, accepting
// both the "-projectPath <path>" and "-projectPath=<path>" forms case-insensitively.
func linuxProjectPathFromArgs(args []string) string {
	const projectPathFlag = "-projectpath"
	for index, arg := range args {
		lowerArg := strings.ToLower(arg)
		if lowerArg == projectPathFlag {
			if index+1 >= len(args) {
				return ""
			}
			return args[index+1]
		}
		if strings.HasPrefix(lowerArg, projectPathFlag+"=") {
			return arg[len(projectPathFlag+"="):]
		}
	}
	return ""
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
