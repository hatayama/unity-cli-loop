package projectrunner

import (
	"context"
	"encoding/json"
	"errors"
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
	"github.com/hatayama/unity-cli-loop/common/vibelog"
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
	// After the three waits and not among them: hot reload causes no domain reload of its own, so
	// it runs on the plain path and only borrows the compile wait once its response asks for one.
	if command == hotReloadCommandName {
		return runHotReloadWithCompileFallback(ctx, connection, params, stdout, stderr)
	}

	result := runToolExecution(ctx, connection, command, params, stderr)
	if len(result.result) > 0 {
		clicore.WriteJSON(stdout, result.result)
	}
	return result.exitCode
}

func runToolExecution(
	ctx context.Context,
	connection unityipc.Connection,
	command string,
	params map[string]any,
	stderr io.Writer,
) toolExecutionResult {
	if command == clicore.RunTestsCommandName && shouldWaitForRunTestsDomainReload(params) {
		return runRunTestsWithDomainReloadWait(ctx, connection, params, stderr)
	}
	return runPlainTool(ctx, connection, command, params, stderr)
}

type toolExecutionResult struct {
	result   json.RawMessage
	exitCode int
}

func runPlainTool(ctx context.Context, connection unityipc.Connection, command string, params map[string]any, stderr io.Writer) toolExecutionResult {
	applyDebugTimingParams(command, params)
	correlationID := vibelog.NewCLIVibeCorrelationID()
	logPlainToolRequestSent(connection, command, params, correlationID)
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
		logPlainToolRequestFailed(connection, command, correlationID, time.Since(startedAt), outcome, err)
		clierrors.WriteToolFailure(stderr, err, outcome, clierrors.ErrorContext{
			ProjectRoot: connection.ProjectRoot,
			Command:     command,
		})
		return toolExecutionResult{exitCode: 1}
	}
	result := stripDebugTimingResult(command, outcome.Result)
	writeDebugTiming(stderr, command, time.Since(startedAt), outcome)
	exitCode := toolEnvelopeExitCode(result)
	logPlainToolResponseReceived(connection, command, correlationID, time.Since(startedAt), outcome, result, exitCode)
	return toolExecutionResult{result: result, exitCode: exitCode}
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
	return runCompileWithReattachPolicy(
		ctx, connection, params, stderr, compileWait, compileReattachAcceptsEarlierResult)
}

// runCompileOfCurrentSourcesResultWithDeps compiles for a command that goes on to rely on the
// sources as they are now (the hot-reload compile fallback, the run-tests implicit compile).
func runCompileOfCurrentSourcesResultWithDeps(
	ctx context.Context,
	connection unityipc.Connection,
	stderr io.Writer,
	compileWait compileWaitDeps,
) compileExecutionResult {
	return runCompileWithReattachPolicy(
		ctx, connection, map[string]any{}, stderr, compileWait, compileReattachRequiresCurrentSources)
}

func runCompileWithReattachPolicy(
	ctx context.Context,
	connection unityipc.Connection,
	params map[string]any,
	stderr io.Writer,
	compileWait compileWaitDeps,
	reattach compileReattachPolicy,
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

	if handled, result := tryAttachToPendingCompile(ctx, connection, params, reattach, waitTimeout, stderr, compileWait); handled {
		return result
	}

	return runFreshCompileRecoveringWithDeps(ctx, connection, params, stderr, compileWait)
}

func runFreshCompileWithDomainReloadWaitResultWithDeps(
	ctx context.Context,
	connection unityipc.Connection,
	params map[string]any,
	stderr io.Writer,
	compileWait compileWaitDeps,
) compileExecutionResult {
	result, _ := runFreshCompileAttempt(ctx, connection, params, stderr, compileWait, freshCompileAttemptOptions{})
	return result
}

