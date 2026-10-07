package projectrunner

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"time"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
	"github.com/hatayama/unity-cli-loop/common/vibelog"
)

const (
	hotReloadBusyWaitDecidedOperation  = "cli_hot_reload_busy_wait_decided"
	hotReloadBusyWaitCompleteOperation = "cli_hot_reload_busy_wait_complete"
	hotReloadBusyUnknownToolName       = "another uloop command"
)

const (
	// %s is the running tool's name, %d the whole seconds this command waited.
	hotReloadBusyWaitNoteFormat         = "The Editor was busy running '%s' when this request arrived, so this command waited %ds for it to finish; every other field describes the answer that followed."
	hotReloadBusyWaitNotReadyNoteFormat = "The Editor was busy running '%s' when this request arrived; this command waited %ds, the Editor did not report ready in that time, and the request was sent again anyway; every other field describes the answer that followed."
	hotReloadBusyWaitingLineFormat      = "hot-reload: the Editor is busy running '%s'; waiting for it to finish, then applying..."
)

// hotReloadBusyWaitSendDeps sends without the bounded busy retry. Why: hot reload waits for the
// Editor on its status instead of resending every second, which would also bring the Editor to the
// front after the busy-stall threshold.
func hotReloadBusyWaitSendDeps() connectionRetryDeps {
	deps := defaultConnectionRetryDeps()
	deps.returnBusyWithoutRetry = true
	return deps
}

// hotReloadBusyWaitOutcome is what the busy wait leaves for the rest of the command.
type hotReloadBusyWaitOutcome struct {
	// result is the answer the command continues with.
	result toolExecutionResult
	// finished is true when stderr already carries the failure and exitCode is the command's.
	finished bool
	exitCode int
}

// sendHotReloadWaitingForBusyEditor sends the request once and, when another uloop command holds
// the Editor, waits for the Editor to be ready and sends the same request once more. It never
// writes stdout.
func sendHotReloadWaitingForBusyEditor(
	ctx context.Context,
	connection unityipc.Connection,
	params map[string]any,
	stderr io.Writer,
) hotReloadBusyWaitOutcome {
	deps := hotReloadBusyWaitSendDeps()
	first, outcome, err := sendPlainTool(ctx, connection, hotReloadCommandName, params, stderr, deps)
	if err == nil {
		return hotReloadBusyWaitOutcome{result: first}
	}
	if !isUnityServerBusyRPCError(err) {
		writeHotReloadToolFailure(stderr, connection, err, outcome)
		return hotReloadBusyWaitOutcome{finished: true, exitCode: 1}
	}

	runningToolName := hotReloadBusyRunningToolName(err)
	logHotReloadBusyWaitDecided(connection, first.correlationID, err)
	// A plain line for the same reason as the settle wait's: the spinner's Stop erases its own line.
	_, _ = fmt.Fprintf(stderr, hotReloadBusyWaitingLineFormat+"\n", runningToolName)
	wait, waitErr := waitForBusyEditor(ctx, connection, params, stderr, deps, hotReloadEditorReadyWaitDefaults)
	if waitErr != nil {
		writeHotReloadClassifiedError(stderr, connection, waitErr)
		logHotReloadBusyWaitComplete(connection, first.correlationID, wait, nil)
		return hotReloadBusyWaitOutcome{finished: true, exitCode: 1}
	}
	second, secondOutcome, sendErr := wait.answer, wait.outcome, wait.sendErr
	if !wait.answered {
		// Why send even when the budget ran out: the Editor's answer now is truer than busy data
		// from the start of the wait, and a second busy answer is reported without another wait.
		second, secondOutcome, sendErr = sendPlainTool(ctx, connection, hotReloadCommandName, params, stderr, deps)
	}
	logHotReloadBusyWaitComplete(connection, first.correlationID, wait, &second)
	if sendErr != nil {
		writeHotReloadToolFailure(stderr, connection, sendErr, secondOutcome)
		return hotReloadBusyWaitOutcome{finished: true, exitCode: 1}
	}
	merged, err := injectHotReloadEditorReadyNote(second.result, hotReloadBusyWaitNote(runningToolName, wait.waited, wait.ready))
	if err == nil && hasHotReloadTiming(merged) {
		// Why only then: Timing exists on apply answers alone; --status and --revert-all stay without it.
		merged, err = addHotReloadTimingMs(merged, hotReloadEditorReadyWaitMsField, wait.waited)
	}
	if err != nil {
		writeHotReloadClassifiedError(stderr, connection, err)
		return hotReloadBusyWaitOutcome{finished: true, exitCode: 1}
	}
	return hotReloadBusyWaitOutcome{result: toolExecutionResult{
		result:        merged,
		exitCode:      second.exitCode,
		correlationID: second.correlationID,
	}}
}

