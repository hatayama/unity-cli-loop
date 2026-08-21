package dispatcher

import (
	"context"
	"time"

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

// logLaunchV2FocusWithDeps focuses a freshly spawned V2 Editor so delayCall can tick.
// Failure is non-fatal: the readiness probe continues either way.
func logLaunchV2FocusWithDeps(ctx context.Context, projectRoot string, pid int, deps launchDeps) {
	correlationID := vibelog.NewCLIVibeCorrelationID()
	logLaunchV2FocusAttempt(projectRoot, pid, correlationID)
	if err := deps.focusUnityProcess(ctx, pid); err != nil {
		logLaunchV2FocusFailure(projectRoot, pid, err, correlationID)
		return
	}
	logLaunchV2FocusSuccess(projectRoot, pid, correlationID)
}

func logLaunchV2FocusAttempt(projectRoot string, pid int, correlationID string) {
	_ = vibelog.WriteCLIVibeLog(projectRoot, vibelog.CLIVibeLogEntry{
		Level:     "INFO",
		Operation: "cli_launch_v2_focus_attempt",
		Message:   "Attempting to focus the newly launched V2 Unity process so server startup can tick.",
		Context: map[string]any{
			"command": "launch",
			"pid":     pid,
		},
		CorrelationID: correlationID,
	})
}

func logLaunchV2FocusSuccess(projectRoot string, pid int, correlationID string) {
	_ = vibelog.WriteCLIVibeLog(projectRoot, vibelog.CLIVibeLogEntry{
		Level:     "INFO",
		Operation: "cli_launch_v2_focus_success",
		Message:   "Focused the newly launched V2 Unity process.",
		Context: map[string]any{
			"command": "launch",
			"pid":     pid,
		},
		CorrelationID: correlationID,
	})
}

func logLaunchV2FocusFailure(projectRoot string, pid int, focusErr error, correlationID string) {
	_ = vibelog.WriteCLIVibeLog(projectRoot, vibelog.CLIVibeLogEntry{
		Level:     "WARNING",
		Operation: "cli_launch_v2_focus_failed",
		Message:   "Failed to focus the newly launched V2 Unity process; continuing readiness probe.",
		Context: map[string]any{
			"command":    "launch",
			"pid":        pid,
			"focusError": clicore.ErrorMessage(focusErr),
		},
		CorrelationID: correlationID,
	})
}

func logLaunchFreshFocusWithDeps(ctx context.Context, projectRoot string, pid int, deps launchDeps) {
	if attemptLaunchFreshFocus(ctx, projectRoot, pid, deps) {
		return
	}
	launchSleep(deps, launchFreshFocusRetryDelay)
	_ = attemptLaunchFreshFocus(ctx, projectRoot, pid, deps)
}

func attemptLaunchFreshFocus(ctx context.Context, projectRoot string, pid int, deps launchDeps) bool {
	correlationID := vibelog.NewCLIVibeCorrelationID()
	logLaunchFreshFocusAttempt(projectRoot, pid, correlationID)
	if err := deps.focusUnityProcess(ctx, pid); err != nil {
		logLaunchFreshFocusFailure(projectRoot, pid, err, correlationID)
		return false
	}
	logLaunchFreshFocusSuccess(projectRoot, pid, correlationID)
	return true
}

func logLaunchFreshFocusAttempt(projectRoot string, pid int, correlationID string) {
	_ = vibelog.WriteCLIVibeLog(projectRoot, vibelog.CLIVibeLogEntry{
		Level:     "INFO",
		Operation: "cli_launch_fresh_focus_attempt",
		Message:   "Attempting to focus the newly launched Unity process.",
		Context: map[string]any{
			"command": "launch",
			"pid":     pid,
		},
		CorrelationID: correlationID,
	})
}

func logLaunchFreshFocusSuccess(projectRoot string, pid int, correlationID string) {
	_ = vibelog.WriteCLIVibeLog(projectRoot, vibelog.CLIVibeLogEntry{
		Level:     "INFO",
		Operation: "cli_launch_fresh_focus_success",
		Message:   "Focused the newly launched Unity process.",
		Context: map[string]any{
			"command": "launch",
			"pid":     pid,
		},
		CorrelationID: correlationID,
	})
}

func logLaunchFreshFocusFailure(projectRoot string, pid int, focusErr error, correlationID string) {
	_ = vibelog.WriteCLIVibeLog(projectRoot, vibelog.CLIVibeLogEntry{
		Level:     "WARNING",
		Operation: "cli_launch_fresh_focus_failure",
		Message:   "Failed to focus the newly launched Unity process.",
		Context: map[string]any{
			"command":    "launch",
			"pid":        pid,
			"focusError": clicore.ErrorMessage(focusErr),
		},
		CorrelationID: correlationID,
	})
}

func launchSleep(deps launchDeps, delay time.Duration) {
	if deps.sleep != nil {
		deps.sleep(delay)
		return
	}
	time.Sleep(delay)
}
