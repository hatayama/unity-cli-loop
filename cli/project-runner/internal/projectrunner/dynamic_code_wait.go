package projectrunner

import (
	"context"
	"encoding/json"
	"io"
	"time"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
	"github.com/hatayama/unity-cli-loop/common/ui"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

const (
	dynamicCodeCompileOnlyParam                    = "CompileOnly"
	dynamicCodeDomainReloadWaitRequiredField       = "DomainReloadWaitRequired"
	legacyDynamicCodeDomainReloadWaitRequiredField = "domainReloadWaitRequired"
)

// dynamicCodeWaitDeps holds what tests replace in execute-dynamic-code's wait for the domain reload.
type dynamicCodeWaitDeps struct {
	waitForToolReadiness func(ctx context.Context, projectRoot string) error
}

func defaultDynamicCodeWaitDeps() dynamicCodeWaitDeps {
	return dynamicCodeWaitDeps{waitForToolReadiness: clicore.WaitForToolReadiness}
}

func runExecuteDynamicCodeWithDomainReloadWait(ctx context.Context, connection unityipc.Connection, params map[string]any, stdout io.Writer, stderr io.Writer) int {
	return runExecuteDynamicCodeWithDomainReloadWaitWithDeps(ctx, connection, params, stdout, stderr, defaultDynamicCodeWaitDeps())
}

func runExecuteDynamicCodeWithDomainReloadWaitWithDeps(
	ctx context.Context,
	connection unityipc.Connection,
	params map[string]any,
	stdout io.Writer,
	stderr io.Writer,
	deps dynamicCodeWaitDeps,
) int {
	applyDebugTimingParams(clicore.ExecuteDynamicCodeCommandName, params)
	startedAt := time.Now()
	spinner := clicore.NewToolSpinner(stderr, clicore.ExecuteDynamicCodeCommandName)
	outcome, err := sendWithTransientConnectionRetry(
		ctx,
		connection,
		clicore.ExecuteDynamicCodeCommandName,
		params,
		ui.NewSpinnerProgressFunc(spinner, "Executing execute-dynamic-code..."),
	)
	if err != nil {
		if shouldWaitForExecuteDynamicCodeDisconnect(err, outcome) {
			spinner.Update("Connection lost during execute-dynamic-code. Waiting for domain reload to complete...")
			if waitErr := deps.waitForToolReadiness(ctx, connection.ProjectRoot); waitErr != nil {
				spinner.Stop()
				clierrors.WriteClassifiedError(stderr, waitErr, clierrors.ErrorContext{
					ProjectRoot: connection.ProjectRoot,
					Command:     clicore.ExecuteDynamicCodeCommandName,
				})
				return 1
			}
			if outcome.RequestAccepted {
				spinner.Stop()
				writeDebugTiming(stderr, clicore.ExecuteDynamicCodeCommandName, time.Since(startedAt), outcome)
				clierrors.WriteErrorEnvelope(stderr, reloadedBeforeAnswerError(err, connection.ProjectRoot))
				return 1
			}
		}
		spinner.Stop()
		writeDebugTiming(stderr, clicore.ExecuteDynamicCodeCommandName, time.Since(startedAt), outcome)
		clierrors.WriteToolFailure(stderr, err, outcome, clierrors.ErrorContext{
			ProjectRoot: connection.ProjectRoot,
			Command:     clicore.ExecuteDynamicCodeCommandName,
		})
		return 1
	}

	if executeDynamicCodeDomainReloadWaitRequired(outcome.Result) {
		spinner.Update("Waiting for domain reload to complete...")
		if err := deps.waitForToolReadiness(ctx, connection.ProjectRoot); err != nil {
			spinner.Stop()
			clierrors.WriteClassifiedError(stderr, err, clierrors.ErrorContext{
				ProjectRoot: connection.ProjectRoot,
				Command:     clicore.ExecuteDynamicCodeCommandName,
			})
			return 1
		}
	}

	spinner.Stop()
	result := stripExecuteDynamicCodeControlResult(outcome.Result)
	result = stripDebugTimingResult(clicore.ExecuteDynamicCodeCommandName, result)
	clicore.WriteJSON(stdout, result)
	writeDebugTiming(stderr, clicore.ExecuteDynamicCodeCommandName, time.Since(startedAt), outcome)
	return toolEnvelopeExitCode(result)
}

func shouldWaitForExecuteDynamicCodeDomainReload(command string, params map[string]any) bool {
	if command != clicore.ExecuteDynamicCodeCommandName {
		return false
	}
	if compileOnly, ok := params[dynamicCodeCompileOnlyParam].(bool); ok && compileOnly {
		return false
	}
	return domainReloadWaitEnabled(params, false)
}

func domainReloadWaitEnabled(params map[string]any, defaultValue bool) bool {
	value, ok := params[clicore.DomainReloadWaitParam].(bool)
	if ok {
		return value
	}

	return defaultValue
}

func executeDynamicCodeDomainReloadWaitRequired(result []byte) bool {
	var payload struct {
		DomainReloadWaitRequired bool `json:"DomainReloadWaitRequired"`
	}
	if err := json.Unmarshal(result, &payload); err != nil {
		return false
	}
	return payload.DomainReloadWaitRequired
}

func shouldWaitForExecuteDynamicCodeDisconnect(err error, outcome unityipc.UnitySendOutcome) bool {
	if err == nil {
		return false
	}
	if !outcome.RequestDispatched {
		return false
	}
	return clierrors.IsTransportDisconnectError(err)
}

func stripExecuteDynamicCodeControlResult(result []byte) []byte {
	var payload map[string]any
	if err := json.Unmarshal(result, &payload); err != nil {
		return result
	}

	delete(payload, dynamicCodeDomainReloadWaitRequiredField)
	delete(payload, legacyDynamicCodeDomainReloadWaitRequiredField)
	sanitized, err := json.Marshal(payload)
	if err != nil {
		return result
	}
	return sanitized
}

// reloadedBeforeAnswerError reports a snippet that Unity accepted and that lost its answer when the
// Editor reloaded its scripts or restarted its server, after which the Editor became ready again.
// Why not a success: uloop cannot tell whether the snippet ran to the end or was cut short by the
// reload, because Unity's answer for either case is lost with the connection.
// Why not safe to retry and no advice to retry: the snippet may already have changed the project,
// and a snippet that asks for the reload itself ends this way on every run.
func reloadedBeforeAnswerError(err error, projectRoot string) clierrors.CLIError {
	return clierrors.CLIError{
		ErrorCode: clierrors.ErrorCodeUnityDisconnectedAfterAccept,
		Phase:     clierrors.ErrorPhaseResponseWaiting,
		Message: "Unity reloaded its scripts or restarted its server before it answered, and is ready again. " +
			"The snippet was sent and may have run; its result is not available.",
		Retryable:   true,
		SafeToRetry: false,
		ProjectRoot: projectRoot,
		Command:     clicore.ExecuteDynamicCodeCommandName,
		NextActions: []string{
			"Before running the snippet again, check whether it took effect, for example with `uloop get-logs` or a snippet that reads the state it changed.",
			"A snippet that asks Unity to reload scripts, such as one calling `EditorUtility.RequestScriptReload`, ends this way on every run. " +
				"Do not run it again to get its result; if you need a value, return it from a separate snippet after the reload.",
		},
		Details: map[string]any{
			"Cause": err.Error(),
		},
	}
}
