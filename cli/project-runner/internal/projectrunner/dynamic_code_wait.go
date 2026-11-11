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

func runExecuteDynamicCodeWithDomainReloadWait(ctx context.Context, connection unityipc.Connection, params map[string]any, stdout io.Writer, stderr io.Writer) int {
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
			if waitErr := clicore.WaitForToolReadiness(ctx, connection.ProjectRoot); waitErr != nil {
				spinner.Stop()
				waitErr = explainReadinessFailureByDialog(ctx, connection, waitErr, defaultDialogWatchDeps())
				clierrors.WriteClassifiedError(stderr, waitErr, clierrors.ErrorContext{
					ProjectRoot: connection.ProjectRoot,
					Command:     clicore.ExecuteDynamicCodeCommandName,
				})
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
		if err := clicore.WaitForToolReadiness(ctx, connection.ProjectRoot); err != nil {
			spinner.Stop()
			err = explainReadinessFailureByDialog(ctx, connection, err, defaultDialogWatchDeps())
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

// explainReadinessFailureByDialog turns a failed wait for the Editor to be ready again after
// execute-dynamic-code into BLOCKED_BY_DIALOG when a dialog holds the main thread, and returns err
// unchanged otherwise. Why one look at the end and not during the wait: the wait lives in the shared
// CLI code, which this lookup must not reach into, and the guidance is what matters to the caller.
func explainReadinessFailureByDialog(
	ctx context.Context,
	connection unityipc.Connection,
	err error,
	deps dialogWatchDeps,
) error {
	if err == nil {
		return nil
	}
	dialogs := findBlockingDialogs(ctx, connection, deps)
	if len(dialogs) == 0 {
		return err
	}
	// Not safe to retry: the snippet already ran, and running it again repeats its effect.
	return startedCommandBlockedByDialog(clicore.ExecuteDynamicCodeCommandName, dialogs, deps.canPressDialogs, false)
}
