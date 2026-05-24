package cli

import (
	"context"
	"encoding/json"
	"fmt"
	"io"
	"os"
	"path/filepath"
	"strings"
	"time"

	"github.com/hatayama/unity-cli-loop/Packages/src/Cli/internal/project"
	"github.com/hatayama/unity-cli-loop/Packages/src/Cli/internal/unityipc"
)

func RunProjectLocal(ctx context.Context, args []string, stdout io.Writer, stderr io.Writer) int {
	remainingArgs, projectPath, err := parseGlobalProjectPath(args)
	if err != nil {
		writeClassifiedError(stderr, err, errorContext{})
		return 1
	}

	if len(remainingArgs) == 0 || isHelpRequest(remainingArgs) {
		printHelpForResolvedProject(stdout, projectPath)
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

	if shouldHandleCompletionRequest(remainingArgs) {
		completionTools := loadCompletionTools(startPath, projectPath)
		if handled, code := tryHandleCompletionRequest(remainingArgs, completionTools, stdout, stderr); handled {
			return code
		}
	}
	if isUnknownLeadingOption(command) {
		writeClassifiedError(stderr, &argumentError{
			message:     "Unknown global option: " + command,
			option:      command,
			nextActions: []string{"Run `uloop --help` to inspect supported global options."},
		}, errorContext{})
		return 1
	}
	if handled, code := tryHandleUpdateRequest(ctx, remainingArgs, stdout, stderr); handled {
		return code
	}
	if handled, code := tryHandleInstallRequest(ctx, remainingArgs, stdout, stderr); handled {
		return code
	}
	if handled, code := tryHandleUninstallRequest(ctx, remainingArgs, stdout, stderr); handled {
		return code
	}
	if handled, code := tryHandleLaunchRequest(ctx, remainingArgs, startPath, projectPath, stdout, stderr); handled {
		return code
	}
	if handled, code := tryHandleSkillsRequest(remainingArgs, startPath, projectPath, stdout, stderr); handled {
		return code
	}
	if containsHelpRequest(commandArgs) {
		if handled, code := tryHandleCommandHelp(command, startPath, projectPath, stdout, stderr); handled {
			return code
		}
	}

	connection, err := project.ResolveConnection(startPath, projectPath)
	if err != nil {
		writeClassifiedError(stderr, err, errorContext{command: command})
		return 1
	}
	switch command {
	case "list":
		return runList(ctx, connection, stdout, stderr)
	case "sync":
		return runSync(ctx, connection, stdout, stderr)
	case "focus-window":
		return runFocusWindow(ctx, connection.ProjectRoot, stdout, stderr)
	default:
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
}

func isUnknownLeadingOption(command string) bool {
	return strings.HasPrefix(command, "-")
}

func runTool(ctx context.Context, connection unityipc.Connection, command string, params map[string]any, stdout io.Writer, stderr io.Writer) int {
	if shouldWaitForCompileDomainReload(command, params) {
		return runCompileWithDomainReloadWait(ctx, connection, params, stdout, stderr)
	}
	if shouldWaitForExecuteDynamicCodeDomainReload(command, params) {
		return runExecuteDynamicCodeWithDomainReloadWait(ctx, connection, params, stdout, stderr)
	}

	applyDebugTimingParams(command, params)
	startedAt := time.Now()
	spinner := newToolSpinner(stderr, command)
	outcome, err := sendWithTransientConnectionRetry(
		ctx,
		connection,
		command,
		params,
		func(string) {
			spinner.Update(fmt.Sprintf("Executing %s...", command))
		},
	)
	spinner.Stop()
	if err != nil {
		writeDebugTiming(stderr, command, time.Since(startedAt), outcome)
		writeToolFailure(stderr, err, outcome, errorContext{
			projectRoot: connection.ProjectRoot,
			command:     command,
		})
		return 1
	}
	writeJSON(stdout, stripDebugTimingResult(command, outcome.Result))
	writeDebugTiming(stderr, command, time.Since(startedAt), outcome)
	return 0
}

func runExecuteDynamicCodeWithDomainReloadWait(ctx context.Context, connection unityipc.Connection, params map[string]any, stdout io.Writer, stderr io.Writer) int {
	applyDebugTimingParams(executeDynamicCodeCommandName, params)
	startedAt := time.Now()
	spinner := newToolSpinner(stderr, executeDynamicCodeCommandName)
	outcome, err := sendWithTransientConnectionRetry(
		ctx,
		connection,
		executeDynamicCodeCommandName,
		params,
		func(string) {
			spinner.Update("Executing execute-dynamic-code...")
		},
	)
	if err != nil {
		if shouldWaitForExecuteDynamicCodeDisconnect(err, outcome) {
			spinner.Update("Connection lost during execute-dynamic-code. Waiting for domain reload to complete...")
			if waitErr := waitForToolReadiness(ctx, connection.ProjectRoot); waitErr != nil {
				spinner.Stop()
				writeClassifiedError(stderr, waitErr, errorContext{
					projectRoot: connection.ProjectRoot,
					command:     executeDynamicCodeCommandName,
				})
				return 1
			}
		}
		spinner.Stop()
		writeDebugTiming(stderr, executeDynamicCodeCommandName, time.Since(startedAt), outcome)
		writeToolFailure(stderr, err, outcome, errorContext{
			projectRoot: connection.ProjectRoot,
			command:     executeDynamicCodeCommandName,
		})
		return 1
	}

	if executeDynamicCodeDomainReloadWaitRequired(outcome.Result) {
		spinner.Update("Waiting for domain reload to complete...")
		if err := waitForToolReadiness(ctx, connection.ProjectRoot); err != nil {
			spinner.Stop()
			writeClassifiedError(stderr, err, errorContext{
				projectRoot: connection.ProjectRoot,
				command:     executeDynamicCodeCommandName,
			})
			return 1
		}
	}

	spinner.Stop()
	result := stripExecuteDynamicCodeControlResult(outcome.Result)
	writeJSON(stdout, stripDebugTimingResult(executeDynamicCodeCommandName, result))
	writeDebugTiming(stderr, executeDynamicCodeCommandName, time.Since(startedAt), outcome)
	return 0
}

func runCompileWithDomainReloadWait(ctx context.Context, connection unityipc.Connection, params map[string]any, stdout io.Writer, stderr io.Writer) int {
	requestID, err := prepareCompileWaitParams(params)
	if err != nil {
		writeClassifiedError(stderr, err, errorContext{
			projectRoot: connection.ProjectRoot,
			command:     compileCommandName,
		})
		return 1
	}

	startedAt := time.Now()
	spinner := newToolSpinner(stderr, compileCommandName)
	outcome, err := sendWithTransientConnectionRetry(
		ctx,
		connection,
		compileCommandName,
		params,
		func(string) {
			spinner.Update("Executing compile...")
		},
	)
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
	if waitErr != nil {
		spinner.Stop()
		writeClassifiedError(stderr, waitErr, errorContext{
			projectRoot: connection.ProjectRoot,
			command:     compileCommandName,
		})
		return 1
	}
	if !completed {
		spinner.Stop()
		writeErrorEnvelope(stderr, compileWaitTimeoutError(connection.ProjectRoot))
		return 1
	}
	if compileResultSucceeded(result) {
		spinner.Update("Warming execute-dynamic-code after compile...")
		if err := waitForToolReadiness(ctx, connection.ProjectRoot); err != nil {
			spinner.Stop()
			writePostCompileWarmupWarning(stderr, err)
		}
	}
	spinner.Stop()
	writeJSON(stdout, result)
	writeDebugTiming(stderr, compileCommandName, time.Since(startedAt), outcome)
	return 0
}

func writePostCompileWarmupWarning(stderr io.Writer, err error) {
	if err == nil {
		return
	}
	// Why: this warmup is a hidden optimization, so it must not turn a
	// successful compile result into a user-visible command failure.
	_, _ = fmt.Fprintf(stderr, "warning: post-compile warmup skipped: %v\n", err)
}

func runList(ctx context.Context, connection unityipc.Connection, stdout io.Writer, stderr io.Writer) int {
	spinner := newToolSpinner(stderr, "list")
	outcome, err := sendWithTransientConnectionRetry(
		ctx,
		connection,
		"get-tool-details",
		map[string]any{},
		func(string) {
			spinner.Update("Fetching tool list...")
		},
	)
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

func runSync(ctx context.Context, connection unityipc.Connection, stdout io.Writer, stderr io.Writer) int {
	spinner := newToolSpinner(stderr, "sync")
	outcome, err := sendWithTransientConnectionRetry(
		ctx,
		connection,
		"get-tool-details",
		map[string]any{},
		func(string) {
			spinner.Update("Syncing tools...")
		},
	)
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

type compileResultStatus struct {
	Success *bool `json:"Success"`
}

func compileResultSucceeded(result json.RawMessage) bool {
	var status compileResultStatus
	if json.Unmarshal(result, &status) != nil {
		return false
	}
	return status.Success != nil && *status.Success
}
