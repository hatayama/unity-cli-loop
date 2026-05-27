package cli

import (
	"encoding/json"
	"fmt"
	"io"
)

const cliStatusBusy = "Busy"

type cliStatusEnvelope struct {
	Status  string `json:"Status"`
	Message string `json:"Message"`
}

func writeBusyStatusEnvelope(writer io.Writer, message string) {
	encoder := json.NewEncoder(writer)
	encoder.SetIndent("", "  ")
	_ = encoder.Encode(cliStatusEnvelope{
		Status:  cliStatusBusy,
		Message: message,
	})
}

func unityServerBusyMessage(fallback string, data map[string]any, requestedCommand string) string {
	runningToolName := rpcStringData(data, "runningToolName")
	requestedToolName := firstNonEmpty(rpcStringData(data, "requestedToolName"), requestedCommand)
	if runningToolName == "" || requestedToolName == "" {
		return fallback
	}
	return fmt.Sprintf(
		"'%s' was not executed because Unity is busy running '%s'. Retry after the running tool completes.",
		requestedToolName,
		runningToolName)
}

func rpcStringData(data map[string]any, key string) string {
	value, ok := data[key].(string)
	if !ok {
		return ""
	}
	return value
}
