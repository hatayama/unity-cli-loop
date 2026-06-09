package cli

import (
	"encoding/json"
	"io"
)

const (
	launchReadinessMessage = "Waiting for Unity CLI Loop server readiness..."
	launchReadyMessage     = "Unity CLI Loop is ready."
)

type launchReadyResponse struct {
	Success         bool   `json:"Success"`
	Ready           bool   `json:"Ready"`
	ServerReady     bool   `json:"ServerReady"`
	ProjectIpcReady bool   `json:"ProjectIpcReady"`
	Message         string `json:"Message"`
}

func writeLaunchReadinessWait(stdout io.Writer, spinner *terminalSpinner) {
	spinner.Update(launchReadinessMessage)
	if !spinner.enabled {
		writeLine(stdout, launchReadinessMessage)
	}
}

func writeLaunchReadyResponse(stdout io.Writer, stderr io.Writer, projectRoot string) int {
	response := launchReadyResponse{
		Success:         true,
		Ready:           true,
		ServerReady:     true,
		ProjectIpcReady: true,
		Message:         launchReadyMessage,
	}
	payload, err := json.Marshal(response)
	if err != nil {
		writeClassifiedError(stderr, err, errorContext{projectRoot: projectRoot, command: launchCommandName})
		return 1
	}
	writeJSON(stdout, payload)
	return 0
}
