package cli

import (
	"context"
	"encoding/json"
	"fmt"
	"io"
	"strconv"
	"time"

	"github.com/hatayama/unity-cli-loop/cli/internal/unityipc"
)

const (
	debugBreakWaitCommandName        = "wait-for-debug-break"
	debugBreakStatusUserCommandName  = "debug-break-status"
	debugBreakStatusCommandName      = "get-debug-break-status"
	debugBreakClearStatusCommandName = "clear-debug-break-status"
	debugBreakIDFlagName             = "id"
	debugBreakTimeoutFlagName        = "timeout-seconds"
	debugBreakDefaultTimeoutSeconds  = 30
	debugBreakStatusProbeTimeout     = 5 * time.Second
	debugBreakStatusEnabled          = "Enabled"
	debugBreakStatusHit              = "Hit"
	debugBreakStatusNotEnabled       = "NotEnabled"
	debugBreakStatusExpired          = "Expired"
	debugBreakStatusCleared          = "Cleared"
)

var (
	debugBreakStatusPoll  = 50 * time.Millisecond
	queryDebugBreakStatus = queryDebugBreakStatusFromUnity
	clearDebugBreakStatus = clearDebugBreakStatusFromUnity
)

type waitForDebugBreakOptions struct {
	id             string
	timeoutSeconds int
	timeout        time.Duration
}

type debugBreakStatusOptions struct {
	id string
}

type debugBreakStatusResponse struct {
	Id                              string `json:"Id"`
	Status                          string `json:"Status"`
	IsEnabled                       bool   `json:"IsEnabled"`
	IsHit                           bool   `json:"IsHit"`
	HitCount                        int    `json:"HitCount"`
	TimeoutSeconds                  int    `json:"TimeoutSeconds"`
	ElapsedSinceEnabledMilliseconds int64  `json:"ElapsedSinceEnabledMilliseconds"`
	IsPlaying                       bool   `json:"IsPlaying"`
	IsPaused                        bool   `json:"IsPaused"`
	Message                         string `json:"Message"`
}

type debugBreakWaitState string

const (
	debugBreakWaitStateHit        debugBreakWaitState = "hit"
	debugBreakWaitStateTimeout    debugBreakWaitState = "timeout"
	debugBreakWaitStateNotEnabled debugBreakWaitState = "not_enabled"
	debugBreakWaitStateExpired    debugBreakWaitState = "expired"
	debugBreakWaitStateCleared    debugBreakWaitState = "cleared"
)

func runWaitForDebugBreakCommand(
	ctx context.Context,
	connection unityipc.Connection,
	args []string,
	stdout io.Writer,
	stderr io.Writer,
) int {
	options, err := parseWaitForDebugBreakOptions(args)
	if err != nil {
		writeClassifiedError(stderr, err, errorContext{
			projectRoot: connection.ProjectRoot,
			command:     debugBreakWaitCommandName,
		})
		return 1
	}

	return runWaitForDebugBreak(ctx, connection, options, stdout, stderr)
}

func runDebugBreakStatusCommand(
	ctx context.Context,
	connection unityipc.Connection,
	args []string,
	stdout io.Writer,
	stderr io.Writer,
) int {
	options, err := parseDebugBreakStatusOptions(args)
	if err != nil {
		writeClassifiedError(stderr, err, errorContext{
			projectRoot: connection.ProjectRoot,
			command:     debugBreakStatusUserCommandName,
		})
		return 1
	}

	response, err := queryDebugBreakStatus(ctx, connection, options.id)
	if err != nil {
		writeClassifiedError(stderr, err, errorContext{
			projectRoot: connection.ProjectRoot,
			command:     debugBreakStatusUserCommandName,
		})
		return 1
	}

	result, err := json.Marshal(response)
	if err != nil {
		writeClassifiedError(stderr, err, errorContext{
			projectRoot: connection.ProjectRoot,
			command:     debugBreakStatusUserCommandName,
		})
		return 1
	}

	writeJSON(stdout, result)
	return 0
}

func runWaitForDebugBreak(
	ctx context.Context,
	connection unityipc.Connection,
	options waitForDebugBreakOptions,
	stdout io.Writer,
	stderr io.Writer,
) int {
	startedAt := time.Now()
	spinner := newToolSpinner(stderr, debugBreakWaitCommandName)
	response, state, err := waitForDebugBreak(ctx, connection, options)
	spinner.Stop()
	if err != nil {
		writeClassifiedError(stderr, err, errorContext{
			projectRoot: connection.ProjectRoot,
			command:     debugBreakWaitCommandName,
		})
		return 1
	}

	if state == debugBreakWaitStateHit {
		result, marshalErr := json.Marshal(response)
		if marshalErr != nil {
			writeClassifiedError(stderr, marshalErr, errorContext{
				projectRoot: connection.ProjectRoot,
				command:     debugBreakWaitCommandName,
			})
			return 1
		}
		writeJSON(stdout, result)
		writeDebugTiming(stderr, debugBreakWaitCommandName, time.Since(startedAt), unityipc.UnitySendOutcome{})
		return 0
	}

	if state == debugBreakWaitStateTimeout {
		clearDebugBreakAfterWaitTimeout(ctx, connection, options.id)
	}

	writeErrorEnvelope(stderr, debugBreakWaitError(connection.ProjectRoot, options, response, state))
	return 1
}

