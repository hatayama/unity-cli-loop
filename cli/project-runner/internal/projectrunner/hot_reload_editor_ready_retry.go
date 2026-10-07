package projectrunner

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"time"

	"github.com/hatayama/unity-cli-loop/common/clicontract"
	"github.com/hatayama/unity-cli-loop/common/clicore"
	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
	"github.com/hatayama/unity-cli-loop/common/vibelog"
)

const (
	hotReloadRetryAfterEditorReadyField = "RetryAfterEditorReady"
	hotReloadSelectedFilesField         = "SelectedFiles"
	hotReloadFilesParam                 = "Files"
	hotReloadEditorReadyRetryNoteField  = "EditorReadyRetryNote"
	hotReloadEditorReadyWaitMsField     = "EditorReadyWaitMs"
)

const (
	hotReloadEditorReadyRetryDecidedOperation  = "cli_hot_reload_editor_ready_retry_decided"
	hotReloadEditorReadyRetryCompleteOperation = "cli_hot_reload_editor_ready_retry_complete"
)

const (
	// %d is the whole seconds this command waited for the Editor.
	hotReloadEditorReadyRetryNoteFormat  = "The first apply was refused because the Editor was compiling or importing, so this command waited %ds for the Editor to settle and applied the same request again; every other field describes the second apply."
	hotReloadEditorReadyGaveUpNoteFormat = "The apply was refused because the Editor was compiling or importing; this command waited %ds for the Editor to settle, but it did not, so no second apply ran."
	hotReloadEditorReadyWaitingLine      = "hot-reload: the Editor is compiling or importing, so the reload was refused; waiting for it to settle, then applying again..."
)

type hotReloadEditorReadyWaitOptions struct {
	pollInterval time.Duration
	budget       time.Duration
	probeTimeout time.Duration
	// busyResendInterval is how often the busy wait sends the request again while a cancelled
	// execute-dynamic-code request holds the Editor.
	busyResendInterval time.Duration
}

// Why a package variable: the fallback tests already swap hotReloadFallbackCompile, so the tests
// of this file are not parallel either.
var hotReloadEditorReadyWaitDefaults = hotReloadEditorReadyWaitOptions{
	pollInterval: time.Second,
	budget:       compileWaitTimeout,
	probeTimeout: 5 * time.Second,
	// The Editor's grace period before it takes back a cancelled request's slot.
	busyResendInterval: 5 * time.Second,
}

// hotReloadEditorReadyRetryOutcome is what the retry leaves for the fallback decision.
type hotReloadEditorReadyRetryOutcome struct {
	// result is the response the fallback decision continues with.
	result toolExecutionResult
	// finished is true when stdout was already written and exitCode is the command's.
	finished bool
	exitCode int
}

// retryHotReloadAfterEditorReady waits for the Editor to settle and applies the same files once
// more when the first response says it was refused only because the Editor was compiling or
// importing. Any other response passes through unchanged.
func retryHotReloadAfterEditorReady(
	ctx context.Context,
	connection unityipc.Connection,
	params map[string]any,
	stdout io.Writer,
	stderr io.Writer,
	first toolExecutionResult,
) hotReloadEditorReadyRetryOutcome {
	// Why the first request's ID: the reader joins these entries to the request's
	// cli_tool_request_sent by it.
	correlationID := first.correlationID
	requested, parseError := isHotReloadRetryAfterEditorReadyRequested(first.result)
	logHotReloadEditorReadyRetryDecided(connection, correlationID, requested, parseError)
	if !requested {
		return hotReloadEditorReadyRetryOutcome{result: first}
	}

	// A plain line rather than the tool spinner: the spinner is off on a non-TTY stderr, and on a
	// TTY its Stop erases its own line, so the reader would not see why the command paused.
	_, _ = fmt.Fprintln(stderr, hotReloadEditorReadyWaitingLine)
	waited, ready, err := waitForEditorReady(ctx, connection, hotReloadEditorReadyWaitDefaults)
	if err != nil {
		clicore.WriteJSON(stdout, first.result)
		writeHotReloadClassifiedError(stderr, connection, err)
		logHotReloadEditorReadyRetryComplete(connection, correlationID, waited, false, nil)
		return hotReloadEditorReadyRetryOutcome{finished: true, exitCode: 1}
	}
	if !ready {
		return finishHotReloadEditorNeverReady(connection, stdout, stderr, first, waited)
	}
	return applyHotReloadAgain(ctx, connection, params, stdout, stderr, first, waited)
}

