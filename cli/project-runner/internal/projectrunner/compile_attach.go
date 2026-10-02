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

// compileReattachPolicy says whether a previously timed-out compile's result may answer this
// compile. That compile started before any edit made since, so its result does not show those
// edits compiled in.
type compileReattachPolicy int

const (
	// The user's own `uloop compile`: retrying after COMPILE_WAIT_TIMEOUT is how it collects the
	// result it timed out on.
	compileReattachAcceptsEarlierResult compileReattachPolicy = iota
	// A command that goes on to rely on the current sources: the earlier compile is only waited
	// out (Unity rejects a new compile while it runs), then a compile of its own is sent.
	compileReattachRequiresCurrentSources
)

// tryAttachToPendingCompile reattaches to a previously timed-out compile when a
// pending record still exists. handled=true means the caller must return exitCode.
func tryAttachToPendingCompile(
	ctx context.Context,
	connection unityipc.Connection,
	params map[string]any,
	reattach compileReattachPolicy,
	waitTimeout time.Duration,
	stderr io.Writer,
	deps compileWaitDeps,
) (bool, compileExecutionResult) {
	record, ok := readCompilePendingRecord(connection.ProjectRoot)
	if !ok {
		return false, compileExecutionResult{}
	}

	status, probeErr := probePendingCompileStatus(ctx, connection, record.RequestID, deps)
	if probeErr != nil {
		// Why wait: Unity acknowledged the query, so its server is alive and only the main thread
		// that answers status queries stayed blocked. While the pending compile holds the
		// single-flight slot a new compile request is rejected as busy, so polling its status is the
		// only way to reach its result. Why check ctx: the caller's own deadline passing after the
		// ack ends the query the same way, and then there is no time left to wait.
		if ctx.Err() == nil && isUnansweredStatusProbe(probeErr) {
			logCompileAttachProbeFailed(connection, record.RequestID, probeErr, "waiting")
			return attachWaitForPendingCompile(ctx, connection, record, params, reattach, waitTimeout, stderr, deps)
		}
		// Why keep the record: probe failures during domain reload are transient and
		// do not prove the in-flight compile is gone.
		logCompileAttachProbeFailed(connection, record.RequestID, probeErr, "new_compile")
		return false, compileExecutionResult{}
	}

	// Why HasResult first: Ready is editor-wide (!compiling/!updating/!reload), not
	// scoped to record.RequestID. Only HasResult is request-specific, and results are
	// stored only after that request finishes — so a result is definitive even when
	// the editor is busy with unrelated work.
	if status.HasResult && len(status.Result) > 0 {
		if reattach == compileReattachRequiresCurrentSources && !status.Ready {
			return attachWaitForPendingCompile(ctx, connection, record, params, reattach, waitTimeout, stderr, deps)
		}
		if compileForceRecompileEnabled(params) || reattach == compileReattachRequiresCurrentSources {
			clearCompilePendingRecord(connection.ProjectRoot)
			return false, compileExecutionResult{}
		}
		return true, returnAttachedStoredCompileResult(ctx, connection, record, status.Result, stderr)
	}

	if !status.Ready {
		return attachWaitForPendingCompile(ctx, connection, record, params, reattach, waitTimeout, stderr, deps)
	}

	clearCompilePendingRecord(connection.ProjectRoot)
	return false, compileExecutionResult{}
}

// probePendingCompileStatus queries the pending compile's status until one query succeeds or the
// probe deadline passes. A failed probe returns the last query's error, or ctx.Err() when ctx ends
// between queries.
func probePendingCompileStatus(
	ctx context.Context,
	connection unityipc.Connection,
	requestID string,
	deps compileWaitDeps,
) (compileStatusResponse, error) {
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
			return status, nil
		}
		if !time.Now().Before(deadline) {
			// Why the last error, not the first: it is the Editor's latest state. An Editor that was
			// unreachable and then acknowledged queries is back with its main thread blocked.
			return compileStatusResponse{}, err
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
			return compileStatusResponse{}, ctx.Err()
		case <-timer.C:
		}
	}
}

func attachWaitForPendingCompile(
	ctx context.Context,
	connection unityipc.Connection,
	record compilePendingRecord,
	params map[string]any,
	reattach compileReattachPolicy,
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
		connection:       connection,
		requestID:        record.RequestID,
		untilEditorReady: reattach == compileReattachRequiresCurrentSources,
		timeout:          waitTimeout,
		pollInterval:     pollInterval,
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
		if reattach == compileReattachRequiresCurrentSources {
			spinner.Stop()
			return false, compileExecutionResult{}
		}
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
			if status.HasResult && len(status.Result) > 0 && (status.Ready || !options.untilEditorReady) {
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

// logCompileAttachProbeFailed records why a status probe for the pending compile failed and whether
// the command then waits for that compile or starts a new one.
func logCompileAttachProbeFailed(connection unityipc.Connection, requestID string, probeErr error, next string) {
	writeCompileVibeLog(connection.ProjectRoot, func() vibelog.CLIVibeLogEntry {
		return vibelog.CLIVibeLogEntry{
			Level:     "WARNING",
			Operation: "cli_compile_attach_probe_failed",
			Message:   "Status probe for a previously timed-out compile request failed.",
			Context: map[string]any{
				"command":          clicore.CompileCommandName,
				"request_id":       requestID,
				"transport_error":  clicore.ErrorMessage(probeErr),
				"next":             next,
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
