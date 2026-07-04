package clicore

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"time"

	"github.com/hatayama/unity-cli-loop/common/project"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

const (
	ToolReadinessTimeout      = 180 * time.Second
	ToolReadinessPoll         = 1 * time.Second
	ToolReadinessProbeTimeout = 5 * time.Second
	toolReadinessProbeCount   = 3

	DomainReloadWaitParam = "WaitForDomainReload"
)

const executeDynamicCodeReadinessProbe = `return "Unity CLI Loop dynamic code prewarm";`

type toolReadinessDeps struct {
	findRunningUnityProcess    func(context.Context, string) (*UnityProcess, error)
	probeToolReadinessSequence func(context.Context, string) error
}

func WaitForToolReadiness(ctx context.Context, projectRoot string) error {
	return WaitForToolReadinessWithTimeout(ctx, projectRoot, ToolReadinessTimeout)
}

func WaitForToolReadinessWithTimeout(ctx context.Context, projectRoot string, timeout time.Duration) error {
	return waitForToolReadinessWithDeps(ctx, projectRoot, timeout, defaultToolReadinessDeps())
}

func defaultToolReadinessDeps() toolReadinessDeps {
	return toolReadinessDeps{
		findRunningUnityProcess:    FindRunningUnityProcess,
		probeToolReadinessSequence: ProbeToolReadinessSequence,
	}
}

func waitForToolReadinessWithDeps(ctx context.Context, projectRoot string, timeout time.Duration, deps toolReadinessDeps) error {
	// Why: launch and compile can both recreate Unity's project IPC server; a real
	// tool request proves the user-visible command will not be the cold transport probe.
	timeoutContext, cancel := context.WithTimeout(ctx, timeout)
	defer cancel()

	var lastErr error
	ticker := time.NewTicker(ToolReadinessPoll)
	defer ticker.Stop()
	for {
		if err := deps.probeToolReadinessSequence(timeoutContext, projectRoot); err == nil {
			return nil
		} else {
			if IsReadinessCLIUpdateRequiredError(err) {
				return err
			}
			lastErr = err
		}

		select {
		case <-timeoutContext.Done():
			return toolReadinessDoneErrorWithDeps(ctx, projectRoot, lastErr, deps)
		case <-ticker.C:
		}
	}
}

func toolReadinessDoneError(ctx context.Context, projectRoot string, cause error) error {
	return toolReadinessDoneErrorWithDeps(ctx, projectRoot, cause, defaultToolReadinessDeps())
}

func toolReadinessDoneErrorWithDeps(ctx context.Context, projectRoot string, cause error, deps toolReadinessDeps) error {
	if err := ctx.Err(); err != nil {
		return err
	}
	runningProcess, err := deps.findRunningUnityProcess(context.Background(), projectRoot)
	if err == nil && runningProcess != nil {
		return UnityServerNotRespondingError{
			ProjectRoot: projectRoot,
			Endpoint:    resolveProjectEndpointAddress(projectRoot),
			Cause:       cause,
		}
	}
	if cause != nil {
		return fmt.Errorf("timed out waiting for Unity tool readiness: %w", cause)
	}
	return fmt.Errorf("timed out waiting for Unity tool readiness")
}

func IsReadinessCLIUpdateRequiredError(err error) bool {
	var rpcErr *unityipc.RPCError
	if !errors.As(err, &rpcErr) || len(rpcErr.Data) == 0 {
		return false
	}

	var data any
	if json.Unmarshal(rpcErr.Data, &data) != nil {
		return false
	}
	typedData, ok := data.(map[string]any)
	if !ok {
		return false
	}
	return RPCDataType(typedData) == "cli_update_required"
}

func ProbeToolReadinessSequence(ctx context.Context, projectRoot string) error {
	// The tool catalog is read from disk; it can change between poll ticks (Unity writes
	// it during server startup) but not within one probe sequence, so read it once here
	// instead of once per probe.
	executeDynamicCodeAvailable := isExecuteDynamicCodeAvailable(projectRoot)
	for probeIndex := 0; probeIndex < toolReadinessProbeCount; probeIndex++ {
		if err := probeToolReadiness(ctx, projectRoot, executeDynamicCodeAvailable); err != nil {
			return err
		}
	}

	return nil
}

func probeToolReadiness(ctx context.Context, projectRoot string, executeDynamicCodeAvailable bool) error {
	probeContext, cancel := context.WithTimeout(ctx, ToolReadinessProbeTimeout)
	defer cancel()

	connection, err := project.ResolveConnection(projectRoot, projectRoot)
	if err != nil {
		return err
	}

	if !executeDynamicCodeAvailable {
		_, err := unityipc.NewClient(connection, Version()).Send(probeContext, "get-version", map[string]any{})
		return err
	}

	response, err := unityipc.NewClient(connection, Version()).Send(probeContext, "execute-dynamic-code", executeDynamicCodeReadinessProbeParams())
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
		DomainReloadWaitParam:       false,
		"YieldToForegroundRequests": false,
	}
}

type executeDynamicCodeReadinessResponse struct {
	Success      bool   `json:"Success"`
	ErrorMessage string `json:"ErrorMessage"`
}

func isExecuteDynamicCodeAvailable(projectRoot string) bool {
	cache, err := LoadTools(projectRoot)
	if err != nil {
		return true
	}
	_, ok := FindTool(cache, "execute-dynamic-code")
	return ok
}
