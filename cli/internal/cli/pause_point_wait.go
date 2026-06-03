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
	pausePointWaitCommandName        = "wait-for-pause-point"
	pausePointStatusCommandName      = "get-pause-point-status"
	pausePointClearStatusCommandName = "clear-pause-point-status"
	pausePointIDFlagName             = "id"
	pausePointTimeoutFlagName        = "timeout-seconds"
	pausePointDefaultTimeoutSeconds  = 30
	pausePointStatusProbeTimeout     = 5 * time.Second
	pausePointStatusArmed            = "Armed"
	pausePointStatusHit              = "Hit"
	pausePointStatusNotArmed         = "NotArmed"
	pausePointStatusExpired          = "Expired"
	pausePointStatusCleared          = "Cleared"
)

var (
	pausePointStatusPoll  = 50 * time.Millisecond
	queryPausePointStatus = queryPausePointStatusFromUnity
	clearPausePointStatus = clearPausePointStatusFromUnity
)

type waitForPausePointOptions struct {
	id             string
	timeoutSeconds int
	timeout        time.Duration
}

type pausePointStatusResponse struct {
	Id                  string `json:"Id"`
	Status              string `json:"Status"`
	IsArmed             bool   `json:"IsArmed"`
	IsHit               bool   `json:"IsHit"`
	HitCount            int    `json:"HitCount"`
	TimeoutSeconds      int    `json:"TimeoutSeconds"`
	ElapsedMilliseconds int64  `json:"ElapsedMilliseconds"`
	IsPlaying           bool   `json:"IsPlaying"`
	IsPaused            bool   `json:"IsPaused"`
	Message             string `json:"Message"`
}

type pausePointWaitState string

const (
	pausePointWaitStateHit      pausePointWaitState = "hit"
	pausePointWaitStateTimeout  pausePointWaitState = "timeout"
	pausePointWaitStateNotArmed pausePointWaitState = "not_armed"
	pausePointWaitStateExpired  pausePointWaitState = "expired"
	pausePointWaitStateCleared  pausePointWaitState = "cleared"
)

func runWaitForPausePointCommand(
	ctx context.Context,
	connection unityipc.Connection,
	args []string,
	stdout io.Writer,
	stderr io.Writer,
) int {
	options, err := parseWaitForPausePointOptions(args)
	if err != nil {
		writeClassifiedError(stderr, err, errorContext{
			projectRoot: connection.ProjectRoot,
			command:     pausePointWaitCommandName,
		})
		return 1
	}

	return runWaitForPausePoint(ctx, connection, options, stdout, stderr)
}

func runWaitForPausePoint(
	ctx context.Context,
	connection unityipc.Connection,
	options waitForPausePointOptions,
	stdout io.Writer,
	stderr io.Writer,
) int {
	startedAt := time.Now()
	spinner := newToolSpinner(stderr, pausePointWaitCommandName)
	response, state, err := waitForPausePoint(ctx, connection, options)
	spinner.Stop()
	if err != nil {
		writeClassifiedError(stderr, err, errorContext{
			projectRoot: connection.ProjectRoot,
			command:     pausePointWaitCommandName,
		})
		return 1
	}

	if state == pausePointWaitStateHit {
		result, marshalErr := json.Marshal(response)
		if marshalErr != nil {
			writeClassifiedError(stderr, marshalErr, errorContext{
				projectRoot: connection.ProjectRoot,
				command:     pausePointWaitCommandName,
			})
			return 1
		}
		writeJSON(stdout, result)
		writeDebugTiming(stderr, pausePointWaitCommandName, time.Since(startedAt), unityipc.UnitySendOutcome{})
		return 0
	}

	if state == pausePointWaitStateTimeout {
		clearPausePointAfterWaitTimeout(ctx, connection, options.id)
	}

	writeErrorEnvelope(stderr, pausePointWaitError(connection.ProjectRoot, options, response, state))
	return 1
}

func parseWaitForPausePointOptions(args []string) (waitForPausePointOptions, error) {
	options := waitForPausePointOptions{
		timeoutSeconds: pausePointDefaultTimeoutSeconds,
		timeout:        time.Duration(pausePointDefaultTimeoutSeconds) * time.Second,
	}

	for index := 0; index < len(args); index++ {
		arg := args[index]
		name, value, consumedNext, err := parseFlagValue(arg, args, index)
		if err != nil {
			return waitForPausePointOptions{}, err
		}

		switch name {
		case pausePointIDFlagName:
			options.id = value
		case pausePointTimeoutFlagName:
			timeoutSeconds, parseErr := parsePausePointTimeoutSeconds(value)
			if parseErr != nil {
				return waitForPausePointOptions{}, parseErr
			}
			options.timeoutSeconds = timeoutSeconds
			options.timeout = time.Duration(timeoutSeconds) * time.Second
		default:
			return waitForPausePointOptions{}, &argumentError{
				message:     "Unknown option for wait-for-pause-point: --" + name,
				option:      "--" + name,
				command:     pausePointWaitCommandName,
				nextActions: []string{"Run `uloop wait-for-pause-point --help` to inspect supported options."},
			}
		}

		if consumedNext {
			index++
		}
	}

	if options.id == "" {
		return waitForPausePointOptions{}, &argumentError{
			message:      "Missing required option: --id",
			option:       "--" + pausePointIDFlagName,
			expectedType: "value",
			command:      pausePointWaitCommandName,
			nextActions:  []string{"Pass `--id <marker-id>` matching UloopPausePoint.Hit(\"<marker-id>\")."},
		}
	}

	return options, nil
}

