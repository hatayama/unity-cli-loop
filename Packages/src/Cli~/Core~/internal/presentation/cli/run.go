package cli

import (
	"context"
	"encoding/json"
	"fmt"
	"io"
	"os"
	"path/filepath"

	corecontract "github.com/hatayama/unity-cli-loop/Packages/src/Cli/Core"
	"github.com/hatayama/unity-cli-loop/Packages/src/Cli/Core/internal/adapters/unity"
	"github.com/hatayama/unity-cli-loop/Packages/src/Cli/Core/internal/application"
	"github.com/hatayama/unity-cli-loop/Packages/src/Cli/Shared/adapters/project"
	"github.com/hatayama/unity-cli-loop/Packages/src/Cli/Shared/domain"
)

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
	if isRequiredDispatcherVersionRequest(remainingArgs) {
		writeLine(stdout, corecontract.Current.MinimumRequiredDispatcherVersion)
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
	if !isDispatcherCompatible(os.Getenv(corecontract.Current.DispatcherVersionEnv)) {
		writeErrorEnvelope(stderr, dispatcherUpdateRequiredError(
			os.Getenv(corecontract.Current.DispatcherVersionEnv),
			corecontract.Current.MinimumRequiredDispatcherVersion,
			command))
		return 1
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

func runTool(ctx context.Context, connection domain.Connection, command string, params map[string]any, stdout io.Writer, stderr io.Writer) int {
	if shouldWaitForCompileDomainReload(command, params) {
		return runCompileWithDomainReloadWait(ctx, connection, params, stdout, stderr)
	}

	spinner := newToolSpinner(stderr, command)
	dispatcher := application.ToolDispatcher{Bridge: unity.NewClient(connection)}
	outcome, err := dispatcher.Dispatch(ctx, application.ToolDispatchRequest{
		Command: command,
		Params:  params,
		Progress: func(string) {
			spinner.Update(fmt.Sprintf("Executing %s...", command))
		},
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

func runCompileWithDomainReloadWait(ctx context.Context, connection domain.Connection, params map[string]any, stdout io.Writer, stderr io.Writer) int {
	requestID, err := ensureCompileRequestID(params)
	if err != nil {
		writeClassifiedError(stderr, err, errorContext{
			projectRoot: connection.ProjectRoot,
			command:     compileCommandName,
		})
		return 1
	}

	spinner := newToolSpinner(stderr, compileCommandName)
	dispatcher := application.ToolDispatcher{Bridge: unity.NewClient(connection)}
	outcome, err := dispatcher.Dispatch(ctx, application.ToolDispatchRequest{
		Command: compileCommandName,
		Params:  params,
		Progress: func(string) {
			spinner.Update("Executing compile...")
		},
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

func runList(ctx context.Context, connection domain.Connection, stdout io.Writer, stderr io.Writer) int {
	spinner := newToolSpinner(stderr, "list")
	dispatcher := application.ToolDispatcher{Bridge: unity.NewClient(connection)}
	outcome, err := dispatcher.Dispatch(ctx, application.ToolDispatchRequest{
		Command: "get-tool-details",
		Params:  map[string]any{},
		Progress: func(string) {
			spinner.Update("Fetching tool list...")
		},
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

func runSync(ctx context.Context, connection domain.Connection, stdout io.Writer, stderr io.Writer) int {
	spinner := newToolSpinner(stderr, "sync")
	dispatcher := application.ToolDispatcher{Bridge: unity.NewClient(connection)}
	outcome, err := dispatcher.Dispatch(ctx, application.ToolDispatchRequest{
		Command: "get-tool-details",
		Params:  map[string]any{},
		Progress: func(string) {
			spinner.Update("Syncing tools...")
		},
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
