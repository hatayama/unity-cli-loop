package cli

import (
	"context"
	"io"
	"strings"

	"github.com/hatayama/unity-cli-loop/cli/internal/project"
	"github.com/hatayama/unity-cli-loop/cli/internal/unityipc"
)

func tryHandleGlobalInfoRequest(args []string, projectPath string, stdout io.Writer) (bool, int) {
	if len(args) == 0 || isHelpRequest(args) {
		printHelpForResolvedProject(stdout, projectPath)
		return true, 0
	}
	if isVersionJSONRequest(args) {
		writeVersionJSON(stdout)
		return true, 0
	}
	if isVersionRequest(args) {
		writeLine(stdout, version)
		return true, 0
	}
	return false, 0
}

func tryHandlePreConnectionRequest(
	ctx context.Context,
	remainingArgs []string,
	command string,
	commandArgs []string,
	startPath string,
	projectPath string,
	stdout io.Writer,
	stderr io.Writer,
) (bool, int) {
	if shouldHandleCompletionRequest(remainingArgs) {
		completionTools := loadCompletionTools(startPath, projectPath)
		if handled, code := tryHandleCompletionRequest(remainingArgs, completionTools, stdout, stderr); handled {
			return true, code
		}
	}
	if isUnknownLeadingOption(command) {
		writeClassifiedError(stderr, &argumentError{
			message:     "Unknown global option: " + command,
			option:      command,
			nextActions: []string{"Run `uloop --help` to inspect supported global options."},
		}, errorContext{})
		return true, 1
	}
	if handled, code := tryHandleUpdateRequest(ctx, remainingArgs, stdout, stderr); handled {
		return true, code
	}
	if handled, code := tryHandleInstallRequest(ctx, remainingArgs, stdout, stderr); handled {
		return true, code
	}
	if handled, code := tryHandleUninstallRequest(ctx, remainingArgs, stdout, stderr); handled {
		return true, code
	}
	if handled, code := tryHandleLaunchRequest(ctx, remainingArgs, startPath, projectPath, stdout, stderr); handled {
		return true, code
	}
	if handled, code := tryHandleSkillsRequest(remainingArgs, startPath, projectPath, stdout, stderr); handled {
		return true, code
	}
	if containsHelpRequest(commandArgs) {
		if handled, code := tryHandleCommandHelp(command, startPath, projectPath, stdout, stderr); handled {
			return true, code
		}
	}
	return false, 0
}

func runResolvedProjectCommand(
	ctx context.Context,
	connection unityipc.Connection,
	command string,
	commandArgs []string,
	startPath string,
	stdout io.Writer,
	stderr io.Writer,
) int {
	if isSettingsManagedNativeToolCommand(command) &&
		isToolDisabledByToolSettings(command, loadDisabledTools(connection.ProjectRoot)) {
		writeErrorEnvelope(stderr, nativeToolDisabledError(connection.ProjectRoot, command))
		return 1
	}
	switch command {
	case "list":
		return runList(ctx, connection, stdout, stderr)
	case "sync":
		return runSync(ctx, connection, stdout, stderr)
	case "focus-window":
		return runFocusWindow(ctx, connection.ProjectRoot, stdout, stderr)
	case pausePointWaitCommandName:
		return runWaitForPausePointCommand(ctx, connection, commandArgs, stdout, stderr)
	case pausePointStatusUserCommandName:
		return runPausePointStatusCommand(ctx, connection, commandArgs, stdout, stderr)
	default:
		return runDynamicProjectTool(ctx, connection, command, commandArgs, startPath, stdout, stderr)
	}
}

func runDynamicProjectTool(
	ctx context.Context,
	connection unityipc.Connection,
	command string,
	commandArgs []string,
	startPath string,
	stdout io.Writer,
	stderr io.Writer,
) int {
	tool, cache, ok, err := findToolForCommand(connection.ProjectRoot, command)
	if err != nil {
		writeClassifiedError(stderr, err, errorContext{projectRoot: connection.ProjectRoot, command: command})
		return 1
	}
	if !ok {
		writeErrorEnvelope(stderr, unknownCommandError(command, cache, errorContext{
			projectRoot: connection.ProjectRoot,
			command:     command,
		}))
		return 1
	}

	params, nestedProjectPath, ok := prepareDynamicToolParams(
		command,
		commandArgs,
		tool,
		connection,
		startPath,
		stderr,
	)
	if !ok {
		return 1
	}
	if nestedProjectPath != "" && nestedProjectPath != connection.ProjectRoot {
		writeErrorEnvelope(stderr, (&argumentError{
			message:      "--project-path must target the same Unity project for this command",
			option:       "--project-path",
			expectedType: "path",
			command:      command,
			nextActions:  []string{"Use one `--project-path <path>` value for the target Unity project."},
		}).toCLIError(errorContext{projectRoot: connection.ProjectRoot, command: command}))
		return 1
	}
	return runTool(ctx, connection, command, params, stdout, stderr)
}

func prepareDynamicToolParams(
	command string,
	commandArgs []string,
	tool toolDefinition,
	connection unityipc.Connection,
	startPath string,
	stderr io.Writer,
) (map[string]any, string, bool) {
	commandArgs, dynamicCodeFilePath, err := extractDynamicCodeFileFlag(command, commandArgs)
	if err != nil {
		writeClassifiedError(stderr, err, errorContext{
			projectRoot: connection.ProjectRoot,
			command:     command,
		})
		return nil, "", false
	}

	params, nestedProjectPath, err := buildToolParams(commandArgs, tool)
	if err != nil {
		writeClassifiedError(stderr, err, errorContext{
			projectRoot: connection.ProjectRoot,
			command:     command,
		})
		return nil, "", false
	}
	if err := applyDynamicCodeFileParam(params, dynamicCodeFilePath); err != nil {
		writeClassifiedError(stderr, err, errorContext{
			projectRoot: connection.ProjectRoot,
			command:     command,
		})
		return nil, "", false
	}
	if nestedProjectPath == "" {
		return params, "", true
	}
	nestedConnection, err := project.ResolveConnection(startPath, nestedProjectPath)
	if err != nil {
		writeClassifiedError(stderr, err, errorContext{
			projectRoot: connection.ProjectRoot,
			command:     command,
		})
		return nil, "", false
	}
	return params, nestedConnection.ProjectRoot, true
}

func isUnknownLeadingOption(command string) bool {
	return strings.HasPrefix(command, "-")
}
