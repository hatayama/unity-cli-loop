package unityprocess

import (
	"bytes"
	"context"
	"encoding/base64"
	"encoding/binary"
	"errors"
	"fmt"
	"os"
	"os/exec"
	"path"
	"path/filepath"
	"regexp"
	"runtime"
	"strconv"
	"strings"
	"time"
)

const windowsPowerShellCommand = "powershell"

var (
	macUnityExecutablePattern     = regexp.MustCompile(`(?i)Unity\.app/Contents/MacOS/Unity`)
	windowsUnityExecutablePattern = regexp.MustCompile(`(?i)Unity\.exe`)
	// Why -createProject too: the Editor that creates a project opens it in the same session, and that
	// session's command line may name the project only through -createProject.
	projectPathFlagPattern = regexp.MustCompile(`(?i)-(?:projectpath|createproject)(?:=|\s+)(.+)$`)
	nextUnityFlagPattern   = regexp.MustCompile(`\s-[A-Za-z][A-Za-z0-9-]*(?:=|\s|$)`)
)

type UnityProcess struct {
	Pid int
	// projectPath is empty for an Editor whose command line does not name its project.
	projectPath string
}

// ErrUnidentifiedUnityEditor reports that no Editor is listed for the project while an Editor whose
// command line does not name its project is running, so that Editor may be the project's.
var ErrUnidentifiedUnityEditor = errors.New(
	"a Unity Editor whose command line does not name its project is running, so it may be this project's Editor")

type RestoreFocusFunc func(context.Context) error

// FindRunningUnityProcess returns the running Editor listed for projectRoot, or nil when none is.
func FindRunningUnityProcess(ctx context.Context, projectRoot string) (*UnityProcess, error) {
	process, _, err := findRunningUnityProcess(ctx, projectRoot)
	return process, err
}

// FindRunningUnityProcessDefinitively is FindRunningUnityProcess for callers that conclude from a nil
// result that no Editor is running for the project: it returns nil only when no running Editor can be
// the project's, and ErrUnidentifiedUnityEditor when an Editor whose project is unknown may be.
func FindRunningUnityProcessDefinitively(ctx context.Context, projectRoot string) (*UnityProcess, error) {
	process, unidentifiedRunning, err := findRunningUnityProcess(ctx, projectRoot)
	if err == nil && process == nil && unidentifiedRunning {
		return nil, ErrUnidentifiedUnityEditor
	}
	return process, err
}

func findRunningUnityProcess(ctx context.Context, projectRoot string) (*UnityProcess, bool, error) {
	processes, err := listUnityProcesses(ctx)
	if err != nil {
		return nil, false, err
	}
	return selectProjectProcess(processes, projectRoot)
}

// selectProjectProcess picks projectRoot's Editor from the listed Editors, and reports whether an Editor
// whose project is unknown was listed.
func selectProjectProcess(processes []UnityProcess, projectRoot string) (*UnityProcess, bool, error) {
	if _, err := normalizeComparablePath(projectRoot); err != nil {
		return nil, false, err
	}

	unidentifiedRunning := false
	for _, candidate := range processes {
		// Why not compare an empty path: filepath.Abs resolves it to the working directory, so it
		// would match a project the command runs from.
		if candidate.projectPath == "" {
			unidentifiedRunning = true
			continue
		}
		if isProjectPath(candidate.projectPath, projectRoot) {
			process := candidate
			return &process, unidentifiedRunning, nil
		}
	}
	return nil, unidentifiedRunning, nil
}

// isProjectPath reports whether path, as an Editor's command line or log records it, names the project at
// projectRoot.
func isProjectPath(path string, projectRoot string) bool {
	normalizedPath, err := normalizeComparablePath(path)
	if err != nil {
		return false
	}
	normalizedRoot, err := normalizeComparablePath(projectRoot)
	if err != nil {
		return false
	}
	return normalizedPath == normalizedRoot || isSameDirectory(path, projectRoot)
}

