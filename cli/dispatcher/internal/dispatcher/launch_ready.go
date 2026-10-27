package dispatcher

import (
	"encoding/json"
	"fmt"
	"io"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"
	"github.com/hatayama/unity-cli-loop/common/ui"

	"github.com/hatayama/unity-cli-loop/common/clicore"
)

const (
	launchReadinessMessage           = "Waiting for Unity CLI Loop server readiness..."
	launchReadyMessage               = "Unity CLI Loop is ready."
	launchAlreadyRunningReadyMessage = "Unity is already running and ready."
	launchQuitStoppedMessage         = "Unity quit. It saved its window layout and discarded unsaved scene changes."
	launchForceKillStoppedMessage    = "Unity was force-killed because it did not take the quit request or did not exit in time. Its window layout was not saved, and unsaved scene changes were lost."
	launchNoProcessMessage           = "No matching Unity process was found; it may have already exited."
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
	StopMethod        string `json:"StopMethod,omitempty"`
	CurrentProcessId  *int   `json:"CurrentProcessId,omitempty"`
	ProjectRoot       string `json:"ProjectRoot"`
	Message           string `json:"Message"`
}

func writeLaunchReadinessWait(stdout io.Writer, spinner *ui.TerminalSpinner) {
	spinner.Update(launchReadinessMessage)
	if !spinner.Enabled {
		clicore.WriteLine(stdout, launchReadinessMessage)
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

func writeExistingV2LaunchReadyResponse(stdout io.Writer, stderr io.Writer, projectRoot string, currentPid int) int {
	return writeLaunchResponse(stdout, stderr, launchReadyResponse{
		Success:          true,
		Ready:            true,
		ServerReady:      true,
		ProjectIpcReady:  false,
		AlreadyRunning:   true,
		CurrentProcessId: &currentPid,
		ProjectRoot:      projectRoot,
		Message:          "Unity is already running and the V2 uloop server is ready. This project uses uloop V2; commands run through the V2 CLI.",
	})
}

func writeLaunchedReadyResponse(
	stdout io.Writer,
	stderr io.Writer,
	projectRoot string,
	previousPid *int,
	previousStop launchStopMethod,
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
		StopMethod:        string(previousStop),
		CurrentProcessId:  &currentPid,
		ProjectRoot:       projectRoot,
		Message:           launchReadyMessage,
	})
}

func writeLaunchedV2ReadyResponse(
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
		ProjectIpcReady:   false,
		Launched:          true,
		Restarted:         previousPid != nil,
		PreviousProcessId: previousPid,
		CurrentProcessId:  &currentPid,
		ProjectRoot:       projectRoot,
		Message:           "Unity started and the V2 uloop server is ready. This project uses uloop V2; commands run through the V2 CLI.",
	})
}

func writeLaunchQuitResponse(
	stdout io.Writer,
	stderr io.Writer,
	projectRoot string,
	previousPid *int,
	stopMethod launchStopMethod,
) int {
	return writeLaunchResponse(stdout, stderr, launchReadyResponse{
		Success:           true,
		Quit:              true,
		PreviousProcessId: previousPid,
		StopMethod:        string(stopMethod),
		ProjectRoot:       projectRoot,
		Message:           launchQuitMessage(stopMethod),
	})
}

// launchQuitMessage words the --quit result for how the Editor stopped, or for no Editor to stop.
func launchQuitMessage(stopMethod launchStopMethod) string {
	switch stopMethod {
	case launchStopMethodQuit:
		return launchQuitStoppedMessage
	case launchStopMethodForceKill:
		return launchForceKillStoppedMessage
	default:
		return launchNoProcessMessage
	}
}

func writeLaunchResponse(stdout io.Writer, stderr io.Writer, response launchReadyResponse) int {
	payload, err := json.Marshal(response)
	if err != nil {
		clierrors.WriteClassifiedError(stderr, err, clierrors.ErrorContext{ProjectRoot: response.ProjectRoot, Command: clicore.LaunchCommandName})
		return 1
	}
	clicore.WriteJSON(stdout, payload)
	return 0
}
