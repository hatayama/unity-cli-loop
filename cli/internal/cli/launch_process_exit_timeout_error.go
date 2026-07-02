package cli

import (
	"fmt"
	"time"
)

type launchProcessExitTimeoutError struct {
	projectRoot string
	pid         int
	timeout     time.Duration
}

func (err launchProcessExitTimeoutError) Error() string {
	return fmt.Sprintf("Unity process %d did not exit within %s", err.pid, err.timeout)
}

func (err launchProcessExitTimeoutError) toCLIError(context errorContext) cliError {
	projectRoot := firstNonEmpty(context.projectRoot, err.projectRoot)
	return cliError{
		ErrorCode:   errorCodeUnityProcessExitTimeout,
		Phase:       errorPhaseExecution,
		Message:     fmt.Sprintf("Unity process %d did not exit before the launch timeout.", err.pid),
		Retryable:   true,
		SafeToRetry: true,
		ProjectRoot: projectRoot,
		Command:     context.command,
		NextActions: []string{
			"Wait for Unity to finish exiting, then retry the launch command.",
			"If Unity remains visible or keeps project files locked, close the Unity process from the OS and retry.",
		},
		Details: map[string]any{
			"Pid":            err.pid,
			"TimeoutSeconds": int(err.timeout.Seconds()),
		},
	}
}
