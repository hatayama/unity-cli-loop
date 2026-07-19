package projectrunner

import (
	"context"
	"encoding/json"
	"strings"

	"github.com/hatayama/unity-cli-loop/common/clicontract"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

const (
	pausePointDefaultLogsMaxCount = 10
	pausePointGetLogsCommandName  = "get-logs"
)

// Injectable so tests can simulate Unity log responses without an Editor.
var fetchMatchingLogs = fetchMatchingLogsFromUnity

type pausePointMatchingLog struct {
	Type       string `json:"Type"`
	Message    string `json:"Message"`
	StackTrace string `json:"StackTrace"`
}

type pausePointMatchingLogsResult struct {
	SearchText        string                  `json:"SearchText"`
	TotalCount        int                     `json:"TotalCount"`
	DisplayedCount    int                     `json:"DisplayedCount"`
	LogType           string                  `json:"LogType"`
	MaxCount          int                     `json:"MaxCount"`
	IncludeStackTrace bool                    `json:"IncludeStackTrace"`
	Logs              []pausePointMatchingLog `json:"Logs"`
}

// pausePointWaitResult extends the hit response with marker-matching logs so
// agents do not need a separate get-logs call while Unity is paused. Warning
// carries the only actionable signal that used to live inside a now-removed
// EvidenceSummary; everything else in that summary duplicated fields already
// present on pausePointStatusResponse or MatchingLogs.
type pausePointWaitResult struct {
	pausePointStatusResponse
	MatchingLogs []pausePointMatchingLog `json:"MatchingLogs"`
	Warning      string                  `json:"Warning,omitempty"`
}

type pausePointGetLogsResponse struct {
	Success           bool                    `json:"Success"`
	TotalCount        int                     `json:"TotalCount"`
	DisplayedCount    int                     `json:"DisplayedCount"`
	LogType           string                  `json:"LogType"`
	MaxCount          int                     `json:"MaxCount"`
	SearchText        string                  `json:"SearchText"`
	IncludeStackTrace bool                    `json:"IncludeStackTrace"`
	Logs              []pausePointMatchingLog `json:"Logs"`
}

func fetchMatchingLogsFromUnity(
	ctx context.Context,
	connection unityipc.Connection,
	searchText string,
	maxCount int,
) (pausePointMatchingLogsResult, error) {
	probeContext, cancel := context.WithTimeout(ctx, pausePointStatusProbeTimeout)
	defer cancel()

	result, err := unityipc.NewClient(connection, clicontract.ProjectRunnerVersion()).Send(
		probeContext,
		pausePointGetLogsCommandName,
		map[string]any{
			"SearchText": searchText,
			"MaxCount":   maxCount,
		},
	)
	if err != nil {
		return pausePointMatchingLogsResult{}, err
	}

	response := pausePointGetLogsResponse{}
	if err := json.Unmarshal(result, &response); err != nil {
		return pausePointMatchingLogsResult{}, err
	}
	if response.Logs == nil {
		response.Logs = []pausePointMatchingLog{}
	}
	if response.SearchText == "" {
		response.SearchText = searchText
	}
	if response.MaxCount == 0 {
		response.MaxCount = maxCount
	}
	if response.DisplayedCount == 0 && len(response.Logs) > 0 {
		response.DisplayedCount = len(response.Logs)
	}
	if response.TotalCount < len(response.Logs) {
		response.TotalCount = len(response.Logs)
	}

	return pausePointMatchingLogsResult{
		SearchText:        response.SearchText,
		TotalCount:        response.TotalCount,
		DisplayedCount:    response.DisplayedCount,
		LogType:           response.LogType,
		MaxCount:          response.MaxCount,
		IncludeStackTrace: response.IncludeStackTrace,
		Logs:              response.Logs,
	}, nil
}

func buildPausePointWarning(logs pausePointMatchingLogsResult, hitCount int) string {
	matchingLogCount := logs.TotalCount
	if matchingLogCount < len(logs.Logs) {
		matchingLogCount = len(logs.Logs)
	}
	returnedLogCount := len(logs.Logs)

	warnings := []string{}
	if matchingLogCount > returnedLogCount {
		warnings = append(
			warnings,
			"Matching logs may be truncated by --matching-logs-max-count; increase the limit before treating this as complete evidence.")
	}
	if matchingLogCount > 1 {
		warnings = append(
			warnings,
			"Multiple matching logs were observed for this pause point id; inspect MatchingLogs before treating the scenario as single-fire evidence.")
	}
	if hitCount > 1 {
		warnings = append(
			warnings,
			"The pause point reports multiple hits; inspect the paused state before treating the scenario as single-fire evidence.")
	}
	return strings.Join(warnings, " ")
}
