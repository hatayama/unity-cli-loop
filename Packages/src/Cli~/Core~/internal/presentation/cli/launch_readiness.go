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
	launchReadinessTimeout = 180 * time.Second
	launchReadinessPoll    = 1 * time.Second
	launchProbeTimeout     = 5 * time.Second
	launchReadyProbeCount  = 3
)

const launchDynamicCodeProbe = `return "Unity CLI Loop dynamic code prewarm";`

func waitForLaunchReady(ctx context.Context, projectRoot string) error {
	timeoutContext, cancel := context.WithTimeout(ctx, launchReadinessTimeout)
	defer cancel()

	for {
		if err := probeLaunchReadySequence(timeoutContext, projectRoot); err == nil {
			return nil
		}

		select {
		case <-timeoutContext.Done():
			return fmt.Errorf("timed out waiting for Unity to become ready after launch")
		case <-time.After(launchReadinessPoll):
		}
	}
}

func probeLaunchReadySequence(ctx context.Context, projectRoot string) error {
	for probeIndex := 0; probeIndex < launchReadyProbeCount; probeIndex++ {
		if err := probeLaunchReady(ctx, projectRoot); err != nil {
			return err
		}
	}

	return nil
}

func probeLaunchReady(ctx context.Context, projectRoot string) error {
	probeContext, cancel := context.WithTimeout(ctx, launchProbeTimeout)
	defer cancel()

	connection, err := project.ResolveConnection(projectRoot, projectRoot)
	if err != nil {
		return err
	}

	if !isExecuteDynamicCodeAvailable(projectRoot) {
		_, err := unity.NewClient(connection).Send(probeContext, "get-version", map[string]any{})
		return err
	}

	response, err := unity.NewClient(connection).Send(probeContext, "execute-dynamic-code", launchDynamicCodeProbeParams())
	if err != nil {
		return err
	}

	var payload executeDynamicCodeLaunchResponse
	if err := json.Unmarshal(response, &payload); err != nil {
		return err
	}
	if !payload.Success {
		if payload.ErrorMessage != "" {
			return fmt.Errorf("execute-dynamic-code launch readiness probe failed: %s", payload.ErrorMessage)
		}
		return fmt.Errorf("execute-dynamic-code launch readiness probe failed")
	}
	return nil
}

func launchDynamicCodeProbeParams() map[string]any {
	return map[string]any{
		"Code":                      launchDynamicCodeProbe,
		"CompileOnly":               false,
		"YieldToForegroundRequests": false,
	}
}

type executeDynamicCodeLaunchResponse struct {
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
