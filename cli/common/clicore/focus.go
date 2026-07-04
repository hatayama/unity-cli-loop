package clicore

import (
	"context"
	"encoding/json"
	"fmt"
	"io"

	"github.com/hatayama/unity-cli-loop/common/unityprocess"
)

type UnityProcess = unityprocess.UnityProcess

type RestoreFocusFunc = unityprocess.RestoreFocusFunc

type focusResponse struct {
	Success bool   `json:"Success"`
	Message string `json:"Message"`
}

type focusWindowDeps struct {
	findRunningUnityProcess func(context.Context, string) (*unityprocess.UnityProcess, error)
	focusUnityProcess       func(context.Context, int) error
}

func RunFocusWindow(ctx context.Context, projectRoot string, stdout io.Writer, stderr io.Writer) int {
	return runFocusWindow(ctx, projectRoot, stdout, stderr, defaultFocusWindowDeps())
}

func FindRunningUnityProcess(ctx context.Context, projectRoot string) (*UnityProcess, error) {
	return unityprocess.FindRunningUnityProcess(ctx, projectRoot)
}

func FocusUnityProcess(ctx context.Context, pid int) error {
	return unityprocess.FocusUnityProcess(ctx, pid)
}

func FocusUnityProcessWithRestore(ctx context.Context, pid int) (RestoreFocusFunc, error) {
	return unityprocess.FocusUnityProcessWithRestore(ctx, pid)
}

func defaultFocusWindowDeps() focusWindowDeps {
	return focusWindowDeps{
		findRunningUnityProcess: unityprocess.FindRunningUnityProcess,
		focusUnityProcess:       unityprocess.FocusUnityProcess,
	}
}

func runFocusWindow(ctx context.Context, projectRoot string, stdout io.Writer, stderr io.Writer, deps focusWindowDeps) int {
	runningProcess, err := deps.findRunningUnityProcess(ctx, projectRoot)
	if err != nil {
		writeFocusResponse(stderr, false, err.Error())
		return 1
	}
	if runningProcess == nil {
		writeFocusResponse(stderr, false, "No running Unity process found for this project")
		return 1
	}

	correlationID := NewCLIVibeCorrelationID()
	logFocusWindowFocusAttempt(projectRoot, runningProcess.Pid, correlationID)
	if err := deps.focusUnityProcess(ctx, runningProcess.Pid); err != nil {
		logFocusWindowFocusFailure(projectRoot, runningProcess.Pid, err, correlationID)
		writeFocusResponse(stderr, false, fmt.Sprintf("Failed to focus Unity window: %s", err.Error()))
		return 1
	}

	logFocusWindowFocusSuccess(projectRoot, runningProcess.Pid, correlationID)
	writeFocusResponse(stdout, true, fmt.Sprintf("Unity Editor window focused (PID: %d)", runningProcess.Pid))
	return 0
}

func writeFocusResponse(writer io.Writer, success bool, message string) {
	response := focusResponse{
		Success: success,
		Message: message,
	}
	encoder := json.NewEncoder(writer)
	encoder.SetIndent("", "  ")
	_ = encoder.Encode(response)
}

func logFocusWindowFocusAttempt(projectRoot string, pid int, correlationID string) {
	_ = WriteCLIVibeLog(projectRoot, CLIVibeLogEntry{
		Level:     "INFO",
		Operation: "cli_focus_window_focus_attempt",
		Message:   "Attempting to focus Unity for the focus-window command.",
		Context: map[string]any{
			"command": "focus-window",
			"pid":     pid,
		},
		CorrelationID: correlationID,
	})
}

func logFocusWindowFocusSuccess(projectRoot string, pid int, correlationID string) {
	_ = WriteCLIVibeLog(projectRoot, CLIVibeLogEntry{
		Level:     "INFO",
		Operation: "cli_focus_window_focus_success",
		Message:   "Focused Unity for the focus-window command.",
		Context: map[string]any{
			"command": "focus-window",
			"pid":     pid,
		},
		CorrelationID: correlationID,
	})
}

func logFocusWindowFocusFailure(projectRoot string, pid int, focusErr error, correlationID string) {
	_ = WriteCLIVibeLog(projectRoot, CLIVibeLogEntry{
		Level:     "WARNING",
		Operation: "cli_focus_window_focus_failed",
		Message:   "Failed to focus Unity for the focus-window command.",
		Context: map[string]any{
			"command":    "focus-window",
			"pid":        pid,
			"focusError": ErrorMessage(focusErr),
		},
		CorrelationID: correlationID,
	})
}
