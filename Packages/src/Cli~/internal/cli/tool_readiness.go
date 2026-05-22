package cli

import (
	"context"
	"encoding/json"
	"fmt"
	"time"

	"github.com/hatayama/unity-cli-loop/Packages/src/Cli/internal/project"
	"github.com/hatayama/unity-cli-loop/Packages/src/Cli/internal/unityipc"
)

const (
	toolReadinessTimeout      = 180 * time.Second
	toolReadinessPoll         = 1 * time.Second
	toolReadinessProbeTimeout = 5 * time.Second
	toolReadinessProbeCount   = 3
)

const executeDynamicCodeReadinessProbe = `return "Unity CLI Loop dynamic code prewarm";`

type toolReadinessWaitMode int

var (
	findRunningUnityProcessForReadiness = findRunningUnityProcess
	stoppedServerStateGrace             = 2 * time.Second
	stoppedServerStatePoll              = 100 * time.Millisecond
)

const (
	toolReadinessWaitThroughStopped toolReadinessWaitMode = iota
	toolReadinessStopWhenServerStops
)

func waitForToolReadiness(ctx context.Context, projectRoot string) error {
	return waitForToolReadinessWithMode(ctx, projectRoot, toolReadinessWaitThroughStopped)
}

func waitForRecoveringToolReadiness(ctx context.Context, projectRoot string) error {
	return waitForToolReadinessWithMode(ctx, projectRoot, toolReadinessStopWhenServerStops)
}

func waitForToolReadinessWithMode(ctx context.Context, projectRoot string, mode toolReadinessWaitMode) error {
	// Why: launch and compile can both recreate Unity's project IPC server; a real
	// tool request proves the user-visible command will not be the cold transport probe.
	timeoutContext, cancel := context.WithTimeout(ctx, toolReadinessTimeout)
	defer cancel()

	for {
		state, ok, err := readServerState(projectRoot)
		if err != nil {
			return err
		}
		if ok {
			if failure := serverStateFailureError(state); failure != nil {
				return failure
			}
			if mode == toolReadinessStopWhenServerStops && isServerStateStopped(state) {
				return serverStoppedError{state: state}
			}
			if mode == toolReadinessWaitThroughStopped && isServerStateStopped(state) {
				changed, err := waitForStoppedServerStateChange(timeoutContext, ctx, projectRoot, state)
				if err != nil {
					return err
				}
				if !changed {
					stale, err := isStoppedServerStateStale(timeoutContext, projectRoot)
					if err != nil {
						return err
					}
					if stale {
						return serverStoppedError{state: state}
					}
					select {
					case <-timeoutContext.Done():
						return toolReadinessDoneError(ctx)
					case <-time.After(toolReadinessPoll):
					}
				}
				continue
			}
			if isServerStateBusy(state) {
				stale, err := isBusyServerStateStale(timeoutContext, projectRoot)
				if err != nil {
					return err
				}
				if stale {
					return staleServerStateError{state: state}
				}
				select {
				case <-timeoutContext.Done():
					return toolReadinessDoneError(ctx)
				case <-time.After(toolReadinessPoll):
				}
				continue
			}
		}

		if err := probeToolReadinessSequence(timeoutContext, projectRoot); err == nil {
			return nil
		}

		select {
		case <-timeoutContext.Done():
			return toolReadinessDoneError(ctx)
		case <-time.After(toolReadinessPoll):
		}
	}
}

func waitForStoppedServerStateChange(timeoutCtx context.Context, parentCtx context.Context, projectRoot string, initialState serverState) (bool, error) {
	if stoppedServerStateGrace <= 0 {
		return false, nil
	}

	graceTimer := time.NewTimer(stoppedServerStateGrace)
	defer graceTimer.Stop()

	ticker := time.NewTicker(stoppedServerStatePoll)
	defer ticker.Stop()

	for {
		select {
		case <-timeoutCtx.Done():
			return false, toolReadinessDoneError(parentCtx)
		case <-graceTimer.C:
			return false, nil
		case <-ticker.C:
			state, ok, err := readServerState(projectRoot)
			if err != nil {
				return false, err
			}
			if !ok || !isSameStoppedServerState(state, initialState) {
				return true, nil
			}
		}
	}
}

func isSameStoppedServerState(state serverState, expected serverState) bool {
	return state.Phase == "stopped" &&
		expected.Phase == "stopped" &&
		state.GenerationID == expected.GenerationID &&
		state.Reason == expected.Reason
}

func isStoppedServerStateStale(ctx context.Context, projectRoot string) (bool, error) {
	runningProcess, err := findRunningUnityProcessForReadiness(ctx, projectRoot)
	if err != nil {
		return false, err
	}
	return runningProcess == nil, nil
}

func isBusyServerStateStale(ctx context.Context, projectRoot string) (bool, error) {
	runningProcess, err := findRunningUnityProcessForReadiness(ctx, projectRoot)
	if err != nil {
		return false, err
	}
	return runningProcess == nil, nil
}

func toolReadinessDoneError(ctx context.Context) error {
	if err := ctx.Err(); err != nil {
		return err
	}
	return fmt.Errorf("timed out waiting for Unity tool readiness")
}

func probeToolReadinessSequence(ctx context.Context, projectRoot string) error {
	for probeIndex := 0; probeIndex < toolReadinessProbeCount; probeIndex++ {
		if err := probeToolReadiness(ctx, projectRoot); err != nil {
			return err
		}
	}

	return nil
}

func probeToolReadiness(ctx context.Context, projectRoot string) error {
	probeContext, cancel := context.WithTimeout(ctx, toolReadinessProbeTimeout)
	defer cancel()

	connection, err := project.ResolveConnection(projectRoot, projectRoot)
	if err != nil {
		return err
	}

	if !isExecuteDynamicCodeAvailable(projectRoot) {
		_, err := unityipc.NewClient(connection, version).Send(probeContext, "get-version", map[string]any{})
		return err
	}

	response, err := unityipc.NewClient(connection, version).Send(probeContext, "execute-dynamic-code", executeDynamicCodeReadinessProbeParams())
	if err != nil {
		return err
	}

	var payload executeDynamicCodeReadinessResponse
	if err := json.Unmarshal(response, &payload); err != nil {
		return err
	}
	if !payload.Success {
		if payload.ErrorMessage != "" {
			return fmt.Errorf("execute-dynamic-code readiness probe failed: %s", payload.ErrorMessage)
		}
		return fmt.Errorf("execute-dynamic-code readiness probe failed")
	}
	return nil
}

func executeDynamicCodeReadinessProbeParams() map[string]any {
	return map[string]any{
		"Code":                      executeDynamicCodeReadinessProbe,
		"CompileOnly":               false,
		domainReloadWaitParam:       false,
		"YieldToForegroundRequests": false,
	}
}

type executeDynamicCodeReadinessResponse struct {
	Success      bool   `json:"Success"`
	ErrorMessage string `json:"ErrorMessage"`
}

func isExecuteDynamicCodeAvailable(projectRoot string) bool {
	cache, err := loadTools(projectRoot)
	if err != nil {
		return true
	}
	_, ok := findTool(cache, "execute-dynamic-code")
	return ok
}
