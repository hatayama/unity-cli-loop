package cli

import (
	"context"
	"encoding/json"
	"fmt"
	"io"
	"os"
	"os/exec"
	"path/filepath"
	"runtime"
	"syscall"

	"github.com/hatayama/unity-cli-loop/Packages/src/GoCli/internal/project"
	"github.com/hatayama/unity-cli-loop/Packages/src/GoCli/internal/unity"
)

const (
	projectLocalUnixPath    = ".uloop/bin/uloop-core"
	projectLocalWindowsPath = ".uloop/bin/uloop-core.exe"
)

func RunProjectLocal(ctx context.Context, args []string, stdout io.Writer, stderr io.Writer) int {
	remainingArgs, projectPath, err := parseGlobalProjectPath(args)
	if err != nil {
		fmt.Fprintln(stderr, err.Error())
		return 1
	}

	if len(remainingArgs) == 0 || isHelpRequest(remainingArgs) {
		printHelp(stdout)
		return 0
	}
	if isVersionRequest(remainingArgs) {
		fmt.Fprintln(stdout, version)
		return 0
	}

	command := remainingArgs[0]
	commandArgs := remainingArgs[1:]

	startPath, err := os.Getwd()
	if err != nil {
		fmt.Fprintln(stderr, err.Error())
		return 1
	}

	completionTools := loadCompletionTools(startPath, projectPath)
	if handled, code := tryHandleCompletionRequest(remainingArgs, completionTools, stdout, stderr); handled {
		return code
	}
	if handled, code := tryHandleUpdateRequest(ctx, remainingArgs, stdout, stderr); handled {
		return code
	}
	if handled, code := tryHandleLaunchRequest(ctx, remainingArgs, startPath, projectPath, stdout, stderr); handled {
		return code
	}
	if handled, code := tryHandleSkillsRequest(remainingArgs, startPath, projectPath, stdout, stderr); handled {
		return code
	}

	connection, err := project.ResolveConnection(startPath, projectPath)
	if err != nil {
		fmt.Fprintln(stderr, err.Error())
		return 1
	}

	cache, err := loadTools(connection.ProjectRoot)
	if err != nil {
		fmt.Fprintln(stderr, err.Error())
		return 1
	}

	switch command {
	case "list":
		return runList(ctx, connection, stdout, stderr)
	case "sync":
		return runSync(ctx, connection, stdout, stderr)
	case "focus-window":
		return runFocusWindow(ctx, connection.ProjectRoot, stdout, stderr)
	case "fix":
		return runFix(connection.ProjectRoot, stdout, stderr)
	default:
		tool, ok := findTool(cache, command)
		if !ok {
			fmt.Fprintf(stderr, "Unknown command: %s\n", command)
			return 1
		}

		params, nestedProjectPath, err := buildToolParams(commandArgs, tool)
		if err != nil {
			fmt.Fprintln(stderr, err.Error())
			return 1
		}
		if nestedProjectPath != "" && nestedProjectPath != connection.ProjectRoot {
			fmt.Fprintln(stderr, "--project-path must be passed before the command in the native CLI")
			return 1
		}
		return runTool(ctx, connection, command, params, stdout, stderr)
	}
}

