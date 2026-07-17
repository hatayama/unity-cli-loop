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

type pausePointEvidenceSummary struct {
	EditorState  pausePointEditorState        `json:"EditorState"`
	PausePoint   pausePointEvidencePausePoint `json:"PausePoint"`
	MatchingLogs pausePointEvidenceLogs       `json:"MatchingLogs"`
	Warning      string                       `json:"Warning"`
}

type pausePointEvidencePausePoint struct {
	Id                   string `json:"Id"`
	Status               string `json:"Status"`
	Generation           int    `json:"Generation"`
	HitCount             int    `json:"HitCount"`
	MultipleHitsObserved bool   `json:"MultipleHitsObserved"`
	FirstHitAtUtc        string `json:"FirstHitAtUtc"`
	LastHitAtUtc         string `json:"LastHitAtUtc"`
	FirstHitSequence     int    `json:"FirstHitSequence"`
	LastHitSequence      int    `json:"LastHitSequence"`
}

type pausePointEvidenceLogs struct {
	SearchText                   string `json:"SearchText"`
	MatchingLogCount             int    `json:"MatchingLogCount"`
	ReturnedLogCount             int    `json:"ReturnedLogCount"`
	LogType                      string `json:"LogType"`
	MaxCount                     int    `json:"MaxCount"`
	IncludeStackTrace            bool   `json:"IncludeStackTrace"`
	MayBeTruncated               bool   `json:"MayBeTruncated"`
	MultipleMatchingLogsObserved bool   `json:"MultipleMatchingLogsObserved"`
}

// pausePointWaitResult extends the hit response with marker-matching logs and
// evidence summary so agents do not need a separate get-logs call while Unity is paused.
type pausePointWaitResult struct {
	pausePointStatusResponse
	MatchingLogs    []pausePointMatchingLog   `json:"MatchingLogs"`
	EvidenceSummary pausePointEvidenceSummary `json:"EvidenceSummary"`
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

func buildPausePointEvidenceSummary(
	response pausePointStatusResponse,
	logs pausePointMatchingLogsResult,
) pausePointEvidenceSummary {
	return pausePointEvidenceSummary{
		EditorState: response.EditorState,
		PausePoint: pausePointEvidencePausePoint{
			Id:                   response.Id,
			Status:               response.Status,
			Generation:           response.Generation,
			HitCount:             response.HitCount,
			MultipleHitsObserved: response.HitCount > 1,
			FirstHitAtUtc:        response.FirstHitAtUtc,
			LastHitAtUtc:         response.LastHitAtUtc,
			FirstHitSequence:     response.FirstHitSequence,
			LastHitSequence:      response.LastHitSequence,
		},
		MatchingLogs: buildPausePointEvidenceLogs(logs),
		Warning:      buildPausePointEvidenceWarning(logs, response.HitCount),
	}
}

func buildPausePointEvidenceLogs(logs pausePointMatchingLogsResult) pausePointEvidenceLogs {
	matchingLogCount := logs.TotalCount
	if matchingLogCount < len(logs.Logs) {
		matchingLogCount = len(logs.Logs)
	}
	returnedLogCount := len(logs.Logs)
	return pausePointEvidenceLogs{
		SearchText:                   logs.SearchText,
		MatchingLogCount:             matchingLogCount,
		ReturnedLogCount:             returnedLogCount,
		LogType:                      logs.LogType,
		MaxCount:                     logs.MaxCount,
		IncludeStackTrace:            logs.IncludeStackTrace,
		MayBeTruncated:               matchingLogCount > returnedLogCount,
		MultipleMatchingLogsObserved: matchingLogCount > 1,
	}
}

func buildPausePointEvidenceWarning(logs pausePointMatchingLogsResult, hitCount int) string {
	evidenceLogs := buildPausePointEvidenceLogs(logs)
	warnings := []string{}
	if evidenceLogs.MayBeTruncated {
		warnings = append(
			warnings,
			"Matching logs may be truncated by --matching-logs-max-count; increase the limit before treating this as complete evidence.")
	}
	if evidenceLogs.MultipleMatchingLogsObserved {
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
