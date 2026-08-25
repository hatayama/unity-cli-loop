package projectrunner

import (
	"context"
	"encoding/json"
	"fmt"
	"io"
	"os"
	"path/filepath"
	"time"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
	"github.com/hatayama/unity-cli-loop/common/ui"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	"github.com/hatayama/unity-cli-loop/common/project"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

func RunProjectLocal(ctx context.Context, args []string, stdout io.Writer, stderr io.Writer) int {
	remainingArgs, projectPath, err := clicore.ParseGlobalProjectPath(args)
	if err != nil {
		clierrors.WriteClassifiedError(stderr, err, clierrors.ErrorContext{})
		return 1
	}

	if handled, code := tryHandleRunnerInfoRequest(remainingArgs, stdout); handled {
		return code
	}

	command := remainingArgs[0]
	commandArgs := remainingArgs[1:]

	if clicore.IsDispatcherOwnedCommandName(command) {
		clierrors.WriteErrorEnvelope(stderr, dispatcherOwnedCommandError(command))
		return 1
	}
	if clicore.IsUnknownLeadingOption(command) {
		clierrors.WriteClassifiedError(stderr, &clierrors.ArgumentError{
			Message:     "Unknown global option: " + command,
			Option:      command,
			NextActions: []string{"Run `uloop --help` to inspect supported global options."},
		}, clierrors.ErrorContext{})
		return 1
	}
	if clicore.ContainsHelpRequest(commandArgs) {
		if tryPrintNativeCommandHelp(command, stdout) {
			return 0
		}
		printRunnerUsage(stdout)
		return 0
	}

	startPath, err := os.Getwd()
	if err != nil {
		clierrors.WriteClassifiedError(stderr, err, clierrors.ErrorContext{Command: command})
		return 1
	}

	connection, err := project.ResolveConnection(startPath, projectPath)
	if err != nil {
		clierrors.WriteClassifiedError(stderr, err, clierrors.ErrorContext{Command: command})
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

	result := runPlainTool(ctx, connection, command, params, stderr)
	if len(result.result) > 0 {
		clicore.WriteJSON(stdout, result.result)
	}
	return result.exitCode
}

type toolExecutionResult struct {
	result   json.RawMessage
	exitCode int
}

func runPlainTool(ctx context.Context, connection unityipc.Connection, command string, params map[string]any, stderr io.Writer) toolExecutionResult {
	applyDebugTimingParams(command, params)
	startedAt := time.Now()
	spinner := clicore.NewToolSpinner(stderr, command)
	outcome, err := sendWithTransientConnectionRetry(
		ctx,
		connection,
		command,
		params,
		ui.NewSpinnerProgressFunc(spinner, fmt.Sprintf("Executing %s...", command)),
	)
	spinner.Stop()
	if err != nil {
		writeDebugTiming(stderr, command, time.Since(startedAt), outcome)
		clierrors.WriteToolFailure(stderr, err, outcome, clierrors.ErrorContext{
			ProjectRoot: connection.ProjectRoot,
			Command:     command,
		})
		return toolExecutionResult{exitCode: 1}
	}
	result := stripDebugTimingResult(command, outcome.Result)
	writeDebugTiming(stderr, command, time.Since(startedAt), outcome)
	return toolExecutionResult{result: result, exitCode: toolEnvelopeExitCode(result)}
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
		ui.NewSpinnerProgressFunc(spinner, "Executing execute-dynamic-code..."),
	)
	if err != nil {
		if shouldWaitForExecuteDynamicCodeDisconnect(err, outcome) {
			spinner.Update("Connection lost during execute-dynamic-code. Waiting for domain reload to complete...")
			if waitErr := clicore.WaitForToolReadiness(ctx, connection.ProjectRoot); waitErr != nil {
				spinner.Stop()
				clierrors.WriteClassifiedError(stderr, waitErr, clierrors.ErrorContext{
					ProjectRoot: connection.ProjectRoot,
					Command:     clicore.ExecuteDynamicCodeCommandName,
				})
				return 1
			}
		}
		spinner.Stop()
		writeDebugTiming(stderr, clicore.ExecuteDynamicCodeCommandName, time.Since(startedAt), outcome)
		clierrors.WriteToolFailure(stderr, err, outcome, clierrors.ErrorContext{
			ProjectRoot: connection.ProjectRoot,
			Command:     clicore.ExecuteDynamicCodeCommandName,
		})
		return 1
	}

	if executeDynamicCodeDomainReloadWaitRequired(outcome.Result) {
		spinner.Update("Waiting for domain reload to complete...")
		if err := clicore.WaitForToolReadiness(ctx, connection.ProjectRoot); err != nil {
			spinner.Stop()
			clierrors.WriteClassifiedError(stderr, err, clierrors.ErrorContext{
				ProjectRoot: connection.ProjectRoot,
				Command:     clicore.ExecuteDynamicCodeCommandName,
			})
			return 1
		}
	}

	spinner.Stop()
	result := stripExecuteDynamicCodeControlResult(outcome.Result)
	result = stripDebugTimingResult(clicore.ExecuteDynamicCodeCommandName, result)
	clicore.WriteJSON(stdout, result)
	writeDebugTiming(stderr, clicore.ExecuteDynamicCodeCommandName, time.Since(startedAt), outcome)
	return toolEnvelopeExitCode(result)
}

func runCompileWithDomainReloadWait(ctx context.Context, connection unityipc.Connection, params map[string]any, stdout io.Writer, stderr io.Writer) int {
	return runCompileWithDomainReloadWaitWithDeps(ctx, connection, params, stdout, stderr, defaultCompileWaitDeps())
}

func runCompileWithDomainReloadWaitWithDeps(
	ctx context.Context,
	connection unityipc.Connection,
	params map[string]any,
	stdout io.Writer,
	stderr io.Writer,
	compileWait compileWaitDeps,
) int {
	result := runCompileWithDomainReloadWaitResultWithDeps(ctx, connection, params, stderr, compileWait)
	return writeCompileExecutionResult(stdout, result)
}

// compileExecutionResult keeps compile's Unity response available to a composing command until
// that command decides its single final stdout payload.
type compileExecutionResult struct {
	result   json.RawMessage
	exitCode int
}

func writeCompileExecutionResult(stdout io.Writer, result compileExecutionResult) int {
	if len(result.result) > 0 {
		clicore.WriteJSON(stdout, result.result)
	}
	return result.exitCode
}

func runCompileWithDomainReloadWaitResultWithDeps(
	ctx context.Context,
	connection unityipc.Connection,
	params map[string]any,
	stderr io.Writer,
	compileWait compileWaitDeps,
) compileExecutionResult {
	waitTimeout, timeoutErr := compileWaitTimeoutFromParams(params)
	if timeoutErr != nil {
		clierrors.WriteClassifiedError(stderr, timeoutErr, clierrors.ErrorContext{
			ProjectRoot: connection.ProjectRoot,
			Command:     clicore.CompileCommandName,
		})
		return compileExecutionResult{exitCode: 1}
	}
	if waitTimeout > time.Duration(compileWaitTimeoutRetentionWarningSeconds)*time.Second {
		_, _ = fmt.Fprintf(
			stderr,
			"warning: --timeout-seconds exceeds the Unity-side compile result retention window (20 minutes); if the wait times out, the result may expire before a retry can recover it.\n",
		)
	}

	if handled, result := tryAttachToPendingCompile(ctx, connection, params, waitTimeout, stderr, compileWait); handled {
		return result
	}

	return runFreshCompileWithDomainReloadWaitResultWithDeps(ctx, connection, params, stderr, compileWait)
}

func runFreshCompileWithDomainReloadWaitWithDeps(
	ctx context.Context,
	connection unityipc.Connection,
	params map[string]any,
	stdout io.Writer,
	stderr io.Writer,
	compileWait compileWaitDeps,
) int {
	result := runFreshCompileWithDomainReloadWaitResultWithDeps(ctx, connection, params, stderr, compileWait)
	return writeCompileExecutionResult(stdout, result)
}

func runFreshCompileWithDomainReloadWaitResultWithDeps(
	ctx context.Context,
	connection unityipc.Connection,
	params map[string]any,
	stderr io.Writer,
	compileWait compileWaitDeps,
) compileExecutionResult {
	waitTimeout, timeoutErr := compileWaitTimeoutFromParams(params)
	if timeoutErr != nil {
		clierrors.WriteClassifiedError(stderr, timeoutErr, clierrors.ErrorContext{
			ProjectRoot: connection.ProjectRoot,
			Command:     clicore.CompileCommandName,
		})
		return compileExecutionResult{exitCode: 1}
	}

	requestID, err := prepareCompileWaitParams(params)
	if err != nil {
		clierrors.WriteClassifiedError(stderr, err, clierrors.ErrorContext{
			ProjectRoot: connection.ProjectRoot,
			Command:     clicore.CompileCommandName,
		})
		return compileExecutionResult{exitCode: 1}
	}

	logCliDebugModeResolved(connection, clicore.CompileCommandName)
	logCompileRequestPrepared(connection, params, requestID, waitTimeout)

	startedAt := time.Now()
	spinner := clicore.NewToolSpinner(stderr, clicore.CompileCommandName)
	outcome, err := compileSendOrDefault(compileWait)(
		ctx,
		connection,
		clicore.CompileCommandName,
		params,
		ui.NewSpinnerProgressFunc(spinner, "Executing compile..."),
		compileResponseTimeout,
	)
	logCompileRequestSendResult(connection, requestID, outcome, err, startedAt)
	if err != nil && shouldWaitForCompileStatus(err, outcome) {
		spinner.Update("Connection changed during compile. Waiting for Unity status...")
	}
	if !shouldWaitForCompileStatus(err, outcome) {
		spinner.Stop()
		clierrors.WriteToolFailure(stderr, err, outcome, clierrors.ErrorContext{
			ProjectRoot: connection.ProjectRoot,
			Command:     clicore.CompileCommandName,
		})
		return compileExecutionResult{exitCode: 1}
	}

	spinner.Update("Waiting for domain reload to complete...")
	waitStartedAt := time.Now()
	bindCompileWaitInterimReporter(stderr, spinner, &compileWait)
	result, completed, lastStatus, waitErr := waitForCompileCompletionWithDeps(ctx, compileCompletionOptions{
		connection:     connection,
		requestID:      requestID,
		forceRecompile: compileForceRecompileEnabled(params),
		timeout:        waitTimeout,
		pollInterval:   compileWaitPollInterval,
	}, compileWait)
	if waitErr != nil {
		spinner.Stop()
		clierrors.WriteClassifiedError(stderr, waitErr, clierrors.ErrorContext{
			ProjectRoot: connection.ProjectRoot,
			Command:     clicore.CompileCommandName,
		})
		return compileExecutionResult{exitCode: 1}
	}
	if !completed {
		spinner.Stop()
		persistCompilePendingRecordOrWarn(connection.ProjectRoot, requestID, stderr)
		clierrors.WriteErrorEnvelope(stderr, compileWaitTimeoutError(
			connection.ProjectRoot,
			waitTimeout,
			lastStatus,
			time.Since(waitStartedAt),
			compilePendingRecordLifetime-waitTimeout,
		))
		return compileExecutionResult{exitCode: 1}
	}
	return completeCompileResult(ctx, connection, result, stderr, spinner, startedAt, outcome)
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
		ui.NewSpinnerProgressFunc(spinner, "Fetching tool list..."),
	)
	spinner.Stop()
	if err != nil {
		clierrors.WriteToolFailure(stderr, err, outcome, clierrors.ErrorContext{
			ProjectRoot: connection.ProjectRoot,
			Command:     "list",
		})
		return 1
	}
	clicore.WriteJSON(stdout, formatToolListResult(outcome.Result, connection.ProjectRoot))
	return 0
}

