package clierrors

import (
	"fmt"
)

func unityServerBusyMessage(fallback string, data map[string]any, requestedCommand string) string {
	runningToolName := rpcStringData(data, "runningToolName")
	requestedToolName := firstNonEmpty(rpcStringData(data, "requestedToolName"), requestedCommand)
	if runningToolName == "" || requestedToolName == "" {
		return fallback
	}
	// This surfaces only after the CLI's bounded busy retry gives up, so it is the one
	// guaranteed teaching moment for the single-flight contract.
	return fmt.Sprintf(
		"'%s' was not executed because Unity is busy running '%s'. uloop is single-flight by design; never run uloop commands in parallel. The CLI already retried for up to 10 seconds, so wait for '%s' to complete and run the command again.",
		requestedToolName,
		runningToolName,
		runningToolName)
}

func rpcStringData(data map[string]any, key string) string {
	value, ok := data[key].(string)
	if !ok {
		return ""
	}
	return value
}
