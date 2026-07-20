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
	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

// pausePointEnableCommandName keeps enable-pause-point as the schema-driven tool it already is
// (default-tools.json), not a clicore.NativeCommands entry: it must keep its existing tool
// identity (help/tool-settings gating) rather than take on a second, native one. This constant
// only lets runResolvedProjectCommand intercept CLI-only await flags before falling through to
// the same generic schema pipeline every other tool call uses, mirroring how
// extractDynamicCodeFileFlag intercepts --code-file for execute-dynamic-code.
const pausePointEnableCommandName = "enable-pause-point"

const pausePointEnableAwaitFlagName = "await"

// extractPausePointEnableAwaitFlags pulls the CLI-only --await/--captured-variables/
// --captured-variable-names flags out of enable-pause-point args before generic schema parsing,
// because none of them are part of the Unity-side EnablePausePointSchema.
func extractPausePointEnableAwaitFlags(args []string) ([]string, bool, pausePointCapturedVariablesMode, []string, error) {
	remaining := make([]string, 0, len(args))
	await := false
	mode := pausePointCapturedVariablesModeFull
	modeSet := false
	var capturedVariableNames []string
	namesSet := false

	for index := 0; index < len(args); index++ {
		arg := args[index]

		if arg == "--"+pausePointEnableAwaitFlagName {
			await = true
			continue
		}

		if isPausePointFlag(arg, PausePointCapturedVariablesFlagName) {
			name, value, consumedNext, err := clicore.ParseFlagValue(arg, args, index)
			if err != nil {
				return nil, false, mode, nil, err
			}
			if name != PausePointCapturedVariablesFlagName {
				remaining = append(remaining, arg)
				continue
			}
			parsedMode, err := parsePausePointCapturedVariablesMode(value)
			if err != nil {
				return nil, false, mode, nil, err
			}
			mode = parsedMode
			modeSet = true
			if consumedNext {
				index++
			}
			continue
		}

		if isPausePointFlag(arg, PausePointCapturedVariableNamesFlagName) {
			name, value, consumedNext, err := clicore.ParseFlagValue(arg, args, index)
			if err != nil {
				return nil, false, mode, nil, err
			}
			if name != PausePointCapturedVariableNamesFlagName {
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

		remaining = append(remaining, arg)
	}

	if !await && (modeSet || namesSet) {
		return nil, false, mode, nil, &clierrors.ArgumentError{
			Message: "--captured-variables and --captured-variable-names require --await",
			Option:  "--" + PausePointCapturedVariablesFlagName,
			Command: pausePointEnableCommandName,
			NextActions: []string{
				"Pass `--await` to wait for the marker after enabling, or drop these options.",
			},
		}
	}

	return remaining, await, mode, capturedVariableNames, nil
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
	remainingArgs, await, capturedVariablesMode, capturedVariableNames, err := extractPausePointEnableAwaitFlags(args)
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

	return runEnablePausePointAndAwait(ctx, connection, params, capturedVariablesMode, capturedVariableNames, stdout, stderr)
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
	stdout io.Writer,
	stderr io.Writer,
) int {
	startedAt := time.Now()
	spinner := clicore.NewToolSpinner(stderr, pausePointEnableCommandName)
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
	}

	return runPausePointWaitAfterEnable(ctx, connection, waitOptions, enableResponse.Warning, stdout, stderr)
}

// runPausePointWaitAfterEnable mirrors runWaitForPausePoint's response shaping (the shared
// helpers it calls are reused as-is) but also folds the enable-time Warning (for example the
// physics-callback cached-dispatch warning) into the merged response, since --await must not
// drop evidence the plain enable-pause-point response would have carried.
func runPausePointWaitAfterEnable(
	ctx context.Context,
	connection unityipc.Connection,
	options waitForPausePointOptions,
	enableWarning string,
	stdout io.Writer,
	stderr io.Writer,
) int {
	spinner := clicore.NewToolSpinner(stderr, pausePointEnableCommandName)
	response, state, err := waitForPausePoint(ctx, connection, options)
	spinner.Stop()
	if err != nil {
		clierrors.WriteClassifiedError(stderr, err, clierrors.ErrorContext{
			ProjectRoot: connection.ProjectRoot,
			Command:     pausePointEnableCommandName,
		})
		return 1
	}

	if state == pausePointWaitStateHit {
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
				Warning:                  joinPausePointWarnings(enableWarning, buildPausePointWarning(logs, response.HitCount)),
			}
		case enableWarning != "":
			// Best-effort like the plain await path: a failed log fetch must not also drop the
			// enable-time warning, since that is the only warning source left in this branch.
			payload = pausePointWaitResult{
				pausePointStatusResponse: response,
				MatchingLogs:             []pausePointMatchingLog{},
				Warning:                  enableWarning,
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
	if enableWarning != "" {
		waitErr.Details["EnableWarning"] = enableWarning
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
