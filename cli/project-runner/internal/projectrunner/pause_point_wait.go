package projectrunner

import (
	"context"
	"encoding/json"
	"fmt"
	"io"
	"strconv"
	"time"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"

	"github.com/hatayama/unity-cli-loop/common/clicontract"
	"github.com/hatayama/unity-cli-loop/common/clicore"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

const (
	pausePointStatusCommandName       = "get-pause-point-status"
	pausePointClearStatusCommandName  = "clear-pause-point-status"
	pausePointDefaultTimeoutSeconds   = 30
	pausePointStatusProbeTimeout      = 5 * time.Second
	pausePointStatusEnabled           = "Enabled"
	pausePointStatusHit               = "Hit"
	pausePointStatusNotEnabled        = "NotEnabled"
	pausePointStatusExpired           = "Expired"
	pausePointStatusCleared           = "Cleared"
	pausePointFinalStatusProbeTimeout = 250 * time.Millisecond
)

var (
	pausePointStatusPoll  = 50 * time.Millisecond
	queryPausePointStatus = queryPausePointStatusFromUnity
	clearPausePointStatus = clearPausePointStatusFromUnity
)

type waitForPausePointOptions struct {
	id                   string
	timeoutSeconds       int
	timeout              time.Duration
	matchingLogsMaxCount int
}

type pausePointStatusOptions struct {
	id string
}

type pausePointWaitState string

const (
	pausePointWaitStateHit        pausePointWaitState = "hit"
	pausePointWaitStateTimeout    pausePointWaitState = "timeout"
	pausePointWaitStateNotEnabled pausePointWaitState = "not_enabled"
	pausePointWaitStateExpired    pausePointWaitState = "expired"
	pausePointWaitStateCleared    pausePointWaitState = "cleared"
)

func normalizePausePointStatusResponse(response pausePointStatusResponse) pausePointStatusResponse {
	if response.Status == pausePointStatusExpired {
		response.Expired = true
		response.RemainingMilliseconds = 0
		return response
	}

	if response.Status != pausePointStatusEnabled || response.RemainingMilliseconds > 0 {
		return response
	}

	if response.TimeoutSeconds <= 0 {
		return response
	}

	totalMilliseconds := int64(response.TimeoutSeconds) * int64(time.Second/time.Millisecond)
	remainingMilliseconds := totalMilliseconds - response.ElapsedSinceEnabledMilliseconds
	if remainingMilliseconds <= 0 {
		return response
	}

	response.RemainingMilliseconds = remainingMilliseconds
	return response
}

func runWaitForPausePointCommand(
	ctx context.Context,
	connection unityipc.Connection,
	args []string,
	stdout io.Writer,
	stderr io.Writer,
) int {
	options, err := parseWaitForPausePointOptions(args)
	if err != nil {
		clierrors.WriteClassifiedError(stderr, err, clierrors.ErrorContext{
			ProjectRoot: connection.ProjectRoot,
			Command:     clicore.PausePointAwaitCommandName,
		})
		return 1
	}

	return runWaitForPausePoint(ctx, connection, options, stdout, stderr)
}

func runPausePointStatusCommand(
	ctx context.Context,
	connection unityipc.Connection,
	args []string,
	stdout io.Writer,
	stderr io.Writer,
) int {
	options, err := parsePausePointStatusOptions(args)
	if err != nil {
		clierrors.WriteClassifiedError(stderr, err, clierrors.ErrorContext{
			ProjectRoot: connection.ProjectRoot,
			Command:     clicore.PausePointStatusUserCommandName,
		})
		return 1
	}

	response, err := queryPausePointStatus(ctx, connection, options.id)
	if err != nil {
		clierrors.WriteClassifiedError(stderr, err, clierrors.ErrorContext{
			ProjectRoot: connection.ProjectRoot,
			Command:     clicore.PausePointStatusUserCommandName,
		})
		return 1
	}
	response = normalizePausePointStatusResponse(response)

	result, err := json.Marshal(response)
	if err != nil {
		clierrors.WriteClassifiedError(stderr, err, clierrors.ErrorContext{
			ProjectRoot: connection.ProjectRoot,
			Command:     clicore.PausePointStatusUserCommandName,
		})
		return 1
	}

	clicore.WriteJSON(stdout, result)
	return 0
}

func runWaitForPausePoint(
	ctx context.Context,
	connection unityipc.Connection,
	options waitForPausePointOptions,
	stdout io.Writer,
	stderr io.Writer,
) int {
	startedAt := time.Now()
	spinner := clicore.NewToolSpinner(stderr, clicore.PausePointAwaitCommandName)
	response, state, err := waitForPausePoint(ctx, connection, options)
	spinner.Stop()
	if err != nil {
		clierrors.WriteClassifiedError(stderr, err, clierrors.ErrorContext{
			ProjectRoot: connection.ProjectRoot,
			Command:     clicore.PausePointAwaitCommandName,
		})
		return 1
	}

	if state == pausePointWaitStateHit {
		// Best-effort: a hit must stay a success even if Unity is busy while paused.
		// On fetch failure MatchingLogs is omitted entirely, so an empty array always
		// means "the fetch succeeded and no matching log exists".
		var payload any = response
		logs, logsErr := fetchMatchingLogs(ctx, connection, options.id, options.matchingLogsMaxCount)
		if logsErr == nil {
			payload = pausePointWaitResult{
				pausePointStatusResponse: response,
				MatchingLogs:             logs.Logs,
				EvidenceSummary:          buildPausePointEvidenceSummary(response, logs),
			}
		}
		result, marshalErr := json.Marshal(payload)
		if marshalErr != nil {
			clierrors.WriteClassifiedError(stderr, marshalErr, clierrors.ErrorContext{
				ProjectRoot: connection.ProjectRoot,
				Command:     clicore.PausePointAwaitCommandName,
			})
			return 1
		}
		clicore.WriteJSON(stdout, result)
		writeDebugTiming(stderr, clicore.PausePointAwaitCommandName, time.Since(startedAt), unityipc.UnitySendOutcome{})
		return 0
	}

	if state == pausePointWaitStateTimeout {
		clearPausePointAfterWaitTimeout(ctx, connection, options.id)
	}

	waitErr := pausePointWaitError(connection.ProjectRoot, options, response, state)
	if state == pausePointWaitStateTimeout {
		// Best-effort: the timeout diagnosis must not depend on a second Unity round trip succeeding.
		logs, logsErr := fetchMatchingLogs(ctx, connection, options.id, options.matchingLogsMaxCount)
		if logsErr == nil {
			waitErr.Details["MatchingLogs"] = logs.Logs
			waitErr.Details["EvidenceSummary"] = buildPausePointEvidenceSummary(response, logs)
		}
	}
	clierrors.WriteErrorEnvelope(stderr, waitErr)
	return 1
}

func parseWaitForPausePointOptions(args []string) (waitForPausePointOptions, error) {
	options := waitForPausePointOptions{
		timeoutSeconds:       pausePointDefaultTimeoutSeconds,
		timeout:              time.Duration(pausePointDefaultTimeoutSeconds) * time.Second,
		matchingLogsMaxCount: pausePointDefaultLogsMaxCount,
	}

	for index := 0; index < len(args); index++ {
		arg := args[index]
		name, value, consumedNext, err := clicore.ParseFlagValue(arg, args, index)
		if err != nil {
			return waitForPausePointOptions{}, err
		}

		switch name {
		case clicore.PausePointIDFlagName:
			options.id = value
		case clicore.PausePointTimeoutFlagName:
			timeoutSeconds, parseErr := parsePausePointTimeoutSeconds(value)
			if parseErr != nil {
				return waitForPausePointOptions{}, parseErr
			}
			options.timeoutSeconds = timeoutSeconds
			options.timeout = time.Duration(timeoutSeconds) * time.Second
		case clicore.PausePointLogsMaxCountFlagName:
			maxCount, parseErr := strconv.Atoi(value)
			if parseErr != nil || maxCount <= 0 {
				return waitForPausePointOptions{}, clierrors.InvalidValueArgumentError(
					"--"+clicore.PausePointLogsMaxCountFlagName, value, "positive integer")
			}
			options.matchingLogsMaxCount = maxCount
		default:
			return waitForPausePointOptions{}, &clierrors.ArgumentError{
				Message:     "Unknown option for await-pause-point: --" + name,
				Option:      "--" + name,
				Command:     clicore.PausePointAwaitCommandName,
				NextActions: []string{"Run `uloop await-pause-point --help` to inspect supported options."},
			}
		}

		if consumedNext {
			index++
		}
	}

	if options.id == "" {
		return waitForPausePointOptions{}, &clierrors.ArgumentError{
			Message:      "Missing required option: --id",
			Option:       "--" + clicore.PausePointIDFlagName,
			ExpectedType: "value",
			Command:      clicore.PausePointAwaitCommandName,
			NextActions:  []string{"Pass `--id <marker-id>` matching UloopPausePoint.Pause(\"<marker-id>\")."},
		}
	}

	return options, nil
}

func parsePausePointStatusOptions(args []string) (pausePointStatusOptions, error) {
	options := pausePointStatusOptions{}

	for index := 0; index < len(args); index++ {
		arg := args[index]
		name, value, consumedNext, err := clicore.ParseFlagValue(arg, args, index)
		if err != nil {
			return pausePointStatusOptions{}, err
		}

		switch name {
		case clicore.PausePointIDFlagName:
			options.id = value
		default:
			return pausePointStatusOptions{}, &clierrors.ArgumentError{
				Message:     "Unknown option for pause-point-status: --" + name,
				Option:      "--" + name,
				Command:     clicore.PausePointStatusUserCommandName,
				NextActions: []string{"Run `uloop pause-point-status --help` to inspect supported options."},
			}
		}

		if consumedNext {
			index++
		}
	}

	if options.id == "" {
		return pausePointStatusOptions{}, &clierrors.ArgumentError{
			Message:      "Missing required option: --id",
			Option:       "--" + clicore.PausePointIDFlagName,
			ExpectedType: "value",
			Command:      clicore.PausePointStatusUserCommandName,
			NextActions:  []string{"Pass `--id <marker-id>` matching UloopPausePoint.Pause(\"<marker-id>\")."},
		}
	}

	return options, nil
}

func parsePausePointTimeoutSeconds(value string) (int, error) {
	timeoutSeconds, err := strconv.Atoi(value)
	if err != nil || timeoutSeconds <= 0 {
		return 0, clierrors.InvalidValueArgumentError("--"+clicore.PausePointTimeoutFlagName, value, "positive integer")
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
	ticker := time.NewTicker(pausePointStatusPoll)
	defer ticker.Stop()
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
			finalResponse, finalState, hasFinalResponse, finalErr := queryPausePointStatusAtTimeout(ctx, connection, options.id)
			if hasFinalResponse {
				lastResponse = finalResponse
				hasResponse = true
				if finalState != "" {
					return finalResponse, finalState, nil
				}
			} else if lastErr == nil {
				lastErr = finalErr
			}
			if hasResponse {
				return lastResponse, pausePointWaitStateTimeout, nil
			}
			if lastErr != nil {
				return lastResponse, "", fmt.Errorf("timed out waiting for pause point status: %w", lastErr)
			}
			return lastResponse, pausePointWaitStateTimeout, nil
		case <-ticker.C:
		}
	}
}

func queryPausePointStatusAtTimeout(
	ctx context.Context,
	connection unityipc.Connection,
	id string,
) (pausePointStatusResponse, pausePointWaitState, bool, error) {
	finalContext, cancel := context.WithTimeout(ctx, pausePointFinalStatusProbeTimeout)
	defer cancel()

	response, err := queryPausePointStatus(finalContext, connection, id)
	if err != nil {
		return pausePointStatusResponse{}, "", false, err
	}

	return response, pausePointWaitStateForStatus(response.Status), true, nil
}

func pausePointWaitStateForStatus(status string) pausePointWaitState {
	switch status {
	case pausePointStatusHit:
		return pausePointWaitStateHit
	case pausePointStatusNotEnabled:
		return pausePointWaitStateNotEnabled
	case pausePointStatusExpired:
		return pausePointWaitStateExpired
	case pausePointStatusCleared:
		return pausePointWaitStateCleared
	case pausePointStatusEnabled:
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

	result, err := unityipc.NewClient(connection, clicontract.ProjectRunnerVersion()).Send(
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

	result, err := unityipc.NewClient(connection, clicontract.ProjectRunnerVersion()).Send(
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
