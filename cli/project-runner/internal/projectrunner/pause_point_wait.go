package projectrunner

import (
	"context"
	"encoding/json"
	"fmt"
	"io"
	"strconv"
	"time"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

const (
	pausePointStatusCommandName       = "get-pause-point-status"
	pausePointClearStatusCommandName  = "clear-pause-point-status"
	pausePointExtendStatusCommandName = "extend-pause-point-status"
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
	pausePointStatusPoll   = 50 * time.Millisecond
	queryPausePointStatus  = queryPausePointStatusFromUnity
	clearPausePointStatus  = clearPausePointStatusFromUnity
	extendPausePointExpiry = extendPausePointExpiryFromUnity
)

type waitForPausePointOptions struct {
	id                    string
	timeoutSeconds        int
	timeout               time.Duration
	matchingLogsMaxCount  int
	capturedVariablesMode pausePointCapturedVariablesMode
	capturedVariableNames []string
}

type pausePointStatusOptions struct {
	id                    string
	capturedVariablesMode pausePointCapturedVariablesMode
	capturedVariableNames []string
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

// filterPausePointCapturedVariableHistory keeps only frames strictly older than the latest
// hit: CapturedVariables already carries the latest hit's variables, so single-shot mode
// (one hit) always yields an empty history and continuous mode never repeats it.
func filterPausePointCapturedVariableHistory(response pausePointStatusResponse) pausePointStatusResponse {
	filtered := make([]pausePointCapturedHistoryFrame, 0, len(response.CapturedVariableHistory))
	for _, frame := range response.CapturedVariableHistory {
		if frame.HitSequence == response.LastHitSequence {
			continue
		}
		filtered = append(filtered, frame)
	}
	response.CapturedVariableHistory = filtered
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

	extendPausePointExpiryBeforeWait(ctx, connection, options, stderr)

	return runWaitForPausePoint(ctx, connection, options, stdout, stderr)
}

// A marker enabled well before a slow multi-step CLI round trip (enable -> seed state via
// execute-dynamic-code -> await) can otherwise expire before this wait ever gets a chance to
// observe a hit, since the marker's countdown starts at enable time, not at await time. Best
// effort: an older Unity package without this bridge command, or a transient IPC failure, must
// not fail the whole await-pause-point call over a lifetime extension it can still work without.
func extendPausePointExpiryBeforeWait(
	ctx context.Context,
	connection unityipc.Connection,
	options waitForPausePointOptions,
	stderr io.Writer,
) {
	if _, err := extendPausePointExpiry(ctx, connection, options.id, options.timeoutSeconds); err != nil {
		_, _ = fmt.Fprintf(
			stderr,
			"warning: could not extend pause point %q expiry before waiting: %v\n",
			options.id, err)
	}
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
	response = filterPausePointCapturedVariableHistory(response)
	response = filterPausePointCapturedVariablesByName(response, options.capturedVariableNames)
	response = applyPausePointCapturedVariablesMode(response, options.capturedVariablesMode)

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
		response = filterPausePointCapturedVariableHistory(response)
		response = filterPausePointCapturedVariablesByName(response, options.capturedVariableNames)
		response = applyPausePointCapturedVariablesMode(response, options.capturedVariablesMode)
		// Best-effort: a hit must stay a success even if Unity is busy while paused.
		// On fetch failure MatchingLogs is omitted entirely, so an empty array always
		// means "the fetch succeeded and no matching log exists".
		var payload any = response
		logs, logsErr := fetchMatchingLogs(ctx, connection, options.id, options.matchingLogsMaxCount)
		if logsErr == nil {
			payload = pausePointWaitResult{
				pausePointStatusResponse: response,
				MatchingLogs:             logs.Logs,
				Warning:                  buildPausePointWarning(logs, response.HitCount),
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
			warning := buildPausePointWarning(logs, response.HitCount)
			if warning != "" {
				waitErr.Details["Warning"] = warning
			}
		}
	}
	clierrors.WriteErrorEnvelope(stderr, waitErr)
	return 1
}

func parseWaitForPausePointOptions(args []string) (waitForPausePointOptions, error) {
	options := waitForPausePointOptions{
		timeoutSeconds:        pausePointDefaultTimeoutSeconds,
		timeout:               time.Duration(pausePointDefaultTimeoutSeconds) * time.Second,
		matchingLogsMaxCount:  pausePointDefaultLogsMaxCount,
		capturedVariablesMode: pausePointCapturedVariablesModeFull,
	}

	for index := 0; index < len(args); index++ {
		arg := args[index]
		name, value, consumedNext, err := clicore.ParseFlagValue(arg, args, index)
		if err != nil {
			return waitForPausePointOptions{}, err
		}

		switch name {
		case PausePointIDFlagName:
			options.id = value
		case PausePointTimeoutFlagName:
			timeoutSeconds, parseErr := parsePausePointTimeoutSeconds(value)
			if parseErr != nil {
				return waitForPausePointOptions{}, parseErr
			}
			options.timeoutSeconds = timeoutSeconds
			options.timeout = time.Duration(timeoutSeconds) * time.Second
		case PausePointLogsMaxCountFlagName:
			maxCount, parseErr := strconv.Atoi(value)
			if parseErr != nil || maxCount <= 0 {
				return waitForPausePointOptions{}, clierrors.InvalidValueArgumentError(
					"--"+PausePointLogsMaxCountFlagName, value, "positive integer")
			}
			options.matchingLogsMaxCount = maxCount
		case PausePointCapturedVariablesFlagName:
			mode, parseErr := parsePausePointCapturedVariablesMode(value)
			if parseErr != nil {
				return waitForPausePointOptions{}, parseErr
			}
			options.capturedVariablesMode = mode
		case PausePointCapturedVariableNamesFlagName:
			options.capturedVariableNames = parsePausePointCapturedVariableNames(value)
		default:
			return waitForPausePointOptions{}, pausePointUnknownOptionError(clicore.PausePointAwaitCommandName, name)
		}

		if consumedNext {
			index++
		}
	}

	if options.id == "" {
		return waitForPausePointOptions{}, &clierrors.ArgumentError{
			Message:      "Missing required option: --id",
			Option:       "--" + PausePointIDFlagName,
			ExpectedType: "value",
			Command:      clicore.PausePointAwaitCommandName,
			NextActions:  []string{"Pass `--id <marker-id>` matching UloopPausePoint.Pause(\"<marker-id>\")."},
		}
	}

	return options, nil
}

func parsePausePointStatusOptions(args []string) (pausePointStatusOptions, error) {
	options := pausePointStatusOptions{capturedVariablesMode: pausePointCapturedVariablesModeFull}

	for index := 0; index < len(args); index++ {
		arg := args[index]
		name, value, consumedNext, err := clicore.ParseFlagValue(arg, args, index)
		if err != nil {
			return pausePointStatusOptions{}, err
		}

		switch name {
		case PausePointIDFlagName:
			options.id = value
		case PausePointCapturedVariablesFlagName:
			mode, parseErr := parsePausePointCapturedVariablesMode(value)
			if parseErr != nil {
				return pausePointStatusOptions{}, parseErr
			}
			options.capturedVariablesMode = mode
		case PausePointCapturedVariableNamesFlagName:
			options.capturedVariableNames = parsePausePointCapturedVariableNames(value)
		default:
			return pausePointStatusOptions{}, pausePointUnknownOptionError(clicore.PausePointStatusUserCommandName, name)
		}

		if consumedNext {
			index++
		}
	}

	if options.id == "" {
		return pausePointStatusOptions{}, &clierrors.ArgumentError{
			Message:      "Missing required option: --id",
			Option:       "--" + PausePointIDFlagName,
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
		return 0, clierrors.InvalidValueArgumentError("--"+PausePointTimeoutFlagName, value, "positive integer")
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