func parseWaitForDebugBreakOptions(args []string) (waitForDebugBreakOptions, error) {
	options := waitForDebugBreakOptions{
		timeoutSeconds: debugBreakDefaultTimeoutSeconds,
		timeout:        time.Duration(debugBreakDefaultTimeoutSeconds) * time.Second,
	}

	for index := 0; index < len(args); index++ {
		arg := args[index]
		name, value, consumedNext, err := parseFlagValue(arg, args, index)
		if err != nil {
			return waitForDebugBreakOptions{}, err
		}

		switch name {
		case debugBreakIDFlagName:
			options.id = value
		case debugBreakTimeoutFlagName:
			timeoutSeconds, parseErr := parseDebugBreakTimeoutSeconds(value)
			if parseErr != nil {
				return waitForDebugBreakOptions{}, parseErr
			}
			options.timeoutSeconds = timeoutSeconds
			options.timeout = time.Duration(timeoutSeconds) * time.Second
		default:
			return waitForDebugBreakOptions{}, &argumentError{
				message:     "Unknown option for wait-for-debug-break: --" + name,
				option:      "--" + name,
				command:     debugBreakWaitCommandName,
				nextActions: []string{"Run `uloop wait-for-debug-break --help` to inspect supported options."},
			}
		}

		if consumedNext {
			index++
		}
	}

	if options.id == "" {
		return waitForDebugBreakOptions{}, &argumentError{
			message:      "Missing required option: --id",
			option:       "--" + debugBreakIDFlagName,
			expectedType: "value",
			command:      debugBreakWaitCommandName,
			nextActions:  []string{"Pass `--id <marker-id>` matching UnityCliLoopDebug.Break(\"<marker-id>\")."},
		}
	}

	return options, nil
}

func parseDebugBreakStatusOptions(args []string) (debugBreakStatusOptions, error) {
	options := debugBreakStatusOptions{}

	for index := 0; index < len(args); index++ {
		arg := args[index]
		name, value, consumedNext, err := parseFlagValue(arg, args, index)
		if err != nil {
			return debugBreakStatusOptions{}, err
		}

		switch name {
		case debugBreakIDFlagName:
			options.id = value
		default:
			return debugBreakStatusOptions{}, &argumentError{
				message:     "Unknown option for debug-break-status: --" + name,
				option:      "--" + name,
				command:     debugBreakStatusUserCommandName,
				nextActions: []string{"Run `uloop debug-break-status --help` to inspect supported options."},
			}
		}

		if consumedNext {
			index++
		}
	}

	if options.id == "" {
		return debugBreakStatusOptions{}, &argumentError{
			message:      "Missing required option: --id",
			option:       "--" + debugBreakIDFlagName,
			expectedType: "value",
			command:      debugBreakStatusUserCommandName,
			nextActions:  []string{"Pass `--id <marker-id>` matching UnityCliLoopDebug.Break(\"<marker-id>\")."},
		}
	}

	return options, nil
}

func parseDebugBreakTimeoutSeconds(value string) (int, error) {
	timeoutSeconds, err := strconv.Atoi(value)
	if err != nil || timeoutSeconds <= 0 {
		return 0, invalidValueArgumentError("--"+debugBreakTimeoutFlagName, value, "positive integer")
	}
	return timeoutSeconds, nil
}

func waitForDebugBreak(
	ctx context.Context,
	connection unityipc.Connection,
	options waitForDebugBreakOptions,
) (debugBreakStatusResponse, debugBreakWaitState, error) {
	waitContext, cancel := context.WithTimeout(ctx, options.timeout)
	defer cancel()

	lastResponse := debugBreakStatusResponse{Id: options.id}
	var lastErr error
	hasResponse := false
	for {
		response, err := queryDebugBreakStatus(waitContext, connection, options.id)
		if err == nil {
			lastResponse = response
			hasResponse = true
			state := debugBreakWaitStateForStatus(response.Status)
			if state != "" {
				return response, state, nil
			}
		} else {
			lastErr = err
		}

		select {
		case <-waitContext.Done():
			if ctx.Err() != nil {
				return lastResponse, "", ctx.Err()
			}
			if hasResponse {
				return lastResponse, debugBreakWaitStateTimeout, nil
			}
			if lastErr != nil {
				return lastResponse, "", fmt.Errorf("timed out waiting for debug break status: %w", lastErr)
			}
			return lastResponse, debugBreakWaitStateTimeout, nil
		case <-time.After(debugBreakStatusPoll):
		}
	}
}

func debugBreakWaitStateForStatus(status string) debugBreakWaitState {
	switch status {
	case debugBreakStatusHit:
		return debugBreakWaitStateHit
	case debugBreakStatusNotEnabled:
		return debugBreakWaitStateNotEnabled
	case debugBreakStatusExpired:
		return debugBreakWaitStateExpired
	case debugBreakStatusCleared:
		return debugBreakWaitStateCleared
	case debugBreakStatusEnabled:
		return ""
	default:
		return ""
	}
}

