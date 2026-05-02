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
	"strings"
	"syscall"

	"github.com/hatayama/unity-cli-loop/Packages/src/GoCli/internal/project"
	"github.com/hatayama/unity-cli-loop/Packages/src/GoCli/internal/unity"
)

const (
	projectLocalUnixPath    = ".uloop/bin/uloop-core"
	projectLocalWindowsPath = ".uloop/bin/uloop-core.exe"
)

type commandHelpEntry struct {
	name        string
	description string
}

var nativeCommandHelpEntries = []commandHelpEntry{
	{name: "launch", description: "Open this Unity project with the matching Editor version"},
	{name: "list", description: "Show Unity tools currently exposed by the Editor"},
	{name: "sync", description: "Refresh .uloop/tools.json from the running Editor"},
	{name: "focus-window", description: "Bring the Unity Editor window to the foreground"},
	{name: "fix", description: "Remove stale uloop lock files after an interrupted run"},
	{name: "skills", description: "List, install, or uninstall agent skills"},
	{name: "completion", description: "Print or install shell completion"},
	{name: "update", description: "Update the global uloop launcher binary"},
}

func RunProjectLocal(ctx context.Context, args []string, stdout io.Writer, stderr io.Writer) int {
	remainingArgs, projectPath, err := parseGlobalProjectPath(args)
	if err != nil {
		writeClassifiedError(stderr, err, errorContext{})
		return 1
	}

	if len(remainingArgs) == 0 || isHelpRequest(remainingArgs) {
		printHelp(stdout)
		return 0
	}
	if isVersionRequest(remainingArgs) {
		writeLine(stdout, version)
		return 0
	}

	command := remainingArgs[0]
	commandArgs := remainingArgs[1:]

	startPath, err := os.Getwd()
	if err != nil {
		writeClassifiedError(stderr, err, errorContext{command: command})
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
		writeClassifiedError(stderr, err, errorContext{command: command})
		return 1
	}

	cache, err := loadTools(connection.ProjectRoot)
	if err != nil {
		writeClassifiedError(stderr, err, errorContext{projectRoot: connection.ProjectRoot, command: command})
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
			writeErrorEnvelope(stderr, unknownCommandError(command, cache, errorContext{
				projectRoot: connection.ProjectRoot,
				command:     command,
			}))
			return 1
		}

		params, nestedProjectPath, err := buildToolParams(commandArgs, tool)
		if err != nil {
			writeClassifiedError(stderr, err, errorContext{
				projectRoot: connection.ProjectRoot,
				command:     command,
			})
			return 1
		}
		if nestedProjectPath != "" && nestedProjectPath != connection.ProjectRoot {
			writeErrorEnvelope(stderr, (&argumentError{
				message:      "--project-path must be passed before the command in the native CLI",
				option:       "--project-path",
				expectedType: "path",
				command:      command,
				nextActions:  []string{"Move `--project-path <path>` before the command name."},
			}).toCLIError(errorContext{projectRoot: connection.ProjectRoot, command: command}))
			return 1
		}
		return runTool(ctx, connection, command, params, stdout, stderr)
	}
}

