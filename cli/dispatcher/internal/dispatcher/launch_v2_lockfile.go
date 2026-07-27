package dispatcher

import (
	"context"
	"fmt"
	"io"
	"os"
	"time"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
	"github.com/hatayama/unity-cli-loop/common/ui"
	"github.com/hatayama/unity-cli-loop/common/unityprocess"
)

type v2LaunchLockfileTimeoutError struct {
	lockfilePath string
}

func (err v2LaunchLockfileTimeoutError) Error() string {
	return fmt.Sprintf("timed out waiting for Unity to update %s", err.lockfilePath)
}

func (err v2LaunchLockfileTimeoutError) ToCLIError(context clierrors.ErrorContext) clierrors.CLIError {
	return clierrors.CLIError{
		ErrorCode:   clierrors.ErrorCodeUnityStartupTimeout,
		Phase:       clierrors.ErrorPhaseConnection,
		Message:     "Unity did not open the V2 project before the launch timeout.",
		Retryable:   true,
		SafeToRetry: true,
		ProjectRoot: context.ProjectRoot,
		Command:     context.Command,
		NextActions: []string{
			"Check whether the Unity Editor opened this project and is still starting.",
			"If Unity appears stuck, focus the Editor and check the Console or Editor log.",
		},
		Details: map[string]any{
			"LockfilePath":   err.lockfilePath,
			"TimeoutSeconds": int(launchReadinessTimeout.Seconds()),
		},
	}
}

// waitForFreshUnityLockfile waits for the Editor process launched by this command to write its own lockfile.
func waitForFreshUnityLockfile(
	ctx context.Context,
	lockfilePath string,
	startedAt time.Time,
	pollInterval time.Duration,
	timeout time.Duration,
) error {
	timeoutContext, cancel := context.WithTimeout(ctx, timeout)
	defer cancel()
	ticker := time.NewTicker(pollInterval)
	defer ticker.Stop()

	for {
		fileInfo, err := os.Stat(lockfilePath)
		if err == nil && !fileInfo.ModTime().Before(startedAt) {
			return nil
		}
		if err != nil && !os.IsNotExist(err) {
			return err
		}

		select {
		case <-timeoutContext.Done():
			if ctx.Err() != nil {
				return ctx.Err()
			}
			return v2LaunchLockfileTimeoutError{lockfilePath: lockfilePath}
		case <-ticker.C:
		}
	}
}

func waitForV2ProjectOpened(
	ctx context.Context,
	projectRoot string,
	runningProcess *unityprocess.UnityProcess,
	currentPid int,
	stdout io.Writer,
	stderr io.Writer,
	launchStartedAt time.Time,
	previousServerSessionID string,
	spinner *ui.TerminalSpinner,
	deps launchDeps,
) int {
	if err := deps.waitForFreshUnityLockfile(ctx, unityLockfilePath(projectRoot), launchStartedAt, launchLockfilePoll, launchReadinessTimeout); err != nil {
		clierrors.WriteClassifiedError(stderr, err, clierrors.ErrorContext{ProjectRoot: projectRoot, Command: clicore.LaunchCommandName})
		return 1
	}
	// Why: V2 server auto-start is scheduled on EditorApplication.delayCall. A CLI-spawned,
	// backgrounded idle Editor may never tick on its own, so delayCall never runs
	// (V3 documents this and uses EditorApplicationTickBridge.SignalTick —
	// Packages/src/Editor/Infrastructure/Server/UnityCliLoopServerController.cs:369-372).
	// V2 has no equivalent workaround, so focus once after the lockfile gate. Focus failure
	// is non-fatal: log and continue into the readiness probe.
	logLaunchExistingFocusWithDeps(ctx, projectRoot, currentPid, deps)
	writeLaunchReadinessWait(stdout, spinner)
	if err := deps.waitForV2ServerReady(ctx, projectRoot, previousServerSessionID, launchV2ServerReadyPoll, launchReadinessTimeout); err != nil {
		spinner.Stop()
		clierrors.WriteClassifiedError(stderr, err, clierrors.ErrorContext{ProjectRoot: projectRoot, Command: clicore.LaunchCommandName})
		return 1
	}
	spinner.Stop()
	var previousPid *int
	if runningProcess != nil {
		previousPid = &runningProcess.Pid
	}
	return writeLaunchedV2ReadyResponse(stdout, stderr, projectRoot, previousPid, currentPid)
}
