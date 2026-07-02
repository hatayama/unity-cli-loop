package projectrunner

import (
	"context"
	"io"

	"github.com/hatayama/unity-cli-loop/cli/internal/clicore"
	"github.com/hatayama/unity-cli-loop/cli/internal/project"
	"github.com/hatayama/unity-cli-loop/cli/internal/unityipc"
)

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
		clicore.IsToolDisabledByToolSettings(command, clicore.LoadDisabledTools(connection.ProjectRoot)) {
		clicore.WriteErrorEnvelope(stderr, nativeToolDisabledError(connection.ProjectRoot, command))
		return 1
	}
	switch command {
	case "list":
		return runList(ctx, connection, stdout, stderr)
	case "sync":
		return runSync(ctx, connection, stdout, stderr)
	case "focus-window":
		return clicore.RunFocusWindow(ctx, connection.ProjectRoot, stdout, stderr)
	case clicore.PausePointWaitCommandName:
		return runWaitForPausePointCommand(ctx, connection, commandArgs, stdout, stderr)
	case clicore.PausePointStatusUserCommandName:
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
	tool, cache, ok, err := clicore.FindToolForCommand(connection.ProjectRoot, command)
	if err != nil {
		clicore.WriteClassifiedError(stderr, err, clicore.ErrorContext{ProjectRoot: connection.ProjectRoot, Command: command})
		return 1
	}
	if !ok {
		clicore.WriteErrorEnvelope(stderr, clicore.UnknownCommandError(command, cache, clicore.ErrorContext{
			ProjectRoot: connection.ProjectRoot,
			Command:     command,
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
		clicore.WriteErrorEnvelope(stderr, (&clicore.ArgumentError{
			Message:      "--project-path must target the same Unity project for this command",
			Option:       "--project-path",
			ExpectedType: "path",
			Command:      command,
			NextActions:  []string{"Use one `--project-path <path>` value for the target Unity project."},
		}).ToCLIError(clicore.ErrorContext{ProjectRoot: connection.ProjectRoot, Command: command}))
		return 1
	}
	return runTool(ctx, connection, command, params, stdout, stderr)
}

func prepareDynamicToolParams(
	command string,
	commandArgs []string,
	tool clicore.ToolDefinition,
	connection unityipc.Connection,
	startPath string,
	stderr io.Writer,
) (map[string]any, string, bool) {
	commandArgs, dynamicCodeFilePath, err := extractDynamicCodeFileFlag(command, commandArgs)
	if err != nil {
		clicore.WriteClassifiedError(stderr, err, clicore.ErrorContext{
			ProjectRoot: connection.ProjectRoot,
			Command:     command,
		})
		return nil, "", false
	}

	params, nestedProjectPath, err := buildToolParams(commandArgs, tool)
	if err != nil {
		clicore.WriteClassifiedError(stderr, err, clicore.ErrorContext{
			ProjectRoot: connection.ProjectRoot,
			Command:     command,
		})
		return nil, "", false
	}
	if err := applyDynamicCodeFileParam(params, dynamicCodeFilePath); err != nil {
		clicore.WriteClassifiedError(stderr, err, clicore.ErrorContext{
			ProjectRoot: connection.ProjectRoot,
			Command:     command,
		})
		return nil, "", false
	}
	if nestedProjectPath == "" {
		return params, "", true
	}
	nestedConnection, err := project.ResolveConnection(startPath, nestedProjectPath)
	if err != nil {
		clicore.WriteClassifiedError(stderr, err, clicore.ErrorContext{
			ProjectRoot: connection.ProjectRoot,
			Command:     command,
		})
		return nil, "", false
	}
	return params, nestedConnection.ProjectRoot, true
}
