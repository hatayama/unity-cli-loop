package dispatcher

import (
	"context"
	"encoding/json"
	"fmt"
	"io"
	"os"
	"os/exec"
	"path/filepath"
	"runtime"
	"sort"
	"strings"
	"syscall"

	dispatchercontract "github.com/hatayama/unity-cli-loop/Packages/src/Cli/Dispatcher"
)

const (
	projectPathFlagName      = "project-path"
	projectLocalUnixPath     = ".uloop/bin/uloop-core"
	projectLocalWindowsPath  = ".uloop/bin/uloop-core.exe"
	assetsDirectoryName      = "Assets"
	projectSettingsDirectory = "ProjectSettings"
	defaultLaunchMaxDepth    = 3
)

var excludedProjectSearchDirs = map[string]bool{
	".git":         true,
	"Build":        true,
	"Builds":       true,
	"Library":      true,
	"Logs":         true,
	"Temp":         true,
	"node_modules": true,
	"obj":          true,
}

var execCoreForDispatch = execProjectLocal

func Run(ctx context.Context, args []string, stdout io.Writer, stderr io.Writer) int {
	if isVersionRequest(args) {
		writeLine(stdout, dispatchercontract.Current.DispatcherVersion)
		return 0
	}
	if handled, code := tryHandleCompletionRequest(args, stdout, stderr); handled {
		return code
	}
	if handled, code := tryHandleUpdateRequest(ctx, args, stdout, stderr); handled {
		return code
	}

	startPath, err := os.Getwd()
	if err != nil {
		writeError(stderr, internalError(err.Error(), ""))
		return 1
	}
	if len(args) == 0 || isHelpRequest(args) {
		printHelpForResolvedProject(stdout, startPath, "")
		return 0
	}

	remainingArgs, explicitProjectPath, err := parseProjectPath(args)
	if err != nil {
		writeError(stderr, argumentError(err.Error(), ""))
		return 1
	}
	if len(remainingArgs) == 0 || isHelpRequest(remainingArgs) {
		printHelpForResolvedProject(stdout, startPath, explicitProjectPath)
		return 0
	}
	if isVersionRequest(remainingArgs) {
		writeLine(stdout, dispatchercontract.Current.DispatcherVersion)
		return 0
	}
	if isLaunchHelpRequest(remainingArgs) {
		printLaunchHelp(stdout)
		return 0
	}
	if isSkillsHelpRequest(remainingArgs) {
		printSkillsHelp(stdout)
		return 0
	}
	if err := validateProjectResolutionArgs(remainingArgs, explicitProjectPath); err != nil {
		writeError(stderr, argumentError(err.Error(), commandName(remainingArgs)))
		return 1
	}

	projectRoot, err := resolveProjectRoot(startPath, explicitProjectPath, remainingArgs)
	if err != nil {
		writeError(stderr, projectNotFoundError(err.Error(), commandName(remainingArgs)))
		return 1
	}

	localPath := projectLocalPath(projectRoot)
	exists, err := projectLocalCoreExists(localPath)
	if err != nil {
		writeError(stderr, internalError(err.Error(), projectRoot))
		return 1
	}
	if !exists {
		if isLaunchCommand(remainingArgs) {
			return runLaunchBootstrap(ctx, remainingArgs[1:], explicitProjectPath, projectRoot, stdout, stderr)
		}
		if isSkillsCommand(remainingArgs) {
			bundledCorePath, err := findBundledCorePath(projectRoot)
			if err != nil {
				writeError(stderr, internalError(err.Error(), projectRoot))
				return 1
			}
			if bundledCorePath != "" {
				forwardedArgs := forwardedProjectLocalArgs(remainingArgs, explicitProjectPath, projectRoot)
				return execCoreForDispatch(ctx, bundledCorePath, forwardedArgs, projectRoot, stderr)
			}
		}
		writeError(stderr, projectLocalCLIMissingError(localPath, projectRoot, commandName(remainingArgs)))
		return 1
	}

	forwardedArgs := forwardedProjectLocalArgs(remainingArgs, explicitProjectPath, projectRoot)
	return execCoreForDispatch(ctx, localPath, forwardedArgs, projectRoot, stderr)
}

