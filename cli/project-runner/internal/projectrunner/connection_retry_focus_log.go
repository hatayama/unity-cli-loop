package projectrunner

// CLI vibe log writers for the connection-retry focus rescue: the focus attempt
// and its restore outcome, joined through a shared correlation ID.

import (
	"github.com/hatayama/unity-cli-loop/common/clicore"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
	"github.com/hatayama/unity-cli-loop/common/vibelog"
)

func logConnectionRetryFocusAttempt(
	connection unityipc.Connection,
	method string,
	pid int,
	reason connectionRetryFocusReason,
	retryCause error,
	correlationID string,
) {
	_ = vibelog.WriteCLIVibeLog(connection.ProjectRoot, vibelog.CLIVibeLogEntry{
		Level:     "INFO",
		Operation: "cli_connection_retry_focus_attempt",
		Message:   "Attempting to focus Unity while recovering a slow or unreachable request.",
		Context: map[string]any{
			"command":  method,
			"pid":      pid,
			"endpoint": connection.Endpoint.Address,
			"reason":   string(reason),
			"cause":    clicore.ErrorMessage(retryCause),
		},
		CorrelationID: correlationID,
	})
}

func logConnectionRetryFocusSuccess(
	connection unityipc.Connection,
	method string,
	pid int,
	reason connectionRetryFocusReason,
	retryCause error,
	correlationID string,
) {
	_ = vibelog.WriteCLIVibeLog(connection.ProjectRoot, vibelog.CLIVibeLogEntry{
		Level:     "INFO",
		Operation: "cli_connection_retry_focus_success",
		Message:   "Focused Unity while recovering a slow or unreachable request.",
		Context: map[string]any{
			"command":  method,
			"pid":      pid,
			"endpoint": connection.Endpoint.Address,
			"reason":   string(reason),
			"cause":    clicore.ErrorMessage(retryCause),
		},
		CorrelationID: correlationID,
	})
}

func logConnectionRetryFocusFailure(
	connection unityipc.Connection,
	method string,
	pid int,
	reason connectionRetryFocusReason,
	retryCause error,
	focusErr error,
	correlationID string,
) {
	_ = vibelog.WriteCLIVibeLog(connection.ProjectRoot, vibelog.CLIVibeLogEntry{
		Level:     "WARNING",
		Operation: "cli_connection_retry_focus_failed",
		Message:   "Failed to focus Unity before retrying an undispatched request.",
		Context: map[string]any{
			"command":    method,
			"pid":        pid,
			"endpoint":   connection.Endpoint.Address,
			"reason":     string(reason),
			"cause":      clicore.ErrorMessage(retryCause),
			"focusError": clicore.ErrorMessage(focusErr),
		},
		CorrelationID: correlationID,
	})
}

func logConnectionRetryFocusRestoreSuccess(
	connection unityipc.Connection,
	method string,
	pid int,
	reason connectionRetryFocusReason,
	correlationID string,
) {
	_ = vibelog.WriteCLIVibeLog(connection.ProjectRoot, vibelog.CLIVibeLogEntry{
		Level:     "INFO",
		Operation: "cli_connection_retry_focus_restore_success",
		Message:   "Restored the previously frontmost application after the focus rescue.",
		Context: map[string]any{
			"command":  method,
			"pid":      pid,
			"endpoint": connection.Endpoint.Address,
			"reason":   string(reason),
		},
		CorrelationID: correlationID,
	})
}

func logConnectionRetryFocusRestoreFailure(
	connection unityipc.Connection,
	method string,
	pid int,
	reason connectionRetryFocusReason,
	restoreErr error,
	correlationID string,
) {
	_ = vibelog.WriteCLIVibeLog(connection.ProjectRoot, vibelog.CLIVibeLogEntry{
		Level:     "WARNING",
		Operation: "cli_connection_retry_focus_restore_failed",
		Message:   "Failed to restore the previously frontmost application after the focus rescue.",
		Context: map[string]any{
			"command":      method,
			"pid":          pid,
			"endpoint":     connection.Endpoint.Address,
			"reason":       string(reason),
			"restoreError": clicore.ErrorMessage(restoreErr),
		},
		CorrelationID: correlationID,
	})
}

func logConnectionRetryFocusRestoreSkipped(
	connection unityipc.Connection,
	method string,
	pid int,
	reason connectionRetryFocusReason,
	correlationID string,
) {
	_ = vibelog.WriteCLIVibeLog(connection.ProjectRoot, vibelog.CLIVibeLogEntry{
		Level:     "INFO",
		Operation: "cli_connection_retry_focus_restore_skipped",
		Message:   "Skipped the focus restore on purpose: terminal timeout recovery keeps Unity in front so the user notices it needs attention.",
		Context: map[string]any{
			"command":  method,
			"pid":      pid,
			"endpoint": connection.Endpoint.Address,
			"reason":   string(reason),
		},
		CorrelationID: correlationID,
	})
}

func logConnectionRetryFocusRestoreUnavailable(
	connection unityipc.Connection,
	method string,
	pid int,
	reason connectionRetryFocusReason,
	correlationID string,
) {
	_ = vibelog.WriteCLIVibeLog(connection.ProjectRoot, vibelog.CLIVibeLogEntry{
		Level:     "INFO",
		Operation: "cli_connection_retry_focus_restore_unavailable",
		Message:   "Focused Unity without a restore target because the previously frontmost process could not be read.",
		Context: map[string]any{
			"command":  method,
			"pid":      pid,
			"endpoint": connection.Endpoint.Address,
			"reason":   string(reason),
		},
		CorrelationID: correlationID,
	})
}