// The first response stays the answer, with a note saying the Editor did not settle; no fallback
// compile runs, because the Editor that is still compiling would refuse it too.
func finishHotReloadEditorNeverReady(
	connection unityipc.Connection,
	stdout io.Writer,
	stderr io.Writer,
	first toolExecutionResult,
	waited time.Duration,
) hotReloadEditorReadyRetryOutcome {
	logHotReloadEditorReadyRetryComplete(connection, first.correlationID, waited, false, nil)
	merged, err := injectHotReloadEditorReadyNote(
		first.result,
		composeHotReloadEditorReadyNote(first.result, fmt.Sprintf(hotReloadEditorReadyGaveUpNoteFormat, wholeSeconds(waited))))
	if err != nil {
		writeHotReloadClassifiedError(stderr, connection, err)
		return hotReloadEditorReadyRetryOutcome{finished: true, exitCode: 1}
	}
	clicore.WriteJSON(stdout, merged)
	return hotReloadEditorReadyRetryOutcome{finished: true, exitCode: first.exitCode}
}

func applyHotReloadAgain(
	ctx context.Context,
	connection unityipc.Connection,
	params map[string]any,
	stdout io.Writer,
	stderr io.Writer,
	first toolExecutionResult,
	waited time.Duration,
) hotReloadEditorReadyRetryOutcome {
	// Why the selected files and not the same params: with no files given, the Editor selects the
	// changed files anew, and after Unity's own compile took the edit in that selection is empty
	// and fails validation; the files the first run named go through the explicit path and report
	// NothingToApply instead.
	retryParams := hotReloadRetryParams(params, hotReloadSelectedFiles(first.result))
	second := runPlainTool(ctx, connection, hotReloadCommandName, retryParams, stderr)
	logHotReloadEditorReadyRetryComplete(connection, first.correlationID, waited, true, &second)
	if len(second.result) == 0 {
		// The transport failure is already classified on stderr; the first apply's answer is the
		// last one the Editor gave.
		clicore.WriteJSON(stdout, first.result)
		return hotReloadEditorReadyRetryOutcome{finished: true, exitCode: second.exitCode}
	}

	merged, err := injectHotReloadEditorReadyNote(
		second.result,
		composeHotReloadEditorReadyNote(first.result, fmt.Sprintf(hotReloadEditorReadyRetryNoteFormat, wholeSeconds(waited))))
	if err == nil {
		merged, err = addHotReloadEditorReadyWaitMs(merged, first.result, waited)
	}
	if err != nil {
		writeHotReloadClassifiedError(stderr, connection, err)
		return hotReloadEditorReadyRetryOutcome{finished: true, exitCode: 1}
	}
	return hotReloadEditorReadyRetryOutcome{result: toolExecutionResult{
		result:        merged,
		exitCode:      second.exitCode,
		correlationID: second.correlationID,
	}}
}

// Only an explicit true asks for the retry: a package that does not report the field never gets a
// second apply. parseError is true when the response is not a JSON object.
func isHotReloadRetryAfterEditorReadyRequested(raw []byte) (bool, bool) {
	var fields map[string]json.RawMessage
	// Why a nil map is a parse error too: JSON null decodes without an error and leaves it nil.
	if err := json.Unmarshal(raw, &fields); err != nil || fields == nil {
		return false, true
	}
	return string(fields[hotReloadRetryAfterEditorReadyField]) == "true", false
}

