package dispatcher

import (
	"context"
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

	"github.com/hatayama/unity-cli-loop/cli/internal/clicore"
	"github.com/hatayama/unity-cli-loop/cli/internal/project"
)

const (
	launchLockfilePoll       = 100 * time.Millisecond
	launchLockfileTimeout    = 5 * time.Second
	launchProcessExitPoll    = 100 * time.Millisecond
	launchProcessExitTimeout = 20 * time.Second
	launchReadinessTimeout   = 10 * time.Minute
	projectVersionFilePath   = "ProjectSettings/ProjectVersion.txt"
	recoveryDirectoryPath    = "Assets/_Recovery"
	launchTempDirectoryName  = "Temp"
	unityLockfileName        = "UnityLockfile"
)

var (
	findRunningUnityProcessForLaunch    = clicore.FindRunningUnityProcess
	focusUnityProcessForLaunch          = clicore.FocusUnityProcess
	killUnityProcessForLaunch           = killUnityProcess
	resolveUnityExecutablePathForLaunch = resolveUnityExecutablePath
	waitForUnityProcessExitForLaunch    = waitForUnityProcessExit
	waitForUnityStartupMarkerForLaunch  = waitForUnityStartupMarkerOrTimeout
	waitForToolReadinessForLaunch       = clicore.WaitForToolReadinessWithTimeout
	probeProjectIpcForLaunchFallback    = clicore.ProbeToolReadinessSequence
)

var editorVersionPattern = regexp.MustCompile(`(?m)^m_EditorVersion:\s*(.+)$`)

type launchOptions struct {
	projectPath    string
	restart        bool
	quit           bool
	deleteRecovery bool
	editorVersion  string
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
	if len(args) == 0 || args[0] != clicore.LaunchCommandName {
		return false, 0
	}
	if clicore.ContainsHelpRequest(args[1:]) {
		printLaunchHelp(stdout)
		return true, 0
	}

	options, err := parseLaunchOptions(args[1:], globalProjectPath)
	if err != nil {
		clicore.WriteClassifiedError(stderr, err, clicore.ErrorContext{Command: clicore.LaunchCommandName})
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
		nextIndex, err := applyLaunchOption(&options, args, index)
		if err != nil {
			return launchOptions{}, err
		}
		index = nextIndex
	}

	return options, nil
}

func runLaunch(ctx context.Context, options launchOptions, startPath string, stdout io.Writer, stderr io.Writer) int {
	writeLaunchProjectSearch(stdout, options, startPath)

	projectRoot, err := resolveLaunchProjectRoot(startPath, options)
	if err != nil {
		clicore.WriteClassifiedError(stderr, err, clicore.ErrorContext{Command: clicore.LaunchCommandName})
		return 1
	}

	if !deleteLaunchRecoveryIfRequested(options, projectRoot, stderr) {
		return 1
	}

	runningProcess, handled, code := findLaunchRunningProcess(ctx, options, projectRoot, stdout, stderr)
	if handled {
		return code
	}

	if runningProcess != nil {
		if handled, code := handleExistingLaunchProcess(ctx, options, projectRoot, runningProcess, stdout, stderr); handled {
			return code
		}
	}

	if options.quit {
		return writeLaunchQuitResponse(stdout, stderr, projectRoot, nil, launchNoProcessMessage)
	}

	return startUnityAndWaitForReadiness(ctx, options, projectRoot, runningProcess, stdout, stderr)
}

func writeLaunchProjectSearch(stdout io.Writer, options launchOptions, startPath string) {
	if options.projectPath != "" {
		return
	}
	depthInfo := strconv.Itoa(options.maxDepth)
	if options.maxDepth == -1 {
		depthInfo = "unlimited"
	}
	clicore.WriteFormat(stdout, "Searching for Unity project under %s (max-depth: %s)...\n\n", startPath, depthInfo)
}

