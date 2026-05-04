package cli

import (
	"context"
	"encoding/json"
	"fmt"
	"io"
	"os"
	"os/exec"
	"path/filepath"
	"regexp"
	"runtime"
	"strconv"
	"strings"
	"time"

	"github.com/hatayama/unity-cli-loop/Packages/src/GoCli/internal/adapters/project"
	"github.com/hatayama/unity-cli-loop/Packages/src/GoCli/internal/adapters/unity"
)

const (
	launchCommandName       = "launch"
	launchLockfilePoll      = 100 * time.Millisecond
	launchLockfileTimeout   = 5 * time.Second
	launchReadinessTimeout  = 180 * time.Second
	launchReadinessPoll     = 1 * time.Second
	launchProbeTimeout      = 5 * time.Second
	projectVersionFilePath  = "ProjectSettings/ProjectVersion.txt"
	recoveryDirectoryPath   = "Assets/_Recovery"
	launchTempDirectoryName = "Temp"
	unityLockfileName       = "UnityLockfile"
)

var editorVersionPattern = regexp.MustCompile(`(?m)^m_EditorVersion:\s*(.+)$`)

const launchDynamicCodeProbe = `UnityEngine.LogType previous = UnityEngine.Debug.unityLogger.filterLogType; UnityEngine.Debug.unityLogger.filterLogType = UnityEngine.LogType.Warning; try { UnityEngine.Debug.Log("Unity CLI Loop dynamic code prewarm"); return "Unity CLI Loop dynamic code prewarm"; } finally { UnityEngine.Debug.unityLogger.filterLogType = previous; }`

type launchOptions struct {
	projectPath    string
	restart        bool
	quit           bool
	deleteRecovery bool
	platform       string
	maxDepth       int
}

func tryHandleLaunchRequest(
	ctx context.Context,
	args []string,
	startPath string,
	globalProjectPath string,
	stdout io.Writer,
	stderr io.Writer,
) (bool, int) {
	if len(args) == 0 || args[0] != launchCommandName {
		return false, 0
	}
	if len(args) == 2 && isHelpRequest(args[1:]) {
		printLaunchHelp(stdout)
		return true, 0
	}

	options, err := parseLaunchOptions(args[1:], globalProjectPath)
	if err != nil {
		writeClassifiedError(stderr, err, errorContext{command: launchCommandName})
		return true, 1
	}

	exitCode := runLaunch(ctx, options, startPath, stdout, stderr)
	return true, exitCode
}

func parseLaunchOptions(args []string, globalProjectPath string) (launchOptions, error) {
	options := launchOptions{
		projectPath: globalProjectPath,
		maxDepth:    3,
	}

	for index := 0; index < len(args); index++ {
		arg := args[index]
		switch {
		case arg == "-r" || arg == "--restart":
			options.restart = true
		case arg == "-q" || arg == "--quit":
			options.quit = true
		case arg == "-d" || arg == "--delete-recovery":
			options.deleteRecovery = true
		case arg == "-a" || arg == "-f" || arg == "--add-unity-hub" || arg == "--favorite" || arg == "--unity-hub-entry":
			return launchOptions{}, &argumentError{
				message:     "Native launch does not support Unity Hub registration options.",
				option:      arg,
				command:     launchCommandName,
				nextActions: []string{"Remove the Unity Hub registration option and retry `uloop launch`."},
			}
		case arg == "-p" || arg == "--platform":
			value, consumed, err := readLaunchOptionValue(arg, args, index)
			if err != nil {
				return launchOptions{}, err
			}
			options.platform = value
			if consumed {
				index++
			}
		case strings.HasPrefix(arg, "--platform="):
			options.platform = strings.TrimPrefix(arg, "--platform=")
		case arg == "--max-depth":
			value, consumed, err := readLaunchOptionValue(arg, args, index)
			if err != nil {
				return launchOptions{}, err
			}
			maxDepth, err := strconv.Atoi(value)
			if err != nil {
				return launchOptions{}, invalidValueArgumentError("--max-depth", value, "integer")
			}
			options.maxDepth = maxDepth
			if consumed {
				index++
			}
		case strings.HasPrefix(arg, "--max-depth="):
			value := strings.TrimPrefix(arg, "--max-depth=")
			maxDepth, err := strconv.Atoi(value)
			if err != nil {
				return launchOptions{}, invalidValueArgumentError("--max-depth", value, "integer")
			}
			options.maxDepth = maxDepth
		case strings.HasPrefix(arg, "-"):
			return launchOptions{}, &argumentError{
				message:     "Unknown launch option: " + arg,
				option:      arg,
				command:     launchCommandName,
				nextActions: []string{"Run `uloop launch --help` to inspect supported launch options."},
			}
		default:
			if options.projectPath != "" {
				return launchOptions{}, &argumentError{
					message:     "Unexpected extra launch argument: " + arg,
					received:    arg,
					command:     launchCommandName,
					nextActions: []string{"Pass only one project path to `uloop launch`."},
				}
			}
			options.projectPath = arg
		}
	}

	return options, nil
}

