package dispatcher

import (
	"fmt"
	"time"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"

	"github.com/hatayama/unity-cli-loop/common/clicore"
)

type launchProcessExitTimeoutError struct {
	projectRoot string
	pid         int
	timeout     time.Duration
}

func (err launchProcessExitTimeoutError) Error() string {
	return fmt.Sprintf("Unity process %d did not exit within %s", err.pid, err.timeout)
}

func (err launchProcessExitTimeoutError) ToCLIError(context clierrors.ErrorContext) clierrors.CLIError {
	projectRoot := clicore.FirstNonEmpty(context.ProjectRoot, err.projectRoot)
	return clierrors.CLIError{
		ErrorCode:   clierrors.ErrorCodeUnityProcessExitTimeout,
		Phase:       clierrors.ErrorPhaseExecution,
		Message:     fmt.Sprintf("Unity process %d did not exit before the launch timeout.", err.pid),
		Retryable:   true,
		SafeToRetry: true,
		ProjectRoot: projectRoot,
		Command:     context.Command,
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
