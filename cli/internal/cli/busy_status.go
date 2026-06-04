package cli

import (
	"encoding/json"
	"fmt"
	"io"
)

const cliStatusBusy = "Busy"

type cliStatusEnvelope struct {
	Status            string `json:"Status"`
	Message           string `json:"Message"`
	RunningToolName   string `json:"RunningToolName,omitempty"`
	RequestedToolName string `json:"RequestedToolName,omitempty"`
	IsPlaying         *bool  `json:"IsPlaying,omitempty"`
	IsPaused          *bool  `json:"IsPaused,omitempty"`
}

type serverBusyStatusDetails struct {
	runningToolName   string
	requestedToolName string
	isPlaying         bool
	hasIsPlaying      bool
	isPaused          bool
	hasIsPaused       bool
}

func writeBusyStatusEnvelope(writer io.Writer, message string, details serverBusyStatusDetails) {
	encoder := json.NewEncoder(writer)
	encoder.SetIndent("", "  ")
	_ = encoder.Encode(cliStatusEnvelope{
		Status:            cliStatusBusy,
		Message:           message,
		RunningToolName:   details.runningToolName,
		RequestedToolName: details.requestedToolName,
		IsPlaying:         optionalBool(details.hasIsPlaying, details.isPlaying),
		IsPaused:          optionalBool(details.hasIsPaused, details.isPaused),
	})
}

func serverBusyStatusDetailsFromError(err cliError) serverBusyStatusDetails {
	data, ok := err.Details["data"].(map[string]any)
	if !ok {
		return serverBusyStatusDetails{
			requestedToolName: err.Command,
		}
	}

	isPlaying, hasIsPlaying := rpcBoolData(data, "isPlaying")
	isPaused, hasIsPaused := rpcBoolData(data, "isPaused")
	return serverBusyStatusDetails{
		runningToolName:   rpcStringData(data, "runningToolName"),
		requestedToolName: firstNonEmpty(rpcStringData(data, "requestedToolName"), err.Command),
		isPlaying:         isPlaying,
		hasIsPlaying:      hasIsPlaying,
		isPaused:          isPaused,
		hasIsPaused:       hasIsPaused,
	}
}

func optionalBool(hasValue bool, value bool) *bool {
	if !hasValue {
		return nil
	}
	return &value
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

func rpcBoolData(data map[string]any, key string) (bool, bool) {
	value, ok := data[key].(bool)
	if !ok {
		return false, false
	}
	return value, true
}