// A missing or unreadable list counts as none, which leaves the request as it was sent.
func hotReloadSelectedFiles(raw []byte) []string {
	answer := struct {
		SelectedFiles []string `json:"SelectedFiles"`
	}{}
	if err := json.Unmarshal(raw, &answer); err != nil {
		return nil
	}
	return answer.SelectedFiles
}

// hotReloadRetryParams copies the first request and, when the first run named its files, sends
// exactly those. The caller's map is left as it was.
func hotReloadRetryParams(params map[string]any, selectedFiles []string) map[string]any {
	retryParams := make(map[string]any, len(params)+1)
	for key, value := range params {
		retryParams[key] = value
	}
	if len(selectedFiles) > 0 {
		retryParams[hotReloadFilesParam] = selectedFiles
	}
	return retryParams
}

// waitForEditorReady polls the Editor status until the Editor answers and is ready, the budget
// runs out, or ctx is cancelled.
// Why every probe error counts as "not yet": the domain reload that follows Unity's compile takes
// the server down and brings it back.
func waitForEditorReady(
	ctx context.Context,
	connection unityipc.Connection,
	options hotReloadEditorReadyWaitOptions,
) (time.Duration, bool, error) {
	startedAt := time.Now()
	deadline := startedAt.Add(options.budget)
	for {
		if probeEditorReady(ctx, connection, options.probeTimeout) {
			return time.Since(startedAt), true, nil
		}
		if !time.Now().Before(deadline) {
			return time.Since(startedAt), false, nil
		}
		select {
		case <-ctx.Done():
			return time.Since(startedAt), false, ctx.Err()
		case <-time.After(options.pollInterval):
		}
	}
}

func probeEditorReady(ctx context.Context, connection unityipc.Connection, probeTimeout time.Duration) bool {
	status, answered := probeHotReloadEditorStatus(ctx, connection, probeTimeout)
	return answered && classifyEditorState(status) == statusStateReady
}

// probeHotReloadEditorStatus asks the Editor for its status once; answered is false when it did not answer
// or the answer was not readable.
func probeHotReloadEditorStatus(
	ctx context.Context,
	connection unityipc.Connection,
	probeTimeout time.Duration,
) (editorStatusResponse, bool) {
	probeCtx, cancel := context.WithTimeout(ctx, probeTimeout)
	defer cancel()
	raw, err := unityipc.NewClient(connection, clicontract.ProjectRunnerVersion()).Send(
		probeCtx,
		editorStatusBridgeCommandName,
		map[string]any{})
	if err != nil {
		return editorStatusResponse{}, false
	}
	var response editorStatusResponse
	if err := json.Unmarshal(raw, &response); err != nil {
		return editorStatusResponse{}, false
	}
	return response, true
}

// composeHotReloadEditorReadyNote appends sentence to the note an earlier wait in this command left
// on prior, so the note tells every wait in order.
func composeHotReloadEditorReadyNote(prior []byte, sentence string) string {
	answer := struct {
		EditorReadyRetryNote string `json:"EditorReadyRetryNote"`
	}{}
	if json.Unmarshal(prior, &answer) != nil || answer.EditorReadyRetryNote == "" {
		return sentence
	}
	return answer.EditorReadyRetryNote + " " + sentence
}

// addHotReloadEditorReadyWaitMs sets EditorReadyWaitMs on raw to the wait an earlier wait left on
// prior plus waited, so the field covers every wait in this command.
func addHotReloadEditorReadyWaitMs(raw []byte, prior []byte, waited time.Duration) ([]byte, error) {
	answer := struct {
		Timing json.RawMessage `json:"Timing"`
	}{}
	earlier := struct {
		EditorReadyWaitMs int64 `json:"EditorReadyWaitMs"`
	}{}
	// Why errors are ignored: a Timing that is not an object, or a value that is not a number,
	// holds no earlier wait, so it counts as zero.
	if json.Unmarshal(prior, &answer) == nil && len(answer.Timing) > 0 && answer.Timing[0] == '{' {
		_ = json.Unmarshal(answer.Timing, &earlier)
	}
	return addHotReloadTimingMs(raw, hotReloadEditorReadyWaitMsField, waited+time.Duration(earlier.EditorReadyWaitMs)*time.Millisecond)
}

