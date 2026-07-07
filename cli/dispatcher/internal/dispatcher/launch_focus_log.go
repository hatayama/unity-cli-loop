package dispatcher

import (
	"context"

	"github.com/hatayama/unity-cli-loop/common/vibelog"

	"github.com/hatayama/unity-cli-loop/common/clicore"
)

func logLaunchExistingFocusWithDeps(ctx context.Context, projectRoot string, pid int, deps launchDeps) {
	correlationID := vibelog.NewCLIVibeCorrelationID()
	logLaunchExistingFocusAttempt(projectRoot, pid, correlationID)
	if err := deps.focusUnityProcess(ctx, pid); err != nil {
		logLaunchExistingFocusFailure(projectRoot, pid, err, correlationID)
		return
	}
	logLaunchExistingFocusSuccess(projectRoot, pid, correlationID)
}

func logLaunchExistingFocusAttempt(projectRoot string, pid int, correlationID string) {
	_ = vibelog.WriteCLIVibeLog(projectRoot, vibelog.CLIVibeLogEntry{
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
	_ = vibelog.WriteCLIVibeLog(projectRoot, vibelog.CLIVibeLogEntry{
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
	_ = vibelog.WriteCLIVibeLog(projectRoot, vibelog.CLIVibeLogEntry{
		Level:     "WARNING",
		Operation: "cli_launch_existing_focus_failed",
		Message:   "Failed to focus the already-running Unity process.",
		Context: map[string]any{
			"command":    "launch",
			"pid":        pid,
			"focusError": clicore.ErrorMessage(focusErr),
		},
		CorrelationID: correlationID,
	})
}
