package projectrunner

import (
	"bytes"
	"context"
	"encoding/json"
	"io"
	"time"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
	"github.com/hatayama/unity-cli-loop/common/ui"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

const pausePointReleaseCodeOptimizationErrorCode = "PAUSE_POINT_RELEASE_CODE_OPTIMIZATION"

const pausePointAutoDebugSwitchWarning = "Code Optimization was Release; switched to Debug and recompiled before arming the pause point. This setting reverts on every Editor restart, and each re-switch costs a full script recompile. Once the current task reaches a natural stopping point, suggest making Debug permanent: with the user's approval, run uloop set-code-optimization debug --startup (machine-wide: applies to every Unity project on this machine; only your project's C# script execution slows down, mainly during Play Mode - the Unity Editor itself is not slowed)."

const pausePointRecoveryCompileBusyRetryInterval = 2 * time.Second

const compileAlreadyInProgressErrorCode = "COMPILE_ALREADY_IN_PROGRESS"

const compileEditorUpdatingErrorCode = "COMPILE_EDITOR_UPDATING"

var sendSetCodeOptimizationDebug = sendSetCodeOptimizationDebugFromUnity

var waitPausePointRecoveryBusyRetry = waitContextDuration

var runFreshCompileForPausePointRecovery = runFreshCompileForPausePointRecoveryDefault

type pausePointEnableFailureProbe struct {
	Success   bool   `json:"Success"`
	ErrorCode string `json:"ErrorCode"`
}

func isReleaseCodeOptimizationEnableFailure(raw []byte) bool {
	var probe pausePointEnableFailureProbe
	if json.Unmarshal(raw, &probe) != nil {
		return false
	}
	return !probe.Success && probe.ErrorCode == pausePointReleaseCodeOptimizationErrorCode
}

func isSuccessfulEnableResponse(raw []byte) bool {
	var probe pausePointEnableFailureProbe
	if json.Unmarshal(raw, &probe) != nil {
		return false
	}
	return probe.Success
}

func applyPausePointRecoverySwitchWarning(response *pausePointStatusResponse) {
	if !response.Success {
		return
	}
	appendPausePointWarningToBothForms(response, pausePointAutoDebugSwitchWarning)
}

// appendPausePointWarningToBothForms adds one CLI-side warning while keeping Warnings the single
// aggregate and Warning its joined form, so neither field can carry a topic the other is missing.
func appendPausePointWarningToBothForms(response *pausePointStatusResponse, warning string) {
	*response = applyPausePointWarnings(*response, warning)
}

// injectPausePointRecoveryWarning adds the auto-switch note to an enable response Unity already
// built. The response is rewritten as a raw field map so unrelated keys survive untouched, while
// Warnings becomes the single aggregate, Warning its joined form, and Message's warning-count
// pointer is restated: Unity computed that count before this note existed.
func injectPausePointRecoveryWarning(raw []byte) ([]byte, error) {
	fields := map[string]json.RawMessage{}
	if err := json.Unmarshal(raw, &fields); err != nil {
		return nil, err
	}

	warnings, err := decodePausePointWarningFields(fields)
	if err != nil {
		return nil, err
	}
	warnings = appendPausePointWarningEntry(warnings, pausePointAutoDebugSwitchWarning)

	message := ""
	if err := decodePausePointStringField(fields, "Message", &message); err != nil {
		return nil, err
	}

	if err := encodePausePointField(fields, "Warning", joinPausePointWarnings(warnings...)); err != nil {
		return nil, err
	}
	if err := encodePausePointField(fields, "Warnings", warnings); err != nil {
		return nil, err
	}
	if err := encodePausePointField(
		fields,
		"Message",
		restatePausePointWarningCountMessage(message, len(warnings)),
	); err != nil {
		return nil, err
	}
	return json.Marshal(fields)
}

// decodePausePointWarningFields reads a response's warnings as a list. A response that carries only
// the joined Warning string still contributes its text as one entry rather than being discarded.
func decodePausePointWarningFields(fields map[string]json.RawMessage) ([]string, error) {
	if warningsRaw, ok := fields["Warnings"]; ok {
		var warnings []string
		if err := json.Unmarshal(warningsRaw, &warnings); err != nil {
			return nil, err
		}
		if len(warnings) > 0 {
			return warnings, nil
		}
	}

	warning := ""
	if err := decodePausePointStringField(fields, "Warning", &warning); err != nil {
		return nil, err
	}
	if warning == "" {
		return nil, nil
	}
	return []string{warning}, nil
}

func decodePausePointStringField(fields map[string]json.RawMessage, key string, target *string) error {
	raw, ok := fields[key]
	if !ok {
		return nil
	}
	return json.Unmarshal(raw, target)
}

func encodePausePointField(fields map[string]json.RawMessage, key string, value any) error {
	encoded, err := json.Marshal(value)
	if err != nil {
		return err
	}
	fields[key] = encoded
	return nil
}

func waitContextDuration(ctx context.Context, duration time.Duration) error {
	timer := time.NewTimer(duration)
	defer timer.Stop()
	select {
	case <-ctx.Done():
		return ctx.Err()
	case <-timer.C:
		return nil
	}
}

// Why a dedicated retry instead of the existing 10s busy window: assignment to
// CodeOptimization.Debug schedules a recompile, so the recovery compile send
// stays busy until that build finishes. The budget matches compile wait timeout
// so a large-project rebuild can complete; non-busy errors fail immediately.
func sendCompileWithBusyRetry(
	ctx context.Context,
	connection unityipc.Connection,
	send compileSendFunc,
	method string,
	params map[string]any,
	progress unityipc.ProgressFunc,
	responseTimeout time.Duration,
	budget time.Duration,
) (unityipc.UnitySendOutcome, error) {
	deadline := time.Now().Add(budget)
	for {
		outcome, err := send(ctx, connection, method, params, progress, responseTimeout)
		if err == nil || !isUnityServerBusyRPCError(err) {
			return outcome, err
		}
		remaining := time.Until(deadline)
		if remaining <= 0 {
			return outcome, err
		}
		wait := pausePointRecoveryCompileBusyRetryInterval
		if wait > remaining {
			wait = remaining
		}
		if waitErr := waitPausePointRecoveryBusyRetry(ctx, wait); waitErr != nil {
			return outcome, waitErr
		}
	}
}

func runFreshCompileForPausePointRecoveryDefault(
	ctx context.Context,
	connection unityipc.Connection,
	params map[string]any,
	stdout io.Writer,
	stderr io.Writer,
) int {
	return runFreshCompileForPausePointRecoveryWithDeps(ctx, connection, params, stdout, stderr, defaultCompileWaitDeps())
}

// runFreshCompileForPausePointRecoveryWithDeps compiles for pause-point recovery. The compile the
// Debug switch scheduled keeps Unity busy, so a send rejected as server_busy is sent again for as
// long as the wait allows; a request Unity lost or rejected as busy is sent again the way
// 'uloop compile' sends it again, under a new request ID.
func runFreshCompileForPausePointRecoveryWithDeps(
	ctx context.Context,
	connection unityipc.Connection,
	params map[string]any,
	stdout io.Writer,
	stderr io.Writer,
	deps compileWaitDeps,
) int {
	// Why read the wait first: it is also the budget for sending a server_busy send again.
	waitTimeout, err := compileWaitTimeoutFromParams(params)
	if err != nil {
		clierrors.WriteClassifiedError(stderr, err, clierrors.ErrorContext{
			ProjectRoot: connection.ProjectRoot,
			Command:     clicore.CompileCommandName,
		})
		return 1
	}
	deadline := time.Now().Add(waitTimeout)
	// Why take the send out first: reading it after deps.sendCompile is replaced would wrap the wrapper.
	inner := compileSendOrDefault(deps)
	deps.sendCompile = func(
		sendCtx context.Context,
		sendConnection unityipc.Connection,
		method string,
		sendParams map[string]any,
		progress unityipc.ProgressFunc,
		responseTimeout time.Duration,
	) (unityipc.UnitySendOutcome, error) {
		return sendCompileWithBusyRetry(
			sendCtx, sendConnection, inner, method, sendParams, progress, responseTimeout, time.Until(deadline))
	}
	result := runFreshCompileRecoveringWithDeps(ctx, connection, params, stderr, deps)
	// Why nothing on success: the enable response is the command's output, and the caller writes the
	// compile result only when the compile failed.
	if result.exitCode == 0 {
		return 0
	}
	return writeCompileExecutionResult(stdout, result)
}

func recoverReleaseCodeOptimization(
	ctx context.Context,
	connection unityipc.Connection,
	stdout io.Writer,
	stderr io.Writer,
) int {
	if err := sendSetCodeOptimizationDebug(ctx, connection); err != nil {
		clierrors.WriteClassifiedError(stderr, err, clierrors.ErrorContext{
			ProjectRoot: connection.ProjectRoot,
			Command:     pausePointEnableCommandName,
		})
		return 1
	}

	var compileOut bytes.Buffer
	code := runFreshCompileForPausePointRecovery(ctx, connection, map[string]any{}, &compileOut, stderr)
	if code != 0 {
		_, _ = stdout.Write(compileOut.Bytes())
		return code
	}
	return 0
}

func completeEnableWithReleaseRecovery(
	ctx context.Context,
	connection unityipc.Connection,
	stdout io.Writer,
	stderr io.Writer,
	sendEnable func(io.Writer) int,
) int {
	var captured bytes.Buffer
	code := sendEnable(&captured)
	if !isReleaseCodeOptimizationEnableFailure(captured.Bytes()) {
		_, _ = stdout.Write(captured.Bytes())
		return code
	}

	if recoverCode := recoverReleaseCodeOptimization(ctx, connection, stdout, stderr); recoverCode != 0 {
		return recoverCode
	}

	captured.Reset()
	code = sendEnable(&captured)
	if !isSuccessfulEnableResponse(captured.Bytes()) {
		_, _ = stdout.Write(captured.Bytes())
		return code
	}

	rewritten, err := injectPausePointRecoveryWarning(captured.Bytes())
	if err != nil {
		clierrors.WriteClassifiedError(stderr, err, clierrors.ErrorContext{
			ProjectRoot: connection.ProjectRoot,
			Command:     pausePointEnableCommandName,
		})
		return 1
	}
	clicore.WriteJSON(stdout, rewritten)
	return code
}

var sendEnablePausePointIPC = sendEnablePausePointIPCDefault

func sendEnablePausePointIPCDefault(
	ctx context.Context,
	connection unityipc.Connection,
	params map[string]any,
	stderr io.Writer,
) (unityipc.UnitySendOutcome, error) {
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
	return outcome, err
}

func sendEnablePausePointAndDecode(
	ctx context.Context,
	connection unityipc.Connection,
	params map[string]any,
	stderr io.Writer,
) ([]byte, pausePointStatusResponse, unityipc.UnitySendOutcome, error) {
	outcome, err := sendEnablePausePointIPC(ctx, connection, params, stderr)
	if err != nil {
		clierrors.WriteToolFailure(stderr, err, outcome, clierrors.ErrorContext{
			ProjectRoot: connection.ProjectRoot,
			Command:     pausePointEnableCommandName,
		})
		return nil, pausePointStatusResponse{}, outcome, err
	}

	enableResult := stripDebugTimingResult(pausePointEnableCommandName, outcome.Result)
	var enableResponse pausePointStatusResponse
	if unmarshalErr := json.Unmarshal(enableResult, &enableResponse); unmarshalErr != nil {
		clierrors.WriteClassifiedError(stderr, unmarshalErr, clierrors.ErrorContext{
			ProjectRoot: connection.ProjectRoot,
			Command:     pausePointEnableCommandName,
		})
		return nil, pausePointStatusResponse{}, outcome, unmarshalErr
	}
	return enableResult, enableResponse, outcome, nil
}
