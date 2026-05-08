package cli

import (
	"context"
	"encoding/json"
	"fmt"
	"time"

	"github.com/hatayama/unity-cli-loop/Packages/src/Cli/Core/internal/adapters/unity"
	"github.com/hatayama/unity-cli-loop/Packages/src/Cli/Shared/adapters/project"
)

const (
	toolReadinessTimeout      = 180 * time.Second
	toolReadinessPoll         = 1 * time.Second
	toolReadinessProbeTimeout = 5 * time.Second
	toolReadinessProbeCount   = 3
)

const executeDynamicCodeReadinessProbe = `return "Unity CLI Loop dynamic code prewarm";`

func waitForToolReadiness(ctx context.Context, projectRoot string) error {
	// Why: launch and compile can both recreate Unity's project IPC server; a real
	// tool request proves the user-visible command will not be the cold transport probe.
	timeoutContext, cancel := context.WithTimeout(ctx, toolReadinessTimeout)
	defer cancel()

	for {
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
		_, err := unity.NewClient(connection).Send(probeContext, "get-version", map[string]any{})
		return err
	}

	response, err := unity.NewClient(connection).Send(probeContext, "execute-dynamic-code", executeDynamicCodeReadinessProbeParams())
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
