package cli

import (
	"encoding/json"
	"fmt"
	"io"
)

const (
	launchReadinessMessage           = "Waiting for Unity CLI Loop server readiness..."
	launchReadyMessage               = "Unity CLI Loop is ready."
	launchAlreadyRunningReadyMessage = "Unity is already running and ready."
	launchStoppedMessage             = "Unity process stopped."
	launchNoProcessMessage           = "No Unity process is running for this project."
)

type launchReadyResponse struct {
	Success           bool   `json:"Success"`
	Ready             bool   `json:"Ready"`
	ServerReady       bool   `json:"ServerReady"`
	ProjectIpcReady   bool   `json:"ProjectIpcReady"`
	AlreadyRunning    bool   `json:"AlreadyRunning"`
	Launched          bool   `json:"Launched"`
	Restarted         bool   `json:"Restarted"`
	Quit              bool   `json:"Quit"`
	PreviousProcessId *int   `json:"PreviousProcessId,omitempty"`
	CurrentProcessId  *int   `json:"CurrentProcessId,omitempty"`
	ProjectRoot       string `json:"ProjectRoot"`
	Message           string `json:"Message"`
}

func writeLaunchReadinessWait(stdout io.Writer, spinner *terminalSpinner) {
	spinner.Update(launchReadinessMessage)
	if !spinner.enabled {
		writeLine(stdout, launchReadinessMessage)
	}
}

// writeDetectionFallbackLaunchReadyResponse reports a running Editor that was proven via
// the project IPC after the process scan failed, so no process id or window focus is available.
func writeDetectionFallbackLaunchReadyResponse(stdout io.Writer, stderr io.Writer, projectRoot string, detectionErr error) int {
	return writeLaunchResponse(stdout, stderr, launchReadyResponse{
		Success:         true,
		Ready:           true,
		ServerReady:     true,
		ProjectIpcReady: true,
		AlreadyRunning:  true,
		ProjectRoot:     projectRoot,
		Message: fmt.Sprintf(
			"Unity is already running and ready. The process scan failed (%v), so the existing window was not focused.",
			detectionErr),
	})
}

func writeExistingLaunchReadyResponse(stdout io.Writer, stderr io.Writer, projectRoot string, currentPid int) int {
	return writeLaunchResponse(stdout, stderr, launchReadyResponse{
		Success:          true,
		Ready:            true,
		ServerReady:      true,
		ProjectIpcReady:  true,
		AlreadyRunning:   true,
		CurrentProcessId: &currentPid,
		ProjectRoot:      projectRoot,
		Message:          launchAlreadyRunningReadyMessage,
	})
}

func writeLaunchedReadyResponse(
	stdout io.Writer,
	stderr io.Writer,
	projectRoot string,
	previousPid *int,
	currentPid int,
) int {
	return writeLaunchResponse(stdout, stderr, launchReadyResponse{
		Success:           true,
		Ready:             true,
		ServerReady:       true,
		ProjectIpcReady:   true,
		Launched:          true,
		Restarted:         previousPid != nil,
		PreviousProcessId: previousPid,
		CurrentProcessId:  &currentPid,
		ProjectRoot:       projectRoot,
		Message:           launchReadyMessage,
	})
}

func writeLaunchQuitResponse(
	stdout io.Writer,
	stderr io.Writer,
	projectRoot string,
	previousPid *int,
	message string,
) int {
	return writeLaunchResponse(stdout, stderr, launchReadyResponse{
		Success:           true,
		Quit:              true,
		PreviousProcessId: previousPid,
		ProjectRoot:       projectRoot,
		Message:           message,
	})
}

func writeLaunchResponse(stdout io.Writer, stderr io.Writer, response launchReadyResponse) int {
	payload, err := json.Marshal(response)
	if err != nil {
		writeClassifiedError(stderr, err, errorContext{projectRoot: response.ProjectRoot, command: launchCommandName})
		return 1
	}
	writeJSON(stdout, payload)
	return 0
}
