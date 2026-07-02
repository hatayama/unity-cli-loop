package cli

import (
	"context"
	"encoding/json"
	"fmt"
	"io"
	"strings"
	"time"

	"github.com/hatayama/unity-cli-loop/cli/internal/clicore"
	"github.com/hatayama/unity-cli-loop/cli/internal/unityipc"
)

const (
	controlPlayModeCommandName     = "control-play-mode"
	controlPlayModeActionParam     = "Action"
	controlPlayModeTimeoutParam    = "TimeoutSeconds"
	controlPlayModeStatusOnlyParam = "StatusOnly"
	controlPlayModeDefaultTimeout  = 180
	controlPlayModeStatusTimeout   = 5 * time.Second
)

var controlPlayModeStatePoll = 50 * time.Millisecond

type controlPlayModeResponse struct {
	IsPlaying              bool                          `json:"IsPlaying"`
	IsPaused               bool                          `json:"IsPaused"`
	Changed                bool                          `json:"Changed"`
	WasAlreadyStopped      bool                          `json:"WasAlreadyStopped"`
	BlockedByCompileErrors bool                          `json:"BlockedByCompileErrors"`
	CompileErrorCount      int                           `json:"CompileErrorCount"`
	CompileErrors          []controlPlayModeCompileError `json:"CompileErrors"`
	Message                string                        `json:"Message"`
}

type controlPlayModeCompileError struct {
	Message string `json:"Message"`
	File    string `json:"File"`
	Line    int    `json:"Line"`
}

func shouldWaitForControlPlayModeState(command string, params map[string]any) bool {
	if command != controlPlayModeCommandName {
		return false
	}
	return controlPlayModeActionCanWait(controlPlayModeAction(params))
}

func runControlPlayModeWithStateWait(
	ctx context.Context,
	connection unityipc.Connection,
	params map[string]any,
	stdout io.Writer,
	stderr io.Writer,
) int {
	action := controlPlayModeAction(params)
	timeout, timeoutSeconds := controlPlayModeTimeout(params)
	startedAt := time.Now()
	spinner := clicore.NewToolSpinner(stderr, controlPlayModeCommandName)
	outcome, err := sendWithTransientConnectionRetry(
		ctx,
		connection,
		controlPlayModeCommandName,
		params,
		clicore.NewSpinnerProgressFunc(spinner, "Executing control-play-mode..."),
	)

	initialResponse := controlPlayModeResponse{}
	hasInitialResponse := false
	if err == nil {
		var decodeErr error
		initialResponse, decodeErr = decodeControlPlayModeResponse(outcome.Result)
		if decodeErr != nil {
			spinner.Stop()
			clicore.WriteClassifiedError(stderr, decodeErr, clicore.ErrorContext{
				ProjectRoot: connection.ProjectRoot,
				Command:     controlPlayModeCommandName,
			})
			return 1
		}
		hasInitialResponse = true
		if initialResponse.BlockedByCompileErrors {
			spinner.Stop()
			writeDebugTiming(stderr, controlPlayModeCommandName, time.Since(startedAt), outcome)
			clicore.WriteErrorEnvelope(stderr, controlPlayModeCompileErrorsError(connection.ProjectRoot, action, initialResponse))
			return 1
		}
		if controlPlayModeStateMatches(action, initialResponse) {
			spinner.Stop()
			clicore.WriteJSON(stdout, outcome.Result)
			writeDebugTiming(stderr, controlPlayModeCommandName, time.Since(startedAt), outcome)
			return 0
		}
	} else if !shouldWaitForControlPlayModeDisconnect(err, outcome) {
		spinner.Stop()
		writeDebugTiming(stderr, controlPlayModeCommandName, time.Since(startedAt), outcome)
		clicore.WriteToolFailure(stderr, err, outcome, clicore.ErrorContext{
			ProjectRoot: connection.ProjectRoot,
			Command:     controlPlayModeCommandName,
		})
		return 1
	}

	spinner.Update("Waiting for play mode state...")
	response, completed, waitErr := waitForControlPlayModeState(ctx, connection, action, timeout)
	spinner.Stop()
	if waitErr != nil {
		writeDebugTiming(stderr, controlPlayModeCommandName, time.Since(startedAt), outcome)
		clicore.WriteClassifiedError(stderr, waitErr, clicore.ErrorContext{
			ProjectRoot: connection.ProjectRoot,
			Command:     controlPlayModeCommandName,
		})
		return 1
	}

	if !completed {
		if response.BlockedByCompileErrors {
			writeDebugTiming(stderr, controlPlayModeCommandName, time.Since(startedAt), outcome)
			clicore.WriteErrorEnvelope(stderr, controlPlayModeCompileErrorsError(connection.ProjectRoot, action, response))
			return 1
		}

		writeDebugTiming(stderr, controlPlayModeCommandName, time.Since(startedAt), outcome)
		clicore.WriteErrorEnvelope(stderr, controlPlayModeWaitTimeoutError(connection.ProjectRoot, action, timeoutSeconds, response))
		return 1
	}

	response.Message = completedControlPlayModeMessage(action, initialResponse, hasInitialResponse)
	if hasInitialResponse {
		response.Changed = initialResponse.Changed
		response.WasAlreadyStopped = initialResponse.WasAlreadyStopped
	}
	result, marshalErr := json.Marshal(response)
	if marshalErr != nil {
		clicore.WriteClassifiedError(stderr, marshalErr, clicore.ErrorContext{
			ProjectRoot: connection.ProjectRoot,
			Command:     controlPlayModeCommandName,
		})
		return 1
	}
	clicore.WriteJSON(stdout, result)
	writeDebugTiming(stderr, controlPlayModeCommandName, time.Since(startedAt), outcome)
	return 0
}