func parsePausePointTimeoutSeconds(value string) (int, error) {
	timeoutSeconds, err := strconv.Atoi(value)
	if err != nil || timeoutSeconds <= 0 {
		return 0, invalidValueArgumentError("--"+pausePointTimeoutFlagName, value, "positive integer")
	}
	return timeoutSeconds, nil
}

func waitForPausePoint(
	ctx context.Context,
	connection unityipc.Connection,
	options waitForPausePointOptions,
) (pausePointStatusResponse, pausePointWaitState, error) {
	waitContext, cancel := context.WithTimeout(ctx, options.timeout)
	defer cancel()

	lastResponse := pausePointStatusResponse{Id: options.id}
	var lastErr error
	hasResponse := false
	for {
		response, err := queryPausePointStatus(waitContext, connection, options.id)
		if err == nil {
			lastResponse = response
			hasResponse = true
			state := pausePointWaitStateForStatus(response.Status)
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
				return lastResponse, pausePointWaitStateTimeout, nil
			}
			if lastErr != nil {
				return lastResponse, "", fmt.Errorf("timed out waiting for pause point status: %w", lastErr)
			}
			return lastResponse, pausePointWaitStateTimeout, nil
		case <-time.After(pausePointStatusPoll):
		}
	}
}

func pausePointWaitStateForStatus(status string) pausePointWaitState {
	switch status {
	case pausePointStatusHit:
		return pausePointWaitStateHit
	case pausePointStatusNotArmed:
		return pausePointWaitStateNotArmed
	case pausePointStatusExpired:
		return pausePointWaitStateExpired
	case pausePointStatusCleared:
		return pausePointWaitStateCleared
	case pausePointStatusArmed:
		return ""
	default:
		return ""
	}
}

func queryPausePointStatusFromUnity(
	ctx context.Context,
	connection unityipc.Connection,
	id string,
) (pausePointStatusResponse, error) {
	probeContext, cancel := context.WithTimeout(ctx, pausePointStatusProbeTimeout)
	defer cancel()

	result, err := unityipc.NewClient(connection, version).Send(
		probeContext,
		pausePointStatusCommandName,
		map[string]any{"Id": id},
	)
	if err != nil {
		return pausePointStatusResponse{}, err
	}

	response := pausePointStatusResponse{}
	if err := json.Unmarshal(result, &response); err != nil {
		return pausePointStatusResponse{}, err
	}
	return response, nil
}

func clearPausePointStatusFromUnity(
	ctx context.Context,
	connection unityipc.Connection,
	id string,
) (pausePointStatusResponse, error) {
	probeContext, cancel := context.WithTimeout(ctx, pausePointStatusProbeTimeout)
	defer cancel()

	result, err := unityipc.NewClient(connection, version).Send(
		probeContext,
		pausePointClearStatusCommandName,
		map[string]any{"Id": id},
	)
	if err != nil {
		return pausePointStatusResponse{}, err
	}

	response := pausePointStatusResponse{}
	if err := json.Unmarshal(result, &response); err != nil {
		return pausePointStatusResponse{}, err
	}
	return response, nil
}

func clearPausePointAfterWaitTimeout(ctx context.Context, connection unityipc.Connection, id string) {
	clearContext, cancel := context.WithTimeout(ctx, pausePointStatusProbeTimeout)
	defer cancel()
	_, _ = clearPausePointStatus(clearContext, connection, id)
}

func pausePointWaitError(
	projectRoot string,
	options waitForPausePointOptions,
	response pausePointStatusResponse,
	state pausePointWaitState,
) cliError {
	switch state {
	case pausePointWaitStateNotArmed:
		return pausePointStateError(
			errorCodePausePointNotArmed,
			"Pause point is not armed.",
			projectRoot,
			options,
			response,
			false)
	case pausePointWaitStateExpired:
		return pausePointStateError(
			errorCodePausePointExpired,
			"Pause point expired before it was hit.",
			projectRoot,
			options,
			response,
			true)
	case pausePointWaitStateCleared:
		return pausePointStateError(
			errorCodePausePointCleared,
			"Pause point was cleared before it was hit.",
			projectRoot,
			options,
			response,
			true)
	default:
		return pausePointStateError(
			errorCodePausePointWaitTimeout,
			fmt.Sprintf("Pause point was not hit within %ds.", options.timeoutSeconds),
			projectRoot,
			options,
			response,
			true)
	}
}

func pausePointStateError(
	errorCode string,
	message string,
	projectRoot string,
	options waitForPausePointOptions,
	response pausePointStatusResponse,
	retryable bool,
) cliError {
	return cliError{
		ErrorCode:   errorCode,
		Phase:       errorPhaseResponseWaiting,
		Message:     message,
		Retryable:   retryable,
		SafeToRetry: retryable,
		ProjectRoot: projectRoot,
		Command:     pausePointWaitCommandName,
		NextActions: []string{
			"Run `uloop arm-pause-point --id <marker-id>` before waiting.",
			"Confirm the code path calls `UloopPausePoint.Hit(\"<marker-id>\")` with the same id.",
		},
		Details: map[string]any{
			"id":                  options.id,
			"status":              response.Status,
			"hitCount":            response.HitCount,
			"timeoutSeconds":      options.timeoutSeconds,
			"elapsedMilliseconds": response.ElapsedMilliseconds,
		},
	}
}
