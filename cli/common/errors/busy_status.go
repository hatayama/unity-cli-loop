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
	// This surfaces only after the CLI's bounded busy retry gives up, so it is the one
	// guaranteed teaching moment for the single-flight contract.
	if data.RunningToolElapsedSeconds != nil {
		return fmt.Sprintf(
			"'%s' was not executed because Unity is busy running '%s' (running for %ds). uloop is single-flight per Editor, so the CLI already retried for up to 10 seconds. Wait for '%s' to complete, then run the command once more; do not retry in a loop.",
			requestedToolName,
			runningToolName,
			*data.RunningToolElapsedSeconds,
			runningToolName)
	}
	return fmt.Sprintf(
		"'%s' was not executed because Unity is busy running '%s'. uloop is single-flight per Editor, so the CLI already retried for up to 10 seconds. Wait for '%s' to complete, then run the command once more; do not retry in a loop.",
		requestedToolName,
		runningToolName,
		runningToolName)
}