func readLaunchOptionValue(option string, args []string, index int) (string, bool, error) {
	if strings.Contains(option, "=") {
		parts := strings.SplitN(option, "=", 2)
		if parts[1] == "" {
			return "", false, missingValueArgumentError(parts[0])
		}
		return parts[1], false, nil
	}
	if index+1 >= len(args) || isInvalidLaunchOptionValue(option, args[index+1]) {
		return "", false, missingValueArgumentError(option)
	}
	return args[index+1], true, nil
}

func isInvalidLaunchOptionValue(option string, value string) bool {
	if option == "--max-depth" {
		return isNextOptionToken(value)
	}
	return strings.HasPrefix(value, "-")
}

func runLaunch(ctx context.Context, options launchOptions, startPath string, stdout io.Writer, stderr io.Writer) int {
	if options.projectPath == "" {
		depthInfo := strconv.Itoa(options.maxDepth)
		if options.maxDepth == -1 {
			depthInfo = "unlimited"
		}
		writeFormat(stdout, "Searching for Unity project under %s (max-depth: %s)...\n\n", startPath, depthInfo)
	}

	projectRoot, err := resolveLaunchProjectRoot(startPath, options)
	if err != nil {
		writeClassifiedError(stderr, err, errorContext{command: launchCommandName})
		return 1
	}

	if options.deleteRecovery {
		if err := os.RemoveAll(filepath.Join(projectRoot, recoveryDirectoryPath)); err != nil {
			writeClassifiedError(stderr, err, errorContext{projectRoot: projectRoot, command: launchCommandName})
			return 1
		}
	}

	runningProcess, err := findRunningUnityProcess(ctx, projectRoot)
	if err != nil {
		writeClassifiedError(stderr, err, errorContext{projectRoot: projectRoot, command: launchCommandName})
		return 1
	}

	if runningProcess != nil {
		if !options.restart && !options.quit {
			_ = focusUnityProcess(ctx, runningProcess.pid)
			writeFormat(stdout, "Unity is already running for %s (PID: %d)\n", projectRoot, runningProcess.pid)
			return 0
		}
		if err := killUnityProcess(runningProcess.pid); err != nil {
			writeClassifiedError(stderr, err, errorContext{projectRoot: projectRoot, command: launchCommandName})
			return 1
		}
		if options.quit {
			writeFormat(stdout, "Unity process stopped (PID: %d)\n", runningProcess.pid)
			return 0
		}
	}

	if options.quit {
		writeLine(stdout, "No Unity process is running for this project.")
		return 0
	}

	removedStaleTemp, err := cleanStaleUnityTemp(projectRoot)
	if err != nil {
		writeClassifiedError(stderr, err, errorContext{projectRoot: projectRoot, command: launchCommandName})
		return 1
	}
	if removedStaleTemp {
		writeFormat(stdout, "UnityLockfile found without active Unity process: %s\n", unityLockfilePath(projectRoot))
		writeLine(stdout, "Assuming previous crash. Cleaning Temp directory and continuing launch.")
		writeLine(stdout, "Deleted Temp directory.")
		writeLine(stdout, "Deleted UnityLockfile.")
		writeLine(stdout, "")
	}

	unityPath, err := resolveUnityExecutablePath(projectRoot)
	if err != nil {
		writeClassifiedError(stderr, err, errorContext{projectRoot: projectRoot, command: launchCommandName})
		return 1
	}

	spinner := newLaunchSpinner(stdout, stderr)
	defer spinner.Stop()

	writeLine(stdout, "Opening Unity...")
	writeFormat(stdout, "Project Path: %s\n", projectRoot)
	writeFormat(stdout, "Detected Unity version: %s\n", readUnityVersionForLog(projectRoot))
	writeLine(stdout, "Unity Hub launch options: none")

	launchArgs := []string{"-projectPath", projectRoot}
	if options.platform != "" {
		launchArgs = append(launchArgs, "-buildTarget", options.platform)
	}

	command := newUnityLaunchCommand(unityPath, launchArgs)
	if err := command.Start(); err != nil {
		writeClassifiedError(stderr, err, errorContext{projectRoot: projectRoot, command: launchCommandName})
		return 1
	}
	if err := command.Process.Release(); err != nil {
		writeClassifiedError(stderr, err, errorContext{projectRoot: projectRoot, command: launchCommandName})
		return 1
	}
	if err := waitForUnityLockfile(ctx, unityLockfilePath(projectRoot), launchLockfilePoll, launchLockfileTimeout); err != nil {
		writeClassifiedError(stderr, err, errorContext{projectRoot: projectRoot, command: launchCommandName})
		return 1
	}
	if err := waitForLaunchReady(ctx, projectRoot); err != nil {
		writeClassifiedError(stderr, err, errorContext{projectRoot: projectRoot, command: launchCommandName})
		return 1
	}
	return 0
}

