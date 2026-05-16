package cli

import (
	"context"
	"encoding/json"
	"fmt"
	"os"
	"path/filepath"
)

const serverStateRelativePath = "Temp/UnityCliLoop/server-state.json"

type serverState struct {
	Phase        string `json:"phase"`
	GenerationID string `json:"generationId"`
	UpdatedAt    string `json:"updatedAt"`
	Reason       string `json:"reason"`
	Endpoint     string `json:"endpoint"`
	LastError    string `json:"lastError"`
}

func readServerState(projectRoot string) (serverState, bool, error) {
	statePath := filepath.Join(projectRoot, serverStateRelativePath)
	content, ok, err := readServerStateFile(statePath)
	if err != nil {
		return serverState{}, false, err
	}
	if !ok {
		return serverState{}, false, nil
	}

	var state serverState
	if err := json.Unmarshal(content, &state); err != nil {
		return serverState{}, false, fmt.Errorf("server readiness state is unreadable: %w", err)
	}
	return state, true, nil
}

func readServerStateFile(statePath string) ([]byte, bool, error) {
	content, err := os.ReadFile(statePath)
	if err != nil {
		if !os.IsNotExist(err) {
			return nil, false, err
		}
	} else {
		return content, true, nil
	}

	for _, sidecarPath := range []string{statePath + ".tmp", statePath + ".bak"} {
		sidecarContent, sidecarErr := os.ReadFile(sidecarPath)
		if sidecarErr == nil {
			return sidecarContent, true, nil
		}
		if !os.IsNotExist(sidecarErr) {
			return nil, false, fmt.Errorf("server readiness state sidecar is unreadable: %w", sidecarErr)
		}
	}

	return nil, false, nil
}

func isServerStateBusy(state serverState) bool {
	switch state.Phase {
	case "starting", "compiling", "reloading", "recovering", "stopping":
		return true
	default:
		return false
	}
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