func validateProjectResolutionArgs(args []string, explicitProjectPath string) error {
	if !isLaunchCommand(args) {
		return nil
	}
	_, err := parseLaunchProjectResolutionOptions(args[1:], explicitProjectPath)
	return err
}

func projectLocalCoreExists(localPath string) (bool, error) {
	_, err := os.Stat(localPath)
	if err == nil {
		return true, nil
	}
	if os.IsNotExist(err) {
		return false, nil
	}
	return false, err
}

func commandName(args []string) string {
	if len(args) == 0 {
		return ""
	}
	return args[0]
}

func forwardedProjectLocalArgs(args []string, explicitProjectPath string, projectRoot string) []string {
	forwardedArgs := append([]string{}, args...)
	if explicitProjectPath != "" {
		return append(forwardedArgs, "--project-path", projectRoot)
	}
	if isLaunchCommand(forwardedArgs) {
		return replaceLaunchPositionalProjectPath(forwardedArgs, projectRoot)
	}
	return forwardedArgs
}

func replaceLaunchPositionalProjectPath(args []string, projectRoot string) []string {
	for index := 1; index < len(args); index++ {
		arg := args[index]
		if arg == "-p" || arg == "--platform" || arg == "--max-depth" {
			if index+1 < len(args) {
				index++
			}
			continue
		}
		if strings.HasPrefix(arg, "-") {
			continue
		}
		args[index] = projectRoot
		return args
	}
	return args
}

func isVersionRequest(args []string) bool {
	return len(args) == 1 && (args[0] == "--version" || args[0] == "-v")
}

func isHelpRequest(args []string) bool {
	return len(args) == 1 && (args[0] == "--help" || args[0] == "-h")
}

func parseProjectPath(args []string) ([]string, string, error) {
	remaining := make([]string, 0, len(args))
	projectPath := ""

	for index := 0; index < len(args); index++ {
		arg := args[index]
		if arg == "--"+projectPathFlagName {
			if index+1 >= len(args) || strings.HasPrefix(args[index+1], "-") {
				return nil, "", fmt.Errorf("--%s requires a value", projectPathFlagName)
			}
			projectPath = args[index+1]
			index++
			continue
		}
		prefix := "--" + projectPathFlagName + "="
		if strings.HasPrefix(arg, prefix) {
			value := strings.TrimPrefix(arg, prefix)
			if value == "" {
				return nil, "", fmt.Errorf("--%s requires a value", projectPathFlagName)
			}
			projectPath = value
			continue
		}
		remaining = append(remaining, arg)
	}

	return remaining, projectPath, nil
}

func resolveProjectRoot(startPath string, explicitProjectPath string, args []string) (string, error) {
	if len(args) > 0 && args[0] == "launch" {
		options, err := parseLaunchProjectResolutionOptions(args[1:], explicitProjectPath)
		if err != nil {
			return "", err
		}
		if options.projectPath != "" {
			projectRoot, err := filepath.Abs(options.projectPath)
			if err != nil {
				return "", err
			}
			if !isUnityProject(projectRoot) {
				if explicitProjectPath != "" {
					return "", fmt.Errorf("--project-path does not point to a Unity project: %s", projectRoot)
				}
				return "", fmt.Errorf("not a Unity project: %s", projectRoot)
			}
			return projectRoot, nil
		}
		return findUnityProjectRootWithin(startPath, options.maxDepth)
	}
	if explicitProjectPath != "" {
		projectRoot, err := filepath.Abs(explicitProjectPath)
		if err != nil {
			return "", err
		}
		if !isUnityProject(projectRoot) {
			return "", fmt.Errorf("--project-path does not point to a Unity project: %s", projectRoot)
		}
		return projectRoot, nil
	}
	return findProjectRoot(startPath)
}

