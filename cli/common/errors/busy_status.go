package clierrors

import (
	"fmt"
)

func unityServerBusyMessage(fallback string, data serverBusyErrorData, requestedCommand string) string {
	runningToolName := data.RunningToolName
	requestedToolName := firstNonEmpty(data.RequestedToolName, requestedCommand)
	if runningToolName == "" || requestedToolName == "" {
		return fallback
	}
	// Both the retrying tool path and commands that send once without retrying reach this
	// message, so it must not claim that a retry already happened.
	if data.RunningToolElapsedSeconds != nil {
		return fmt.Sprintf(
			"'%s' was not executed because Unity is busy running '%s' (running for %ds). uloop is single-flight per Editor. Wait for '%s' to complete, then run the command once more; do not retry in a loop.",
			requestedToolName,
			runningToolName,
			*data.RunningToolElapsedSeconds,
			runningToolName)
	}
	return fmt.Sprintf(
		"'%s' was not executed because Unity is busy running '%s'. uloop is single-flight per Editor. Wait for '%s' to complete, then run the command once more; do not retry in a loop.",
		requestedToolName,
		runningToolName,
		runningToolName)
}