func injectHotReloadEditorReadyNote(raw []byte, note string) ([]byte, error) {
	fields := map[string]json.RawMessage{}
	if err := json.Unmarshal(raw, &fields); err != nil {
		return nil, err
	}
	if fields == nil {
		return nil, errors.New("hot-reload response must be a JSON object")
	}
	encoded, err := json.Marshal(note)
	if err != nil {
		return nil, err
	}
	fields[hotReloadEditorReadyRetryNoteField] = encoded
	return json.Marshal(fields)
}

func wholeSeconds(duration time.Duration) int64 {
	return int64(duration / time.Second)
}

func writeHotReloadClassifiedError(stderr io.Writer, connection unityipc.Connection, err error) {
	clierrors.WriteClassifiedError(stderr, err, clierrors.ErrorContext{
		ProjectRoot: connection.ProjectRoot,
		Command:     hotReloadCommandName,
	})
}

// Written whether or not the retry was requested, so the log shows a retry that did not run as
// well as one that did.
func logHotReloadEditorReadyRetryDecided(
	connection unityipc.Connection,
	correlationID string,
	requested bool,
	parseError bool,
) {
	writePlainToolVibeLog(connection.ProjectRoot, func() vibelog.CLIVibeLogEntry {
		return vibelog.CLIVibeLogEntry{
			Level:     "INFO",
			Operation: hotReloadEditorReadyRetryDecidedOperation,
			Message:   "Decided whether hot reload waits for the Editor and applies again.",
			Context: map[string]any{
				"correlation_id": correlationID,
				"requested":      requested,
				"parse_error":    parseError,
			},
			CorrelationID: correlationID,
		}
	})
}

// Written once on every way out of a retry that was requested. second is nil when no second
// apply was sent.
func logHotReloadEditorReadyRetryComplete(
	connection unityipc.Connection,
	correlationID string,
	waited time.Duration,
	ready bool,
	second *toolExecutionResult,
) {
	writePlainToolVibeLog(connection.ProjectRoot, func() vibelog.CLIVibeLogEntry {
		entryContext := map[string]any{
			"correlation_id":        correlationID,
			"second_correlation_id": "",
			"waited_ms":             waited.Milliseconds(),
			"ready":                 ready,
			"second_result":         false,
		}
		level := "WARN"
		if second != nil {
			entryContext["second_correlation_id"] = second.correlationID
			addHotReloadSecondApplyContext(entryContext, second.result)
		}
		if ready && entryContext["second_result"] == true {
			level = "INFO"
		}
		return vibelog.CLIVibeLogEntry{
			Level:         level,
			Operation:     hotReloadEditorReadyRetryCompleteOperation,
			Message:       "Finished waiting for the Editor and applying hot reload again.",
			Context:       entryContext,
			CorrelationID: correlationID,
		}
	})
}

// Reads only Success and Outcome: the rest of the response is text about the project's code,
// which the log never carries.
func addHotReloadSecondApplyContext(entryContext map[string]any, raw []byte) {
	if len(raw) == 0 {
		return
	}
	entryContext["second_result"] = true
	var fields map[string]json.RawMessage
	if err := json.Unmarshal(raw, &fields); err != nil || fields == nil {
		return
	}
	switch string(fields[hotReloadSuccessField]) {
	case "true":
		entryContext["second_success"] = true
	case "false":
		entryContext["second_success"] = false
	}
	outcome, _, _ := readHotReloadStringField(fields, hotReloadOutcomeField)
	entryContext["second_outcome"] = outcome
}
