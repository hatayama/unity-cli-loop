package dispatcher

import (
	"bufio"
	"context"
	"encoding/json"
	"fmt"
	"time"

	"github.com/hatayama/unity-cli-loop/Packages/src/Cli/Shared/adapters/framing"
	"github.com/hatayama/unity-cli-loop/Packages/src/Cli/Shared/adapters/project"
	"github.com/hatayama/unity-cli-loop/Packages/src/Cli/Shared/domain"
)

const (
	executeDynamicCodeReadinessProbe = `return "Unity CLI Loop dynamic code prewarm";`
	toolReadinessProbeCount          = 3
)

type toolReadinessRequest struct {
	JSONRPC string         `json:"jsonrpc"`
	Method  string         `json:"method"`
	Params  map[string]any `json:"params"`
	ID      int            `json:"id"`
}

type toolReadinessResponse struct {
	Result json.RawMessage        `json:"result,omitempty"`
	Error  *toolReadinessRPCError `json:"error,omitempty"`
	ID     int                    `json:"id"`
}

type toolReadinessRPCError struct {
	Message string `json:"message"`
}

type executeDynamicCodeReadinessResponse struct {
	Success      bool   `json:"Success"`
	ErrorMessage string `json:"ErrorMessage"`
}

func waitForToolReadiness(ctx context.Context, projectRoot string) error {
	// Why: dispatcher launch returns directly to the user; probing a real tool request
	// prevents the first command after launch from paying the cold project IPC path.
	timeoutContext, cancel := context.WithTimeout(ctx, toolReadinessTimeout)
	defer cancel()

	ticker := time.NewTicker(toolReadinessPoll)
	defer ticker.Stop()

	for {
		if err := probeToolReadinessSequence(timeoutContext, projectRoot); err == nil {
			return nil
		}

		select {
		case <-timeoutContext.Done():
			if ctx.Err() != nil {
				return ctx.Err()
			}
			return fmt.Errorf("timed out waiting for Unity tool readiness")
		case <-ticker.C:
		}
	}
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

	if isExecuteDynamicCodeAvailable(projectRoot) {
		return probeExecuteDynamicCodeReadiness(probeContext, connection)
	}
	return probeVersionReadiness(probeContext, connection)
}

func probeVersionReadiness(ctx context.Context, connection domain.Connection) error {
	response, err := sendToolReadinessRequest(ctx, connection, "get-version", map[string]any{})
	if err != nil {
		return err
	}
	if len(response.Result) == 0 {
		return fmt.Errorf("tool readiness probe returned no result")
	}
	return nil
}

func probeExecuteDynamicCodeReadiness(ctx context.Context, connection domain.Connection) error {
	response, err := sendToolReadinessRequest(ctx, connection, "execute-dynamic-code", executeDynamicCodeReadinessProbeParams())
	if err != nil {
		return err
	}

	var payload executeDynamicCodeReadinessResponse
	if err := json.Unmarshal(response.Result, &payload); err != nil {
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

func sendToolReadinessRequest(ctx context.Context, connection domain.Connection, method string, params map[string]any) (toolReadinessResponse, error) {
	conn, err := dialToolReadinessEndpoint(ctx, connection.Endpoint)
	if err != nil {
		return toolReadinessResponse{}, err
	}
	defer func() {
		_ = conn.Close()
	}()

	if deadline, ok := ctx.Deadline(); ok {
		_ = conn.SetDeadline(deadline)
	}

	payload, err := json.Marshal(toolReadinessRequest{
		JSONRPC: "2.0",
		Method:  method,
		Params:  params,
		ID:      1,
	})
	if err != nil {
		return toolReadinessResponse{}, err
	}
	if err := framing.Write(conn, payload); err != nil {
		return toolReadinessResponse{}, err
	}

	responsePayload, err := framing.Read(bufio.NewReader(conn))
	if err != nil {
		return toolReadinessResponse{}, err
	}

	var response toolReadinessResponse
	if err := json.Unmarshal(responsePayload, &response); err != nil {
		return toolReadinessResponse{}, err
	}
	if response.Error != nil {
		return toolReadinessResponse{}, fmt.Errorf("tool readiness probe failed: %s", response.Error.Message)
	}
	return response, nil
}

func isExecuteDynamicCodeAvailable(projectRoot string) bool {
	cache, ok := loadCachedTools(projectRoot)
	if !ok {
		return false
	}
	for _, tool := range cache.Tools {
		if tool.Name == "execute-dynamic-code" {
			return true
		}
	}
	return false
}