func deleteLaunchRecoveryIfRequested(options launchOptions, projectRoot string, stderr io.Writer) bool {
	if !options.deleteRecovery {
		return true
	}
	if err := os.RemoveAll(filepath.Join(projectRoot, recoveryDirectoryPath)); err != nil {
		clicore.WriteClassifiedError(stderr, err, clicore.ErrorContext{ProjectRoot: projectRoot, Command: clicore.LaunchCommandName})
		return false
	}
	return true
}

func findLaunchRunningProcess(
	ctx context.Context,
	options launchOptions,
	projectRoot string,
	stdout io.Writer,
	stderr io.Writer,
) (*clicore.UnityProcess, bool, int) {
	runningProcess, err := findRunningUnityProcessForLaunch(ctx, projectRoot)
	if err == nil {
		return runningProcess, false, 0
	}
	// Sandboxes can block the process scan (e.g. /bin/ps). A responding project IPC
	// proves Unity is running, so plain launch must not fail on the scan alone.
	// Restart and quit still fail because they need a process id to kill.
	if !options.restart && !options.quit && probeProjectIpcForLaunchFallback(ctx, projectRoot) == nil {
		return nil, true, writeDetectionFallbackLaunchReadyResponse(stdout, stderr, projectRoot, err)
	}
	clicore.WriteClassifiedError(stderr, err, clicore.ErrorContext{ProjectRoot: projectRoot, Command: clicore.LaunchCommandName})
	return nil, true, 1
}

func handleExistingLaunchProcess(
	ctx context.Context,
	options launchOptions,
	projectRoot string,
	runningProcess *clicore.UnityProcess,
	stdout io.Writer,
	stderr io.Writer,
) (bool, int) {
	if !options.restart && !options.quit {
		if options.editorVersion != "" {
			clicore.WriteClassifiedError(stderr, launchEditorVersionRequiresRestartError(options.editorVersion), clicore.ErrorContext{ProjectRoot: projectRoot, Command: clicore.LaunchCommandName})
			return true, 1
		}
		return true, waitForExistingLaunchReadiness(ctx, projectRoot, runningProcess.Pid, stdout, stderr)
	}
	if err := killUnityProcessForLaunch(runningProcess.Pid); err != nil {
		clicore.WriteClassifiedError(stderr, err, clicore.ErrorContext{ProjectRoot: projectRoot, Command: clicore.LaunchCommandName})
		return true, 1
	}
	if err := waitForUnityProcessExitForLaunch(ctx, projectRoot, runningProcess.Pid, launchProcessExitPoll, launchProcessExitTimeout); err != nil {
		clicore.WriteClassifiedError(stderr, err, clicore.ErrorContext{ProjectRoot: projectRoot, Command: clicore.LaunchCommandName})
		return true, 1
	}
	if options.quit {
		return true, writeLaunchQuitResponse(stdout, stderr, projectRoot, &runningProcess.Pid, launchStoppedMessage)
	}
	return false, 0
}

func launchEditorVersionRequiresRestartError(editorVersion string) error {
	return &clicore.ArgumentError{
		Message:  "--editor-version requires --restart when Unity is already running for this project.",
		Option:   "--editor-version",
		Received: editorVersion,
		Command:  clicore.LaunchCommandName,
		NextActions: []string{
			fmt.Sprintf("Run `uloop launch --restart --editor-version %s` to relaunch Unity with the requested Editor version.", editorVersion),
		},
	}
}

func waitForExistingLaunchReadiness(ctx context.Context, projectRoot string, pid int, stdout io.Writer, stderr io.Writer) int {
	logLaunchExistingFocus(ctx, projectRoot, pid)
	spinner := clicore.NewLaunchSpinner(stdout, stderr)
	defer spinner.Stop()
	writeLaunchReadinessWait(stdout, spinner)
	if err := waitForLaunchReadiness(ctx, projectRoot); err != nil {
		clicore.WriteClassifiedError(stderr, err, clicore.ErrorContext{ProjectRoot: projectRoot, Command: clicore.LaunchCommandName})
		return 1
	}
	spinner.Stop()
	return writeExistingLaunchReadyResponse(stdout, stderr, projectRoot, pid)
}