func newUnityLaunchCommand(unityPath string, launchArgs []string) *exec.Cmd {
	command := exec.Command(unityPath, launchArgs...)
	command.Env = append(os.Environ(), "MSYS_NO_PATHCONV=1")
	configureDetachedUnityLaunchCommand(command)
	return command
}

func cleanStaleUnityTemp(projectRoot string) (bool, error) {
	lockfilePath := unityLockfilePath(projectRoot)
	if _, err := os.Stat(lockfilePath); err != nil {
		if os.IsNotExist(err) {
			return false, nil
		}
		return false, err
	}

	return true, os.RemoveAll(filepath.Join(projectRoot, launchTempDirectoryName))
}

func waitForUnityLockfile(ctx context.Context, lockfilePath string, pollInterval time.Duration, timeout time.Duration) error {
	timeoutContext, cancel := context.WithTimeout(ctx, timeout)
	defer cancel()

	ticker := time.NewTicker(pollInterval)
	defer ticker.Stop()

	for {
		if _, err := os.Stat(lockfilePath); err == nil {
			return nil
		} else if !os.IsNotExist(err) {
			return err
		}

		select {
		case <-timeoutContext.Done():
			if ctx.Err() != nil {
				return ctx.Err()
			}
			return nil
		case <-ticker.C:
		}
	}
}

func unityLockfilePath(projectRoot string) string {
	return filepath.Join(projectRoot, launchTempDirectoryName, unityLockfileName)
}

func waitForLaunchReady(ctx context.Context, projectRoot string) error {
	timeoutContext, cancel := context.WithTimeout(ctx, launchReadinessTimeout)
	defer cancel()

	for {
		if err := probeLaunchReady(timeoutContext, projectRoot); err == nil {
			return nil
		}

		select {
		case <-timeoutContext.Done():
			return fmt.Errorf("timed out waiting for Unity to become ready after launch")
		case <-time.After(launchReadinessPoll):
		}
	}
}

func probeLaunchReady(ctx context.Context, projectRoot string) error {
	probeContext, cancel := context.WithTimeout(ctx, launchProbeTimeout)
	defer cancel()

	connection, err := project.ResolveConnection(projectRoot, projectRoot)
	if err != nil {
		return err
	}

	if !isExecuteDynamicCodeAvailable(projectRoot) {
		_, err := unity.NewClient(connection).Send(probeContext, "get-version", map[string]any{})
		return err
	}

	response, err := unity.NewClient(connection).Send(probeContext, "execute-dynamic-code", map[string]any{
		"Code":                      launchDynamicCodeProbe,
		"CompileOnly":               false,
		"YieldToForegroundRequests": true,
	})
	if err != nil {
		return err
	}

	var payload executeDynamicCodeLaunchResponse
	if err := json.Unmarshal(response, &payload); err != nil {
		return err
	}
	if !payload.Success {
		if payload.ErrorMessage != "" {
			return fmt.Errorf("execute-dynamic-code launch readiness probe failed: %s", payload.ErrorMessage)
		}
		return fmt.Errorf("execute-dynamic-code launch readiness probe failed")
	}
	return nil
}