func runSync(ctx context.Context, connection unityipc.Connection, stdout io.Writer, stderr io.Writer) int {
	spinner := clicore.NewToolSpinner(stderr, "sync")
	outcome, err := sendWithTransientConnectionRetry(
		ctx,
		connection,
		"get-tool-details",
		map[string]any{},
		ui.NewSpinnerProgressFunc(spinner, "Syncing tools..."),
	)
	spinner.Stop()
	if err != nil {
		clierrors.WriteToolFailure(stderr, err, outcome, clierrors.ErrorContext{
			ProjectRoot: connection.ProjectRoot,
			Command:     "sync",
		})
		return 1
	}

	cachePath := filepath.Join(connection.ProjectRoot, clicore.CacheDirectoryName, clicore.CacheFileName)
	if err := os.MkdirAll(filepath.Dir(cachePath), 0o755); err != nil {
		clierrors.WriteClassifiedError(stderr, err, clierrors.ErrorContext{ProjectRoot: connection.ProjectRoot, Command: "sync"})
		return 1
	}
	if err := os.WriteFile(cachePath, outcome.Result, 0o644); err != nil {
		clierrors.WriteClassifiedError(stderr, err, clierrors.ErrorContext{ProjectRoot: connection.ProjectRoot, Command: "sync"})
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
