package projectrunner

import (
	"context"
	"encoding/json"
	"fmt"
	"io"
	"os"
	"path/filepath"
	"time"

	"github.com/hatayama/unity-cli-loop/cli/internal/clicore"
	"github.com/hatayama/unity-cli-loop/cli/internal/project"
	"github.com/hatayama/unity-cli-loop/cli/internal/unityipc"
)

func RunProjectLocal(ctx context.Context, args []string, stdout io.Writer, stderr io.Writer) int {
	remainingArgs, projectPath, err := clicore.ParseGlobalProjectPath(args)
	if err != nil {
		clicore.WriteClassifiedError(stderr, err, clicore.ErrorContext{})
		return 1
	}

	if handled, code := tryHandleRunnerInfoRequest(remainingArgs, stdout); handled {
		return code
	}

	command := remainingArgs[0]
	commandArgs := remainingArgs[1:]

	if clicore.IsDispatcherOwnedCommandName(command) || clicore.ShouldHandleCompletionRequest(remainingArgs) {
		clicore.WriteErrorEnvelope(stderr, dispatcherOwnedCommandError(command))
		return 1
	}
	if clicore.IsUnknownLeadingOption(command) {
		clicore.WriteClassifiedError(stderr, &clicore.ArgumentError{
			Message:     "Unknown global option: " + command,
			Option:      command,
			NextActions: []string{"Run `uloop --help` to inspect supported global options."},
		}, clicore.ErrorContext{})
		return 1
	}
	if clicore.ContainsHelpRequest(commandArgs) {
		printRunnerUsage(stdout)
		return 0
	}

	startPath, err := os.Getwd()
	if err != nil {
		clicore.WriteClassifiedError(stderr, err, clicore.ErrorContext{Command: command})
		return 1
	}

	connection, err := project.ResolveConnection(startPath, projectPath)
	if err != nil {
		clicore.WriteClassifiedError(stderr, err, clicore.ErrorContext{Command: command})
		return 1
	}
	return runResolvedProjectCommand(ctx, connection, command, commandArgs, startPath, stdout, stderr)
}

func runTool(ctx context.Context, connection unityipc.Connection, command string, params map[string]any, stdout io.Writer, stderr io.Writer) int {
	if shouldWaitForCompileDomainReload(command, params) {
		return runCompileWithDomainReloadWait(ctx, connection, params, stdout, stderr)
	}
	if shouldWaitForExecuteDynamicCodeDomainReload(command, params) {
		return runExecuteDynamicCodeWithDomainReloadWait(ctx, connection, params, stdout, stderr)
	}
	if shouldWaitForControlPlayModeState(command, params) {
		return runControlPlayModeWithStateWait(ctx, connection, params, stdout, stderr)
	}

	applyDebugTimingParams(command, params)
	startedAt := time.Now()
	spinner := clicore.NewToolSpinner(stderr, command)
	outcome, err := sendWithTransientConnectionRetry(
		ctx,
		connection,
		command,
		params,
		clicore.NewSpinnerProgressFunc(spinner, fmt.Sprintf("Executing %s...", command)),
	)
	spinner.Stop()
	if err != nil {
		writeDebugTiming(stderr, command, time.Since(startedAt), outcome)
		clicore.WriteToolFailure(stderr, err, outcome, clicore.ErrorContext{
			ProjectRoot: connection.ProjectRoot,
			Command:     command,
		})
		return 1
	}
	clicore.WriteJSON(stdout, stripDebugTimingResult(command, outcome.Result))
	writeDebugTiming(stderr, command, time.Since(startedAt), outcome)
	return 0
}

