package cli

import (
	"context"
	"encoding/json"
	"fmt"
	"io"
	"strings"
	"time"

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
	IsPlaying         bool   `json:"IsPlaying"`
	IsPaused          bool   `json:"IsPaused"`
	Changed           bool   `json:"Changed"`
	WasAlreadyStopped bool   `json:"WasAlreadyStopped"`
	Message           string `json:"Message"`
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
	spinner := newToolSpinner(stderr, controlPlayModeCommandName)
	outcome, err := sendWithTransientConnectionRetry(
		ctx,
		connection,
		controlPlayModeCommandName,
		params,
		newSpinnerProgressFunc(spinner, "Executing control-play-mode..."),
	)

	initialResponse := controlPlayModeResponse{}
	hasInitialResponse := false
	if err == nil {
		var decodeErr error
		initialResponse, decodeErr = decodeControlPlayModeResponse(outcome.Result)
		if decodeErr != nil {
			spinner.Stop()
			writeClassifiedError(stderr, decodeErr, errorContext{
				projectRoot: connection.ProjectRoot,
				command:     controlPlayModeCommandName,
			})
			return 1
		}
		hasInitialResponse = true
		if controlPlayModeStateMatches(action, initialResponse) {
			spinner.Stop()
			writeJSON(stdout, outcome.Result)
			writeDebugTiming(stderr, controlPlayModeCommandName, time.Since(startedAt), outcome)
			return 0
		}
	} else if !shouldWaitForControlPlayModeDisconnect(err, outcome) {
		spinner.Stop()
		writeDebugTiming(stderr, controlPlayModeCommandName, time.Since(startedAt), outcome)
		writeToolFailure(stderr, err, outcome, errorContext{
			projectRoot: connection.ProjectRoot,
			command:     controlPlayModeCommandName,
		})
		return 1
	}

	spinner.Update("Waiting for play mode state...")
	response, completed, waitErr := waitForControlPlayModeState(ctx, connection, action, timeout)
	spinner.Stop()
	if waitErr != nil {
		writeDebugTiming(stderr, controlPlayModeCommandName, time.Since(startedAt), outcome)
		writeClassifiedError(stderr, waitErr, errorContext{
			projectRoot: connection.ProjectRoot,
			command:     controlPlayModeCommandName,
		})
		return 1
	}

	if !completed {
		writeDebugTiming(stderr, controlPlayModeCommandName, time.Since(startedAt), outcome)
		writeErrorEnvelope(stderr, controlPlayModeWaitTimeoutError(connection.ProjectRoot, action, timeoutSeconds, response))
		return 1
	}

	response.Message = completedControlPlayModeMessage(action, initialResponse, hasInitialResponse)
	if hasInitialResponse {
		response.Changed = initialResponse.Changed
		response.WasAlreadyStopped = initialResponse.WasAlreadyStopped
	}
	result, marshalErr := json.Marshal(response)
	if marshalErr != nil {
		writeClassifiedError(stderr, marshalErr, errorContext{
			projectRoot: connection.ProjectRoot,
			command:     controlPlayModeCommandName,
		})
		return 1
	}
	writeJSON(stdout, result)
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
		response, err := requestControlPlayModeStatus(waitContext, connection)
		if err == nil {
			lastResponse = response
			hasResponse = true
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

func requestControlPlayModeStatus(ctx context.Context, connection unityipc.Connection) (controlPlayModeResponse, error) {
	probeContext, cancel := context.WithTimeout(ctx, controlPlayModeStatusTimeout)
	defer cancel()

	result, err := unityipc.NewClient(connection, version).Send(
		probeContext,
		controlPlayModeCommandName,
		map[string]any{controlPlayModeStatusOnlyParam: true},
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
	return isTransportDisconnectError(err)
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
) cliError {
	return cliError{
		ErrorCode:   errorCodeControlPlayModeWaitTimeout,
		Phase:       errorPhaseResponseWaiting,
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
			"requestedAction": action,
			"isPlaying":       response.IsPlaying,
			"isPaused":        response.IsPaused,
			"timeoutSeconds":  timeoutSeconds,
		},
	}
}

func maxIntValue() int64 {
	return int64(^uint(0) >> 1)
}