// isSameDirectory reports whether both paths name one existing directory. Why on top of the string
// comparison: a command's project root has its symlinks evaluated, while an Editor's command line keeps
// the path it was started with, and a case-insensitive file system accepts either spelling.
func isSameDirectory(first string, second string) bool {
	firstInfo, err := os.Stat(first)
	if err != nil {
		return false
	}
	secondInfo, err := os.Stat(second)
	if err != nil {
		return false
	}
	return os.SameFile(firstInfo, secondInfo)
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
	output, err := runProcessListCommandWithin(
		ctx,
		ProcessListCommandTimeout,
		windowsPowerShellCommand,
		"-NoProfile",
		"-Command",
		windowsUnityProcessListScript(),
	)
	if err != nil {
		return nil, err
	}
	return parseWindowsUnityProcesses(string(output)), nil
}

// runProcessListCommandWithin takes the timeout as a parameter so a test can reach the
// timeout path without waiting for ProcessListCommandTimeout.
// why the timeout is spelled out: the kill leaves Windows reporting only exit status 1,
// so a PowerShell or WMI that is slow to start and a failing script look the same. It is
// claimed only while ctx is alive: a caller whose own deadline or cancellation came
// first reports that itself, and this limit was not what stopped the command.
func runProcessListCommandWithin(ctx context.Context, timeout time.Duration, name string, args ...string) ([]byte, error) {
	commandContext, cancel := withCommandTimeout(ctx, timeout)
	defer cancel()
	output, err := exec.CommandContext(commandContext, name, args...).Output()
	if err == nil {
		return output, nil
	}
	if ctx.Err() == nil && errors.Is(commandContext.Err(), context.DeadlineExceeded) {
		return nil, fmt.Errorf(
			"listing Unity processes on Windows timed out after %s; PowerShell or WMI did not respond, retry the command: %w",
			timeout,
			err,
		)
	}
	return nil, fmt.Errorf("failed to retrieve Unity process list on Windows: %w", commandErrorWithStderr(err, exitErrorStderr(err)))
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
	return UnityProcess{Pid: pid, projectPath: extractProjectPath(command)}, true
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
// matcher it does not join argv and match substrings or regexes over it: argv
// boundaries are preserved in /proc, so a project path containing spaces is read
// without guessing where it ends. path.Base (not filepath.Base) keeps the result
// identical when the tests run on Windows, since Linux paths always use '/'.
func matchLinuxUnityProcess(pid int, args []string) (UnityProcess, bool) {
	if len(args) == 0 {
		return UnityProcess{}, false
	}
	if !isLinuxUnityEditorExecutable(path.Base(args[0])) {
		return UnityProcess{}, false
	}
	if hasNonEditorLinuxArg(args) {
		return UnityProcess{}, false
	}
	return UnityProcess{Pid: pid, projectPath: linuxProjectPathFromArgs(args)}, true
}

// isLinuxUnityEditorExecutable reports whether argv[0]'s basename is a Unity Editor
// binary. Unity Hub launches the Editor through a hardlink named
// unityhub-unity-editor-<version> that sits beside Editor/Unity, so the basename
// is not always "Unity".
func isLinuxUnityEditorExecutable(executableName string) bool {
	if executableName == "Unity" {
		return true
	}
	return strings.HasPrefix(executableName, "unityhub-unity-editor-")
}

// hasNonEditorLinuxArg reports whether argv marks a batchmode run or an asset
// import worker. It compares whole arguments rather than substrings of the joined
// command so a project path that merely contains these words is not excluded.
func hasNonEditorLinuxArg(args []string) bool {
	for _, arg := range args {
		lowerArg := strings.ToLower(arg)
		if lowerArg == "-batchmode" || strings.HasPrefix(lowerArg, "assetimportworker") {
			return true
		}
	}
	return false
}

// linuxProjectPathFromArgs returns the value of Unity's -projectPath or -createProject flag, accepting
// both the "-projectPath <path>" and "-projectPath=<path>" forms case-insensitively.
func linuxProjectPathFromArgs(args []string) string {
	for index, arg := range args {
		lowerArg := strings.ToLower(arg)
		for _, flag := range []string{"-projectpath", "-createproject"} {
			if lowerArg == flag {
				if index+1 >= len(args) {
					return ""
				}
				return args[index+1]
			}
			if strings.HasPrefix(lowerArg, flag+"=") {
				return arg[len(flag+"="):]
			}
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
		processes = append(processes, UnityProcess{Pid: pid, projectPath: extractProjectPath(command)})
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