func startUnityAndWaitForReadiness(
	ctx context.Context,
	options launchOptions,
	projectRoot string,
	runningProcess *clicore.UnityProcess,
	stdout io.Writer,
	stderr io.Writer,
) int {
	removedStaleTemp, err := cleanStaleUnityTemp(projectRoot)
	if err != nil {
		clicore.WriteClassifiedError(stderr, err, clicore.ErrorContext{ProjectRoot: projectRoot, Command: clicore.LaunchCommandName})
		return 1
	}
	if removedStaleTemp {
		clicore.WriteFormat(stdout, "UnityLockfile found without active Unity process: %s\n", unityLockfilePath(projectRoot))
		clicore.WriteLine(stdout, "Assuming previous crash. Cleaning Temp directory and continuing launch.")
		clicore.WriteLine(stdout, "Deleted Temp directory.")
		clicore.WriteLine(stdout, "Deleted UnityLockfile.")
		clicore.WriteLine(stdout, "")
	}

	unityVersion, err := resolveLaunchEditorVersion(projectRoot, options)
	if err != nil {
		clicore.WriteClassifiedError(stderr, err, clicore.ErrorContext{ProjectRoot: projectRoot, Command: clicore.LaunchCommandName})
		return 1
	}

	unityPath, err := resolveUnityExecutablePathForLaunch(unityVersion)
	if err != nil {
		clicore.WriteClassifiedError(stderr, err, clicore.ErrorContext{ProjectRoot: projectRoot, Command: clicore.LaunchCommandName})
		return 1
	}

	spinner := clicore.NewLaunchSpinner(stdout, stderr)
	defer spinner.Stop()

	clicore.WriteLine(stdout, "Opening Unity...")
	clicore.WriteFormat(stdout, "Project Path: %s\n", projectRoot)
	clicore.WriteFormat(stdout, "Detected Unity version: %s\n", unityVersion)
	clicore.WriteLine(stdout, "Unity Hub launch options: none")

	command := newUnityLaunchCommand(unityPath, buildUnityLaunchArgs(projectRoot, options))
	if err := command.Start(); err != nil {
		clicore.WriteClassifiedError(stderr, err, clicore.ErrorContext{ProjectRoot: projectRoot, Command: clicore.LaunchCommandName})
		return 1
	}
	currentPid := command.Process.Pid
	if err := command.Process.Release(); err != nil {
		clicore.WriteClassifiedError(stderr, err, clicore.ErrorContext{ProjectRoot: projectRoot, Command: clicore.LaunchCommandName})
		return 1
	}
	if err := waitForUnityStartupMarkerForLaunch(ctx, unityLockfilePath(projectRoot), launchLockfilePoll, launchLockfileTimeout); err != nil {
		clicore.WriteClassifiedError(stderr, err, clicore.ErrorContext{ProjectRoot: projectRoot, Command: clicore.LaunchCommandName})
		return 1
	}
	writeLaunchReadinessWait(stdout, spinner)
	if err := waitForLaunchReadiness(ctx, projectRoot); err != nil {
		clicore.WriteClassifiedError(stderr, err, clicore.ErrorContext{ProjectRoot: projectRoot, Command: clicore.LaunchCommandName})
		return 1
	}
	spinner.Stop()
	var previousPid *int
	if runningProcess != nil {
		previousPid = &runningProcess.Pid
	}
	return writeLaunchedReadyResponse(stdout, stderr, projectRoot, previousPid, currentPid)
}

