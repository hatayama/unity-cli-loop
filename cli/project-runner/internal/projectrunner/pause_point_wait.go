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
	pausePointStatusCommandName           = "get-pause-point-status"
	pausePointClearStatusCommandName      = "clear-pause-point-status"
	pausePointExtendStatusCommandName     = "extend-pause-point-status"
	pausePointDefaultTimeoutSeconds       = 30
	pausePointStatusProbeTimeout          = 5 * time.Second
	pausePointStatusEnabled               = "Enabled"
	pausePointStatusHit                   = "Hit"
	pausePointStatusNotEnabled            = "NotEnabled"
	pausePointStatusExpired               = "Expired"
	pausePointStatusCleared               = "Cleared"
	pausePointAwaitTimeoutAutoClearReason = "AwaitTimeoutAutoClear"

	// pausePointCapturedVariableHistoryNote explains why the latest hit is absent from
	// CapturedVariableHistory: repeating it would duplicate CapturedVariables.
	pausePointCapturedVariableHistoryNote = "CapturedVariableHistory lists hits before the latest one; the latest hit's variables are in CapturedVariables."

	// pausePointTraceStatusNote explains that a trace-mode Hit did not pause Play Mode.
	pausePointTraceStatusNote = "Trace mode does not pause Play Mode; Status 'Hit' records that the marker fired while the game kept running."

	// Why: a non-trace Hit pauses at the next frame boundary, so live reads after the
	// pause are already post-frame; agents otherwise treat them as at-line evidence.
	pausePointHitFrameBoundaryStatusNote = "Unity pauses at the next frame boundary; the rest of the hit frame already ran. Read at-line values from CapturedVariables; live reads via execute-dynamic-code reflect post-frame state."

	// Mode strings mirror UloopPausePointCaptureMode on the Unity side. Await uses an allowlist
	// (continuous/trace) for the new-hit baseline — never `Mode != "single-shot"` — so an empty
	// Mode from an older package keeps the historical immediate-Hit success path.
	pausePointModeContinuous = "continuous"
	pausePointModeTrace      = "trace"
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

	// markerJustEnabled is set only by enable-pause-point --await. Why: enable finishes before the
	// first status query, and a real hit can race into that window on continuous/trace markers.
	// Baselining that hit would demand a later sequence that never comes (continuous pauses on hit).
	markerJustEnabled bool
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
	excludedCount := 0
	for _, frame := range response.CapturedVariableHistory {
		if frame.HitSequence == response.LastHitSequence {
			excludedCount++
			continue
		}
		filtered = append(filtered, frame)
	}
	response.CapturedVariableHistory = filtered
	if excludedCount > 0 {
		response.CapturedVariableHistoryNote = pausePointCapturedVariableHistoryNote
	}
	return response
}

// applyPausePointHitStatusNote records mode-specific Hit guidance: trace did not
// pause Play Mode; other modes paused at the next frame boundary.
func applyPausePointHitStatusNote(response pausePointStatusResponse) pausePointStatusResponse {
	if response.Status != pausePointStatusHit {
		return response
	}
	if response.Mode == pausePointModeTrace {
		response.StatusNote = pausePointTraceStatusNote
		return response
	}
	response.StatusNote = pausePointHitFrameBoundaryStatusNote
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
	announceAwaitPausePointWaitStart(stderr, options.id, options.timeoutSeconds)

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
	response = applyPausePointHitStatusNote(response)
	// Evaluated against the raw CapturedVariables, before the filters below can narrow or strip
	// values, for the same reason as on the await path (runWaitForPausePoint): otherwise an --expect
	// target not also requested via --captured-variable-names, or whose value names mode stripped,
	// would be reported as missing or failing.
	expectations := evaluatePausePointExpectations(response.CapturedVariables, options.expectations)
	response = filterPausePointCapturedVariablesByName(response, options.capturedVariableNames)
	response = applyPausePointCapturedVariablesMode(response, options.capturedVariablesMode)
	response = applyPausePointCapturedVariablePreviewNote(response)

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
	response, state, triggerResult, resumeResult, hasNewHitBaseline, err := waitForPausePoint(ctx, connection, options)
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
		response = applyPausePointHitStatusNote(response)
		response = filterPausePointCapturedVariablesByName(response, options.capturedVariableNames)
		response = applyPausePointCapturedVariablesMode(response, options.capturedVariablesMode)
		response = applyPausePointCapturedVariablePreviewNote(response)
		// Best-effort: a hit must stay a success even if Unity is busy while paused.
		logs, logsErr := fetchMatchingLogs(ctx, connection, options.id, options.matchingLogsMaxCount)
		payload := buildPausePointHitPayload(pausePointHitPayloadInputs{
			response:            response,
			logs:                logs,
			logsErr:             logsErr,
			unityWarning:        response.Warning,
			triggerResult:       triggerResult,
			awaitedPausePointID: options.id,
			expectations:        expectations,
		})
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

	// Why skip clear when hasNewHitBaseline: the continuous/trace marker is still armed, and the
	// timeout hint tells the caller to await again (with --resume-play). Clearing here would disarm
	// it and discard the raw capture holder, making that recovery path impossible.
	markerClearedByThisCommand := false
	if state == pausePointWaitStateTimeout && !hasNewHitBaseline {
		response, markerClearedByThisCommand = refreshPausePointStatusAfterWaitTimeoutAutoClear(
			ctx, connection, options.id, response)
	}

	waitErr := pausePointWaitError(
		connection.ProjectRoot,
		options,
		response,
		state,
		hasNewHitBaseline,
		markerClearedByThisCommand,
	)
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
		if applyErr := applyWaitForPausePointFlag(&options, name, value); applyErr != nil {
			return waitForPausePointOptions{}, applyErr
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
