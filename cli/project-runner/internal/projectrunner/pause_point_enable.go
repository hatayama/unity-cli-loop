package projectrunner

import (
	"context"
	"encoding/json"
	"io"
	"strings"
	"time"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
	"github.com/hatayama/unity-cli-loop/common/ui"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	"github.com/hatayama/unity-cli-loop/common/tooldocs"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

// pausePointEnableCommandName keeps enable-pause-point as the schema-driven tool it already is
// (default-tools.json), not a clicore.NativeCommands entry: it must keep its existing tool
// identity (help/tool-settings gating) rather than take on a second, native one. This constant
// only lets runResolvedProjectCommand intercept CLI-only await flags before falling through to
// the same generic schema pipeline every other tool call uses, mirroring how
// extractDynamicCodeFileFlag intercepts --code-file for execute-dynamic-code.
const pausePointEnableCommandName = "enable-pause-point"

// extractPausePointEnableAwaitFlags pulls the CLI-only --await/--captured-variables/
// --captured-variable-names/--expect/--trigger/--resume-play flags out of enable-pause-point args
// before generic schema parsing, because none of them are part of the Unity-side
// EnablePausePointSchema.
func extractPausePointEnableAwaitFlags(
	args []string,
) ([]string, bool, pausePointCapturedVariablesMode, []string, []pausePointExpectation, string, []string, bool, error) {
	remaining := make([]string, 0, len(args))
	await := false
	mode := pausePointCapturedVariablesModeFull
	modeSet := false
	var capturedVariableNames []string
	namesSet := false
	var expectations []pausePointExpectation
	var triggerCommand string
	var triggerArgs []string
	triggerSet := false
	resumePlay := false

	for index := 0; index < len(args); index++ {
		arg := args[index]

		if arg == "--"+tooldocs.PausePointEnableAwaitFlagName {
			await = true
			continue
		}

		if arg == "--"+tooldocs.PausePointResumePlayFlagName {
			resumePlay = true
			continue
		}

		// --resume-play=true|1 must be accepted here too: otherwise the =value form falls through
		// to Unity schema parsing and becomes a confusing unrelated error.
		if isPausePointFlag(arg, tooldocs.PausePointResumePlayFlagName) {
			name, value, consumedNext, err := clicore.ParseFlagValue(arg, args, index)
			if err != nil {
				return nil, false, mode, nil, nil, "", nil, false, err
			}
			if name != tooldocs.PausePointResumePlayFlagName {
				remaining = append(remaining, arg)
				continue
			}
			if value != "true" && value != "1" {
				return nil, false, mode, nil, nil, "", nil, false, clierrors.InvalidValueArgumentError(
					"--"+tooldocs.PausePointResumePlayFlagName, value, "boolean flag (pass with no value, or =true)")
			}
			resumePlay = true
			if consumedNext {
				index++
			}
			continue
		}

		if isPausePointFlag(arg, tooldocs.PausePointTriggerFlagName) {
			name, value, consumedNext, err := clicore.ParseFlagValue(arg, args, index)
			if err != nil {
				return nil, false, mode, nil, nil, "", nil, false, err
			}
			if name != tooldocs.PausePointTriggerFlagName {
				remaining = append(remaining, arg)
				continue
			}
			parsedCommand, parsedArgs, parseErr := parsePausePointTriggerCommand(pausePointEnableCommandName, value)
			if parseErr != nil {
				return nil, false, mode, nil, nil, "", nil, false, parseErr
			}
			triggerCommand = parsedCommand
			triggerArgs = parsedArgs
			triggerSet = true
			if consumedNext {
				index++
			}
			continue
		}

		if isPausePointFlag(arg, tooldocs.PausePointCapturedVariablesFlagName) {
			name, value, consumedNext, err := clicore.ParseFlagValue(arg, args, index)
			if err != nil {
				return nil, false, mode, nil, nil, "", nil, false, err
			}
			if name != tooldocs.PausePointCapturedVariablesFlagName {
				remaining = append(remaining, arg)
				continue
			}
			parsedMode, err := parsePausePointCapturedVariablesMode(value)
			if err != nil {
				return nil, false, mode, nil, nil, "", nil, false, err
			}
			mode = parsedMode
			modeSet = true
			if consumedNext {
				index++
			}
			continue
		}

		if isPausePointFlag(arg, tooldocs.PausePointCapturedVariableNamesFlagName) {
			name, value, consumedNext, err := clicore.ParseFlagValue(arg, args, index)
			if err != nil {
				return nil, false, mode, nil, nil, "", nil, false, err
			}
			if name != tooldocs.PausePointCapturedVariableNamesFlagName {
				remaining = append(remaining, arg)
				continue
			}
			capturedVariableNames = parsePausePointCapturedVariableNames(value)
			namesSet = true
			if consumedNext {
				index++
			}
			continue
		}

		if isPausePointFlag(arg, tooldocs.PausePointExpectFlagName) {
			name, value, consumedNext, err := clicore.ParseFlagValue(arg, args, index)
			if err != nil {
				return nil, false, mode, nil, nil, "", nil, false, err
			}
			if name != tooldocs.PausePointExpectFlagName {
				remaining = append(remaining, arg)
				continue
			}
			expectation, parseErr := parsePausePointExpectFlagValue(value)
			if parseErr != nil {
				return nil, false, mode, nil, nil, "", nil, false, parseErr
			}
			expectations = append(expectations, expectation)
			if consumedNext {
				index++
			}
			continue
		}

		remaining = append(remaining, arg)
	}

	if !await && (modeSet || namesSet || len(expectations) > 0 || triggerSet || resumePlay) {
		option := "--" + tooldocs.PausePointCapturedVariablesFlagName
		switch {
		case resumePlay:
			option = "--" + tooldocs.PausePointResumePlayFlagName
		case triggerSet:
			option = "--" + tooldocs.PausePointTriggerFlagName
		case len(expectations) > 0:
			option = "--" + tooldocs.PausePointExpectFlagName
		case namesSet:
			option = "--" + tooldocs.PausePointCapturedVariableNamesFlagName
		}
		return nil, false, mode, nil, nil, "", nil, false, &clierrors.ArgumentError{
			Message: "--captured-variables, --captured-variable-names, --expect, --trigger, and --resume-play require --await",
			Option:  option,
			Command: pausePointEnableCommandName,
			NextActions: []string{
				"Pass `--await` to wait for the marker after enabling, or drop these options.",
			},
		}
	}

	return remaining, await, mode, capturedVariableNames, expectations, triggerCommand, triggerArgs, resumePlay, nil
}

func isPausePointFlag(arg string, flagName string) bool {
	return arg == "--"+flagName || strings.HasPrefix(arg, "--"+flagName+"=")
}

func runEnablePausePointCommand(
	ctx context.Context,
	connection unityipc.Connection,
	args []string,
	startPath string,
	stdout io.Writer,
	stderr io.Writer,
) int {
	remainingArgs, await, capturedVariablesMode, capturedVariableNames, expectations, triggerCommand, triggerArgs, resumePlay, err := extractPausePointEnableAwaitFlags(args)
	if err != nil {
		clierrors.WriteClassifiedError(stderr, err, clierrors.ErrorContext{
			ProjectRoot: connection.ProjectRoot,
			Command:     pausePointEnableCommandName,
		})
		return 1
	}

	if !await {
		return runDynamicProjectTool(ctx, connection, pausePointEnableCommandName, remainingArgs, startPath, stdout, stderr)
	}

	tool, cache, ok, err := clicore.FindToolForCommand(connection.ProjectRoot, pausePointEnableCommandName)
	if err != nil {
		clierrors.WriteClassifiedError(stderr, err, clierrors.ErrorContext{
			ProjectRoot: connection.ProjectRoot,
			Command:     pausePointEnableCommandName,
		})
		return 1
	}
	if !ok {
		clierrors.WriteErrorEnvelope(stderr, clicore.UnknownCommandError(pausePointEnableCommandName, cache, clierrors.ErrorContext{
			ProjectRoot: connection.ProjectRoot,
			Command:     pausePointEnableCommandName,
		}))
		return 1
	}

	params, nestedProjectPath, ok := prepareDynamicToolParams(
		pausePointEnableCommandName,
		remainingArgs,
		tool,
		connection,
		startPath,
		stderr,
	)
	if !ok {
		return 1
	}
	if nestedProjectPath != "" && nestedProjectPath != connection.ProjectRoot {
		clierrors.WriteErrorEnvelope(stderr, (&clierrors.ArgumentError{
			Message:      "--project-path must target the same Unity project for this command",
			Option:       "--project-path",
			ExpectedType: "path",
			Command:      pausePointEnableCommandName,
			NextActions:  []string{"Use one `--project-path <path>` value for the target Unity project."},
		}).ToCLIError(clierrors.ErrorContext{ProjectRoot: connection.ProjectRoot, Command: pausePointEnableCommandName}))
		return 1
	}

	return runEnablePausePointAndAwait(
		ctx, connection, params, capturedVariablesMode, capturedVariableNames, expectations,
		triggerCommand, triggerArgs, resumePlay, startPath, stdout, stderr)
}

// runEnablePausePointAndAwait sends the same single enable-pause-point IPC request the
// non-awaiting path sends, then, only on a successful enable, reuses the existing
// waitForPausePoint poll loop (get-pause-point-status) exactly as await-pause-point does. Unity
// never sees a different request shape or an extra IPC call for --await.
func runEnablePausePointAndAwait(
	ctx context.Context,
	connection unityipc.Connection,
	params map[string]any,
	capturedVariablesMode pausePointCapturedVariablesMode,
	capturedVariableNames []string,
	expectations []pausePointExpectation,
	triggerCommand string,
	triggerArgs []string,
	resumePlay bool,
	startPath string,
	stdout io.Writer,
	stderr io.Writer,
) int {
	startedAt := time.Now()
	spinner := clicore.NewToolSpinner(stderr, pausePointEnableCommandName)
	applyDebugTimingParams(pausePointEnableCommandName, params)
	outcome, err := sendWithTransientConnectionRetry(
		ctx,
		connection,
		pausePointEnableCommandName,
		params,
		ui.NewSpinnerProgressFunc(spinner, "Executing enable-pause-point..."),
	)
	spinner.Stop()
	if err != nil {
		writeDebugTiming(stderr, pausePointEnableCommandName, time.Since(startedAt), outcome)
		clierrors.WriteToolFailure(stderr, err, outcome, clierrors.ErrorContext{
			ProjectRoot: connection.ProjectRoot,
			Command:     pausePointEnableCommandName,
		})
		return 1
	}

	enableResult := stripDebugTimingResult(pausePointEnableCommandName, outcome.Result)

	var enableResponse pausePointStatusResponse
	if unmarshalErr := json.Unmarshal(enableResult, &enableResponse); unmarshalErr != nil {
		clierrors.WriteClassifiedError(stderr, unmarshalErr, clierrors.ErrorContext{
			ProjectRoot: connection.ProjectRoot,
			Command:     pausePointEnableCommandName,
		})
		return 1
	}

	if !enableResponse.Success {
		clicore.WriteJSON(stdout, enableResult)
		writeDebugTiming(stderr, pausePointEnableCommandName, time.Since(startedAt), outcome)
		return toolEnvelopeExitCode(enableResult)
	}

	waitOptions := waitForPausePointOptions{
		id:                    enableResponse.Id,
		timeoutSeconds:        enableResponse.TimeoutSeconds,
		timeout:               time.Duration(enableResponse.TimeoutSeconds) * time.Second,
		matchingLogsMaxCount:  pausePointDefaultLogsMaxCount,
		capturedVariablesMode: capturedVariablesMode,
		capturedVariableNames: capturedVariableNames,
		expectations:          expectations,
		triggerCommand:        triggerCommand,
		triggerArgs:           triggerArgs,
		startPath:             startPath,
		resumePlay:            resumePlay,
	}

	return runPausePointWaitAfterEnable(
		ctx,
		connection,
		waitOptions,
		enablePausePointPropagatedFields{
			Warning:          enableResponse.Warning,
			ResolvedLine:     enableResponse.ResolvedLine,
			ResolvedLineText: enableResponse.ResolvedLineText,
			ResolvedMethod:   enableResponse.ResolvedMethod,
			SnapshotTiming:   enableResponse.SnapshotTiming,
		},
		stdout,
		stderr,
	)
}

// enablePausePointPropagatedFields carries enable-time fields into the --await hit response,
// matching how Warning alone used to be forwarded from runEnablePausePointAndAwait.
type enablePausePointPropagatedFields struct {
	Warning          string
	ResolvedLine     int
	ResolvedLineText string
	ResolvedMethod   string
	SnapshotTiming   string
}

// runPausePointWaitAfterEnable mirrors runWaitForPausePoint's response shaping (the shared
// helpers it calls are reused as-is) but also folds enable-time fields (Warning and the
// file:line resolution details) into the merged response, since --await must not drop evidence
// the plain enable-pause-point response would have carried.
func runPausePointWaitAfterEnable(
	ctx context.Context,
	connection unityipc.Connection,
	options waitForPausePointOptions,
	enableFields enablePausePointPropagatedFields,
	stdout io.Writer,
	stderr io.Writer,
) int {
	spinner := clicore.NewToolSpinner(stderr, pausePointEnableCommandName)
	response, state, triggerResult, resumeResult, err := waitForPausePoint(ctx, connection, options)
	spinner.Stop()
	if err != nil {
		clierrors.WriteClassifiedError(stderr, err, clierrors.ErrorContext{
			ProjectRoot: connection.ProjectRoot,
			Command:     pausePointEnableCommandName,
		})
		return 1
	}

	if state == pausePointWaitStateHit {
		// Evaluated against the raw, unfiltered CapturedVariables before the filters below can
		// narrow or strip values, same as the plain await-pause-point path.
		expectations := evaluatePausePointExpectations(response.CapturedVariables, options.expectations)

		response.TriggerResult = triggerResult
		response.ResumePlayResult = resumeResult
		// Status polls never carry these fields today; always prefer the enable-time values.
		response.ResolvedLine = enableFields.ResolvedLine
		response.ResolvedLineText = enableFields.ResolvedLineText
		response.ResolvedMethod = enableFields.ResolvedMethod
		response.SnapshotTiming = enableFields.SnapshotTiming
		response = filterPausePointCapturedVariableHistory(response)
		response = filterPausePointCapturedVariablesByName(response, options.capturedVariableNames)
		response = applyPausePointCapturedVariablesMode(response, options.capturedVariablesMode)

		var payload any = response
		logs, logsErr := fetchMatchingLogs(ctx, connection, options.id, options.matchingLogsMaxCount)
		switch {
		case logsErr == nil:
			payload = pausePointWaitResult{
				pausePointStatusResponse: response,
				MatchingLogs:             logs.Logs,
				Warning:                  joinPausePointWarnings(enableFields.Warning, buildPausePointWarning(logs, response.HitCount)),
				Expectations:             expectations,
				AllExpectationsPassed:    pausePointAllExpectationsPassedPointer(expectations),
			}
		case enableFields.Warning != "" || len(expectations) > 0:
			// Best-effort like the plain await path: a failed log fetch must not also drop the
			// enable-time warning or --expect results, since those are the only evidence left in
			// this branch. Uses an anonymous struct (not pausePointWaitResult) so MatchingLogs is
			// omitted entirely rather than serialized as an empty array, preserving "empty array
			// only means a successful fetch with no matches".
			payload = struct {
				pausePointStatusResponse
				Warning               string                        `json:"Warning,omitempty"`
				Expectations          []pausePointExpectationResult `json:"Expectations,omitempty"`
				AllExpectationsPassed *bool                         `json:"AllExpectationsPassed,omitempty"`
			}{
				pausePointStatusResponse: response,
				Warning:                  enableFields.Warning,
				Expectations:             expectations,
				AllExpectationsPassed:    pausePointAllExpectationsPassedPointer(expectations),
			}
		}
		result, marshalErr := json.Marshal(payload)
		if marshalErr != nil {
			clierrors.WriteClassifiedError(stderr, marshalErr, clierrors.ErrorContext{
				ProjectRoot: connection.ProjectRoot,
				Command:     pausePointEnableCommandName,
			})
			return 1
		}
		clicore.WriteJSON(stdout, result)
		return 0
	}

	if state == pausePointWaitStateTimeout {
		clearPausePointAfterWaitTimeout(ctx, connection, options.id)
	}

	waitErr := pausePointWaitError(connection.ProjectRoot, options, response, state)
	waitErr.Command = pausePointEnableCommandName
	if enableFields.Warning != "" {
		waitErr.Details["EnableWarning"] = enableFields.Warning
	}
	if triggerResult != nil {
		waitErr.Details["TriggerResult"] = triggerResult
	}
	if resumeResult != nil {
		waitErr.Details["ResumePlayResult"] = resumeResult
	}
	if state == pausePointWaitStateTimeout {
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

func joinPausePointWarnings(warnings ...string) string {
	nonEmpty := make([]string, 0, len(warnings))
	for _, warning := range warnings {
		if warning != "" {
			nonEmpty = append(nonEmpty, warning)
		}
	}
	return strings.Join(nonEmpty, " ")
}