func runExecuteDynamicCodeWithDomainReloadWait(ctx context.Context, connection unityipc.Connection, params map[string]any, stdout io.Writer, stderr io.Writer) int {
	applyDebugTimingParams(clicore.ExecuteDynamicCodeCommandName, params)
	startedAt := time.Now()
	spinner := clicore.NewToolSpinner(stderr, clicore.ExecuteDynamicCodeCommandName)
	outcome, err := sendWithTransientConnectionRetry(
		ctx,
		connection,
		clicore.ExecuteDynamicCodeCommandName,
		params,
		clicore.NewSpinnerProgressFunc(spinner, "Executing execute-dynamic-code..."),
	)
	if err != nil {
		if shouldWaitForExecuteDynamicCodeDisconnect(err, outcome) {
			spinner.Update("Connection lost during execute-dynamic-code. Waiting for domain reload to complete...")
			if waitErr := clicore.WaitForToolReadiness(ctx, connection.ProjectRoot); waitErr != nil {
				spinner.Stop()
				clicore.WriteClassifiedError(stderr, waitErr, clicore.ErrorContext{
					ProjectRoot: connection.ProjectRoot,
					Command:     clicore.ExecuteDynamicCodeCommandName,
				})
				return 1
			}
		}
		spinner.Stop()
		writeDebugTiming(stderr, clicore.ExecuteDynamicCodeCommandName, time.Since(startedAt), outcome)
		clicore.WriteToolFailure(stderr, err, outcome, clicore.ErrorContext{
			ProjectRoot: connection.ProjectRoot,
			Command:     clicore.ExecuteDynamicCodeCommandName,
		})
		return 1
	}

	if executeDynamicCodeDomainReloadWaitRequired(outcome.Result) {
		spinner.Update("Waiting for domain reload to complete...")
		if err := clicore.WaitForToolReadiness(ctx, connection.ProjectRoot); err != nil {
			spinner.Stop()
			clicore.WriteClassifiedError(stderr, err, clicore.ErrorContext{
				ProjectRoot: connection.ProjectRoot,
				Command:     clicore.ExecuteDynamicCodeCommandName,
			})
			return 1
		}
	}

	spinner.Stop()
	result := stripExecuteDynamicCodeControlResult(outcome.Result)
	clicore.WriteJSON(stdout, stripDebugTimingResult(clicore.ExecuteDynamicCodeCommandName, result))
	writeDebugTiming(stderr, clicore.ExecuteDynamicCodeCommandName, time.Since(startedAt), outcome)
	return 0
}

