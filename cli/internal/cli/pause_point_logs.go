package cli

import (
	"context"
	"encoding/json"
	"strings"

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
	Type    string `json:"type"`
	Message string `json:"message"`
}

type pausePointMatchingLogsResult struct {
	SearchText     string                  `json:"searchText"`
	TotalCount     int                     `json:"totalCount"`
	DisplayedCount int                     `json:"displayedCount"`
	MaxCount       int                     `json:"maxCount"`
	Logs           []pausePointMatchingLog `json:"logs"`
}

type pausePointEvidenceSummary struct {
	EditorState  pausePointEditorState        `json:"editorState"`
	PausePoint   pausePointEvidencePausePoint `json:"pausePoint"`
	MatchingLogs pausePointEvidenceLogs       `json:"matchingLogs"`
	Warning      string                       `json:"warning"`
}

type pausePointEvidencePausePoint struct {
	Id                   string `json:"id"`
	Status               string `json:"status"`
	Generation           int    `json:"generation"`
	HitCount             int    `json:"hitCount"`
	MultipleHitsObserved bool   `json:"multipleHitsObserved"`
	FirstHitAtUtc        string `json:"firstHitAtUtc"`
	LastHitAtUtc         string `json:"lastHitAtUtc"`
	FirstHitSequence     int    `json:"firstHitSequence"`
	LastHitSequence      int    `json:"lastHitSequence"`
}

type pausePointEvidenceLogs struct {
	SearchText                   string `json:"searchText"`
	MatchingLogCount             int    `json:"matchingLogCount"`
	ReturnedLogCount             int    `json:"returnedLogCount"`
	MaxCount                     int    `json:"maxCount"`
	MayBeTruncated               bool   `json:"mayBeTruncated"`
	MultipleMatchingLogsObserved bool   `json:"multipleMatchingLogsObserved"`
}

// pausePointWaitResult extends the hit response with marker-matching logs and
// evidence summary so agents do not need a separate get-logs call while Unity is paused.
type pausePointWaitResult struct {
	pausePointStatusResponse
	MatchingLogs    []pausePointMatchingLog   `json:"matchingLogs"`
	EvidenceSummary pausePointEvidenceSummary `json:"evidenceSummary"`
}

type pausePointGetLogsResponse struct {
	TotalCount     int                     `json:"totalCount"`
	DisplayedCount int                     `json:"displayedCount"`
	MaxCount       int                     `json:"maxCount"`
	SearchText     string                  `json:"searchText"`
	Logs           []pausePointMatchingLog `json:"logs"`
}

func fetchMatchingLogsFromUnity(
	ctx context.Context,
	connection unityipc.Connection,
	searchText string,
	maxCount int,
) (pausePointMatchingLogsResult, error) {
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
		SearchText:     response.SearchText,
		TotalCount:     response.TotalCount,
		DisplayedCount: response.DisplayedCount,
		MaxCount:       response.MaxCount,
		Logs:           response.Logs,
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
		MaxCount:                     logs.MaxCount,
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
