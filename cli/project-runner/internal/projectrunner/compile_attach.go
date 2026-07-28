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
)

// tryAttachToPendingCompile reattaches to a previously timed-out compile when a
// pending record still exists. handled=true means the caller must return exitCode.
func tryAttachToPendingCompile(
	ctx context.Context,
	connection unityipc.Connection,
	params map[string]any,
	waitTimeout time.Duration,
	stdout io.Writer,
	stderr io.Writer,
	deps compileWaitDeps,
) (bool, int) {
	record, ok := readCompilePendingRecord(connection.ProjectRoot)
	if !ok {
		return false, 0
	}

	status, probed := probePendingCompileStatus(ctx, connection, record.RequestID, deps)
	if !probed {
		// Why keep the record: probe failures during domain reload are transient and
		// do not prove the in-flight compile is gone.
		return false, 0
	}

	if !status.Ready {
		return true, attachWaitForPendingCompile(ctx, connection, record, waitTimeout, stdout, stderr, deps)
	}

	if status.HasResult && len(status.Result) > 0 {
		if compileForceRecompileEnabled(params) {
			clearCompilePendingRecord(connection.ProjectRoot)
			return false, 0
		}
		return true, returnAttachedStoredCompileResult(ctx, connection, record, status.Result, stdout, stderr)
	}

	clearCompilePendingRecord(connection.ProjectRoot)
	return false, 0
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
	waitTimeout time.Duration,
	stdout io.Writer,
	stderr io.Writer,
	deps compileWaitDeps,
) int {
	startedAt := time.Now()
	logCompileAttachStart(connection, record.RequestID, "waiting")
	spinner := clicore.NewToolSpinner(stderr, clicore.CompileCommandName)
	spinner.Update("Reattaching to in-flight compile...")

	result, completed, waitErr := waitForCompileCompletionWithDeps(ctx, compileCompletionOptions{
		connection:     connection,
		requestID:      record.RequestID,
		forceRecompile: false,
		timeout:        waitTimeout,
		pollInterval:   compileWaitPollInterval,
	}, deps)
	if waitErr != nil {
		spinner.Stop()
		logCompileAttachResult(connection, record.RequestID, "error", false)
		clierrors.WriteClassifiedError(stderr, waitErr, clierrors.ErrorContext{
			ProjectRoot: connection.ProjectRoot,
			Command:     clicore.CompileCommandName,
		})
		return 1
	}
	if !completed {
		spinner.Stop()
		// Why not refresh TimedOutAtUtc: stale expiry must stay anchored to the first timeout.
		logCompileAttachResult(connection, record.RequestID, "timeout", false)
		clierrors.WriteErrorEnvelope(stderr, compileWaitTimeoutError(connection.ProjectRoot, waitTimeout))
		return 1
	}

	clearCompilePendingRecord(connection.ProjectRoot)
	logCompileAttachResult(connection, record.RequestID, "completed", true)
	return completeCompileResultOutput(ctx, connection, result, stdout, stderr, spinner, startedAt, unityipc.UnitySendOutcome{})
}

func returnAttachedStoredCompileResult(
	ctx context.Context,
	connection unityipc.Connection,
	record compilePendingRecord,
	result json.RawMessage,
	stdout io.Writer,
	stderr io.Writer,
) int {
	startedAt := time.Now()
	logCompileAttachStart(connection, record.RequestID, "stored_result")
	spinner := clicore.NewToolSpinner(stderr, clicore.CompileCommandName)
	clearCompilePendingRecord(connection.ProjectRoot)
	logCompileAttachResult(connection, record.RequestID, "stored_result", true)
	return completeCompileResultOutput(ctx, connection, result, stdout, stderr, spinner, startedAt, unityipc.UnitySendOutcome{})
}

func completeCompileResultOutput(
	ctx context.Context,
	connection unityipc.Connection,
	result json.RawMessage,
	stdout io.Writer,
	stderr io.Writer,
	spinner *ui.TerminalSpinner,
	startedAt time.Time,
	outcome unityipc.UnitySendOutcome,
) int {
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
	return toolEnvelopeExitCode(result)
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
