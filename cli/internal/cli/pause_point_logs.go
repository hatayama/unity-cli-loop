package cli

import (
	"context"
	"encoding/json"

	"github.com/hatayama/unity-cli-loop/cli/internal/unityipc"
)

const (
	pausePointLogsMaxCountFlagName = "matching-logs-max-count"
	pausePointDefaultLogsMaxCount  = 10
	pausePointGetLogsCommandName   = "get-logs"
)

// Injectable so tests can simulate Unity log responses without an Editor.
var fetchMatchingLogs = fetchMatchingLogsFromUnity

type pausePointMatchingLog struct {
	Type    string `json:"Type"`
	Message string `json:"Message"`
}

// pausePointWaitResult extends the hit response with marker-matching logs so
// agents do not need a separate get-logs call while Unity is paused.
type pausePointWaitResult struct {
	pausePointStatusResponse
	MatchingLogs []pausePointMatchingLog `json:"MatchingLogs"`
}

type pausePointGetLogsResponse struct {
	Logs []pausePointMatchingLog `json:"Logs"`
}

func fetchMatchingLogsFromUnity(
	ctx context.Context,
	connection unityipc.Connection,
	searchText string,
	maxCount int,
) ([]pausePointMatchingLog, error) {
	probeContext, cancel := context.WithTimeout(ctx, pausePointStatusProbeTimeout)
	defer cancel()

	result, err := unityipc.NewClient(connection, version).Send(
		probeContext,
		pausePointGetLogsCommandName,
		map[string]any{
			"SearchText": searchText,
			"MaxCount":   maxCount,
		},
	)
	if err != nil {
		return nil, err
	}

	response := pausePointGetLogsResponse{}
	if err := json.Unmarshal(result, &response); err != nil {
		return nil, err
	}
	if response.Logs == nil {
		return []pausePointMatchingLog{}, nil
	}
	return response.Logs, nil
}