// hotReloadBusyWait is how the wait for a busy Editor ended.
type hotReloadBusyWait struct {
	waited time.Duration
	// ready is true when the Editor reported ready, or a resent request got in.
	ready bool
	// answered is true when a request resent during the wait got an answer other than busy, or
	// failed; answer, outcome and sendErr are then that request's.
	answered bool
	answer   toolExecutionResult
	outcome  unityipc.UnitySendOutcome
	sendErr  error
	resends  int
	// resendCorrelationIDs holds the correlation id of every resent request, in order.
	resendCorrelationIDs []string
}

// waitForBusyEditor polls the Editor status until it is ready, the budget runs out, or ctx is
// cancelled. While a cancelled execute-dynamic-code request holds the Editor, it also sends the
// request again every busyResendInterval: the Editor takes such a request's slot back only when a
// tool request arrives, never on a status answer, so status polling alone would wait the whole
// budget for a slot the first attempt already started to free.
func waitForBusyEditor(
	ctx context.Context,
	connection unityipc.Connection,
	params map[string]any,
	stderr io.Writer,
	deps connectionRetryDeps,
	options hotReloadEditorReadyWaitOptions,
) (hotReloadBusyWait, error) {
	startedAt := time.Now()
	deadline := startedAt.Add(options.budget)
	// The first attempt counts: its arrival started the Editor's grace period for the slot.
	lastResend := startedAt
	wait := hotReloadBusyWait{}
	for {
		status, answered := probeHotReloadEditorStatus(ctx, connection, options.probeTimeout)
		if answered && classifyEditorState(status) == statusStateReady {
			wait.waited, wait.ready = time.Since(startedAt), true
			return wait, nil
		}
		if answered && isHeldByExecuteDynamicCode(status) && time.Since(lastResend) >= options.busyResendInterval {
			answer, outcome, err := sendPlainTool(ctx, connection, hotReloadCommandName, params, stderr, deps)
			lastResend = time.Now()
			wait.resends++
			wait.resendCorrelationIDs = append(wait.resendCorrelationIDs, answer.correlationID)
			if !isUnityServerBusyRPCError(err) {
				wait.waited, wait.ready, wait.answered = time.Since(startedAt), err == nil, true
				wait.answer, wait.outcome, wait.sendErr = answer, outcome, err
				return wait, nil
			}
		}
		if !time.Now().Before(deadline) {
			wait.waited = time.Since(startedAt)
			return wait, nil
		}
		select {
		case <-ctx.Done():
			wait.waited = time.Since(startedAt)
			return wait, ctx.Err()
		case <-time.After(options.pollInterval):
		}
	}
}

// Why only execute-dynamic-code: it is the one tool whose cancelled request the Editor revokes on
// the next tool request; every other tool gives the slot back when it finishes, which the status
// shows.
func isHeldByExecuteDynamicCode(status editorStatusResponse) bool {
	return status.IsBusy && status.RunningToolName == clicore.ExecuteDynamicCodeCommandName
}

// hotReloadBusyRunningToolName names the command that held the Editor, from the busy answer's data.
func hotReloadBusyRunningToolName(err error) string {
	name, ok := serverBusyRunningToolName(err)
	if !ok {
		return hotReloadBusyUnknownToolName
	}
	return name
}