func buildUnityLaunchArgs(projectRoot string, options launchOptions) []string {
	launchArgs := []string{"-projectPath", projectRoot}
	if options.platform != "" {
		launchArgs = append(launchArgs, "-buildTarget", options.platform)
	}
	launchArgs = append(launchArgs, "-ignorecompilererrors")
	return launchArgs
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

func waitForUnityProcessExit(ctx context.Context, projectRoot string, pid int, pollInterval time.Duration, timeout time.Duration) error {
	timeoutContext, cancel := context.WithTimeout(ctx, timeout)
	defer cancel()
	timeoutError := func() error {
		if ctx.Err() != nil {
			return ctx.Err()
		}
		return launchProcessExitTimeoutError{
			projectRoot: projectRoot,
			pid:         pid,
			timeout:     timeout,
		}
	}

	ticker := time.NewTicker(pollInterval)
	defer ticker.Stop()

	for {
		runningProcess, err := findRunningUnityProcessForLaunch(timeoutContext, projectRoot)
		if err != nil {
			if timeoutContext.Err() != nil {
				return timeoutError()
			}
			return err
		}
		if runningProcess == nil || runningProcess.Pid != pid {
			return nil
		}

		select {
		case <-timeoutContext.Done():
			return timeoutError()
		case <-ticker.C:
		}
	}
}

func waitForUnityStartupMarkerOrTimeout(ctx context.Context, lockfilePath string, pollInterval time.Duration, timeout time.Duration) error {
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

func resolveLaunchProjectRoot(startPath string, options launchOptions) (string, error) {
	if options.projectPath != "" {
		return project.ResolveExplicitProjectRoot(options.projectPath)
	}
	return project.FindUnityProjectRootWithin(startPath, options.maxDepth)
}

func resolveUnityExecutablePath(version string) (string, error) {
	candidates := unityExecutableCandidates(version)
	return resolveExistingUnityExecutablePath(version, candidates)
}

func resolveLaunchEditorVersion(projectRoot string, options launchOptions) (string, error) {
	if options.editorVersion != "" {
		return options.editorVersion, nil
	}
	return readUnityEditorVersion(projectRoot)
}

func resolveExistingUnityExecutablePath(version string, candidates []string) (string, error) {
	for _, candidate := range candidates {
		if _, err := os.Stat(candidate); err == nil {
			return candidate, nil
		}
	}
	if len(candidates) == 0 {
		return "", fmt.Errorf("unity launch is not supported on %s", runtime.GOOS)
	}
	return "", fmt.Errorf("unity %s executable not found; searched: %s", version, strings.Join(candidates, ", "))
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

func killUnityProcess(pid int) error {
	process, err := os.FindProcess(pid)
	if err != nil {
		return err
	}
	return process.Kill()
}

func printLaunchHelp(stdout io.Writer) {
	clicore.WriteLine(stdout, "Usage:")
	clicore.WriteLine(stdout, "  uloop launch [options] [project-path]")
	clicore.WriteLine(stdout, "")
	clicore.WriteLine(stdout, "Options:")
	clicore.WriteLine(stdout, "  -r, --restart          Kill an existing Unity process for the project before launching")
	clicore.WriteLine(stdout, "  -q, --quit             Kill an existing Unity process for the project without launching")
	clicore.WriteLine(stdout, "  -d, --delete-recovery  Delete Assets/_Recovery before launch")
	clicore.WriteLine(stdout, "      --editor-version <version>")
	clicore.WriteLine(stdout, "                          Use this Unity Editor version instead of ProjectVersion.txt")
	clicore.WriteLine(stdout, "  -p, --platform <name>  Pass Unity -buildTarget when launching")
	clicore.WriteLine(stdout, "      --max-depth <n>    Accepted for compatibility when searching from the current directory")
	clicore.WriteLine(stdout, "")
	clicore.WriteLine(stdout, "Compiler errors are ignored by default during Unity startup.")
	clicore.WriteLine(stdout, "")
	printGlobalOptionsHelp(stdout)
}
