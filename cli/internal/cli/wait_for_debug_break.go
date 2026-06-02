package cli

import (
	"context"
	"encoding/json"
	"io"
	"time"

	"github.com/hatayama/unity-cli-loop/cli/internal/unityipc"
)

const (
	waitForDebugBreakCommandName        = "wait-for-debug-break"
	playModeStateCommandName            = "get-play-mode-state"
	waitForDebugBreakStatusProbeTimeout = 5 * time.Second
)

var (
	waitForDebugBreakPollInterval = 50 * time.Millisecond
	queryPlayModeState            = queryPlayModeStateFromUnity
)

type playModeStateResponse struct {
	IsPlaying bool   `json:"IsPlaying"`
	IsPaused  bool   `json:"IsPaused"`
	Message   string `json:"Message"`
}

type waitForDebugBreakResponse struct {
	Success             bool   `json:"Success"`
	IsPlaying           bool   `json:"IsPlaying"`
	IsPaused            bool   `json:"IsPaused"`
	ElapsedMilliseconds int64  `json:"ElapsedMilliseconds"`
	Message             string `json:"Message"`
}

func runWaitForDebugBreak(
	ctx context.Context,
	connection unityipc.Connection,
	args []string,
	stdout io.Writer,
	stderr io.Writer,
) int {
	if err := parseWaitForDebugBreakArgs(args); err != nil {
		writeClassifiedError(stderr, err, errorContext{
			projectRoot: connection.ProjectRoot,
			command:     waitForDebugBreakCommandName,
		})
		return 1
	}

	startedAt := time.Now()
	spinner := newToolSpinner(stderr, waitForDebugBreakCommandName)
	spinner.Update("Checking Unity play mode state...")

	initialState, err := queryPlayModeState(ctx, connection)
	if err != nil {
		spinner.Stop()
		writeClassifiedError(stderr, err, errorContext{
			projectRoot: connection.ProjectRoot,
			command:     waitForDebugBreakCommandName,
		})
		return 1
	}
	if initialState.IsPaused {
		spinner.Stop()
		writeErrorEnvelope(stderr, debugBreakAlreadyPausedError(connection.ProjectRoot, initialState))
		return 1
	}
	if !initialState.IsPlaying {
		spinner.Stop()
		writeErrorEnvelope(stderr, debugBreakNotPlayingError(connection.ProjectRoot, initialState))
		return 1
	}

	spinner.Update("Waiting for Debug.Break...")
	finalState, waitErr := waitForDebugBreak(ctx, connection, initialState)
	spinner.Stop()
	if waitErr != nil {
		writeClassifiedError(stderr, waitErr, errorContext{
			projectRoot: connection.ProjectRoot,
			command:     waitForDebugBreakCommandName,
		})
		return 1
	}

	response := waitForDebugBreakResponse{
		Success:             true,
		IsPlaying:           finalState.IsPlaying,
		IsPaused:            finalState.IsPaused,
		ElapsedMilliseconds: time.Since(startedAt).Milliseconds(),
		Message:             "Debug break observed.",
	}
	result, marshalErr := json.Marshal(response)
	if marshalErr != nil {
		writeClassifiedError(stderr, marshalErr, errorContext{
			projectRoot: connection.ProjectRoot,
			command:     waitForDebugBreakCommandName,
		})
		return 1
	}
	writeJSON(stdout, result)
	return 0
}

func parseWaitForDebugBreakArgs(args []string) error {
	for _, arg := range args {
		return &argumentError{
			message:     "Unknown wait-for-debug-break option: " + arg,
			option:      arg,
			command:     waitForDebugBreakCommandName,
			nextActions: []string{"Run `uloop wait-for-debug-break --help` to inspect supported options."},
		}
	}

	return nil
}

func waitForDebugBreak(
	ctx context.Context,
	connection unityipc.Connection,
	initialState playModeStateResponse,
) (playModeStateResponse, error) {
	lastState := initialState
	var lastErr error
	for {
		state, err := queryPlayModeState(ctx, connection)
		if err == nil {
			lastState = state
			if state.IsPaused {
				return state, nil
			}
		} else {
			lastErr = err
		}

		select {
		case <-ctx.Done():
			if lastErr != nil {
				return lastState, lastErr
			}
			return lastState, ctx.Err()
		case <-time.After(waitForDebugBreakPollInterval):
		}
	}
}

func queryPlayModeStateFromUnity(ctx context.Context, connection unityipc.Connection) (playModeStateResponse, error) {
	probeContext, cancel := context.WithTimeout(ctx, waitForDebugBreakStatusProbeTimeout)
	defer cancel()

	result, err := unityipc.NewClient(connection, version).Send(
		probeContext,
		playModeStateCommandName,
		map[string]any{},
	)
	if err != nil {
		return playModeStateResponse{}, err
	}

	response := playModeStateResponse{}
	if err := json.Unmarshal(result, &response); err != nil {
		return playModeStateResponse{}, err
	}
	return response, nil
}

func debugBreakAlreadyPausedError(projectRoot string, state playModeStateResponse) cliError {
	return cliError{
		ErrorCode:   errorCodeDebugBreakAlreadyPaused,
		Phase:       errorPhaseResponseWaiting,
		Message:     "Unity Editor is already paused before waiting for Debug.Break.",
		Retryable:   true,
		SafeToRetry: true,
		ProjectRoot: projectRoot,
		Command:     waitForDebugBreakCommandName,
		NextActions: []string{
			"Run `uloop control-play-mode --action Play` to resume Unity from the existing pause, then run `uloop wait-for-debug-break` before triggering the target action.",
		},
		Details: map[string]any{
			"isPlaying": state.IsPlaying,
			"isPaused":  state.IsPaused,
		},
	}
}

func debugBreakNotPlayingError(projectRoot string, state playModeStateResponse) cliError {
	return cliError{
		ErrorCode:   errorCodeDebugBreakNotPlaying,
		Phase:       errorPhaseResponseWaiting,
		Message:     "Unity Editor is not in PlayMode before waiting for Debug.Break.",
		Retryable:   true,
		SafeToRetry: true,
		ProjectRoot: projectRoot,
		Command:     waitForDebugBreakCommandName,
		NextActions: []string{
			"Run `uloop control-play-mode --action Play`, then run `uloop wait-for-debug-break` before triggering the target action.",
		},
		Details: map[string]any{
			"isPlaying": state.IsPlaying,
			"isPaused":  state.IsPaused,
		},
	}
}
