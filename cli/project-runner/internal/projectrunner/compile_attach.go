package projectrunner

import (
	"context"
	"encoding/json"
	"fmt"
	"io"
	"time"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
	"github.com/hatayama/unity-cli-loop/common/ui"
	"github.com/hatayama/unity-cli-loop/common/vibelog"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

const (
	compileAttachProbeTimeout  = 10 * time.Second
	compileAttachProbeInterval = 1 * time.Second
	// Why 3: a single Ready&&!HasResult sample can race the completion→store window.
	compileAttachMissingResultStreak = 3
)

type attachWaitOutcome int

const (
	attachWaitCompleted attachWaitOutcome = iota
	attachWaitTimedOut
	attachWaitDisappeared
	attachWaitFailed
)

// tryAttachToPendingCompile reattaches to a previously timed-out compile when a
// pending record still exists. handled=true means the caller must return exitCode.
func tryAttachToPendingCompile(
	ctx context.Context,
	connection unityipc.Connection,
	params map[string]any,
	waitTimeout time.Duration,
	stderr io.Writer,
	deps compileWaitDeps,
) (bool, compileExecutionResult) {
	record, ok := readCompilePendingRecord(connection.ProjectRoot)
	if !ok {
		return false, compileExecutionResult{}
	}

	status, probed := probePendingCompileStatus(ctx, connection, record.RequestID, deps)
	if !probed {
		// Why keep the record: probe failures during domain reload are transient and
		// do not prove the in-flight compile is gone.
		return false, compileExecutionResult{}
	}

	// Why HasResult first: Ready is editor-wide (!compiling/!updating/!reload), not
	// scoped to record.RequestID. Only HasResult is request-specific, and results are
	// stored only after that request finishes — so a result is definitive even when
	// the editor is busy with unrelated work.
	if status.HasResult && len(status.Result) > 0 {
		if compileForceRecompileEnabled(params) {
			clearCompilePendingRecord(connection.ProjectRoot)
			return false, compileExecutionResult{}
		}
		return true, returnAttachedStoredCompileResult(ctx, connection, record, status.Result, stderr)
	}

	if !status.Ready {
		return attachWaitForPendingCompile(ctx, connection, record, params, waitTimeout, stderr, deps)
	}

	clearCompilePendingRecord(connection.ProjectRoot)
	return false, compileExecutionResult{}
}

func probePendingCompileStatus(
	ctx context.Context,
	connection unityipc.Connection,
	requestID string,
	deps compileWaitDeps,
) (compileStatusResponse, bool) {
	timeout := deps.attachProbeTimeout
	if timeout <= 0 {
		timeout = compileAttachProbeTimeout
	}
	interval := deps.attachProbeInterval
	if interval <= 0 {
		interval = compileAttachProbeInterval
	}

	deadline := time.Now().Add(timeout)
	for {
		status, err := deps.queryCompileStatus(ctx, connection, requestID)
		if err == nil {
			return status, true
		}
		if !time.Now().Before(deadline) {
			return compileStatusResponse{}, false
		}

		remaining := time.Until(deadline)
		sleepFor := interval
		if sleepFor > remaining {
			sleepFor = remaining
		}
		timer := time.NewTimer(sleepFor)
		select {
		case <-ctx.Done():
			timer.Stop()
			return compileStatusResponse{}, false
		case <-timer.C:
		}
	}
}

func attachWaitForPendingCompile(
	ctx context.Context,
	connection unityipc.Connection,
	record compilePendingRecord,
	params map[string]any,
	waitTimeout time.Duration,
	stderr io.Writer,
	deps compileWaitDeps,
) (bool, compileExecutionResult) {
	if compileForceRecompileEnabled(params) {
		_, _ = fmt.Fprintf(
			stderr,
			"warning: reattaching to an in-flight compile; --force-recompile is not applied. Re-run with --force-recompile after this compile finishes if you still need a forced recompile.\n",
		)
	}

	startedAt := time.Now()
	logCompileAttachStart(connection, record.RequestID, "waiting")
	spinner := clicore.NewToolSpinner(stderr, clicore.CompileCommandName)
	spinner.Update("Reattaching to in-flight compile...")

	pollInterval := deps.attachWaitPollInterval
	if pollInterval <= 0 {
		pollInterval = compileWaitPollInterval
	}
	waitStartedAt := time.Now()
	bindCompileWaitInterimReporter(stderr, spinner, &deps)
	result, outcome, lastStatus, waitErr := waitForAttachedCompileCompletion(ctx, compileCompletionOptions{
		connection:   connection,
		requestID:    record.RequestID,
		timeout:      waitTimeout,
		pollInterval: pollInterval,
	}, deps)
	if waitErr != nil {
		spinner.Stop()
		logCompileAttachResult(connection, record.RequestID, "error", false)
		clierrors.WriteClassifiedError(stderr, waitErr, clierrors.ErrorContext{
			ProjectRoot: connection.ProjectRoot,
			Command:     clicore.CompileCommandName,
		})
		return true, compileExecutionResult{exitCode: 1}
	}

	switch outcome {
	case attachWaitDisappeared:
		spinner.Stop()
		clearCompilePendingRecord(connection.ProjectRoot)
		logCompileAttachResult(connection, record.RequestID, "disappeared", true)
		return false, compileExecutionResult{}
	case attachWaitTimedOut:
		spinner.Stop()
		// Why not refresh TimedOutAtUtc: stale expiry must stay anchored to the first timeout.
		logCompileAttachResult(connection, record.RequestID, "timeout", false)
		// Why not (TTL - waitTimeout): attach keeps the first TimedOutAtUtc, so remaining
		// retrieval time is wall-clock until that anchor plus compilePendingRecordLifetime.
		retentionRemaining := time.Until(record.TimedOutAtUtc.Add(compilePendingRecordLifetime))
		clierrors.WriteErrorEnvelope(stderr, compileWaitTimeoutError(
			connection.ProjectRoot,
			waitTimeout,
			lastStatus,
			time.Since(waitStartedAt),
			retentionRemaining,
		))
		return true, compileExecutionResult{exitCode: 1}
	case attachWaitCompleted:
		clearCompilePendingRecord(connection.ProjectRoot)
		logCompileAttachResult(connection, record.RequestID, "completed", true)
		return true, completeCompileResult(ctx, connection, result, stderr, spinner, startedAt, unityipc.UnitySendOutcome{})
	default:
		spinner.Stop()
		logCompileAttachResult(connection, record.RequestID, "error", false)
		return true, compileExecutionResult{exitCode: 1}
	}
}

func waitForAttachedCompileCompletion(
	ctx context.Context,
	options compileCompletionOptions,
	deps compileWaitDeps,
) (json.RawMessage, attachWaitOutcome, *compileStatusResponse, error) {
	startedAt := time.Now()
	deadline := startedAt.Add(options.timeout)
	attempts := 0
	missingResultStreak := 0
	var lastStatus compileStatusResponse
	observedStatus := false
	var lastErr error
	lastObservationKey := ""

	logCompileStatusPollStart(options, startedAt, deadline)
	interim := newCompileWaitInterimState(compileWaitNow(deps))

	ticker := time.NewTicker(options.pollInterval)
	defer ticker.Stop()
	for {
		now := time.Now()
		if !now.Before(deadline) {
			break
		}

		attempts++
		status, err := deps.queryCompileStatus(ctx, options.connection, options.requestID)
		lastErr = err
		if err == nil {
			lastStatus = status
			observedStatus = true
			if status.HasResult && len(status.Result) > 0 {
				logCompileStatusPollObservedIfChanged(options, startedAt, attempts, status, nil, &lastObservationKey)
				logCompileStatusPollComplete(options, startedAt, attempts, status)
				return status.Result, attachWaitCompleted, nil, nil
			}
			if status.Ready && !status.HasResult {
				missingResultStreak++
				if missingResultStreak >= compileAttachMissingResultStreak {
					logCompileStatusPollObservedIfChanged(options, startedAt, attempts, status, nil, &lastObservationKey)
					return nil, attachWaitDisappeared, lastObservedCompileStatus(lastStatus, observedStatus), nil
				}
			} else {
				missingResultStreak = 0
			}
		}
		logCompileStatusPollObservedIfChanged(options, startedAt, attempts, status, err, &lastObservationKey)
		observeCompileWaitInterim(&interim, deps, status, err)

		select {
		case <-ctx.Done():
			logCompileWaitCancelled(options, startedAt, attempts, lastStatus, lastErr, ctx.Err())
			return nil, attachWaitFailed, lastObservedCompileStatus(lastStatus, observedStatus), ctx.Err()
		case <-ticker.C:
		}
	}

	logCompileWaitTimedOut(options, startedAt, attempts, lastStatus, lastErr)
	return nil, attachWaitTimedOut, lastObservedCompileStatus(lastStatus, observedStatus), nil
}

func returnAttachedStoredCompileResult(
	ctx context.Context,
	connection unityipc.Connection,
	record compilePendingRecord,
	result json.RawMessage,
	stderr io.Writer,
) compileExecutionResult {
	startedAt := time.Now()
	logCompileAttachStart(connection, record.RequestID, "stored_result")
	spinner := clicore.NewToolSpinner(stderr, clicore.CompileCommandName)
	clearCompilePendingRecord(connection.ProjectRoot)
	logCompileAttachResult(connection, record.RequestID, "stored_result", true)
	return completeCompileResult(ctx, connection, result, stderr, spinner, startedAt, unityipc.UnitySendOutcome{})
}

func completeCompileResult(
	ctx context.Context,
	connection unityipc.Connection,
	result json.RawMessage,
	stderr io.Writer,
	spinner *ui.TerminalSpinner,
	startedAt time.Time,
	outcome unityipc.UnitySendOutcome,
) compileExecutionResult {
	switch compileResultReadinessWaitMode(result) {
	case compileReadinessWaitWarmup:
		spinner.Update("Warming execute-dynamic-code after compile...")
		if err := clicore.WaitForToolReadiness(ctx, connection.ProjectRoot); err != nil {
			spinner.Stop()
			writePostCompileWarmupWarning(stderr, err)
		}
	}
	spinner.Stop()
	writeDebugTiming(stderr, clicore.CompileCommandName, time.Since(startedAt), outcome)
	return compileExecutionResult{result: result, exitCode: toolEnvelopeExitCode(result)}
}

func persistCompilePendingRecordOrWarn(projectRoot string, requestID string, stderr io.Writer) {
	err := writeCompilePendingRecord(projectRoot, compilePendingRecord{
		RequestID:     requestID,
		TimedOutAtUtc: time.Now().UTC(),
	})
	if err == nil {
		return
	}
	_, _ = fmt.Fprintf(stderr, "warning: failed to persist pending compile request for retry attach: %v\n", err)
}

func logCompileAttachStart(connection unityipc.Connection, requestID string, mode string) {
	writeCompileVibeLog(connection.ProjectRoot, func() vibelog.CLIVibeLogEntry {
		return vibelog.CLIVibeLogEntry{
			Level:     "INFO",
			Operation: "cli_compile_attach_start",
			Message:   "Reattaching to a previously timed-out compile request.",
			Context: map[string]any{
				"command":          clicore.CompileCommandName,
				"request_id":       requestID,
				"attach_mode":      mode,
				"project_identity": vibelog.ProjectIdentity(connection.ProjectRoot),
				"endpoint":         connection.Endpoint.Address,
			},
			CorrelationID: requestID,
		}
	})
}

func logCompileAttachResult(connection unityipc.Connection, requestID string, outcome string, clearedRecord bool) {
	writeCompileVibeLog(connection.ProjectRoot, func() vibelog.CLIVibeLogEntry {
		return vibelog.CLIVibeLogEntry{
			Level:     "INFO",
			Operation: "cli_compile_attach_result",
			Message:   "Finished attach attempt for a previously timed-out compile request.",
			Context: map[string]any{
				"command":          clicore.CompileCommandName,
				"request_id":       requestID,
				"attach_outcome":   outcome,
				"record_cleared":   clearedRecord,
				"project_identity": vibelog.ProjectIdentity(connection.ProjectRoot),
				"endpoint":         connection.Endpoint.Address,
			},
			CorrelationID: requestID,
		}
	})
}