func queryDebugBreakStatusFromUnity(
	ctx context.Context,
	connection unityipc.Connection,
	id string,
) (debugBreakStatusResponse, error) {
	probeContext, cancel := context.WithTimeout(ctx, debugBreakStatusProbeTimeout)
	defer cancel()

	result, err := unityipc.NewClient(connection, version).Send(
		probeContext,
		debugBreakStatusCommandName,
		map[string]any{"Id": id},
	)
	if err != nil {
		return debugBreakStatusResponse{}, err
	}

	response := debugBreakStatusResponse{}
	if err := json.Unmarshal(result, &response); err != nil {
		return debugBreakStatusResponse{}, err
	}
	return response, nil
}

func clearDebugBreakStatusFromUnity(
	ctx context.Context,
	connection unityipc.Connection,
	id string,
) (debugBreakStatusResponse, error) {
	probeContext, cancel := context.WithTimeout(ctx, debugBreakStatusProbeTimeout)
	defer cancel()

	result, err := unityipc.NewClient(connection, version).Send(
		probeContext,
		debugBreakClearStatusCommandName,
		map[string]any{"Id": id},
	)
	if err != nil {
		return debugBreakStatusResponse{}, err
	}

	response := debugBreakStatusResponse{}
	if err := json.Unmarshal(result, &response); err != nil {
		return debugBreakStatusResponse{}, err
	}
	return response, nil
}

func clearDebugBreakAfterWaitTimeout(ctx context.Context, connection unityipc.Connection, id string) {
	clearContext, cancel := context.WithTimeout(ctx, debugBreakStatusProbeTimeout)
	defer cancel()
	_, _ = clearDebugBreakStatus(clearContext, connection, id)
}

func debugBreakWaitError(
	projectRoot string,
	options waitForDebugBreakOptions,
	response debugBreakStatusResponse,
	state debugBreakWaitState,
) cliError {
	switch state {
	case debugBreakWaitStateNotEnabled:
		return debugBreakStateError(
			errorCodeDebugBreakNotEnabled,
			"Debug break is not enabled.",
			projectRoot,
			options,
			response,
			false)
	case debugBreakWaitStateExpired:
		return debugBreakStateError(
			errorCodeDebugBreakExpired,
			"Debug break expired before it was hit.",
			projectRoot,
			options,
			response,
			true)
	case debugBreakWaitStateCleared:
		return debugBreakStateError(
			errorCodeDebugBreakCleared,
			"Debug break was cleared before it was hit.",
			projectRoot,
			options,
			response,
			true)
	default:
		return debugBreakStateError(
			errorCodeDebugBreakWaitTimeout,
			fmt.Sprintf("Debug break was not hit within %ds.", options.timeoutSeconds),
			projectRoot,
			options,
			response,
			true)
	}
}

func debugBreakStateError(
	errorCode string,
	message string,
	projectRoot string,
	options waitForDebugBreakOptions,
	response debugBreakStatusResponse,
	retryable bool,
) cliError {
	return cliError{
		ErrorCode:   errorCode,
		Phase:       errorPhaseResponseWaiting,
		Message:     message,
		Retryable:   retryable,
		SafeToRetry: retryable,
		ProjectRoot: projectRoot,
		Command:     debugBreakWaitCommandName,
		NextActions: []string{
			"Run `uloop enable-debug-break --id <marker-id>` before waiting.",
			"Confirm the code path calls `UnityCliLoopDebug.Break(\"<marker-id>\")` with the same id.",
			"Check `details.status`, `details.isPlaying`, `details.isPaused`, `details.elapsedSinceEnabledMilliseconds`, and `details.remainingMilliseconds` to distinguish a missed code path from an already-paused Editor.",
			"If the marker is inside a custom asmdef, add a reference to `UnityCLILoop.PausePoints.Runtime`.",
		},
		Details: map[string]any{
			"id":                              options.id,
			"status":                          response.Status,
			"hitCount":                        response.HitCount,
			"timeoutSeconds":                  options.timeoutSeconds,
			"elapsedSinceEnabledMilliseconds": response.ElapsedSinceEnabledMilliseconds,
			"isPlaying":                       response.IsPlaying,
			"isPaused":                        response.IsPaused,
			"remainingMilliseconds":           debugBreakRemainingMilliseconds(options, response),
			"markerMessage":                   response.Message,
		},
	}
}

func debugBreakRemainingMilliseconds(options waitForDebugBreakOptions, response debugBreakStatusResponse) int64 {
	timeoutSeconds := response.TimeoutSeconds
	if timeoutSeconds <= 0 {
		return 0
	}

	totalMilliseconds := int64(timeoutSeconds) * int64(time.Second/time.Millisecond)
	remainingMilliseconds := totalMilliseconds - response.ElapsedSinceEnabledMilliseconds
	if remainingMilliseconds <= 0 {
		return 0
	}
	return remainingMilliseconds
}
