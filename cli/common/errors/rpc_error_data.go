package clierrors

import "encoding/json"

// Mirrors Packages/src/Editor/Infrastructure/Api/ServerBusyErrorData.cs public properties.
type serverBusyErrorData struct {
	Type                           string   `json:"type"`
	Message                        string   `json:"message"`
	RunningToolName                string   `json:"runningToolName"`
	RequestedToolName              string   `json:"requestedToolName"`
	IsPlaying                      *bool    `json:"isPlaying"`
	IsPaused                       *bool    `json:"isPaused"`
	IsCompiling                    *bool    `json:"isCompiling"`
	IsUpdating                     *bool    `json:"isUpdating"`
	SecondsSinceLastMainThreadTick *float64 `json:"secondsSinceLastMainThreadTick"`
	RunningToolElapsedSeconds      *int     `json:"runningToolElapsedSeconds"`
}

// Mirrors Packages/src/Editor/Infrastructure/Api/CliUpdateRequiredErrorData.cs public properties.
type cliUpdateRequiredErrorData struct {
	Type                    string `json:"type"`
	Message                 string `json:"message"`
	CurrentCliVersion       string `json:"currentCliVersion"`
	CurrentProtocolVersion  *int   `json:"currentProtocolVersion"`
	RequiredProtocolVersion int    `json:"requiredProtocolVersion"`
	UpdateCommand           string `json:"updateCommand"`
	RetryableAfterUpdate    bool   `json:"retryableAfterUpdate"`
}

func decodeServerBusyErrorData(data json.RawMessage) serverBusyErrorData {
	var decoded serverBusyErrorData
	// Why ignore: keep the previous missing-key = zero-value fallback. A type mismatch
	// on one field used to zero only that field via map assertions; failing closed to
	// the zero struct is the typed equivalent and avoids inventing per-field recovery.
	_ = json.Unmarshal(data, &decoded)
	return decoded
}

func decodeCliUpdateRequiredErrorData(data json.RawMessage) cliUpdateRequiredErrorData {
	var decoded cliUpdateRequiredErrorData
	// Why ignore: keep the previous missing-key = zero-value fallback.
	_ = json.Unmarshal(data, &decoded)
	return decoded
}

func RPCDataType(data json.RawMessage) string {
	var typed struct {
		Type string `json:"type"`
	}
	// Why ignore: empty or unknown payloads classify as no type, matching a map key miss.
	_ = json.Unmarshal(data, &typed)
	return typed.Type
}
