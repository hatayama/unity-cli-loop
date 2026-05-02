package cli

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

	"github.com/hatayama/unity-cli-loop/Packages/src/GoCli/internal/project"
	"github.com/hatayama/unity-cli-loop/Packages/src/GoCli/internal/unity"
)

const (
	launchCommandName      = "launch"
	launchReadinessTimeout = 120 * time.Second
	launchReadinessPoll    = 1 * time.Second
	projectVersionFilePath = "ProjectSettings/ProjectVersion.txt"
	recoveryDirectoryPath  = "Assets/_Recovery"
)

var editorVersionPattern = regexp.MustCompile(`(?m)^m_EditorVersion:\s*(.+)$`)

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
		writeLine(stderr, err.Error())
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
			return launchOptions{}, fmt.Errorf("native launch does not support Unity Hub registration options")
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
				return launchOptions{}, fmt.Errorf("invalid --max-depth value: %s", value)
			}
			options.maxDepth = maxDepth
			if consumed {
				index++
			}
		case strings.HasPrefix(arg, "--max-depth="):
			value := strings.TrimPrefix(arg, "--max-depth=")
			maxDepth, err := strconv.Atoi(value)
			if err != nil {
				return launchOptions{}, fmt.Errorf("invalid --max-depth value: %s", value)
			}
			options.maxDepth = maxDepth
		case strings.HasPrefix(arg, "-"):
			return launchOptions{}, fmt.Errorf("unknown launch option: %s", arg)
		default:
			if options.projectPath != "" {
				return launchOptions{}, fmt.Errorf("unexpected extra launch argument: %s", arg)
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
			return "", false, fmt.Errorf("%s requires a value", parts[0])
		}
		return parts[1], false, nil
	}
	if index+1 >= len(args) || isInvalidLaunchOptionValue(option, args[index+1]) {
		return "", false, fmt.Errorf("%s requires a value", option)
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
	projectRoot, err := resolveLaunchProjectRoot(startPath, options)
	if err != nil {
		writeLine(stderr, err.Error())
		return 1
	}

	if options.deleteRecovery {
		if err := os.RemoveAll(filepath.Join(projectRoot, recoveryDirectoryPath)); err != nil {
			writeLine(stderr, err.Error())
			return 1
		}
	}

	runningProcess, err := findRunningUnityProcess(ctx, projectRoot)
	if err != nil {
		writeLine(stderr, err.Error())
		return 1
	}

	if runningProcess != nil {
		if !options.restart && !options.quit {
			_ = focusUnityProcess(ctx, runningProcess.pid)
			writeFormat(stdout, "Unity is already running for %s (PID: %d)\n", projectRoot, runningProcess.pid)
			return 0
		}
		if err := killUnityProcess(runningProcess.pid); err != nil {
			writeLine(stderr, err.Error())
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

	unityPath, err := resolveUnityExecutablePath(projectRoot)
	if err != nil {
		writeLine(stderr, err.Error())
		return 1
	}

	launchArgs := []string{"-projectPath", projectRoot}
	if options.platform != "" {
		launchArgs = append(launchArgs, "-buildTarget", options.platform)
	}

	command := exec.CommandContext(ctx, unityPath, launchArgs...)
	if err := command.Start(); err != nil {
		writeLine(stderr, err.Error())
		return 1
	}
	writeFormat(stdout, "Unity launch started for %s (PID: %d)\n", projectRoot, command.Process.Pid)

	if err := waitForLaunchReady(ctx, projectRoot); err != nil {
		writeLine(stderr, err.Error())
		return 1
	}
	writeLine(stdout, "Unity is ready.")
	return 0
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

func killUnityProcess(pid int) error {
	process, err := os.FindProcess(pid)
	if err != nil {
		return err
	}
	return process.Kill()
}

func waitForLaunchReady(ctx context.Context, projectRoot string) error {
	timeoutContext, cancel := context.WithTimeout(ctx, launchReadinessTimeout)
	defer cancel()

	ticker := time.NewTicker(launchReadinessPoll)
	defer ticker.Stop()

	for {
		select {
		case <-timeoutContext.Done():
			return fmt.Errorf("timed out waiting for Unity to become ready after launch")
		case <-ticker.C:
			connection, err := project.ResolveConnection(projectRoot, projectRoot)
			if err != nil {
				continue
			}
			if _, err := unity.NewClient(connection).Send(timeoutContext, "get-version", map[string]any{}); err == nil {
				return nil
			}
		}
	}
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