func RunLauncher(ctx context.Context, args []string, stdout io.Writer, stderr io.Writer) int {
	if isVersionRequest(args) {
		fmt.Fprintln(stdout, version)
		return 0
	}
	if len(args) == 0 || isHelpRequest(args) {
		printLauncherHelp(stdout)
		return 0
	}
	if handled, code := tryHandleCompletionRequest(args, loadDefaultTools(), stdout, stderr); handled {
		return code
	}
	if handled, code := tryHandleUpdateRequest(ctx, args, stdout, stderr); handled {
		return code
	}

	startPath, err := os.Getwd()
	if err != nil {
		fmt.Fprintln(stderr, err.Error())
		return 1
	}

	remainingArgs, explicitProjectPath, err := parseGlobalProjectPath(args)
	if err != nil {
		fmt.Fprintln(stderr, err.Error())
		return 1
	}
	if handled, code := tryHandleLaunchRequest(ctx, remainingArgs, startPath, explicitProjectPath, stdout, stderr); handled {
		return code
	}
	if handled, code := tryHandleSkillsRequest(remainingArgs, startPath, explicitProjectPath, stdout, stderr); handled {
		return code
	}

	projectRoot, err := resolveLauncherProjectRoot(startPath, explicitProjectPath)
	if err != nil {
		fmt.Fprintln(stderr, err.Error())
		return 1
	}

	localPath := filepath.Join(projectRoot, projectLocalUnixPath)
	if runtime.GOOS == "windows" {
		localPath = filepath.Join(projectRoot, projectLocalWindowsPath)
	}
	if _, err := os.Stat(localPath); err != nil {
		fmt.Fprintf(stderr, "Project-local uloop-core CLI was not found at %s\n", localPath)
		return 1
	}

	forwardedArgs := append([]string{}, remainingArgs...)
	if explicitProjectPath != "" {
		forwardedArgs = append(forwardedArgs, "--project-path", projectRoot)
	}
	return execProjectLocal(ctx, localPath, forwardedArgs, projectRoot, stderr)
}

func runTool(ctx context.Context, connection project.Connection, command string, params map[string]any, stdout io.Writer, stderr io.Writer) int {
	if shouldWaitForCompileDomainReload(command, params) {
		return runCompileWithDomainReloadWait(ctx, connection, params, stdout, stderr)
	}

	spinner := newToolSpinner(stderr, command)
	result, err := unity.NewClient(connection).SendWithProgress(ctx, command, params, func(string) {
		spinner.Update(fmt.Sprintf("Executing %s...", command))
	})
	spinner.Stop()
	if err != nil {
		fmt.Fprintln(stderr, err.Error())
		return 1
	}
	writeJSON(stdout, result)
	return 0
}

func runCompileWithDomainReloadWait(ctx context.Context, connection project.Connection, params map[string]any, stdout io.Writer, stderr io.Writer) int {
	requestID, err := ensureCompileRequestID(params)
	if err != nil {
		fmt.Fprintln(stderr, err.Error())
		return 1
	}

	spinner := newToolSpinner(stderr, compileCommandName)
	outcome, err := unity.NewClient(connection).SendWithProgressOutcome(ctx, compileCommandName, params, func(string) {
		spinner.Update("Executing compile...")
	})
	if err != nil && shouldWaitForCompileResult(err, outcome) {
		spinner.Update("Connection lost during compile. Waiting for result file...")
	}
	if !shouldWaitForCompileResult(err, outcome) {
		spinner.Stop()
		fmt.Fprintln(stderr, err.Error())
		return 1
	}

	spinner.Update("Waiting for domain reload to complete...")
	result, completed, waitErr := waitForCompileCompletion(ctx, compileCompletionOptions{
		projectRoot:  connection.ProjectRoot,
		requestID:    requestID,
		timeout:      compileWaitTimeout,
		pollInterval: compileWaitPollInterval,
		lockGrace:    compileLockGracePeriod,
	})
	spinner.Stop()
	if waitErr != nil {
		fmt.Fprintln(stderr, waitErr.Error())
		return 1
	}
	if !completed {
		fmt.Fprintln(stderr, "Compile wait timed out after 90000ms. Run 'uloop fix' and retry.")
		return 1
	}
	writeJSON(stdout, result)
	return 0
}

func runList(ctx context.Context, connection project.Connection, stdout io.Writer, stderr io.Writer) int {
	spinner := newToolSpinner(stderr, "list")
	result, err := unity.NewClient(connection).SendWithProgress(ctx, "get-tool-details", map[string]any{}, func(string) {
		spinner.Update("Fetching tool list...")
	})
	spinner.Stop()
	if err != nil {
		fmt.Fprintln(stderr, err.Error())
		return 1
	}
	writeJSON(stdout, result)
	return 0
}