func waitForControlPlayModeState(
	ctx context.Context,
	connection unityipc.Connection,
	action string,
	timeout time.Duration,
) (controlPlayModeResponse, bool, error) {
	waitContext, cancel := context.WithTimeout(ctx, timeout)
	defer cancel()

	lastResponse := controlPlayModeResponse{}
	var lastErr error
	hasResponse := false
	ticker := time.NewTicker(controlPlayModeStatePoll)
	defer ticker.Stop()
	for {
		response, err := requestControlPlayModeStatus(waitContext, connection, action)
		if err == nil {
			lastResponse = response
			hasResponse = true
			if strings.EqualFold(action, "Play") && response.BlockedByCompileErrors {
				return response, false, nil
			}
			if controlPlayModeStateMatches(action, response) {
				return response, true, nil
			}
		} else {
			lastErr = err
		}

		select {
		case <-waitContext.Done():
			if ctx.Err() != nil {
				return lastResponse, false, ctx.Err()
			}
			if hasResponse {
				return lastResponse, false, nil
			}
			if lastErr != nil {
				return lastResponse, false, fmt.Errorf("timed out waiting for play mode state: %w", lastErr)
			}
			return lastResponse, false, fmt.Errorf("timed out waiting for play mode state")
		case <-ticker.C:
		}
	}
}

func requestControlPlayModeStatus(
	ctx context.Context,
	connection unityipc.Connection,
	action string,
) (controlPlayModeResponse, error) {
	probeContext, cancel := context.WithTimeout(ctx, controlPlayModeStatusTimeout)
	defer cancel()

	result, err := unityipc.NewClient(connection, clicore.Version).Send(
		probeContext,
		controlPlayModeCommandName,
		map[string]any{
			controlPlayModeActionParam:     action,
			controlPlayModeStatusOnlyParam: true,
		},
	)
	if err != nil {
		return controlPlayModeResponse{}, err
	}

	return decodeControlPlayModeResponse(result)
}

func decodeControlPlayModeResponse(result []byte) (controlPlayModeResponse, error) {
	response := controlPlayModeResponse{}
	if err := json.Unmarshal(result, &response); err != nil {
		return controlPlayModeResponse{}, err
	}
	return response, nil
}

func shouldWaitForControlPlayModeDisconnect(err error, outcome unityipc.UnitySendOutcome) bool {
	if err == nil {
		return false
	}
	if !outcome.RequestDispatched {
		return false
	}
	return clicore.IsTransportDisconnectError(err)
}

func controlPlayModeAction(params map[string]any) string {
	value, ok := params[controlPlayModeActionParam].(string)
	if ok && value != "" {
		return value
	}
	return "Play"
}

func controlPlayModeTimeout(params map[string]any) (time.Duration, int) {
	seconds := controlPlayModeTimeoutSeconds(params)
	return time.Duration(seconds) * time.Second, seconds
}