func findProjectRoot(startPath string) (string, error) {
	currentPath, err := filepath.Abs(startPath)
	if err != nil {
		return "", err
	}

	for {
		if isUnityProject(currentPath) {
			return currentPath, nil
		}
		if pathExists(filepath.Join(currentPath, ".git")) {
			return "", fmt.Errorf("unity project not found. Use --project-path option to specify the target")
		}

		parentPath := filepath.Dir(currentPath)
		if parentPath == currentPath {
			return "", fmt.Errorf("unity project not found. Use --project-path option to specify the target")
		}
		currentPath = parentPath
	}
}

func findUnityProjectRootWithin(startPath string, maxDepth int) (string, error) {
	currentPath, err := filepath.Abs(startPath)
	if err != nil {
		return "", err
	}
	if isUnityProject(currentPath) {
		return currentPath, nil
	}

	childProjects := findUnityProjectsInChildren(currentPath, maxDepth)
	if len(childProjects) == 1 {
		return childProjects[0], nil
	}
	if len(childProjects) > 1 {
		return "", fmt.Errorf("multiple Unity projects found under %s; use --project-path to choose one", currentPath)
	}
	return findProjectRoot(currentPath)
}

func findUnityProjectsInChildren(startPath string, maxDepth int) []string {
	projects := []string{}

	var scan func(string, int)
	scan = func(currentPath string, depth int) {
		if maxDepth >= 0 && depth > maxDepth {
			return
		}
		if isUnityProject(currentPath) {
			projects = append(projects, currentPath)
			return
		}

		entries, err := os.ReadDir(currentPath)
		if err != nil {
			return
		}

		for _, entry := range entries {
			if !entry.IsDir() || excludedProjectSearchDirs[entry.Name()] {
				continue
			}
			scan(filepath.Join(currentPath, entry.Name()), depth+1)
		}
	}

	scan(startPath, 0)
	sort.Strings(projects)
	return projects
}

func isUnityProject(projectPath string) bool {
	return pathExists(filepath.Join(projectPath, assetsDirectoryName)) &&
		pathExists(filepath.Join(projectPath, projectSettingsDirectory))
}

func pathExists(path string) bool {
	_, err := os.Stat(path)
	return err == nil
}

func projectLocalPath(projectRoot string) string {
	if runtime.GOOS == "windows" {
		return filepath.Join(projectRoot, projectLocalWindowsPath)
	}
	return filepath.Join(projectRoot, projectLocalUnixPath)
}

func execProjectLocal(ctx context.Context, localPath string, args []string, projectRoot string, stderr io.Writer) int {
	environment := dispatcherEnvironment(os.Environ())
	if runtime.GOOS != "windows" {
		err := syscall.Exec(localPath, append([]string{localPath}, args...), environment)
		if err != nil {
			writeError(stderr, internalError(err.Error(), projectRoot))
			return 1
		}
		return 0
	}

	command := exec.CommandContext(ctx, localPath, args...)
	command.Dir = projectRoot
	command.Stdout = os.Stdout
	command.Stderr = os.Stderr
	command.Stdin = os.Stdin
	command.Env = environment
	if err := command.Run(); err != nil {
		return 1
	}
	return 0
}

func dispatcherEnvironment(baseEnvironment []string) []string {
	prefix := dispatchercontract.Current.DispatcherVersionEnv + "="
	environment := make([]string, 0, len(baseEnvironment)+1)
	for _, entry := range baseEnvironment {
		if strings.HasPrefix(entry, prefix) {
			continue
		}
		environment = append(environment, entry)
	}
	return append(environment, prefix+dispatchercontract.Current.DispatcherVersion)
}

func writeLine(writer io.Writer, value string) {
	_, _ = fmt.Fprintln(writer, value)
}

func writeFormat(writer io.Writer, format string, args ...any) {
	_, _ = fmt.Fprintf(writer, format, args...)
}

func writeError(writer io.Writer, err cliError) {
	encoder := json.NewEncoder(writer)
	encoder.SetIndent("", "  ")
	_ = encoder.Encode(cliErrorEnvelope{
		Success: false,
		Error:   err,
	})
}