type executeDynamicCodeLaunchResponse struct {
	Success      bool   `json:"Success"`
	ErrorMessage string `json:"ErrorMessage"`
}

func isExecuteDynamicCodeAvailable(projectRoot string) bool {
	cache, err := loadTools(projectRoot)
	if err != nil {
		return true
	}
	_, ok := findTool(cache, "execute-dynamic-code")
	return ok
}

func resolveLaunchProjectRoot(startPath string, options launchOptions) (string, error) {
	if options.projectPath != "" {
		projectRoot, err := filepath.Abs(options.projectPath)
		if err != nil {
			return "", err
		}
		if !project.IsUnityProject(projectRoot) {
			return "", fmt.Errorf("not a Unity project: %s", projectRoot)
		}
		return projectRoot, nil
	}
	return project.FindUnityProjectRootWithin(startPath, options.maxDepth)
}

func resolveUnityExecutablePath(projectRoot string) (string, error) {
	version, err := readUnityEditorVersion(projectRoot)
	if err != nil {
		return "", err
	}

	candidates := unityExecutableCandidates(version)
	for _, candidate := range candidates {
		if _, err := os.Stat(candidate); err == nil {
			return candidate, nil
		}
	}
	if len(candidates) == 0 {
		return "", fmt.Errorf("unity launch is not supported on %s", runtime.GOOS)
	}
	return candidates[0], nil
}

func unityExecutableCandidates(version string) []string {
	switch runtime.GOOS {
	case "darwin":
		return []string{fmt.Sprintf("/Applications/Unity/Hub/Editor/%s/Unity.app/Contents/MacOS/Unity", version)}
	case "windows":
		return windowsUnityExecutableCandidates(version)
	default:
		return []string{}
	}
}

func windowsUnityExecutableCandidates(version string) []string {
	candidates := []string{}
	for _, base := range []string{
		os.Getenv("ProgramFiles"),
		os.Getenv("ProgramFiles(x86)"),
		os.Getenv("LOCALAPPDATA"),
		`C:\Program Files`,
	} {
		if base == "" {
			continue
		}
		candidates = append(candidates, filepath.Join(base, "Unity", "Hub", "Editor", version, "Editor", "Unity.exe"))
	}
	return candidates
}

func readUnityEditorVersion(projectRoot string) (string, error) {
	content, err := os.ReadFile(filepath.Join(projectRoot, projectVersionFilePath))
	if err != nil {
		return "", err
	}
	matches := editorVersionPattern.FindStringSubmatch(string(content))
	if len(matches) != 2 {
		return "", fmt.Errorf("unity editor version not found in %s", projectVersionFilePath)
	}
	version := strings.TrimSpace(matches[1])
	if version == "" {
		return "", fmt.Errorf("unity editor version is empty in %s", projectVersionFilePath)
	}
	return version, nil
}

func readUnityVersionForLog(projectRoot string) string {
	version, err := readUnityEditorVersion(projectRoot)
	if err != nil {
		return "unknown"
	}
	return version
}

func killUnityProcess(pid int) error {
	process, err := os.FindProcess(pid)
	if err != nil {
		return err
	}
	return process.Kill()
}

func printLaunchHelp(stdout io.Writer) {
	writeLine(stdout, "Usage:")
	writeLine(stdout, "  uloop launch [options] [project-path]")
	writeLine(stdout, "")
	writeLine(stdout, "Options:")
	writeLine(stdout, "  -r, --restart          Kill an existing Unity process for the project before launching")
	writeLine(stdout, "  -q, --quit             Kill an existing Unity process for the project without launching")
	writeLine(stdout, "  -d, --delete-recovery  Delete Assets/_Recovery before launch")
	writeLine(stdout, "  -p, --platform <name>  Pass Unity -buildTarget when launching")
	writeLine(stdout, "      --max-depth <n>    Accepted for compatibility when searching from the current directory")
}
