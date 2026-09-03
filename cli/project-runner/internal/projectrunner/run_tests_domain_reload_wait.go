package projectrunner

import (
	"context"
	"crypto/rand"
	"encoding/hex"
	"encoding/json"
	"fmt"
	"io"
	"strings"
	"time"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
	"github.com/hatayama/unity-cli-loop/common/ui"

	"github.com/hatayama/unity-cli-loop/common/clicontract"
	"github.com/hatayama/unity-cli-loop/common/clicore"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

const (
	runTestsStatusCommandName                 = "get-run-tests-status"
	runTestsRequestIDParam                    = "RequestId"
	runTestsRespectEnterPlayModeSettingsParam = "RespectEnterPlayModeSettings"
	runTestsTestModeParam                     = "TestMode"
	runTestsTimeoutParam                      = "TimeoutSeconds"
	runTestsWaitTimeoutMargin                 = 60 * time.Second
	runTestsWaitDefaultTimeoutSeconds         = 600
	runTestsStatusProbeTimeout                = clicore.ToolReadinessProbeTimeout
	runTestsWaitPollInterval                  = clicore.ToolReadinessPoll
	runTestsWaitTimeoutErrorCode              = "RUN_TESTS_WAIT_TIMEOUT"
	runTestsDomainReloadWaitSpinnerMessage    = "Domain Reload interrupted the connection. Waiting for the PlayMode test run to finish..."
)

type runTestsStatusResponse struct {
	Success                  bool            `json:"Success"`
	Ready                    bool            `json:"Ready"`
	HasResult                bool            `json:"HasResult"`
	IsCompiling              bool            `json:"IsCompiling"`
	IsUpdating               bool            `json:"IsUpdating"`
	IsDomainReloadInProgress bool            `json:"IsDomainReloadInProgress"`
	Result                   json.RawMessage `json:"Result"`
	Message                  string          `json:"Message"`
}

type runTestsStatusQueryFunc func(context.Context, unityipc.Connection, string) (runTestsStatusResponse, error)

type runTestsSendFunc func(
	ctx context.Context,
	connection unityipc.Connection,
	method string,
	params map[string]any,
	progress unityipc.ProgressFunc,
) (unityipc.UnitySendOutcome, error)

type runTestsWaitDeps struct {
	send  runTestsSendFunc
	query runTestsStatusQueryFunc
}

func defaultRunTestsWaitDeps() runTestsWaitDeps {
	return runTestsWaitDeps{
		send:  sendWithTransientConnectionRetry,
		query: queryRunTestsStatusFromUnity,
	}
}

// shouldWaitForRunTestsDomainReload is true only for PlayMode runs that keep the
// project's Enter Play Mode settings. EditMode and the default force-off path
// still use the in-memory response and must not poll get-run-tests-status.
func shouldWaitForRunTestsDomainReload(params map[string]any) bool {
	respect, ok := params[runTestsRespectEnterPlayModeSettingsParam].(bool)
	if !ok || !respect {
		return false
	}
	testMode, ok := params[runTestsTestModeParam].(string)
	if !ok {
		return false
	}
	return strings.EqualFold(testMode, "PlayMode")
}

func prepareRunTestsWaitParams(params map[string]any) (string, error) {
	if value, ok := params[runTestsRequestIDParam].(string); ok && value != "" && isSafeCompileRequestID(value) {
		return value, nil
	}

	requestID, err := createRunTestsRequestID()
	if err != nil {
		return "", err
	}
	params[runTestsRequestIDParam] = requestID
	return requestID, nil
}

func createRunTestsRequestID() (string, error) {
	token := [4]byte{}
	if _, err := rand.Read(token[:]); err != nil {
		return "", err
	}
	return fmt.Sprintf("run_tests_%d_%s", time.Now().UnixMilli(), hex.EncodeToString(token[:])), nil
}

func runTestsWaitTimeoutFromParams(params map[string]any) time.Duration {
	seconds := runTestsWaitDefaultTimeoutSeconds
	value, exists := params[runTestsTimeoutParam]
	if exists && value != nil {
		if parsed, ok := positiveInt64FromAny(value); ok {
			seconds = int(parsed)
		}
	}
	return time.Duration(seconds)*time.Second + runTestsWaitTimeoutMargin
}

func queryRunTestsStatusFromUnity(ctx context.Context, connection unityipc.Connection, requestID string) (runTestsStatusResponse, error) {
	probeContext, cancel := context.WithTimeout(ctx, runTestsStatusProbeTimeout)
	defer cancel()

	response, err := unityipc.NewClient(connection, clicontract.ProjectRunnerVersion()).Send(
		probeContext,
		runTestsStatusCommandName,
		map[string]any{runTestsRequestIDParam: requestID},
	)
	if err != nil {
		return runTestsStatusResponse{}, err
	}

	status := runTestsStatusResponse{}
	if err := json.Unmarshal(response, &status); err != nil {
		return runTestsStatusResponse{}, err
	}
	return status, nil
}

// waitForRunTestsResult polls get-run-tests-status until that request id has a
// stored result. Ready is ignored: it is editor-wide, not request-specific.
func waitForRunTestsResult(
	ctx context.Context,
	connection unityipc.Connection,
	requestID string,
	timeout time.Duration,
	pollInterval time.Duration,
	query runTestsStatusQueryFunc,
) (json.RawMessage, bool, error) {
	deadline := time.Now().Add(timeout)
	ticker := time.NewTicker(pollInterval)
	defer ticker.Stop()
	for {
		if !time.Now().Before(deadline) {
			return nil, false, nil
		}

		status, err := query(ctx, connection, requestID)
		if err == nil && status.HasResult && len(status.Result) > 0 {
			return status.Result, true, nil
		}

		select {
		case <-ctx.Done():
			return nil, false, ctx.Err()
		case <-ticker.C:
		}
	}
}

func runRunTestsWithDomainReloadWait(
	ctx context.Context,
	connection unityipc.Connection,
	params map[string]any,
	stderr io.Writer,
) toolExecutionResult {
	return runRunTestsWithDomainReloadWaitWithDeps(ctx, connection, params, stderr, defaultRunTestsWaitDeps())
}

func runRunTestsWithDomainReloadWaitWithDeps(
	ctx context.Context,
	connection unityipc.Connection,
	params map[string]any,
	stderr io.Writer,
	deps runTestsWaitDeps,
) toolExecutionResult {
	requestID, err := prepareRunTestsWaitParams(params)
	if err != nil {
		clierrors.WriteClassifiedError(stderr, err, clierrors.ErrorContext{
			ProjectRoot: connection.ProjectRoot,
			Command:     clicore.RunTestsCommandName,
		})
		return toolExecutionResult{exitCode: 1}
	}

	applyDebugTimingParams(clicore.RunTestsCommandName, params)
	startedAt := time.Now()
	spinner := clicore.NewToolSpinner(stderr, clicore.RunTestsCommandName)
	outcome, err := deps.send(
		ctx,
		connection,
		clicore.RunTestsCommandName,
		params,
		ui.NewSpinnerProgressFunc(spinner, "Executing run-tests..."),
	)
	if err == nil {
		return finishRunTestsSendSuccess(stderr, spinner, startedAt, outcome)
	}
	if !shouldWaitForCompileStatus(err, outcome) {
		spinner.Stop()
		writeDebugTiming(stderr, clicore.RunTestsCommandName, time.Since(startedAt), outcome)
		clierrors.WriteToolFailure(stderr, err, outcome, clierrors.ErrorContext{
			ProjectRoot: connection.ProjectRoot,
			Command:     clicore.RunTestsCommandName,
		})
		return toolExecutionResult{exitCode: 1}
	}

	spinner.Update(runTestsDomainReloadWaitSpinnerMessage)
	return finishRunTestsRecoveredResult(
		ctx,
		connection,
		requestID,
		runTestsWaitTimeoutFromParams(params),
		stderr,
		spinner,
		startedAt,
		outcome,
		deps.query,
	)
}

func finishRunTestsSendSuccess(
	stderr io.Writer,
	spinner *ui.TerminalSpinner,
	startedAt time.Time,
	outcome unityipc.UnitySendOutcome,
) toolExecutionResult {
	spinner.Stop()
	result := stripDebugTimingResult(clicore.RunTestsCommandName, outcome.Result)
	writeDebugTiming(stderr, clicore.RunTestsCommandName, time.Since(startedAt), outcome)
	return toolExecutionResult{result: result, exitCode: toolEnvelopeExitCode(result)}
}

func finishRunTestsRecoveredResult(
	ctx context.Context,
	connection unityipc.Connection,
	requestID string,
	timeout time.Duration,
	stderr io.Writer,
	spinner *ui.TerminalSpinner,
	startedAt time.Time,
	outcome unityipc.UnitySendOutcome,
	query runTestsStatusQueryFunc,
) toolExecutionResult {
	result, completed, waitErr := waitForRunTestsResult(
		ctx,
		connection,
		requestID,
		timeout,
		runTestsWaitPollInterval,
		query,
	)
	spinner.Stop()
	writeDebugTiming(stderr, clicore.RunTestsCommandName, time.Since(startedAt), outcome)
	if waitErr != nil {
		clierrors.WriteClassifiedError(stderr, waitErr, clierrors.ErrorContext{
			ProjectRoot: connection.ProjectRoot,
			Command:     clicore.RunTestsCommandName,
		})
		return toolExecutionResult{exitCode: 1}
	}
	if !completed {
		clierrors.WriteErrorEnvelope(stderr, runTestsWaitTimeoutError(connection.ProjectRoot, timeout))
		return toolExecutionResult{exitCode: 1}
	}
	result = stripDebugTimingResult(clicore.RunTestsCommandName, result)
	return toolExecutionResult{result: result, exitCode: toolEnvelopeExitCode(result)}
}

func runTestsWaitTimeoutError(projectRoot string, timeout time.Duration) clierrors.CLIError {
	return clierrors.CLIError{
		ErrorCode: runTestsWaitTimeoutErrorCode,
		Phase:     clierrors.ErrorPhaseResponseWaiting,
		Message: fmt.Sprintf(
			"run-tests status wait timed out after %dms. This does not mean the Unity Editor is frozen; the PlayMode test run may still be finishing after Domain Reload.",
			timeout.Milliseconds()),
		Retryable: true,
		// Why not SafeToRetry: Unity may still be running the accepted suite after the reload,
		// so an immediate rerun would start a duplicate run.
		SafeToRetry: false,
		ProjectRoot: projectRoot,
		Command:     clicore.RunTestsCommandName,
		NextActions: []string{
			"Increase --timeout-seconds for long PlayMode suites and rerun with --respect-enter-play-mode-settings.",
			"Run a light command such as `uloop get-logs --max-count 1` to check whether Unity is responsive before treating this as a freeze.",
			"Only if Unity does not respond to any command, restart it with `uloop launch -r`.",
		},
	}
}
