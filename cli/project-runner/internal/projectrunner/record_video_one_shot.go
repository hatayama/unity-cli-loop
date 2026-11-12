package projectrunner

import (
	"context"
	"encoding/json"
	"errors"
	"io"
	"strings"
	"time"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	"github.com/hatayama/unity-cli-loop/common/tooldocs"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

const (
	recordVideoStopFailedErrorCode    = "RECORD_VIDEO_STOP_FAILED"
	recordVideoTriggerFailedErrorCode = "RECORD_VIDEO_TRIGGER_FAILED"
	recordVideoCancelledErrorCode     = "RECORD_VIDEO_CANCELLED"
	recordVideoOneShotSpinnerName     = "record-video one-shot"

	// The stop must outlive the caller's context so a cancelled one-shot never leaves the recording
	// running; this bounds how long that last request may take on its own.
	recordVideoOneShotStopTimeout = 10 * time.Second

	recordVideoFocusWindowNote = "focus-window failed; the Editor may throttle drawing while unfocused"
	recordVideoMarkWarning     = "The recording did not accept the mark (it may have stopped before the trigger returned, " +
		"or the Unity package predates --action mark); the burst sheet is anchored on the largest change instead."
	recordVideoPausingAwaitWarning = "The pause point paused Play Mode; frames after the hit are identical. " +
		"Use --mode trace when recording."
)

// Injection points so tests can drive the one-shot's request sequence without a Unity connection.
var (
	sendRecordVideoStart         = sendRecordVideoStartDefault
	sendRecordVideoRequest       = sendRecordVideoRequestDefault
	runFocusWindowForRecordVideo = clicore.RunFocusWindow
	waitForRecordVideo           = waitContextDuration
)

// errRecordVideoRequestRejected reports that a one-shot's own mark or stop request did not get past
// argument building; its envelope was discarded because the one-shot reports the outcome itself.
var errRecordVideoRequestRejected = errors.New("the record-video request was rejected before it reached Unity")

// recordVideoOneShotReport is added to the stop response as OneShot so the caller sees what the
// one-shot did around the recording.
type recordVideoOneShotReport struct {
	Seconds         int                      `json:"Seconds"`
	FocusWindowNote string                   `json:"FocusWindowNote,omitempty"`
	TriggerCommand  string                   `json:"TriggerCommand,omitempty"`
	TriggerResult   *pausePointTriggerResult `json:"TriggerResult,omitempty"`
	MarkAccepted    *bool                    `json:"MarkAccepted,omitempty"`
	Warning         string                   `json:"Warning,omitempty"`
}

// recordVideoOneShotOutcome is how the steps between start and stop ended.
type recordVideoOneShotOutcome struct {
	triggerFailed bool
	cancelled     bool
}

// runRecordVideoCommand runs record-video. Without --seconds or --trigger it is the plain tool call;
// with either it records one clip in a single command: start, the trigger, a mark, a wait, and stop.
func runRecordVideoCommand(
	ctx context.Context,
	connection unityipc.Connection,
	commandArgs []string,
	startPath string,
	stdout io.Writer,
	stderr io.Writer,
) int {
	extract, err := parseRecordVideoExtractOptions(commandArgs)
	if err != nil {
		clierrors.WriteClassifiedError(stderr, err, recordVideoErrorContext(connection))
		return 1
	}
	if extract.requested {
		return runRecordVideoExtract(ctx, connection, extract, startPath, stdout, stderr)
	}

	options, rest, err := parseRecordVideoOneShotOptions(commandArgs)
	if err != nil {
		clierrors.WriteClassifiedError(stderr, err, recordVideoErrorContext(connection))
		return 1
	}
	if !options.oneShot {
		return runDynamicProjectTool(ctx, connection, tooldocs.RecordVideoCommandName, commandArgs, startPath, stdout, stderr)
	}
	return runRecordVideoOneShot(ctx, connection, options, rest, startPath, stdout, stderr)
}

// runRecordVideoOneShot keeps one invariant on every exit after a successful start: the recording is
// stopped by this command, with a context the caller's cancellation cannot cut short.
func runRecordVideoOneShot(
	ctx context.Context,
	connection unityipc.Connection,
	options recordVideoOneShotOptions,
	rest []string,
	startPath string,
	stdout io.Writer,
	stderr io.Writer,
) int {
	report := recordVideoOneShotReport{Seconds: options.seconds}
	// An unfocused Editor throttles drawing, and the recording would then read as "no change".
	if runFocusWindowForRecordVideo(ctx, connection.ProjectRoot, io.Discard, io.Discard) != 0 {
		report.FocusWindowNote = recordVideoFocusWindowNote
	}

	startResult := sendRecordVideoStart(ctx, connection, recordVideoStartArgs(rest), startPath, stderr)
	if startResult.exitCode != 0 {
		// Nothing is recording, so neither the trigger nor a stop is sent.
		if len(startResult.result) > 0 {
			clicore.WriteJSON(stdout, startResult.result)
		}
		return startResult.exitCode
	}

	spinner := clicore.NewToolSpinner(stderr, recordVideoOneShotSpinnerName)
	outcome := runRecordVideoOneShotBetweenStartAndStop(ctx, connection, options, startPath, &report)
	stopCtx, cancel := context.WithTimeout(context.Background(), recordVideoOneShotStopTimeout)
	defer cancel()
	stopResult, stopErr := sendRecordVideoRequest(stopCtx, connection, recordVideoActionArgs("stop"), startPath)
	stopErr = recordingStopBlockedByDialog(stopErr)
	spinner.Stop()

	return writeRecordVideoOneShotResult(connection, report, outcome, stopResult, stopErr, stdout, stderr)
}

func runRecordVideoOneShotBetweenStartAndStop(
	ctx context.Context,
	connection unityipc.Connection,
	options recordVideoOneShotOptions,
	startPath string,
	report *recordVideoOneShotReport,
) recordVideoOneShotOutcome {
	triggerFailed := false
	if options.hasTrigger {
		triggerFailed = runRecordVideoOneShotTrigger(ctx, connection, options, startPath, report)
	}

	var waitErr error
	// A failed trigger means nothing happened worth waiting for; stop at once.
	if ctx.Err() == nil && !triggerFailed {
		waitErr = waitForRecordVideo(ctx, time.Duration(options.seconds)*time.Second)
	}
	return recordVideoOneShotOutcome{
		triggerFailed: triggerFailed,
		cancelled:     ctx.Err() != nil || waitErr != nil,
	}
}

// runRecordVideoOneShotTrigger runs the trigger and, when it succeeded, marks the recording: the
// moment the trigger returns is the moment worth seeing, and Unity cannot know it on its own.
// It reports whether the trigger failed; a cancellation is not a trigger failure.
func runRecordVideoOneShotTrigger(
	ctx context.Context,
	connection unityipc.Connection,
	options recordVideoOneShotOptions,
	startPath string,
	report *recordVideoOneShotReport,
) bool {
	commandString := pausePointTriggerCommandString(options.triggerCommand, options.triggerArgs)
	report.TriggerCommand = commandString
	result := runPausePointTriggerSync(ctx, connection, startPath, options.triggerCommand, options.triggerArgs, commandString)
	report.TriggerResult = result
	if ctx.Err() != nil {
		return false
	}
	if recordVideoTriggerFailed(result) {
		return true
	}

	// A mark that is not accepted only loses the anchor; the recording itself is still worth returning.
	markResult, markErr := sendRecordVideoRequest(ctx, connection, recordVideoActionArgs("mark"), startPath)
	accepted := markErr == nil && markResult.exitCode == 0
	report.MarkAccepted = &accepted
	report.Warning = recordVideoOneShotWarning(accepted, options.triggerCommand, result)
	return false
}

// recordVideoTriggerFailed treats every Success:false as a failure, unlike the pause-point wait
// (which only gives up on a pre-execution rejection): a recording shows the result of the action,
// so an action that reported failure leaves nothing to look at.
func recordVideoTriggerFailed(result *pausePointTriggerResult) bool {
	if pausePointTriggerRejectedBeforeExecution(result) {
		return true
	}
	if result == nil || !result.Completed || result.Error != "" {
		return true
	}
	response, ok := decodePausePointTriggerResponse(result)
	return ok && response.Success != nil && !*response.Success
}

func recordVideoOneShotWarning(markAccepted bool, triggerCommand string, result *pausePointTriggerResult) string {
	warnings := make([]string, 0, 2)
	if !markAccepted {
		warnings = append(warnings, recordVideoMarkWarning)
	}
	if triggerCommand == clicore.PausePointAwaitCommandName && recordVideoAwaitTriggerPaused(result) {
		warnings = append(warnings, recordVideoPausingAwaitWarning)
	}
	return strings.Join(warnings, " ")
}

// recordVideoAwaitTriggerPaused reports whether an await-pause-point trigger waited on a pause point
// whose mode pauses Play Mode. An unknown mode is not reported: guessing would be worse than silence.
func recordVideoAwaitTriggerPaused(result *pausePointTriggerResult) bool {
	if result == nil || len(result.Response) == 0 {
		return false
	}
	view := struct {
		Mode string `json:"Mode"`
	}{}
	if err := json.Unmarshal(result.Response, &view); err != nil {
		return false
	}
	return view.Mode != "" && !strings.EqualFold(view.Mode, pausePointTraceMode)
}

func writeRecordVideoOneShotResult(
	connection unityipc.Connection,
	report recordVideoOneShotReport,
	outcome recordVideoOneShotOutcome,
	stopResult toolExecutionResult,
	stopErr error,
	stdout io.Writer,
	stderr io.Writer,
) int {
	errorContext := recordVideoErrorContext(connection)
	if stopErr != nil {
		clierrors.WriteClassifiedError(stderr, stopErr, errorContext)
		return 1
	}
	payload, err := attachRecordVideoOneShotReport(stopResult.result, report)
	if err != nil {
		clierrors.WriteClassifiedError(stderr, err, errorContext)
		return 1
	}

	// Checked first: a stop that did not stop anything (for example another caller stopped the
	// recording during the wait) must not read as "recorded and stopped" under any other outcome.
	if stopResult.exitCode != 0 {
		clierrors.WriteErrorEnvelope(stderr, recordVideoOneShotError(connection, recordVideoStopFailedErrorCode,
			"The one-shot recording was not stopped by this command; see Recording.",
			"Read Recording for the stop response, then check record-video --action status.", payload))
		return 1
	}
	if outcome.cancelled {
		clierrors.WriteErrorEnvelope(stderr, recordVideoOneShotError(connection, recordVideoCancelledErrorCode,
			"The one-shot was cancelled; the recording was stopped and is attached.",
			"Re-run the one-shot if the recording does not cover the moment you need.", payload))
		return 1
	}
	if outcome.triggerFailed {
		clierrors.WriteErrorEnvelope(stderr, recordVideoOneShotError(connection, recordVideoTriggerFailedErrorCode,
			"The trigger command failed; the recording was stopped and is attached.",
			"Read Recording.OneShot.TriggerResult for the reason, fix the trigger, and re-run.", payload))
		return 1
	}
	clicore.WriteJSON(stdout, payload)
	return 0
}

func attachRecordVideoOneShotReport(stopResponse json.RawMessage, report recordVideoOneShotReport) (json.RawMessage, error) {
	fields := map[string]json.RawMessage{}
	if err := json.Unmarshal(stopResponse, &fields); err != nil {
		return nil, err
	}
	if fields == nil {
		return nil, errors.New("record-video stop response must be a JSON object")
	}
	encoded, err := json.Marshal(report)
	if err != nil {
		return nil, err
	}
	fields["OneShot"] = encoded
	return json.Marshal(fields)
}

func recordVideoOneShotError(
	connection unityipc.Connection,
	code string,
	message string,
	nextAction string,
	payload json.RawMessage,
) clierrors.CLIError {
	return clierrors.CLIError{
		ErrorCode:   code,
		Phase:       clierrors.ErrorPhaseExecution,
		Message:     message,
		ProjectRoot: connection.ProjectRoot,
		Command:     tooldocs.RecordVideoCommandName,
		NextActions: []string{nextAction},
		Details:     map[string]any{"Recording": payload},
	}
}

func recordVideoErrorContext(connection unityipc.Connection) clierrors.ErrorContext {
	return clierrors.ErrorContext{ProjectRoot: connection.ProjectRoot, Command: tooldocs.RecordVideoCommandName}
}

// recordVideoStartArgs adds --action start unless the caller already passed it; parsing already
// rejected any other action for a one-shot.
func recordVideoStartArgs(rest []string) []string {
	action, _, err := findRecordVideoActionAndMaxDuration(rest)
	if err == nil && action != "" {
		return rest
	}
	return append(append([]string{}, rest...), recordVideoActionArgs(recordVideoStartAction)...)
}

func recordVideoActionArgs(action string) []string {
	return []string{"--" + recordVideoActionFlagName, action}
}

// sendRecordVideoStartDefault sends start as a plain record-video call does and writes failures to
// stderr the same way. The response is returned unwritten: on success the one-shot prints the stop
// response instead, and on failure the caller prints it exactly as the plain call would.
func sendRecordVideoStartDefault(
	ctx context.Context,
	connection unityipc.Connection,
	commandArgs []string,
	startPath string,
	stderr io.Writer,
) toolExecutionResult {
	params, ok := resolveDynamicToolParams(connection, tooldocs.RecordVideoCommandName, commandArgs, startPath, stderr)
	if !ok {
		return toolExecutionResult{exitCode: 1}
	}
	return runPlainTool(ctx, connection, tooldocs.RecordVideoCommandName, params, stderr)
}

// sendRecordVideoRequestDefault sends mark or stop and writes nothing: the one-shot decides what the
// caller sees. An older catalog that does not know mark is rejected here, before Unity.
func sendRecordVideoRequestDefault(
	ctx context.Context,
	connection unityipc.Connection,
	commandArgs []string,
	startPath string,
) (toolExecutionResult, error) {
	params, ok := resolveDynamicToolParams(connection, tooldocs.RecordVideoCommandName, commandArgs, startPath, io.Discard)
	if !ok {
		return toolExecutionResult{exitCode: 1}, errRecordVideoRequestRejected
	}
	result, _, err := sendPlainTool(ctx, connection, tooldocs.RecordVideoCommandName, params, io.Discard, defaultConnectionRetryDeps())
	return result, err
}