// runFreshCompileAttempt sends one compile request and waits for Unity to report its result.
func runFreshCompileAttempt(
	ctx context.Context,
	connection unityipc.Connection,
	params map[string]any,
	stderr io.Writer,
	compileWait compileWaitDeps,
	options freshCompileAttemptOptions,
) (compileExecutionResult, freshCompileAttemptOutcome) {
	waitTimeout, timeoutErr := compileWaitTimeoutFromParams(params)
	if timeoutErr != nil {
		clierrors.WriteClassifiedError(stderr, timeoutErr, clierrors.ErrorContext{
			ProjectRoot: connection.ProjectRoot,
			Command:     clicore.CompileCommandName,
		})
		return compileExecutionResult{exitCode: 1}, freshCompileAttemptFinal
	}
	// Why only a resent attempt: the first one keeps the exact --timeout-seconds wait, which its log
	// entry and its timeout message report.
	if options.timeoutOverride > 0 {
		waitTimeout = options.timeoutOverride
	}

	requestID, err := prepareCompileWaitParams(params)
	if err != nil {
		clierrors.WriteClassifiedError(stderr, err, clierrors.ErrorContext{
			ProjectRoot: connection.ProjectRoot,
			Command:     clicore.CompileCommandName,
		})
		return compileExecutionResult{exitCode: 1}, freshCompileAttemptFinal
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
		return compileExecutionResult{exitCode: 1}, freshCompileAttemptFinal
	}

	spinner.Update("Waiting for domain reload to complete...")
	waitStartedAt := time.Now()
	bindCompileWaitInterimReporter(stderr, spinner, &compileWait)
	result, completed, lastStatus, waitErr := waitForCompileCompletionWithDeps(ctx, compileCompletionOptions{
		connection:     connection,
		requestID:      requestID,
		forceRecompile: compileForceRecompileEnabled(params),
		timeout:        waitTimeout,
		pollInterval:   freshWaitPollIntervalFor(compileWait),
		resendBefore:   options.resendBefore,
		// Only a dispatched send reaches this wait, so a dropped connection here came after the
		// request was sent.
		serverRestartSeen: err != nil && clierrors.IsTransportDisconnectError(err),
	}, compileWait)
	if errors.Is(waitErr, errCompileRequestMissing) {
		spinner.Stop()
		return compileExecutionResult{}, freshCompileAttemptRequestMissing
	}
	if waitErr != nil {
		spinner.Stop()
		clierrors.WriteClassifiedError(stderr, waitErr, clierrors.ErrorContext{
			ProjectRoot: connection.ProjectRoot,
			Command:     clicore.CompileCommandName,
		})
		return compileExecutionResult{exitCode: 1}, freshCompileAttemptFinal
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
		return compileExecutionResult{exitCode: 1}, freshCompileAttemptFinal
	}
	if canResendCompile(options.resendBefore) && isCompileEditorBusyRejection(result) {
		spinner.Stop()
		return compileExecutionResult{}, freshCompileAttemptEditorBusy
	}
	return completeCompileResult(ctx, connection, result, stderr, spinner, startedAt, outcome), freshCompileAttemptFinal
}

func writePostCompileWarmupWarning(stderr io.Writer, err error) {
	// Why: this warmup is a hidden optimization, so it must not turn a
	// successful compile result into a user-visible command failure.
	_, _ = fmt.Fprintf(stderr, "warning: post-compile warmup skipped: %v\n", err)
}

func runList(ctx context.Context, connection unityipc.Connection, commandArgs []string, stdout io.Writer, stderr io.Writer) int {
	options, err := parseListOptions(commandArgs)
	if err != nil {
		clierrors.WriteClassifiedError(stderr, err, clierrors.ErrorContext{
			ProjectRoot: connection.ProjectRoot,
			Command:     "list",
		})
		return 1
	}

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
	if options.namesOnly {
		if err := writeToolNames(outcome.Result, stdout); err != nil {
			clierrors.WriteClassifiedError(stderr, err, clierrors.ErrorContext{
				ProjectRoot: connection.ProjectRoot,
				Command:     "list",
			})
			return 1
		}
		return 0
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
