package cli

import (
	"context"
)

func logLaunchExistingFocus(ctx context.Context, projectRoot string, pid int) {
	correlationID := newCliVibeCorrelationID()
	logLaunchExistingFocusAttempt(projectRoot, pid, correlationID)
	if err := focusUnityProcessForLaunch(ctx, pid); err != nil {
		logLaunchExistingFocusFailure(projectRoot, pid, err, correlationID)
		return
	}
	logLaunchExistingFocusSuccess(projectRoot, pid, correlationID)
}

func logLaunchExistingFocusAttempt(projectRoot string, pid int, correlationID string) {
	_ = writeCliVibeLog(projectRoot, cliVibeLogEntry{
		Level:     "INFO",
		Operation: "cli_launch_existing_focus_attempt",
		Message:   "Attempting to focus the already-running Unity process.",
		Context: map[string]any{
			"command": "launch",
			"pid":     pid,
		},
		CorrelationID: correlationID,
	})
}

func logLaunchExistingFocusSuccess(projectRoot string, pid int, correlationID string) {
	_ = writeCliVibeLog(projectRoot, cliVibeLogEntry{
		Level:     "INFO",
		Operation: "cli_launch_existing_focus_success",
		Message:   "Focused the already-running Unity process.",
		Context: map[string]any{
			"command": "launch",
			"pid":     pid,
		},
		CorrelationID: correlationID,
	})
}

func logLaunchExistingFocusFailure(projectRoot string, pid int, focusErr error, correlationID string) {
	_ = writeCliVibeLog(projectRoot, cliVibeLogEntry{
		Level:     "WARNING",
		Operation: "cli_launch_existing_focus_failed",
		Message:   "Failed to focus the already-running Unity process.",
		Context: map[string]any{
			"command":    "launch",
			"pid":        pid,
			"focusError": errorMessage(focusErr),
		},
		CorrelationID: correlationID,
	})
}
