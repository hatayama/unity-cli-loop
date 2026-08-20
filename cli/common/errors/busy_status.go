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
			"'%s' was not executed because Unity is busy running '%s' (running for %ds). uloop is single-flight by design; never run uloop commands in parallel. The CLI already retried for up to 10 seconds, so wait for '%s' to complete and run the command again.",
			requestedToolName,
			runningToolName,
			*data.RunningToolElapsedSeconds,
			runningToolName)
	}
	return fmt.Sprintf(
		"'%s' was not executed because Unity is busy running '%s'. uloop is single-flight by design; never run uloop commands in parallel. The CLI already retried for up to 10 seconds, so wait for '%s' to complete and run the command again.",
		requestedToolName,
		runningToolName,
		runningToolName)
}
