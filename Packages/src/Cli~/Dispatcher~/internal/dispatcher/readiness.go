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

const launchDynamicCodeProbe = `UnityEngine.LogType previous = UnityEngine.Debug.unityLogger.filterLogType; UnityEngine.Debug.unityLogger.filterLogType = UnityEngine.LogType.Warning; try { UnityEngine.Debug.Log("Unity CLI Loop dynamic code prewarm"); return "Unity CLI Loop dynamic code prewarm"; } finally { UnityEngine.Debug.unityLogger.filterLogType = previous; }`

type launchReadyRequest struct {
	JSONRPC string                  `json:"jsonrpc"`
	Method  string                  `json:"method"`
	Params  map[string]any          `json:"params"`
	ID      int                     `json:"id"`
	Uloop   *domain.RequestMetadata `json:"x-uloop,omitempty"`
}

type launchReadyResponse struct {
	Result json.RawMessage      `json:"result,omitempty"`
	Error  *launchReadyRPCError `json:"error,omitempty"`
	ID     int                  `json:"id"`
}

type launchReadyRPCError struct {
	Message string `json:"message"`
}

type launchDynamicCodeResponse struct {
	Success      bool   `json:"Success"`
	ErrorMessage string `json:"ErrorMessage"`
}

func waitForLaunchReady(ctx context.Context, projectRoot string) error {
	timeoutContext, cancel := context.WithTimeout(ctx, launchReadinessTimeout)
	defer cancel()

	ticker := time.NewTicker(launchReadinessPoll)
	defer ticker.Stop()

	for {
		if err := probeLaunchReady(timeoutContext, projectRoot); err == nil {
			return nil
		}

		select {
		case <-timeoutContext.Done():
			if ctx.Err() != nil {
				return ctx.Err()
			}
			return fmt.Errorf("timed out waiting for Unity to become ready after launch")
		case <-ticker.C:
		}
	}
}

func probeLaunchReady(ctx context.Context, projectRoot string) error {
	probeContext, cancel := context.WithTimeout(ctx, launchProbeTimeout)
	defer cancel()

	connection, err := project.ResolveConnection(projectRoot, projectRoot)
	if err != nil {
		return err
	}

	if isExecuteDynamicCodeAvailable(projectRoot) {
		return probeLaunchDynamicCode(probeContext, connection)
	}
	return probeLaunchVersion(probeContext, connection)
}

func probeLaunchVersion(ctx context.Context, connection domain.Connection) error {
	response, err := sendLaunchReadyRequest(ctx, connection, "get-version", map[string]any{})
	if err != nil {
		return err
	}
	if len(response.Result) == 0 {
		return fmt.Errorf("launch readiness probe returned no result")
	}
	return nil
}

func probeLaunchDynamicCode(ctx context.Context, connection domain.Connection) error {
	response, err := sendLaunchReadyRequest(ctx, connection, "execute-dynamic-code", map[string]any{
		"Code":                      launchDynamicCodeProbe,
		"CompileOnly":               false,
		"YieldToForegroundRequests": true,
	})
	if err != nil {
		return err
	}

	var payload launchDynamicCodeResponse
	if err := json.Unmarshal(response.Result, &payload); err != nil {
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

func sendLaunchReadyRequest(ctx context.Context, connection domain.Connection, method string, params map[string]any) (launchReadyResponse, error) {
	conn, err := dialLaunchReadyEndpoint(ctx, connection.Endpoint)
	if err != nil {
		return launchReadyResponse{}, err
	}
	defer func() {
		_ = conn.Close()
	}()

	if deadline, ok := ctx.Deadline(); ok {
		_ = conn.SetDeadline(deadline)
	}

	payload, err := json.Marshal(launchReadyRequest{
		JSONRPC: "2.0",
		Method:  method,
		Params:  params,
		ID:      1,
		Uloop:   connection.RequestMetadata,
	})
	if err != nil {
		return launchReadyResponse{}, err
	}
	if err := framing.Write(conn, payload); err != nil {
		return launchReadyResponse{}, err
	}

	responsePayload, err := framing.Read(bufio.NewReader(conn))
	if err != nil {
		return launchReadyResponse{}, err
	}

	var response launchReadyResponse
	if err := json.Unmarshal(responsePayload, &response); err != nil {
		return launchReadyResponse{}, err
	}
	if response.Error != nil {
		return launchReadyResponse{}, fmt.Errorf("launch readiness probe failed: %s", response.Error.Message)
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
