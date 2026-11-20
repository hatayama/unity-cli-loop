package projectrunner

import (
	"context"
	"encoding/json"
	"errors"
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
	waitForToolReadiness func(ctx context.Context, projectRoot string, findDialogs clicore.DialogFinder) error
	dialogWatch          dialogWatchDeps
}

func defaultDynamicCodeWaitDeps() dynamicCodeWaitDeps {
	return dynamicCodeWaitDeps{
		waitForToolReadiness: clicore.WaitForToolReadiness,
		dialogWatch:          defaultDialogWatchDeps(),
	}
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
			if waitErr := deps.waitForToolReadiness(ctx, connection.ProjectRoot, reloadWaitDialogFinder(connection, deps.dialogWatch)); waitErr != nil {
				waitErr = explainReadinessFailureByDialog(ctx, connection, waitErr, deps.dialogWatch)
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
		if err := deps.waitForToolReadiness(ctx, connection.ProjectRoot, reloadWaitDialogFinder(connection, deps.dialogWatch)); err != nil {
			err = explainReadinessFailureByDialog(ctx, connection, err, deps.dialogWatch)
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

// reloadWaitDialogFinder is the look the reload wait makes while it waits: the one uloop status makes,
// which reads the Editor's own report where the Editor gives one and Unity's windows otherwise.
func reloadWaitDialogFinder(connection unityipc.Connection, deps dialogWatchDeps) clicore.DialogFinder {
	return func(ctx context.Context) ([]statusDialog, error) {
		return findBlockingDialogs(ctx, connection, deps), nil
	}
}

// explainReadinessFailureByDialog turns a failed wait for the Editor to be ready again after
// execute-dynamic-code into BLOCKED_BY_DIALOG when a dialog holds the main thread, and returns err
// unchanged otherwise. The wait stops by itself at a dialog its looks find; the look here after a
// wait that ran out of time catches a dialog that opened after its last look.
func explainReadinessFailureByDialog(
	ctx context.Context,
	connection unityipc.Connection,
	err error,
	deps dialogWatchDeps,
) error {
	if err == nil {
		return nil
	}
	var blockedWait clicore.ToolReadinessBlockedByDialogError
	if errors.As(err, &blockedWait) {
		// Why a server that does not answer: the wait stopped before its timeout, and with Unity held
		// by a dialog, the timeout would have ended it as one.
		notResponding := clierrors.UnityServerNotRespondingError{
			ProjectRoot: connection.ProjectRoot,
			Endpoint:    connection.Endpoint.Address,
			Cause:       blockedWait.Cause,
		}
		return neutralReloadWaitBlockedByDialog(connection, notResponding, blockedWait.Dialogs, deps)
	}
	dialogs := findBlockingDialogs(ctx, connection, deps)
	if len(dialogs) == 0 {
		return err
	}
	return neutralReloadWaitBlockedByDialog(connection, err, dialogs, deps)
}

// neutralReloadWaitBlockedByDialog replaces the reload wait's failure with the dialogs.
// Why the neutral form: the snippet may have returned, so only the reload it started is held.
// Not safe to retry: the snippet ran, and running it again repeats its effect.
func neutralReloadWaitBlockedByDialog(
	connection unityipc.Connection,
	replaced error,
	dialogs []statusDialog,
	deps dialogWatchDeps,
) error {
	original := clierrors.ClassifyError(replaced, clierrors.ErrorContext{ProjectRoot: connection.ProjectRoot, Command: clicore.ExecuteDynamicCodeCommandName})
	original.SafeToRetry = false
	return neutralBlockedByDialog(clicore.ExecuteDynamicCodeCommandName, dialogs, deps.canPressDialogs, original)
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