func runCompileWithDomainReloadWait(ctx context.Context, connection unityipc.Connection, params map[string]any, stdout io.Writer, stderr io.Writer) int {
	requestID, err := prepareCompileWaitParams(params)
	if err != nil {
		clicore.WriteClassifiedError(stderr, err, clicore.ErrorContext{
			ProjectRoot: connection.ProjectRoot,
			Command:     clicore.CompileCommandName,
		})
		return 1
	}

	logCliDebugModeResolved(connection, clicore.CompileCommandName)
	logCompileRequestPrepared(connection, params, requestID)

	startedAt := time.Now()
	spinner := clicore.NewToolSpinner(stderr, clicore.CompileCommandName)
	outcome, err := sendWithTransientConnectionRetryAndResponseTimeout(
		ctx,
		connection,
		clicore.CompileCommandName,
		params,
		clicore.NewSpinnerProgressFunc(spinner, "Executing compile..."),
		compileResponseTimeout,
	)
	logCompileRequestSendResult(connection, requestID, outcome, err, startedAt)
	if err != nil && shouldWaitForCompileStatus(err, outcome) {
		spinner.Update("Connection changed during compile. Waiting for Unity status...")
	}
	if !shouldWaitForCompileStatus(err, outcome) {
		spinner.Stop()
		clicore.WriteToolFailure(stderr, err, outcome, clicore.ErrorContext{
			ProjectRoot: connection.ProjectRoot,
			Command:     clicore.CompileCommandName,
		})
		return 1
	}

	spinner.Update("Waiting for domain reload to complete...")
	result, completed, waitErr := waitForCompileCompletion(ctx, compileCompletionOptions{
		connection:     connection,
		requestID:      requestID,
		forceRecompile: compileForceRecompileEnabled(params),
		timeout:        compileWaitTimeout,
		pollInterval:   compileWaitPollInterval,
	})
	if waitErr != nil {
		spinner.Stop()
		clicore.WriteClassifiedError(stderr, waitErr, clicore.ErrorContext{
			ProjectRoot: connection.ProjectRoot,
			Command:     clicore.CompileCommandName,
		})
		return 1
	}
	if !completed {
		spinner.Stop()
		clicore.WriteErrorEnvelope(stderr, compileWaitTimeoutError(connection.ProjectRoot))
		return 1
	}
	switch compileResultReadinessWaitMode(result) {
	case compileReadinessWaitWarmup:
		spinner.Update("Warming execute-dynamic-code after compile...")
		if err := clicore.WaitForToolReadiness(ctx, connection.ProjectRoot); err != nil {
			spinner.Stop()
			writePostCompileWarmupWarning(stderr, err)
		}
	}
	spinner.Stop()
	clicore.WriteJSON(stdout, result)
	writeDebugTiming(stderr, clicore.CompileCommandName, time.Since(startedAt), outcome)
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
	spinner := clicore.NewToolSpinner(stderr, "list")
	outcome, err := sendWithTransientConnectionRetry(
		ctx,
		connection,
		"get-tool-details",
		map[string]any{},
		clicore.NewSpinnerProgressFunc(spinner, "Fetching tool list..."),
	)
	spinner.Stop()
	if err != nil {
		clicore.WriteToolFailure(stderr, err, outcome, clicore.ErrorContext{
			ProjectRoot: connection.ProjectRoot,
			Command:     "list",
		})
		return 1
	}
	clicore.WriteJSON(stdout, formatToolListResult(outcome.Result))
	return 0
}

func runSync(ctx context.Context, connection unityipc.Connection, stdout io.Writer, stderr io.Writer) int {
	spinner := clicore.NewToolSpinner(stderr, "sync")
	outcome, err := sendWithTransientConnectionRetry(
		ctx,
		connection,
		"get-tool-details",
		map[string]any{},
		clicore.NewSpinnerProgressFunc(spinner, "Syncing tools..."),
	)
	spinner.Stop()
	if err != nil {
		clicore.WriteToolFailure(stderr, err, outcome, clicore.ErrorContext{
			ProjectRoot: connection.ProjectRoot,
			Command:     "sync",
		})
		return 1
	}

	cachePath := filepath.Join(connection.ProjectRoot, clicore.CacheDirectoryName, clicore.CacheFileName)
	if err := os.MkdirAll(filepath.Dir(cachePath), 0o755); err != nil {
		clicore.WriteClassifiedError(stderr, err, clicore.ErrorContext{ProjectRoot: connection.ProjectRoot, Command: "sync"})
		return 1
	}
	if err := os.WriteFile(cachePath, outcome.Result, 0o644); err != nil {
		clicore.WriteClassifiedError(stderr, err, clicore.ErrorContext{ProjectRoot: connection.ProjectRoot, Command: "sync"})
		return 1
	}
	clicore.WriteFormat(stdout, "Tools synced to %s\n", cachePath)
	return 0
}

type compileResultStatus struct {
	Success *bool `json:"Success"`
}

type compileReadinessWaitMode int

const (
	compileReadinessWaitNone compileReadinessWaitMode = iota
	compileReadinessWaitWarmup
)

func compileResultReadinessWaitMode(result json.RawMessage) compileReadinessWaitMode {
	var status compileResultStatus
	if json.Unmarshal(result, &status) != nil {
		return compileReadinessWaitNone
	}
	if status.Success == nil {
		return compileReadinessWaitNone
	}
	if *status.Success {
		return compileReadinessWaitWarmup
	}
	return compileReadinessWaitNone
}