func runSync(ctx context.Context, connection project.Connection, stdout io.Writer, stderr io.Writer) int {
	spinner := newToolSpinner(stderr, "sync")
	result, err := unity.NewClient(connection).SendWithProgress(ctx, "get-tool-details", map[string]any{}, func(string) {
		spinner.Update("Syncing tools...")
	})
	spinner.Stop()
	if err != nil {
		fmt.Fprintln(stderr, err.Error())
		return 1
	}

	cachePath := filepath.Join(connection.ProjectRoot, cacheDirectoryName, cacheFileName)
	if err := os.MkdirAll(filepath.Dir(cachePath), 0o755); err != nil {
		fmt.Fprintln(stderr, err.Error())
		return 1
	}
	if err := os.WriteFile(cachePath, result, 0o644); err != nil {
		fmt.Fprintln(stderr, err.Error())
		return 1
	}
	fmt.Fprintf(stdout, "Tools synced to %s\n", cachePath)
	return 0
}

func writeJSON(stdout io.Writer, result json.RawMessage) {
	var pretty any
	if json.Unmarshal(result, &pretty) != nil {
		fmt.Fprintln(stdout, string(result))
		return
	}
	encoder := json.NewEncoder(stdout)
	encoder.SetIndent("", "  ")
	_ = encoder.Encode(pretty)
}

func resolveLauncherProjectRoot(startPath string, explicitProjectPath string) (string, error) {
	if explicitProjectPath != "" {
		projectRoot, err := filepath.Abs(explicitProjectPath)
		if err != nil {
			return "", err
		}
		if !project.IsUnityProject(projectRoot) {
			return "", fmt.Errorf("--project-path does not point to a Unity project: %s", projectRoot)
		}
		return projectRoot, nil
	}
	return project.FindProjectRoot(startPath)
}

func execProjectLocal(ctx context.Context, localPath string, args []string, projectRoot string, stderr io.Writer) int {
	if runtime.GOOS != "windows" {
		err := syscall.Exec(localPath, append([]string{localPath}, args...), os.Environ())
		if err != nil {
			fmt.Fprintln(stderr, err.Error())
			return 1
		}
		return 0
	}

	command := exec.CommandContext(ctx, localPath, args...)
	command.Dir = projectRoot
	command.Stdout = os.Stdout
	command.Stderr = os.Stderr
	command.Stdin = os.Stdin
	if err := command.Run(); err != nil {
		return 1
	}
	return 0
}

func isVersionRequest(args []string) bool {
	return len(args) == 1 && (args[0] == "--version" || args[0] == "-v")
}

func isHelpRequest(args []string) bool {
	return len(args) == 1 && (args[0] == "--help" || args[0] == "-h")
}

func printHelp(stdout io.Writer) {
	fmt.Fprintf(stdout, "uloop %s\n\nUsage:\n  uloop <command> [options]\n\n", version)
	fmt.Fprintln(stdout, "Native Go CLI preview. Dynamic Unity tool commands are loaded from .uloop/tools.json.")
}

func printLauncherHelp(stdout io.Writer) {
	fmt.Fprintf(stdout, "uloop %s\n\nUsage:\n  uloop <command> [options]\n\n", version)
	fmt.Fprintln(stdout, "Native Go dispatcher preview. Dispatches to the project-local uloop-core binary.")
}

func loadCompletionTools(startPath string, projectPath string) toolsCache {
	connection, err := project.ResolveConnection(startPath, projectPath)
	if err != nil {
		return loadDefaultTools()
	}
	cache, err := loadTools(connection.ProjectRoot)
	if err != nil {
		return loadDefaultTools()
	}
	return cache
}