func hotReloadBusyWaitNote(runningToolName string, waited time.Duration, ready bool) string {
	if ready {
		return fmt.Sprintf(hotReloadBusyWaitNoteFormat, runningToolName, wholeSeconds(waited))
	}
	return fmt.Sprintf(hotReloadBusyWaitNotReadyNoteFormat, runningToolName, wholeSeconds(waited))
}

// hasHotReloadTiming is true when the answer carries a Timing object, which only apply answers do.
func hasHotReloadTiming(raw []byte) bool {
	fields := map[string]json.RawMessage{}
	if json.Unmarshal(raw, &fields) != nil {
		return false
	}
	existing := fields[hotReloadTimingField]
	return len(existing) > 0 && existing[0] == '{'
}

func writeHotReloadToolFailure(stderr io.Writer, connection unityipc.Connection, err error, outcome unityipc.UnitySendOutcome) {
	clierrors.WriteToolFailure(stderr, err, outcome, clierrors.ErrorContext{
		ProjectRoot: connection.ProjectRoot,
		Command:     hotReloadCommandName,
	})
}

// Written only when the first answer is busy: an answer that is not busy is already shown by
// cli_tool_response_received.
func logHotReloadBusyWaitDecided(connection unityipc.Connection, correlationID string, err error) {
	writePlainToolVibeLog(connection.ProjectRoot, func() vibelog.CLIVibeLogEntry {
		busy := struct {
			RunningToolName           string   `json:"runningToolName"`
			RunningToolPhase          string   `json:"runningToolPhase"`
			RunningToolElapsedSeconds *float64 `json:"runningToolElapsedSeconds"`
		}{}
		var rpcErr *unityipc.RPCError
		if errors.As(err, &rpcErr) {
			_ = json.Unmarshal(rpcErr.Data, &busy)
		}
		var elapsedSeconds any
		if busy.RunningToolElapsedSeconds != nil {
			elapsedSeconds = *busy.RunningToolElapsedSeconds
		}
		return vibelog.CLIVibeLogEntry{
			Level:     "INFO",
			Operation: hotReloadBusyWaitDecidedOperation,
			Message:   "Hot reload found the Editor busy and waits for the running command.",
			Context: map[string]any{
				"correlation_id":               correlationID,
				"running_tool_name":            busy.RunningToolName,
				"running_tool_phase":           busy.RunningToolPhase,
				"running_tool_elapsed_seconds": elapsedSeconds,
				"budget_ms":                    hotReloadEditorReadyWaitDefaults.budget.Milliseconds(),
				"resend_interval_ms":           hotReloadEditorReadyWaitDefaults.busyResendInterval.Milliseconds(),
			},
			CorrelationID: correlationID,
		}
	})
}

// Why never nil: a nil slice is written as null, and an empty wait should read as [].
func resendCorrelationIDsOrEmpty(wait hotReloadBusyWait) []string {
	if wait.resendCorrelationIDs == nil {
		return []string{}
	}
	return wait.resendCorrelationIDs
}

// Written once on every way out of a wait that started. second is nil when no request was sent
// after the wait.
func logHotReloadBusyWaitComplete(
	connection unityipc.Connection,
	correlationID string,
	wait hotReloadBusyWait,
	second *toolExecutionResult,
) {
	writePlainToolVibeLog(connection.ProjectRoot, func() vibelog.CLIVibeLogEntry {
		entryContext := map[string]any{
			"correlation_id":         correlationID,
			"second_correlation_id":  "",
			"waited_ms":              wait.waited.Milliseconds(),
			"ready":                  wait.ready,
			"resends":                wait.resends,
			"resend_correlation_ids": resendCorrelationIDsOrEmpty(wait),
			"second_result":          false,
		}
		if second != nil {
			entryContext["second_correlation_id"] = second.correlationID
			addHotReloadSecondApplyContext(entryContext, second.result)
		}
		level := "WARN"
		if wait.ready && entryContext["second_result"] == true {
			level = "INFO"
		}
		return vibelog.CLIVibeLogEntry{
			Level:         level,
			Operation:     hotReloadBusyWaitCompleteOperation,
			Message:       "Finished waiting for the busy Editor and sending hot reload again.",
			Context:       entryContext,
			CorrelationID: correlationID,
		}
	})
}