func RunLauncher(ctx context.Context, args []string, stdout io.Writer, stderr io.Writer) int {
	if isVersionRequest(args) {
		writeLine(stdout, version)
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
		writeClassifiedError(stderr, err, errorContext{})
		return 1
	}

	remainingArgs, explicitProjectPath, err := parseGlobalProjectPath(args)
	if err != nil {
		writeClassifiedError(stderr, err, errorContext{})
		return 1
	}
	if len(remainingArgs) == 0 || isHelpRequest(remainingArgs) {
		printLauncherHelpForResolvedProject(stdout, startPath, explicitProjectPath)
		return 0
	}
	if isVersionRequest(remainingArgs) {
		writeLine(stdout, version)
		return 0
	}
	if handled, code := tryHandleLaunchRequest(ctx, remainingArgs, startPath, explicitProjectPath, stdout, stderr); handled {
		return code
	}
	if handled, code := tryHandleSkillsRequest(remainingArgs, startPath, explicitProjectPath, stdout, stderr); handled {
		return code
	}

	projectRoot, err := resolveLauncherProjectRoot(startPath, explicitProjectPath)
	if err != nil {
		writeClassifiedError(stderr, err, errorContext{})
		return 1
	}

	localPath := filepath.Join(projectRoot, projectLocalUnixPath)
	if runtime.GOOS == "windows" {
		localPath = filepath.Join(projectRoot, projectLocalWindowsPath)
	}
	if _, err := os.Stat(localPath); err != nil {
		command := ""
		if len(remainingArgs) > 0 {
			command = remainingArgs[0]
		}
		writeErrorEnvelope(stderr, projectLocalCLIMissingError(localPath, projectRoot, command))
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
	outcome, err := unity.NewClient(connection).SendWithProgressOutcome(ctx, command, params, func(string) {
		spinner.Update(fmt.Sprintf("Executing %s...", command))
	})
	spinner.Stop()
	if err != nil {
		writeToolFailure(stderr, err, outcome, errorContext{
			projectRoot: connection.ProjectRoot,
			command:     command,
		})
		return 1
	}
	writeJSON(stdout, outcome.Result)
	return 0
}

func runCompileWithDomainReloadWait(ctx context.Context, connection project.Connection, params map[string]any, stdout io.Writer, stderr io.Writer) int {
	requestID, err := ensureCompileRequestID(params)
	if err != nil {
		writeClassifiedError(stderr, err, errorContext{
			projectRoot: connection.ProjectRoot,
			command:     compileCommandName,
		})
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
		writeToolFailure(stderr, err, outcome, errorContext{
			projectRoot: connection.ProjectRoot,
			command:     compileCommandName,
		})
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
		writeClassifiedError(stderr, waitErr, errorContext{
			projectRoot: connection.ProjectRoot,
			command:     compileCommandName,
		})
		return 1
	}
	if !completed {
		writeErrorEnvelope(stderr, compileWaitTimeoutError(connection.ProjectRoot))
		return 1
	}
	writeJSON(stdout, result)
	return 0
}

func runList(ctx context.Context, connection project.Connection, stdout io.Writer, stderr io.Writer) int {
	spinner := newToolSpinner(stderr, "list")
	outcome, err := unity.NewClient(connection).SendWithProgressOutcome(ctx, "get-tool-details", map[string]any{}, func(string) {
		spinner.Update("Fetching tool list...")
	})
	spinner.Stop()
	if err != nil {
		writeToolFailure(stderr, err, outcome, errorContext{
			projectRoot: connection.ProjectRoot,
			command:     "list",
		})
		return 1
	}
	writeJSON(stdout, outcome.Result)
	return 0
}

func runSync(ctx context.Context, connection project.Connection, stdout io.Writer, stderr io.Writer) int {
	spinner := newToolSpinner(stderr, "sync")
	outcome, err := unity.NewClient(connection).SendWithProgressOutcome(ctx, "get-tool-details", map[string]any{}, func(string) {
		spinner.Update("Syncing tools...")
	})
	spinner.Stop()
	if err != nil {
		writeToolFailure(stderr, err, outcome, errorContext{
			projectRoot: connection.ProjectRoot,
			command:     "sync",
		})
		return 1
	}

	cachePath := filepath.Join(connection.ProjectRoot, cacheDirectoryName, cacheFileName)
	if err := os.MkdirAll(filepath.Dir(cachePath), 0o755); err != nil {
		writeClassifiedError(stderr, err, errorContext{projectRoot: connection.ProjectRoot, command: "sync"})
		return 1
	}
	if err := os.WriteFile(cachePath, outcome.Result, 0o644); err != nil {
		writeClassifiedError(stderr, err, errorContext{projectRoot: connection.ProjectRoot, command: "sync"})
		return 1
	}
	writeFormat(stdout, "Tools synced to %s\n", cachePath)
	return 0
}

func writeJSON(stdout io.Writer, result json.RawMessage) {
	var pretty any
	if json.Unmarshal(result, &pretty) != nil {
		writeLine(stdout, string(result))
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
			writeClassifiedError(stderr, err, errorContext{projectRoot: projectRoot})
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
	printMainHelp(
		stdout,
		"Project-local CLI. Runs native uloop commands and dispatches live Unity tool commands.",
		toolsCache{},
		false)
}

func printLauncherHelp(stdout io.Writer) {
	printMainHelp(
		stdout,
		"Global dispatcher. Finds the Unity project, then dispatches to the project-local uloop-core binary.",
		toolsCache{},
		false)
}

func printLauncherHelpForResolvedProject(stdout io.Writer, startPath string, explicitProjectPath string) {
	projectRoot, err := resolveLauncherProjectRoot(startPath, explicitProjectPath)
	if err != nil {
		printLauncherHelp(stdout)
		return
	}

	cache, ok := loadCachedTools(projectRoot)
	printMainHelp(
		stdout,
		"Global dispatcher. Finds the Unity project, then dispatches to the project-local uloop-core binary.",
		cache,
		ok)
}

func printMainHelp(stdout io.Writer, description string, cache toolsCache, hasProjectToolCache bool) {
	writeFormat(stdout, "uloop %s\n\n", version)
	writeLine(stdout, "Usage:")
	writeLine(stdout, "  uloop <command> [options]")
	writeLine(stdout, "")
	writeLine(stdout, description)
	writeLine(stdout, "")
	printNativeCommandHelp(stdout)
	writeLine(stdout, "")
	printGlobalOptionsHelp(stdout)
	writeLine(stdout, "")
	printUnityToolCommandHelp(stdout, cache, hasProjectToolCache)
	writeLine(stdout, "")
	writeLine(stdout, "More:")
	writeLine(stdout, "  uloop list                                  Show the live Unity tool list")
	writeLine(stdout, "  uloop --project-path /path/to/project list  Show tools for another Unity project")
	writeLine(stdout, "  uloop <command> --help                      Show help for native commands that support it")
	writeLine(stdout, "  uloop completion --list-commands            Print command names for completion")
	writeLine(stdout, "  uloop completion --list-options <command>   Print options for a Unity tool command")
}

func printNativeCommandHelp(stdout io.Writer) {
	writeLine(stdout, "Native commands:")
	for _, entry := range nativeCommandHelpEntries {
		writeFormat(stdout, "  %-14s %s\n", entry.name, entry.description)
	}
}

func printGlobalOptionsHelp(stdout io.Writer) {
	writeLine(stdout, "Global options:")
	writeLine(stdout, "  --project-path <path>   Run against a Unity project outside the current directory")
}

func printUnityToolCommandHelp(stdout io.Writer, cache toolsCache, hasProjectToolCache bool) {
	if !hasProjectToolCache {
		writeLine(stdout, "Unity tool commands are project-specific.")
		writeLine(stdout, "  Run `uloop list` inside a Unity project to show the live tool list.")
		writeLine(stdout, "  Run `uloop sync` after the Editor tool set changes to refresh cached commands.")
		return
	}

	writeLine(stdout, "Unity tool commands from this project's cache:")
	if len(cache.Tools) == 0 {
		writeLine(stdout, "  No cached Unity tools found. Run `uloop sync` while Unity is running.")
		return
	}

	for _, tool := range cache.Tools {
		if isNativeCommandName(tool.Name) {
			continue
		}
		writeFormat(stdout, "  %-22s %s\n", tool.Name, firstHelpLine(tool.Description))
	}
	writeLine(stdout, "  Run `uloop sync` after the Editor tool set changes to refresh this list.")
}

func isNativeCommandName(name string) bool {
	for _, entry := range nativeCommandHelpEntries {
		if entry.name == name {
			return true
		}
	}
	return false
}

func firstHelpLine(description string) string {
	for _, line := range strings.Split(description, "\n") {
		trimmed := strings.TrimSpace(line)
		if trimmed != "" {
			return trimmed
		}
	}
	return ""
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
