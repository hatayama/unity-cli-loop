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
	"github.com/hatayama/unity-cli-loop/common/tooldocs"
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
)

var (
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
	expectations          []pausePointExpectation

	// triggerCommand/triggerArgs come from --trigger. startPath is threaded through so the
	// trigger, when dispatched via runResolvedProjectCommand, can satisfy that function's
	// signature; --trigger forbids --project-path, so the nested-project-path branch it would
	// otherwise feed is unreachable here.
	triggerCommand string
	triggerArgs    []string
	startPath      string

	// resumePlay comes from --resume-play: after the marker is confirmed armed, resume PlayMode
	// (if paused) before dispatching --trigger so a paused-arm workflow can fire input triggers
	// in one CLI call.
	resumePlay bool
}

type pausePointStatusOptions struct {
	id                    string
	capturedVariablesMode pausePointCapturedVariablesMode
	capturedVariableNames []string
	expectations          []pausePointExpectation
}

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
	startPath string,
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
	options.startPath = startPath

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
	// Evaluated against the raw CapturedVariables, before the filters below can narrow or strip
	// values, for the same reason as on the await path (runWaitForPausePoint): otherwise an --expect
	// target not also requested via --captured-variable-names, or whose value names mode stripped,
	// would be reported as missing or failing.
	expectations := evaluatePausePointExpectations(response.CapturedVariables, options.expectations)
	response = filterPausePointCapturedVariablesByName(response, options.capturedVariableNames)
	response = applyPausePointCapturedVariablesMode(response, options.capturedVariablesMode)

	// Expectation verdicts never change the exit code: whether the query succeeded and whether the
	// captured state matched are separate questions, as on await-pause-point.
	result, err := json.Marshal(pausePointStatusResult{
		pausePointStatusResponse: response,
		Expectations:             expectations,
		AllExpectationsPassed:    pausePointAllExpectationsPassedPointer(expectations),
	})
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
	response, state, triggerResult, resumeResult, err := waitForPausePoint(ctx, connection, options)
	spinner.Stop()
	if err != nil {
		clierrors.WriteClassifiedError(stderr, err, clierrors.ErrorContext{
			ProjectRoot: connection.ProjectRoot,
			Command:     clicore.PausePointAwaitCommandName,
		})
		return 1
	}

	if state == pausePointWaitStateHit {
		// Evaluated against the raw, unfiltered CapturedVariables before the filters below can
		// narrow or strip values, so an --expect target is never dropped just because it was not
		// also requested via --captured-variable-names or --captured-variables=names.
		expectations := evaluatePausePointExpectations(response.CapturedVariables, options.expectations)

		response.TriggerResult = triggerResult
		response.ResumePlayResult = resumeResult
		response = filterPausePointCapturedVariableHistory(response)
		response = filterPausePointCapturedVariablesByName(response, options.capturedVariableNames)
		response = applyPausePointCapturedVariablesMode(response, options.capturedVariablesMode)
		// Best-effort: a hit must stay a success even if Unity is busy while paused.
		// On fetch failure MatchingLogs is omitted entirely, so an empty array always
		// means "the fetch succeeded and no matching log exists".
		logs, logsErr := fetchMatchingLogs(ctx, connection, options.id, options.matchingLogsMaxCount)
		var payload any
		if logsErr == nil {
			payload = pausePointWaitResult{
				pausePointStatusResponse: response,
				MatchingLogs:             logs.Logs,
				Warning: joinPausePointWarnings(
					response.Warning,
					buildPausePointWarning(logs, response.HitCount)),
				Expectations:          expectations,
				AllExpectationsPassed: pausePointAllExpectationsPassedPointer(expectations),
			}
		} else {
			// Best-effort: a failed log fetch must not also drop the CLI-side evidence — the
			// --expect results a caller asked for by name, or a warning about the hit itself. Uses
			// an anonymous struct (not pausePointWaitResult) so MatchingLogs is omitted entirely
			// rather than serialized as an empty array, preserving "empty array only means a
			// successful fetch with no matches".
			payload = struct {
				pausePointStatusResponse
				Warning               string                        `json:"Warning,omitempty"`
				Expectations          []pausePointExpectationResult `json:"Expectations,omitempty"`
				AllExpectationsPassed *bool                         `json:"AllExpectationsPassed,omitempty"`
			}{
				pausePointStatusResponse: response,
				Warning:                  response.Warning,
				Expectations:             expectations,
				AllExpectationsPassed:    pausePointAllExpectationsPassedPointer(expectations),
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
	if triggerResult != nil {
		waitErr.Details["TriggerResult"] = triggerResult
	}
	if resumeResult != nil {
		waitErr.Details["ResumePlayResult"] = resumeResult
	}
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

		// --resume-play is a boolean flag (no value). ParseFlagValue would otherwise demand one.
		if arg == "--"+tooldocs.PausePointResumePlayFlagName {
			options.resumePlay = true
			continue
		}

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
		case tooldocs.PausePointCapturedVariablesFlagName:
			mode, parseErr := parsePausePointCapturedVariablesMode(value)
			if parseErr != nil {
				return waitForPausePointOptions{}, parseErr
			}
			options.capturedVariablesMode = mode
		case tooldocs.PausePointCapturedVariableNamesFlagName:
			options.capturedVariableNames = parsePausePointCapturedVariableNames(value)
		case tooldocs.PausePointExpectFlagName:
			expectation, parseErr := parsePausePointExpectFlagValue(value)
			if parseErr != nil {
				return waitForPausePointOptions{}, parseErr
			}
			options.expectations = append(options.expectations, expectation)
		case tooldocs.PausePointTriggerFlagName:
			triggerCommand, triggerArgs, parseErr := parsePausePointTriggerCommand(clicore.PausePointAwaitCommandName, value)
			if parseErr != nil {
				return waitForPausePointOptions{}, parseErr
			}
			options.triggerCommand = triggerCommand
			options.triggerArgs = triggerArgs
		case tooldocs.PausePointResumePlayFlagName:
			// --resume-play=true style is accepted; any other value is rejected so a typo cannot
			// silently disable the resume step.
			if value != "true" && value != "1" {
				return waitForPausePointOptions{}, clierrors.InvalidValueArgumentError(
					"--"+tooldocs.PausePointResumePlayFlagName, value, "boolean flag (pass with no value)")
			}
			options.resumePlay = true
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
		case tooldocs.PausePointCapturedVariablesFlagName:
			mode, parseErr := parsePausePointCapturedVariablesMode(value)
			if parseErr != nil {
				return pausePointStatusOptions{}, parseErr
			}
			options.capturedVariablesMode = mode
		case tooldocs.PausePointCapturedVariableNamesFlagName:
			options.capturedVariableNames = parsePausePointCapturedVariableNames(value)
		case tooldocs.PausePointExpectFlagName:
			expectation, parseErr := parsePausePointExpectFlagValue(value)
			if parseErr != nil {
				return pausePointStatusOptions{}, parseErr
			}
			options.expectations = append(options.expectations, expectation)
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
