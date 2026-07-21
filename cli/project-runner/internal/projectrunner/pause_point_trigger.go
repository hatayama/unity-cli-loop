package projectrunner

import (
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	"io"
	"strings"
	"time"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	"github.com/hatayama/unity-cli-loop/common/tooldocs"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

// pausePointTriggerResult carries the outcome of the --trigger command dispatched from inside
// await-pause-point/enable-pause-point --await, so a caller sees what the trigger did without a
// second CLI round trip.
type pausePointTriggerResult struct {
	// Command echoes the --trigger string as given.
	Command string `json:"Command"`

	// Completed is false when the pause-point wait settled (hit/timeout/cleared/etc.) before the
	// trigger command's own goroutine returned within the short grace window this CLI waits for
	// it. The triggered command keeps running inside Unity even after this CLI process exits —
	// only this process's own wait stops. In practice simulate-* commands return immediately once
	// paused (see PR-1), so this should only surface for a trigger unrelated to the pause itself.
	Completed bool `json:"Completed"`

	// Response is the triggered command's raw, unmodified JSON response.
	Response json.RawMessage `json:"Response,omitempty"`

	// Error is set only when the triggered command's own dispatch failed outright (unparseable
	// output, non-zero exit with no JSON) — not when the triggered command completed with its own
	// Success:false, which is passed through in Response untouched.
	Error string `json:"Error,omitempty"`
}

// parsePausePointTriggerCommand splits a --trigger value into a command name and its arguments,
// and rejects shapes that cannot behave sensibly when dispatched from inside this CLI process.
func parsePausePointTriggerCommand(command string, value string) (string, []string, error) {
	tokens, err := tokenizePausePointTriggerValue(value)
	if err != nil {
		return "", nil, &clierrors.ArgumentError{
			Message: err.Error(),
			Option:  "--" + PausePointTriggerFlagName,
			Command: command,
			NextActions: []string{
				"Quote the trigger command consistently, e.g. --trigger \"simulate-keyboard --action Press --key Space --duration 5\".",
			},
		}
	}
	if len(tokens) == 0 {
		return "", nil, clierrors.MissingValueArgumentError("--" + PausePointTriggerFlagName)
	}

	triggerCommand := tokens[0]
	triggerArgs := tokens[1:]

	// A pause-point wait cannot make progress from inside another pause-point wait: there is no
	// legitimate use case, only a wasted goroutine that outlives its parent's own timeout.
	if triggerCommand == clicore.PausePointAwaitCommandName || triggerCommand == pausePointEnableCommandName {
		return "", nil, &clierrors.ArgumentError{
			Message: fmt.Sprintf(
				"--trigger cannot target %q: waiting for a pause point from inside another pause-point wait cannot make progress.",
				triggerCommand),
			Option:  "--" + PausePointTriggerFlagName,
			Command: command,
			NextActions: []string{
				"Pass a command that performs an action (for example simulate-keyboard), not another pause-point wait.",
			},
		}
	}

	for _, arg := range triggerArgs {
		if isPausePointFlag(arg, tooldocs.ProjectPathFlagName) {
			return "", nil, &clierrors.ArgumentError{
				Message: "--trigger cannot include --project-path: the triggered command always runs " +
					"against the same project as the parent command.",
				Option:  "--" + PausePointTriggerFlagName,
				Command: command,
				NextActions: []string{
					"Remove --project-path from the trigger command string.",
				},
			}
		}
	}

	return triggerCommand, triggerArgs, nil
}

// tokenizePausePointTriggerValue splits a --trigger value into argv-style tokens, honoring single
// and double quotes so an argument value (for example a key name) can contain a space. This is
// intentionally not a full shell parser (no escape sequences, no nested quoting) — the trigger
// strings this flag targets are plain `uloop` subcommand invocations, not shell pipelines.
func tokenizePausePointTriggerValue(raw string) ([]string, error) {
	var tokens []string
	var current strings.Builder
	hasCurrent := false
	inQuote := false
	var quoteChar rune

	flush := func() {
		if hasCurrent {
			tokens = append(tokens, current.String())
			current.Reset()
			hasCurrent = false
		}
	}

	for _, r := range raw {
		switch {
		case inQuote:
			if r == quoteChar {
				inQuote = false
				continue
			}
			current.WriteRune(r)
		case r == '\'' || r == '"':
			inQuote = true
			quoteChar = r
			hasCurrent = true
		case r == ' ' || r == '\t':
			flush()
		default:
			current.WriteRune(r)
			hasCurrent = true
		}
	}
	if inQuote {
		return nil, fmt.Errorf("unterminated %q quote in --trigger value", string(quoteChar))
	}
	flush()

	return tokens, nil
}

// dispatchPausePointTriggerCommand delegates to runResolvedProjectCommand by default. Injectable
// so tests can simulate a trigger command's dispatch outcome without a real Unity connection.
// runResolvedProjectCommand's own call graph loops back through waitForPausePoint into this very
// variable, so it is assigned in init() (a statement, not a variable initializer) rather than as
// this var's own initializer expression — Go's package-initialization dependency analysis reports
// a cycle for the latter even though the actual call only happens later, at runtime.
var dispatchPausePointTriggerCommand func(
	ctx context.Context,
	connection unityipc.Connection,
	command string,
	commandArgs []string,
	startPath string,
	stdout io.Writer,
	stderr io.Writer,
) int

func init() {
	dispatchPausePointTriggerCommand = runResolvedProjectCommand
}

// pausePointTriggerHandle lets the pause-point wait loop start the trigger command concurrently
// (right after the marker is confirmed armed) and join it once the wait itself has settled.
type pausePointTriggerHandle struct {
	command string
	done    chan *pausePointTriggerResult
}

// startPausePointTrigger dispatches the trigger command in-process, through the same command
// dispatch entry point every ordinary CLI invocation uses (runResolvedProjectCommand) — not a
// subprocess. It races against the pause-point wait loop that started it.
func startPausePointTrigger(
	ctx context.Context,
	connection unityipc.Connection,
	startPath string,
	triggerCommand string,
	triggerArgs []string,
) *pausePointTriggerHandle {
	handle := &pausePointTriggerHandle{
		command: pausePointTriggerCommandString(triggerCommand, triggerArgs),
		done:    make(chan *pausePointTriggerResult, 1),
	}

	go func() {
		handle.done <- runPausePointTriggerSync(ctx, connection, startPath, triggerCommand, triggerArgs, handle.command)
	}()

	return handle
}

// join waits briefly for the trigger goroutine started by startPausePointTrigger, once the
// pause-point wait itself has already settled. The grace window mirrors
// pausePointFinalStatusProbeTimeout: a genuine hit interrupts simulate-* commands immediately
// (see PR-1), so a trigger that is still running after this window is treated as still in flight
// rather than blocking the whole await call further.
func (h *pausePointTriggerHandle) join() *pausePointTriggerResult {
	if h == nil {
		return nil
	}

	select {
	case result := <-h.done:
		return result
	case <-time.After(pausePointFinalStatusProbeTimeout):
		return &pausePointTriggerResult{Command: h.command, Completed: false}
	}
}

func runPausePointTriggerSync(
	ctx context.Context,
	connection unityipc.Connection,
	startPath string,
	triggerCommand string,
	triggerArgs []string,
	commandString string,
) *pausePointTriggerResult {
	var stdout bytes.Buffer
	var stderr bytes.Buffer
	exitCode := dispatchPausePointTriggerCommand(ctx, connection, triggerCommand, triggerArgs, startPath, &stdout, &stderr)

	result := &pausePointTriggerResult{Command: commandString, Completed: true}

	trimmed := bytes.TrimSpace(stdout.Bytes())
	if json.Valid(trimmed) {
		result.Response = json.RawMessage(trimmed)
		return result
	}

	result.Error = strings.TrimSpace(stderr.String())
	if result.Error == "" {
		result.Error = fmt.Sprintf(
			"trigger command %q exited with code %d and produced no parseable output", commandString, exitCode)
	}
	return result
}

func pausePointTriggerCommandString(triggerCommand string, triggerArgs []string) string {
	if len(triggerArgs) == 0 {
		return triggerCommand
	}
	return triggerCommand + " " + strings.Join(triggerArgs, " ")
}
