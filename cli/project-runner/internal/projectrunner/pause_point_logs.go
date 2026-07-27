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

	// Expectations and AllExpectationsPassed are populated only when --expect was passed, so a
	// caller that never used --expect sees neither field rather than a vacuous
	// "AllExpectationsPassed":true with no Expectations behind it. AllExpectationsPassed is a
	// pointer so omitempty can distinguish "no --expect given" (nil, omitted) from "the given
	// expectations failed" (non-nil false, still emitted).
	Expectations          []pausePointExpectationResult `json:"Expectations,omitempty"`
	AllExpectationsPassed *bool                         `json:"AllExpectationsPassed,omitempty"`
}

// pausePointHitPayloadInputs gathers everything a hit payload is built from. Both hit paths
// (await-pause-point and enable-pause-point --await) share one builder because they had drifted
// apart before: a field added to one silently stayed missing from the other.
type pausePointHitPayloadInputs struct {
	response pausePointStatusResponse

	// logs / logsErr come straight from fetchMatchingLogs. A failed fetch omits MatchingLogs
	// entirely rather than emitting an empty array, so "empty array" keeps meaning "the fetch
	// succeeded and nothing matched".
	logs    pausePointMatchingLogsResult
	logsErr error

	// unityWarning is Unity's own warning for this hit: the status response's on the plain await
	// path, the enable response's on the enable --await path.
	unityWarning string

	triggerResult       *pausePointTriggerResult
	awaitedPausePointID string
	expectations        []pausePointExpectationResult
}

// buildPausePointHitPayload assembles the JSON payload for a hit, folding the CLI-side diagnosis
// (trigger outcome, warnings, --expect verdicts) into the Unity response.
func buildPausePointHitPayload(inputs pausePointHitPayloadInputs) any {
	response := inputs.response
	response.TriggerFailed = pausePointTriggerFailedPointer(inputs.triggerResult)
	triggerWarning := pausePointTriggerRefusalWarning(inputs.triggerResult, inputs.awaitedPausePointID)

	if inputs.logsErr != nil {
		// Best-effort: a failed log fetch must not also drop the CLI-side evidence — the warnings
		// or the --expect results, which are the only evidence left in this branch.
		return struct {
			pausePointStatusResponse
			Warning               string                        `json:"Warning,omitempty"`
			Expectations          []pausePointExpectationResult `json:"Expectations,omitempty"`
			AllExpectationsPassed *bool                         `json:"AllExpectationsPassed,omitempty"`
		}{
			pausePointStatusResponse: response,
			Warning:                  joinPausePointWarnings(inputs.unityWarning, triggerWarning),
			Expectations:             inputs.expectations,
			AllExpectationsPassed:    pausePointAllExpectationsPassedPointer(inputs.expectations),
		}
	}

	return pausePointWaitResult{
		pausePointStatusResponse: response,
		MatchingLogs:             inputs.logs.Logs,
		Warning: joinPausePointWarnings(
			inputs.unityWarning,
			buildPausePointWarning(inputs.logs, response.HitCount),
			triggerWarning),
		Expectations:          inputs.expectations,
		AllExpectationsPassed: pausePointAllExpectationsPassedPointer(inputs.expectations),
	}
}

// pausePointAllExpectationsPassedPointer returns nil when no --expect was given, and otherwise
// a pointer to whether every expectation passed.
func pausePointAllExpectationsPassedPointer(results []pausePointExpectationResult) *bool {
	if results == nil {
		return nil
	}
	passed := allPausePointExpectationsPassed(results)
	return &passed
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
