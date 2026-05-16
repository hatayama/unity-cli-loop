package cli

import (
	"context"
	"encoding/json"
	"fmt"
	"os"
	"path/filepath"
	"time"
)

const (
	serverStateRelativePath         = "Temp/UnityCliLoop/server-state.json"
	serverStateCompletedTempSuffix  = ".tmp"
	serverStateInProgressTempSuffix = ".tmp.write"
	serverStateBackupSuffix         = ".bak"
	serverStateReadRetryAttempts    = 20
)

var (
	readServerStateFileContent = os.ReadFile
	serverStateReadRetryDelay  = 25 * time.Millisecond
)

type serverState struct {
	Phase        string `json:"phase"`
	GenerationID string `json:"generationId"`
	UpdatedAt    string `json:"updatedAt"`
	Reason       string `json:"reason"`
	Endpoint     string `json:"endpoint"`
	LastError    string `json:"lastError"`
}

type serverStoppedError struct {
	state serverState
}

type staleServerStateError struct {
	state serverState
}

func (err serverStoppedError) Error() string {
	if err.state.Reason != "" {
		return fmt.Sprintf("unity cli loop server stopped during %s", err.state.Reason)
	}
	return "unity cli loop server is stopped"
}

func (err staleServerStateError) Error() string {
	if err.state.Phase != "" {
		return fmt.Sprintf("unity cli loop server readiness state is stale: %s", err.state.Phase)
	}
	return "unity cli loop server readiness state is stale"
}

func readServerState(projectRoot string) (serverState, bool, error) {
	statePath := filepath.Join(projectRoot, serverStateRelativePath)
	var lastErr error
	for attempt := 0; attempt <= serverStateReadRetryAttempts; attempt++ {
		content, ok, err := readServerStateFile(statePath)
		if err != nil {
			return serverState{}, false, err
		}
		if !ok {
			return serverState{}, false, nil
		}

		var state serverState
		if err := json.Unmarshal(content, &state); err == nil {
			return state, true, nil
		} else {
			lastErr = err
		}

		if attempt < serverStateReadRetryAttempts {
			// Unity can briefly expose a truncated readiness file while replacing it.
			time.Sleep(serverStateReadRetryDelay)
		}
	}
	return serverState{}, false, fmt.Errorf("server readiness state is unreadable: %w", lastErr)
}

func readServerStateFile(statePath string) ([]byte, bool, error) {
	content, err := readServerStateFileWithRetry(statePath)
	if err != nil {
		if !os.IsNotExist(err) {
			return nil, false, err
		}
	} else {
		return content, true, nil
	}

	for _, sidecarPath := range []string{statePath + serverStateCompletedTempSuffix, statePath + serverStateBackupSuffix} {
		sidecarContent, sidecarErr := readServerStateFileWithRetry(sidecarPath)
		if sidecarErr == nil {
			return sidecarContent, true, nil
		}
		if !os.IsNotExist(sidecarErr) {
			return nil, false, fmt.Errorf("server readiness state sidecar is unreadable: %w", sidecarErr)
		}
	}

	return nil, false, nil
}

func readServerStateFileWithRetry(statePath string) ([]byte, error) {
	var lastErr error
	for attempt := 0; attempt <= serverStateReadRetryAttempts; attempt++ {
		content, err := readServerStateFileContent(statePath)
		if err == nil || os.IsNotExist(err) {
			return content, err
		}

		lastErr = err
		if attempt < serverStateReadRetryAttempts {
			// Unity can briefly publish the readiness file with an exclusive Windows handle.
			time.Sleep(serverStateReadRetryDelay)
		}
	}
	return nil, lastErr
}

func isServerStateBusy(state serverState) bool {
	switch state.Phase {
	case "starting", "compiling", "reloading", "recovering", "stopping":
		return true
	default:
		return false
	}
}

func isServerStateStopped(state serverState) bool {
	return state.Phase == "stopped"
}

func waitForRecoveringServerIfNeeded(
	ctx context.Context,
	projectRoot string,
	waitForReadiness func(context.Context, string) error,
) error {
	state, ok, err := readServerState(projectRoot)
	if err != nil {
		return err
	}
	if !ok {
		return nil
	}
	if failure := serverStateFailureError(state); failure != nil {
		return failure
	}
	if !isServerStateBusy(state) {
		return nil
	}
	return waitForReadiness(ctx, projectRoot)
}

func serverStateFailureError(state serverState) error {
	if state.Phase != "failed" {
		return nil
	}
	if state.LastError != "" {
		return fmt.Errorf("unity cli loop server recovery failed: %s", state.LastError)
	}
	if state.Reason != "" {
		return fmt.Errorf("unity cli loop server recovery failed during %s", state.Reason)
	}
	return fmt.Errorf("unity cli loop server recovery failed")
}
