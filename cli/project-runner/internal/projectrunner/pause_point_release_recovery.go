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

var sendFreshCompileRequest = sendWithTransientConnectionRetryAndResponseTimeout

var waitPausePointRecoveryBusyRetry = waitContextDuration

var runOneFreshCompileForPausePointRecovery = runOneFreshCompileForPausePointRecoveryDefault

var runFreshCompileForPausePointRecovery = runFreshCompileWithBusyRetryForPausePointRecovery

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

func injectPausePointRecoveryWarning(raw []byte) ([]byte, error) {
	fields := map[string]json.RawMessage{}
	if err := json.Unmarshal(raw, &fields); err != nil {
		return nil, err
	}
	existing := ""
	if warningRaw, ok := fields["Warning"]; ok {
		if err := json.Unmarshal(warningRaw, &existing); err != nil {
			return nil, err
		}
	}
	joined, err := json.Marshal(joinPausePointWarnings(existing, pausePointAutoDebugSwitchWarning))
	if err != nil {
		return nil, err
	}
	fields["Warning"] = joined
	return json.Marshal(fields)
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
	method string,
	params map[string]any,
	progress unityipc.ProgressFunc,
	responseTimeout time.Duration,
	budget time.Duration,
) (unityipc.UnitySendOutcome, error) {
	deadline := time.Now().Add(budget)
	for {
		outcome, err := sendFreshCompileRequest(ctx, connection, method, params, progress, responseTimeout)
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

type compileErrorCodeProbe struct {
	ErrorCode string `json:"ErrorCode"`
}

// Why ErrorCode only: the collision is a structured compile result, not a message string.
func isRetryablePausePointRecoveryCompileResult(raw []byte) bool {
	var probe compileErrorCodeProbe
	if json.Unmarshal(raw, &probe) != nil {
		return false
	}
	return probe.ErrorCode == compileAlreadyInProgressErrorCode ||
		probe.ErrorCode == compileEditorUpdatingErrorCode
}

func runOneFreshCompileForPausePointRecoveryDefault(
	ctx context.Context,
	connection unityipc.Connection,
	params map[string]any,
	stdout io.Writer,
	stderr io.Writer,
	budget time.Duration,
) int {
	deps := defaultCompileWaitDeps()
	deps.sendCompile = func(
		sendCtx context.Context,
		sendConnection unityipc.Connection,
		method string,
		sendParams map[string]any,
		progress unityipc.ProgressFunc,
		responseTimeout time.Duration,
	) (unityipc.UnitySendOutcome, error) {
		return sendCompileWithBusyRetry(
			sendCtx, sendConnection, method, sendParams, progress, responseTimeout, budget)
	}
	return runFreshCompileWithDomainReloadWaitWithDeps(ctx, connection, params, stdout, stderr, deps)
}

// Issues a fresh compile for pause-point recovery, retrying server_busy sends and
// compile results that only mean Unity is still compiling or updating.
func runFreshCompileWithBusyRetryForPausePointRecovery(
	ctx context.Context,
	connection unityipc.Connection,
	params map[string]any,
	stdout io.Writer,
	stderr io.Writer,
) int {
	waitTimeout, timeoutErr := compileWaitTimeoutFromParams(params)
	if timeoutErr != nil {
		clierrors.WriteClassifiedError(stderr, timeoutErr, clierrors.ErrorContext{
			ProjectRoot: connection.ProjectRoot,
			Command:     clicore.CompileCommandName,
		})
		return 1
	}

	deadline := time.Now().Add(waitTimeout)
	for {
		var attemptOut bytes.Buffer
		remaining := time.Until(deadline)
		if remaining < 0 {
			remaining = 0
		}
		code := runOneFreshCompileForPausePointRecovery(
			ctx, connection, params, &attemptOut, stderr, remaining)
		if code == 0 {
			return 0
		}
		if !isRetryablePausePointRecoveryCompileResult(attemptOut.Bytes()) {
			_, _ = stdout.Write(attemptOut.Bytes())
			return code
		}
		remaining = time.Until(deadline)
		if remaining <= 0 {
			_, _ = stdout.Write(attemptOut.Bytes())
			return code
		}
		wait := pausePointRecoveryCompileBusyRetryInterval
		if wait > remaining {
			wait = remaining
		}
		if waitErr := waitPausePointRecoveryBusyRetry(ctx, wait); waitErr != nil {
			clierrors.WriteClassifiedError(stderr, waitErr, clierrors.ErrorContext{
				ProjectRoot: connection.ProjectRoot,
				Command:     pausePointEnableCommandName,
			})
			return 1
		}
	}
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
