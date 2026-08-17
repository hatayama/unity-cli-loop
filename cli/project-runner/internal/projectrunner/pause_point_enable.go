package projectrunner

import (
	"context"
	"encoding/json"
	"io"
	"slices"
	"strings"
	"time"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"

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

// pausePointEnableFlagState accumulates the CLI-only flags pulled out of enable-pause-point's argv.
type pausePointEnableFlagState struct {
	await                 bool
	mode                  pausePointCapturedVariablesMode
	modeSet               bool
	capturedVariableNames []string
	namesSet              bool
	expectations          []pausePointExpectation
	triggerCommand        string
	triggerArgs           []string
	triggerSet            bool
	resumePlay            bool
}

// pausePointEnableFlagHandler consumes one CLI-only flag. applyBare is set for a flag that may be
// passed with no value, applyValue for a flag that accepts one; --await has no value form at all, so
// `--await=x` is deliberately left for Unity schema parsing exactly as before.
type pausePointEnableFlagHandler struct {
	applyBare  func(state *pausePointEnableFlagState)
	applyValue func(state *pausePointEnableFlagState, value string) error
}

// pausePointEnableFlagHandlers is keyed by the same flag names the option listings advertise
// (tooldocs.PausePointEnableCLIOnlyOptions), so a flag can no longer be documented without being
// parsed or parsed without being documented — a contract test compares the two key sets.
var pausePointEnableFlagHandlers = map[string]pausePointEnableFlagHandler{
	tooldocs.PausePointEnableAwaitFlagName: {
		applyBare: func(state *pausePointEnableFlagState) { state.await = true },
	},
	tooldocs.PausePointResumePlayFlagName: {
		applyBare: func(state *pausePointEnableFlagState) { state.resumePlay = true },
		// --resume-play=true|1 must be accepted here too: otherwise the =value form falls through
		// to Unity schema parsing and becomes a confusing unrelated error.
		applyValue: func(state *pausePointEnableFlagState, value string) error {
			if value != "true" && value != "1" {
				return clierrors.InvalidValueArgumentError(
					"--"+tooldocs.PausePointResumePlayFlagName, value, "boolean flag (pass with no value, or =true)")
			}
			state.resumePlay = true
			return nil
		},
	},
	tooldocs.PausePointTriggerFlagName: {
		applyValue: func(state *pausePointEnableFlagState, value string) error {
			triggerCommand, triggerArgs, err := parsePausePointTriggerCommand(pausePointEnableCommandName, value)
			if err != nil {
				return err
			}
			state.triggerCommand = triggerCommand
			state.triggerArgs = triggerArgs
			state.triggerSet = true
			return nil
		},
	},
	tooldocs.PausePointCapturedVariablesFlagName: {
		applyValue: func(state *pausePointEnableFlagState, value string) error {
			mode, err := parsePausePointCapturedVariablesMode(value)
			if err != nil {
				return err
			}
			state.mode = mode
			state.modeSet = true
			return nil
		},
	},
	tooldocs.PausePointCapturedVariableNamesFlagName: {
		applyValue: func(state *pausePointEnableFlagState, value string) error {
			state.capturedVariableNames = parsePausePointCapturedVariableNames(value)
			state.namesSet = true
			return nil
		},
	},
	tooldocs.PausePointExpectFlagName: {
		applyValue: func(state *pausePointEnableFlagState, value string) error {
			expectation, err := parsePausePointExpectFlagValue(value)
			if err != nil {
				return err
			}
			state.expectations = append(state.expectations, expectation)
			return nil
		},
	},
}

// extractPausePointEnableAwaitFlags pulls the CLI-only --await/--captured-variables/
// --captured-variable-names/--expect/--trigger/--resume-play flags out of enable-pause-point args
// before generic schema parsing, because none of them are part of the Unity-side
// EnablePausePointSchema. Anything this function does not recognize is passed through untouched for
// the generic schema pipeline to handle.
func extractPausePointEnableAwaitFlags(
	args []string,
) ([]string, bool, pausePointCapturedVariablesMode, []string, []pausePointExpectation, string, []string, bool, error) {
	remaining := make([]string, 0, len(args))
	state := pausePointEnableFlagState{mode: pausePointCapturedVariablesModeFull}

	for index := 0; index < len(args); index++ {
		arg := args[index]

		flagName, handler, ok := findPausePointEnableFlagHandler(arg)
		if !ok {
			remaining = append(remaining, arg)
			continue
		}

		if arg == "--"+flagName && handler.applyBare != nil {
			handler.applyBare(&state)
			continue
		}
		if handler.applyValue == nil {
			remaining = append(remaining, arg)
			continue
		}

		name, value, consumedNext, err := clicore.ParseFlagValue(arg, args, index)
		if err != nil {
			return nil, false, state.mode, nil, nil, "", nil, false, err
		}
		if name != flagName {
			remaining = append(remaining, arg)
			continue
		}
		if err := handler.applyValue(&state, value); err != nil {
			return nil, false, state.mode, nil, nil, "", nil, false, err
		}
		if consumedNext {
			index++
		}
	}

	if err := pausePointEnableAwaitRequirementError(state); err != nil {
		return nil, false, state.mode, nil, nil, "", nil, false, err
	}

	return remaining, state.await, state.mode, state.capturedVariableNames, state.expectations,
		state.triggerCommand, state.triggerArgs, state.resumePlay, nil
}

// findPausePointEnableFlagHandler resolves an argv token to the CLI-only flag it names. The match is
// exact or `--flag=`-prefixed, and the flag names share no such prefix, so at most one handler can
// match regardless of map iteration order.
func findPausePointEnableFlagHandler(arg string) (string, pausePointEnableFlagHandler, bool) {
	for flagName, handler := range pausePointEnableFlagHandlers {
		if isPausePointFlag(arg, flagName) {
			return flagName, handler, true
		}
	}
	return "", pausePointEnableFlagHandler{}, false
}

// pausePointEnableAwaitRequirementError rejects the orchestration flags when --await was not passed:
// without the wait there is nothing for them to configure. The reported Option follows a fixed
// priority so the message names one concrete flag instead of whichever the parser saw last.
func pausePointEnableAwaitRequirementError(state pausePointEnableFlagState) error {
	if state.await {
		return nil
	}
	if !state.modeSet && !state.namesSet && len(state.expectations) == 0 && !state.triggerSet && !state.resumePlay {
		return nil
	}

	option := "--" + tooldocs.PausePointCapturedVariablesFlagName
	switch {
	case state.resumePlay:
		option = "--" + tooldocs.PausePointResumePlayFlagName
	case state.triggerSet:
		option = "--" + tooldocs.PausePointTriggerFlagName
	case len(state.expectations) > 0:
		option = "--" + tooldocs.PausePointExpectFlagName
	case state.namesSet:
		option = "--" + tooldocs.PausePointCapturedVariableNamesFlagName
	}

	return &clierrors.ArgumentError{
		Message: "--captured-variables, --captured-variable-names, --expect, --trigger, and --resume-play require --await",
		Option:  option,
		Command: pausePointEnableCommandName,
		NextActions: []string{
			"Pass `--await` to wait for the marker after enabling, or drop these options.",
		},
	}
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
		return completeEnableWithReleaseRecovery(
			ctx,
			connection,
			stdout,
			stderr,
			func(writer io.Writer) int {
				return runDynamicProjectTool(
					ctx, connection, pausePointEnableCommandName, remainingArgs, startPath, writer, stderr)
			},
		)
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
	enableResult, enableResponse, outcome, err := sendEnablePausePointAndDecode(ctx, connection, params, stderr)
	if err != nil {
		writeDebugTiming(stderr, pausePointEnableCommandName, time.Since(startedAt), outcome)
		return 1
	}

	if !enableResponse.Success && enableResponse.ErrorCode == pausePointReleaseCodeOptimizationErrorCode {
		if recoverCode := recoverReleaseCodeOptimization(ctx, connection, stdout, stderr); recoverCode != 0 {
			writeDebugTiming(stderr, pausePointEnableCommandName, time.Since(startedAt), outcome)
			return recoverCode
		}
		enableResult, enableResponse, outcome, err = sendEnablePausePointAndDecode(ctx, connection, params, stderr)
		if err != nil {
			writeDebugTiming(stderr, pausePointEnableCommandName, time.Since(startedAt), outcome)
			return 1
		}
		if enableResponse.Success {
			enableResponse.Warning = joinPausePointWarnings(enableResponse.Warning, pausePointAutoDebugSwitchWarning)
		}
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
		markerJustEnabled:     true,
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
	response, state, triggerResult, resumeResult, hasNewHitBaseline, err := waitForPausePoint(ctx, connection, options)
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

		logs, logsErr := fetchMatchingLogs(ctx, connection, options.id, options.matchingLogsMaxCount)
		// Why not join enableFields.Warning into Warning: that text is an enable-time patch
		// diagnostic (for example "may not hit on pre-existing GameObjects") and contradicts a
		// successful hit when folded into the hit-time Warning. It is exposed separately.
		payload := buildPausePointHitPayload(pausePointHitPayloadInputs{
			response:            response,
			logs:                logs,
			logsErr:             logsErr,
			unityWarning:        response.Warning,
			enableTimeWarning:   enableFields.Warning,
			triggerResult:       triggerResult,
			awaitedPausePointID: options.id,
			expectations:        expectations,
		})
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

	// Why skip clear when hasNewHitBaseline: the continuous/trace marker is still armed, and the
	// timeout hint tells the caller to await again (with --resume-play). Clearing here would disarm
	// it and discard the raw capture holder, making that recovery path impossible.
	if state == pausePointWaitStateTimeout && !hasNewHitBaseline {
		clearPausePointAfterWaitTimeout(ctx, connection, options.id)
	}

	waitErr := pausePointWaitError(connection.ProjectRoot, options, response, state, hasNewHitBaseline)
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

// joinPausePointWarnings concatenates hit-time warnings for one response, dropping empty ones and
// repeats. Inputs are status-poll text (usually empty), matching-logs diagnosis, and trigger-refusal
// text — enable-time patch diagnostics are not joined here; they use EnableTimeWarning.
func joinPausePointWarnings(warnings ...string) string {
	unique := make([]string, 0, len(warnings))
	for _, warning := range warnings {
		if warning == "" || slices.Contains(unique, warning) {
			continue
		}
		unique = append(unique, warning)
	}
	return strings.Join(unique, " ")
}
