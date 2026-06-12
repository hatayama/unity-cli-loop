package cli

import (
	"context"
	"encoding/json"
	"fmt"
	"time"

	"github.com/hatayama/unity-cli-loop/cli/internal/project"
	"github.com/hatayama/unity-cli-loop/cli/internal/unityipc"
)

const (
	toolReadinessTimeout      = 180 * time.Second
	toolReadinessPoll         = 1 * time.Second
	toolReadinessProbeTimeout = 5 * time.Second
	toolReadinessProbeCount   = 3
)

const executeDynamicCodeReadinessProbe = `return "Unity CLI Loop dynamic code prewarm";`

var findRunningUnityProcessForReadiness = findRunningUnityProcess

func waitForToolReadiness(ctx context.Context, projectRoot string) error {
	// Why: launch and compile can both recreate Unity's project IPC server; a real
	// tool request proves the user-visible command will not be the cold transport probe.
	timeoutContext, cancel := context.WithTimeout(ctx, toolReadinessTimeout)
	defer cancel()

	var lastErr error
	ticker := time.NewTicker(toolReadinessPoll)
	defer ticker.Stop()
	for {
		if err := probeToolReadinessSequence(timeoutContext, projectRoot); err == nil {
			return nil
		} else {
			lastErr = err
		}

		select {
		case <-timeoutContext.Done():
			return toolReadinessDoneError(ctx, projectRoot, lastErr)
		case <-ticker.C:
		}
	}
}

func toolReadinessDoneError(ctx context.Context, projectRoot string, cause error) error {
	if err := ctx.Err(); err != nil {
		return err
	}
	runningProcess, err := findRunningUnityProcessForReadiness(context.Background(), projectRoot)
	if err == nil && runningProcess != nil {
		return unityServerNotRespondingError{
			projectRoot: projectRoot,
			endpoint:    resolveProjectEndpointAddress(projectRoot),
			cause:       cause,
		}
	}
	if cause != nil {
		return fmt.Errorf("timed out waiting for Unity tool readiness: %w", cause)
	}
	return fmt.Errorf("timed out waiting for Unity tool readiness")
}

func probeToolReadinessSequence(ctx context.Context, projectRoot string) error {
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
	probeContext, cancel := context.WithTimeout(ctx, toolReadinessProbeTimeout)
	defer cancel()

	connection, err := project.ResolveConnection(projectRoot, projectRoot)
	if err != nil {
		return err
	}

	if !executeDynamicCodeAvailable {
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