func controlPlayModeTimeoutSeconds(params map[string]any) int {
	value, ok := params[controlPlayModeTimeoutParam]
	if !ok {
		return controlPlayModeDefaultTimeout
	}

	seconds := 0
	switch typedValue := value.(type) {
	case int:
		seconds = typedValue
	case int64:
		if typedValue <= maxIntValue() {
			seconds = int(typedValue)
		}
	case float64:
		if typedValue <= float64(maxIntValue()) {
			seconds = int(typedValue)
		}
	}
	if seconds > 0 {
		return seconds
	}
	return controlPlayModeDefaultTimeout
}

func controlPlayModeActionCanWait(action string) bool {
	return strings.EqualFold(action, "Play") ||
		strings.EqualFold(action, "Stop") ||
		strings.EqualFold(action, "Pause")
}

func controlPlayModeStateMatches(action string, response controlPlayModeResponse) bool {
	switch {
	case strings.EqualFold(action, "Play"):
		return response.IsPlaying && !response.IsPaused
	case strings.EqualFold(action, "Stop"):
		return !response.IsPlaying && !response.IsPaused
	case strings.EqualFold(action, "Pause"):
		return response.IsPaused
	default:
		return false
	}
}

func completedControlPlayModeMessage(action string, initialResponse controlPlayModeResponse, hasInitialResponse bool) string {
	if hasInitialResponse && initialResponse.Message != "" {
		return initialResponse.Message
	}
	switch {
	case strings.EqualFold(action, "Stop"):
		return "Play mode stopped"
	case strings.EqualFold(action, "Pause"):
		return "Play mode paused"
	default:
		return "Play mode started"
	}
}

func requestedControlPlayModeMessage(action string) string {
	switch {
	case strings.EqualFold(action, "Stop"):
		return "Play mode stop"
	case strings.EqualFold(action, "Pause"):
		return "Play mode pause"
	default:
		return "Play mode start"
	}
}

func controlPlayModeWaitTimeoutError(
	projectRoot string,
	action string,
	timeoutSeconds int,
	response controlPlayModeResponse,
) clicore.CLIError {
	return clicore.CLIError{
		ErrorCode:   clicore.ErrorCodeControlPlayModeWaitTimeout,
		Phase:       clicore.ErrorPhaseResponseWaiting,
		Message:     fmt.Sprintf("%s requested but did not complete within %ds", requestedControlPlayModeMessage(action), timeoutSeconds),
		Retryable:   true,
		SafeToRetry: true,
		ProjectRoot: projectRoot,
		Command:     controlPlayModeCommandName,
		NextActions: []string{
			"Check Unity Console logs for PlayMode transition errors.",
			"Retry `uloop control-play-mode` after Unity finishes compiling, reloading scripts, or entering PlayMode.",
		},
		Details: map[string]any{
			"RequestedAction": action,
			"IsPlaying":       response.IsPlaying,
			"IsPaused":        response.IsPaused,
			"TimeoutSeconds":  timeoutSeconds,
		},
	}
}

func controlPlayModeCompileErrorsError(
	projectRoot string,
	action string,
	response controlPlayModeResponse,
) clicore.CLIError {
	compileErrorCount := response.CompileErrorCount
	if compileErrorCount == 0 && len(response.CompileErrors) > 0 {
		compileErrorCount = len(response.CompileErrors)
	}

	return clicore.CLIError{
		ErrorCode:   clicore.ErrorCodeControlPlayModeCompileErrors,
		Phase:       clicore.ErrorPhaseExecution,
		Message:     "Play mode start was blocked because Unity has compiler errors.",
		Retryable:   false,
		SafeToRetry: true,
		ProjectRoot: projectRoot,
		Command:     controlPlayModeCommandName,
		NextActions: []string{
			"Fix the compiler errors reported in the details.",
			"Run `uloop compile` to verify the project compiles, then retry `uloop control-play-mode --action Play`.",
		},
		Details: map[string]any{
			"RequestedAction":   action,
			"CompileErrorCount": compileErrorCount,
			"CompileErrors":     response.CompileErrors,
			"Message":           response.Message,
		},
	}
}

func maxIntValue() int64 {
	return int64(^uint(0) >> 1)
}
