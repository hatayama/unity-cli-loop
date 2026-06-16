package cli

import (
	"bytes"
	"context"
	"encoding/json"
	"path/filepath"
	"strings"
	"testing"
	"time"

	"github.com/hatayama/unity-cli-loop/cli/internal/unityipc"
)

// Verifies wait-for-pause-point polls until Unity reports the marker hit.
func TestWaitForPausePointReturnsHitAfterEnabledStatus(t *testing.T) {
	originalQuery := queryPausePointStatus
	originalPoll := pausePointStatusPoll
	pausePointStatusPoll = time.Millisecond
	defer func() {
		queryPausePointStatus = originalQuery
		pausePointStatusPoll = originalPoll
	}()

	responses := []pausePointStatusResponse{
		{Id: "jump", Status: pausePointStatusEnabled, IsEnabled: true},
		{
			Id:          "jump",
			Status:      pausePointStatusHit,
			IsHit:       true,
			HitCount:    1,
			EditorState: pausePointEditorState{IsPlaying: true, IsPaused: true, CapturedAt: "PausePointHit"},
		},
	}
	requestCount := 0
	queryPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		if id != "jump" {
			t.Fatalf("id mismatch: %s", id)
		}
		response := responses[requestCount]
		requestCount++
		return response, nil
	}

	response, state, err := waitForPausePoint(context.Background(), unityipc.Connection{}, waitForPausePointOptions{
		id:             "jump",
		timeoutSeconds: 1,
		timeout:        time.Second,
	})
	if err != nil {
		t.Fatalf("waitForPausePoint failed: %v", err)
	}
	if state != pausePointWaitStateHit {
		t.Fatalf("state mismatch: %s", state)
	}
	if response.Status != pausePointStatusHit || response.HitCount != 1 {
		t.Fatalf("response mismatch: %#v", response)
	}
	if requestCount != 2 {
		t.Fatalf("request count mismatch: %d", requestCount)
	}
}

// Verifies wait-for-pause-point clears the enabled marker after its own timeout.
func TestRunWaitForPausePointClearsEnabledMarkerAfterTimeout(t *testing.T) {
	originalQuery := queryPausePointStatus
	originalClear := clearPausePointStatus
	originalPoll := pausePointStatusPoll
	pausePointStatusPoll = time.Millisecond
	defer func() {
		queryPausePointStatus = originalQuery
		clearPausePointStatus = originalClear
		pausePointStatusPoll = originalPoll
	}()

	queryPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		return pausePointStatusResponse{
			Id:                              id,
			Status:                          pausePointStatusEnabled,
			IsEnabled:                       true,
			TimeoutSeconds:                  1,
			ElapsedSinceEnabledMilliseconds: 100,
			EditorState:                     pausePointEditorState{IsPlaying: true, CapturedAt: "Current"},
			Message:                         "Pause point enabled.",
		}, nil
	}

	clearedID := ""
	clearPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		clearedID = id
		return pausePointStatusResponse{Id: id, Status: pausePointStatusCleared}, nil
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runWaitForPausePoint(context.Background(), unityipc.Connection{}, waitForPausePointOptions{
		id:             "jump",
		timeoutSeconds: 1,
		timeout:        5 * time.Millisecond,
	}, &stdout, &stderr)

	if code != 1 {
		t.Fatalf("expected failure, got %d with stdout %s", code, stdout.String())
	}
	if clearedID != "jump" {
		t.Fatalf("cleared id mismatch: %s", clearedID)
	}
	if !strings.Contains(stderr.String(), errorCodePausePointWaitTimeout) {
		t.Fatalf("timeout error missing from stderr: %s", stderr.String())
	}
	envelope := parsePausePointErrorEnvelope(t, stderr.Bytes())
	editorState, ok := envelope.Error.Details["editorState"].(map[string]any)
	if !ok || editorState["isPlaying"] != true || editorState["isPaused"] != false || editorState["capturedAt"] != "Current" {
		t.Fatalf("editorState detail mismatch: %#v", envelope.Error.Details)
	}
	if envelope.Error.Details["markerMessage"] != "Pause point enabled." {
		t.Fatalf("markerMessage detail mismatch: %#v", envelope.Error.Details)
	}
	if envelope.Error.Details["elapsedSinceEnabledMilliseconds"] != float64(100) {
		t.Fatalf("elapsedSinceEnabledMilliseconds detail mismatch: %#v", envelope.Error.Details)
	}
	if envelope.Error.Details["remainingMilliseconds"] != float64(900) {
		t.Fatalf("remainingMilliseconds detail mismatch: %#v", envelope.Error.Details)
	}
}

// Verifies pause point wait errors expose recovery and generation details from Unity.
func TestPausePointExpiredErrorReportsRecoveryFields(t *testing.T) {
	response := pausePointStatusResponse{
		Id:                              "jump",
		Status:                          pausePointStatusExpired,
		Expired:                         true,
		TimeoutSeconds:                  1,
		EnabledAtUtc:                    "2026-06-03T00:00:00.0000000Z",
		ElapsedSinceEnabledMilliseconds: 1200,
		RemainingMilliseconds:           0,
		Generation:                      7,
		EditorState:                     pausePointEditorState{IsPlaying: true, CapturedAt: "Current"},
		Message:                         "Pause point expired before it was hit.",
		RecommendedNextAction:           "Clear this marker, then re-enable it with the same Id and TimeoutSeconds values.",
	}

	cliErr := pausePointWaitError("/tmp/MyProject", waitForPausePointOptions{
		id:             "jump",
		timeoutSeconds: 1,
	}, response, pausePointWaitStateExpired)

	if cliErr.Details["expired"] != true {
		t.Fatalf("expired detail mismatch: %#v", cliErr.Details)
	}
	if cliErr.Details["enabledAtUtc"] != "2026-06-03T00:00:00.0000000Z" {
		t.Fatalf("enabledAtUtc detail mismatch: %#v", cliErr.Details)
	}
	if cliErr.Details["generation"] != 7 {
		t.Fatalf("generation detail mismatch: %#v", cliErr.Details)
	}
	if cliErr.Details["recommendedNextAction"] != response.RecommendedNextAction {
		t.Fatalf("recommendedNextAction detail mismatch: %#v", cliErr.Details)
	}
}

// Verifies recovery details use the marker lifetime instead of the wait deadline.
func TestPausePointExpiredErrorReportsMarkerTimeoutSeconds(t *testing.T) {
	response := pausePointStatusResponse{
		Id:             "jump",
		Status:         pausePointStatusExpired,
		TimeoutSeconds: 30,
		EditorState:    pausePointEditorState{IsPlaying: true, CapturedAt: "Current"},
		Message:        "Pause point expired before it was hit.",
	}

	cliErr := pausePointWaitError("/tmp/MyProject", waitForPausePointOptions{
		id:             "jump",
		timeoutSeconds: 5,
	}, response, pausePointWaitStateExpired)

	if cliErr.Details["timeoutSeconds"] != 30 {
		t.Fatalf("timeoutSeconds detail mismatch: %#v", cliErr.Details)
	}
}

// Verifies wait errors derive Expired from Status when older Unity packages omit the bool field.
func TestPausePointExpiredErrorDerivesExpiredFromStatus(t *testing.T) {
	response := pausePointStatusResponse{
		Id:          "jump",
		Status:      pausePointStatusExpired,
		EditorState: pausePointEditorState{IsPlaying: true, CapturedAt: "Current"},
		Message:     "Pause point expired before it was hit.",
	}

	cliErr := pausePointWaitError("/tmp/MyProject", waitForPausePointOptions{
		id:             "jump",
		timeoutSeconds: 1,
	}, response, pausePointWaitStateExpired)

	if cliErr.Details["expired"] != true {
		t.Fatalf("expired detail mismatch: %#v", cliErr.Details)
	}
}

// Verifies wait-for-pause-point does one final status probe before treating timeout as missed.
func TestRunWaitForPausePointReturnsFinalHitAtTimeoutBoundary(t *testing.T) {
	originalQuery := queryPausePointStatus
	originalClear := clearPausePointStatus
	defer func() {
		queryPausePointStatus = originalQuery
		clearPausePointStatus = originalClear
	}()

	requestCount := 0
	queryPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		requestCount++
		if requestCount == 1 {
			return pausePointStatusResponse{
				Id:        id,
				Status:    pausePointStatusEnabled,
				IsEnabled: true,
			}, nil
		}
		return pausePointStatusResponse{
			Id:          id,
			Status:      pausePointStatusHit,
			IsHit:       true,
			HitCount:    1,
			EditorState: pausePointEditorState{IsPlaying: true, IsPaused: true, CapturedAt: "PausePointHit"},
		}, nil
	}

	clearedID := ""
	clearPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		clearedID = id
		return pausePointStatusResponse{Id: id, Status: pausePointStatusCleared}, nil
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runWaitForPausePoint(context.Background(), unityipc.Connection{}, waitForPausePointOptions{
		id:             "jump",
		timeoutSeconds: 1,
		timeout:        5 * time.Millisecond,
	}, &stdout, &stderr)

	if code != 0 {
		t.Fatalf("expected success, got %d with stderr %s", code, stderr.String())
	}
	if clearedID != "" {
		t.Fatalf("marker should not be cleared after final hit: %s", clearedID)
	}
	var response pausePointStatusResponse
	if err := json.Unmarshal(stdout.Bytes(), &response); err != nil {
		t.Fatalf("stdout is not valid JSON: %v\n%s", err, stdout.String())
	}
	if response.Status != pausePointStatusHit || response.HitCount != 1 {
		t.Fatalf("response mismatch: %#v", response)
	}
	if requestCount != 2 {
		t.Fatalf("request count mismatch: %d", requestCount)
	}
}

// Verifies wait-for-pause-point rejects calls before the marker is enabled.
func TestWaitForPausePointReturnsNotEnabledStateImmediately(t *testing.T) {
	originalQuery := queryPausePointStatus
	defer func() {
		queryPausePointStatus = originalQuery
	}()

	queryPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		return pausePointStatusResponse{
			Id:          id,
			Status:      pausePointStatusNotEnabled,
			EditorState: pausePointEditorState{IsPlaying: true, CapturedAt: "Current"},
		}, nil
	}

	response, state, err := waitForPausePoint(context.Background(), unityipc.Connection{}, waitForPausePointOptions{
		id:             "jump",
		timeoutSeconds: 1,
		timeout:        time.Second,
	})
	if err != nil {
		t.Fatalf("waitForPausePoint failed: %v", err)
	}
	if state != pausePointWaitStateNotEnabled {
		t.Fatalf("state mismatch: %s", state)
	}
	if response.Status != pausePointStatusNotEnabled {
		t.Fatalf("response mismatch: %#v", response)
	}
}

// Verifies not-enabled failures use the user-facing enabled terminology.
func TestRunWaitForPausePointReportsNotEnabledError(t *testing.T) {
	originalQuery := queryPausePointStatus
	defer func() {
		queryPausePointStatus = originalQuery
	}()

	queryPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		return pausePointStatusResponse{
			Id:          id,
			Status:      pausePointStatusNotEnabled,
			EditorState: pausePointEditorState{IsPlaying: true, CapturedAt: "Current"},
			Message:     "Pause point is not enabled.",
		}, nil
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runWaitForPausePoint(context.Background(), unityipc.Connection{}, waitForPausePointOptions{
		id:             "jump",
		timeoutSeconds: 1,
		timeout:        time.Second,
	}, &stdout, &stderr)

	if code != 1 {
		t.Fatalf("expected failure, got %d with stdout %s", code, stdout.String())
	}
	envelope := parsePausePointErrorEnvelope(t, stderr.Bytes())
	if envelope.Error.ErrorCode != errorCodePausePointNotEnabled {
		t.Fatalf("error code mismatch: %#v", envelope.Error)
	}
	if envelope.Error.Details["status"] != pausePointStatusNotEnabled {
		t.Fatalf("status detail mismatch: %#v", envelope.Error.Details)
	}
	editorState, ok := envelope.Error.Details["editorState"].(map[string]any)
	if !ok || editorState["isPlaying"] != true || editorState["isPaused"] != false || editorState["capturedAt"] != "Current" {
		t.Fatalf("editorState details mismatch: %#v", envelope.Error.Details)
	}
}

// Verifies the matching-log count flag parses with a safe default and rejects non-positive counts.
func TestParseWaitForPausePointOptionsParsesMatchingLogFlags(t *testing.T) {
	defaults, err := parseWaitForPausePointOptions([]string{"--id", "jump"})
	if err != nil {
		t.Fatalf("default parse failed: %v", err)
	}
	if defaults.matchingLogsMaxCount != pausePointDefaultLogsMaxCount {
		t.Fatalf("default matching-log options mismatch: %#v", defaults)
	}

	options, err := parseWaitForPausePointOptions([]string{
		"--id", "jump", "--matching-logs-max-count", "5",
	})
	if err != nil {
		t.Fatalf("parse failed: %v", err)
	}
	if options.matchingLogsMaxCount != 5 {
		t.Fatalf("matching-log options mismatch: %#v", options)
	}

	if _, err := parseWaitForPausePointOptions([]string{"--id", "jump", "--matching-logs-max-count", "0"}); err == nil {
		t.Fatalf("expected error for non-positive max count")
	}

	// Log embedding is always on, so the retired opt-in flag must be rejected as unknown.
	if _, err := parseWaitForPausePointOptions([]string{"--id", "jump", "--include-matching-logs"}); err == nil {
		t.Fatalf("expected error for the retired include flag")
	}
}

// Verifies a hit response always embeds marker-matching logs.
func TestRunWaitForPausePointEmbedsMatchingLogsOnHit(t *testing.T) {
	originalQuery := queryPausePointStatus
	originalFetch := fetchMatchingLogs
	defer func() {
		queryPausePointStatus = originalQuery
		fetchMatchingLogs = originalFetch
	}()

	queryPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		return pausePointStatusResponse{
			Id:               id,
			Status:           pausePointStatusHit,
			IsHit:            true,
			HitCount:         1,
			Generation:       7,
			FirstHitAtUtc:    "2026-06-03T00:00:00.1250000Z",
			LastHitAtUtc:     "2026-06-03T00:00:00.1250000Z",
			FirstHitSequence: 3,
			LastHitSequence:  3,
			EditorState:      pausePointEditorState{IsPlaying: true, IsPaused: true, CapturedAt: "PausePointHit"},
		}, nil
	}

	fetchedSearchText := ""
	fetchedMaxCount := 0
	fetchMatchingLogs = func(
		ctx context.Context,
		connection unityipc.Connection,
		searchText string,
		maxCount int,
	) (pausePointMatchingLogsResult, error) {
		fetchedSearchText = searchText
		fetchedMaxCount = maxCount
		return pausePointMatchingLogsResult{
			SearchText:     searchText,
			TotalCount:     2,
			DisplayedCount: 2,
			MaxCount:       maxCount,
			Logs: []pausePointMatchingLog{
				{Type: "Log", Message: "[jump] velocity=4.2"},
				{Type: "Log", Message: "[jump] grounded=false"},
			},
		}, nil
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runWaitForPausePoint(context.Background(), unityipc.Connection{}, waitForPausePointOptions{
		id:                   "jump",
		timeoutSeconds:       1,
		timeout:              time.Second,
		matchingLogsMaxCount: 5,
	}, &stdout, &stderr)

	if code != 0 {
		t.Fatalf("expected success, got %d with stderr %s", code, stderr.String())
	}
	if fetchedSearchText != "jump" || fetchedMaxCount != 5 {
		t.Fatalf("fetch arguments mismatch: %s %d", fetchedSearchText, fetchedMaxCount)
	}

	result := pausePointWaitResult{}
	if err := json.Unmarshal(stdout.Bytes(), &result); err != nil {
		t.Fatalf("stdout parse failed: %v from %s", err, stdout.String())
	}
	if len(result.MatchingLogs) != 2 || result.MatchingLogs[0].Message != "[jump] velocity=4.2" {
		t.Fatalf("matching logs mismatch: %#v", result.MatchingLogs)
	}
	if result.EvidenceSummary.EditorState.CapturedAt != "PausePointHit" {
		t.Fatalf("editor state summary mismatch: %#v", result.EvidenceSummary)
	}
	if result.EvidenceSummary.PausePoint.Generation != 7 || result.EvidenceSummary.PausePoint.FirstHitSequence != 3 {
		t.Fatalf("pause point summary mismatch: %#v", result.EvidenceSummary)
	}
	if !result.EvidenceSummary.MatchingLogs.MultipleMatchingLogsObserved ||
		result.EvidenceSummary.MatchingLogs.MatchingLogCount != 2 ||
		result.EvidenceSummary.MatchingLogs.ReturnedLogCount != 2 {
		t.Fatalf("matching log summary mismatch: %#v", result.EvidenceSummary)
	}
	if !strings.Contains(result.EvidenceSummary.Warning, "Multiple matching logs") {
		t.Fatalf("warning mismatch: %#v", result.EvidenceSummary)
	}
}

// Verifies a successful fetch with zero matches yields an explicit empty MatchingLogs array,
// so agents can tell "no matching log appeared" apart from "log fetch failed" (field absent).
func TestRunWaitForPausePointEmbedsEmptyMatchingLogsWhenNoneMatch(t *testing.T) {
	originalQuery := queryPausePointStatus
	originalFetch := fetchMatchingLogs
	defer func() {
		queryPausePointStatus = originalQuery
		fetchMatchingLogs = originalFetch
	}()

	queryPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		return pausePointStatusResponse{
			Id:          id,
			Status:      pausePointStatusHit,
			IsHit:       true,
			HitCount:    1,
			EditorState: pausePointEditorState{IsPlaying: true, IsPaused: true, CapturedAt: "PausePointHit"},
		}, nil
	}
	fetchMatchingLogs = func(
		ctx context.Context,
		connection unityipc.Connection,
		searchText string,
		maxCount int,
	) (pausePointMatchingLogsResult, error) {
		return pausePointMatchingLogsResult{
			SearchText: searchText,
			MaxCount:   maxCount,
			Logs:       []pausePointMatchingLog{},
		}, nil
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runWaitForPausePoint(context.Background(), unityipc.Connection{}, waitForPausePointOptions{
		id:                   "jump",
		timeoutSeconds:       1,
		timeout:              time.Second,
		matchingLogsMaxCount: pausePointDefaultLogsMaxCount,
	}, &stdout, &stderr)

	if code != 0 {
		t.Fatalf("expected success, got %d with stderr %s", code, stderr.String())
	}
	var result pausePointWaitResult
	if err := json.Unmarshal(stdout.Bytes(), &result); err != nil {
		t.Fatalf("stdout parse failed: %v from %s", err, stdout.String())
	}
	if result.MatchingLogs == nil || len(result.MatchingLogs) != 0 {
		t.Fatalf("MatchingLogs must be an explicit empty array: %#v", result.MatchingLogs)
	}
	if result.EvidenceSummary.Warning != "" {
		t.Fatalf("warning should be empty when there are no matching logs: %#v", result.EvidenceSummary)
	}
}

// Verifies a log fetch failure never turns a successful hit into an error.
func TestRunWaitForPausePointIgnoresLogFetchFailure(t *testing.T) {
	originalQuery := queryPausePointStatus
	originalFetch := fetchMatchingLogs
	defer func() {
		queryPausePointStatus = originalQuery
		fetchMatchingLogs = originalFetch
	}()

	queryPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		return pausePointStatusResponse{
			Id:          id,
			Status:      pausePointStatusHit,
			IsHit:       true,
			HitCount:    1,
			EditorState: pausePointEditorState{IsPlaying: true, IsPaused: true, CapturedAt: "PausePointHit"},
		}, nil
	}
	fetchMatchingLogs = func(
		ctx context.Context,
		connection unityipc.Connection,
		searchText string,
		maxCount int,
	) (pausePointMatchingLogsResult, error) {
		return pausePointMatchingLogsResult{}, context.DeadlineExceeded
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runWaitForPausePoint(context.Background(), unityipc.Connection{}, waitForPausePointOptions{
		id:                   "jump",
		timeoutSeconds:       1,
		timeout:              time.Second,
		matchingLogsMaxCount: pausePointDefaultLogsMaxCount,
	}, &stdout, &stderr)

	if code != 0 {
		t.Fatalf("expected success despite log fetch failure, got %d with stderr %s", code, stderr.String())
	}
	if strings.Contains(stdout.String(), "matchingLogs") {
		t.Fatalf("MatchingLogs must be omitted when the fetch fails: %s", stdout.String())
	}
	if strings.Contains(stdout.String(), "evidenceSummary") {
		t.Fatalf("EvidenceSummary must be omitted when the fetch fails: %s", stdout.String())
	}
}

// Verifies timeout envelopes always embed marker-matching logs best-effort.
func TestRunWaitForPausePointEmbedsMatchingLogsOnTimeout(t *testing.T) {
	originalQuery := queryPausePointStatus
	originalClear := clearPausePointStatus
	originalFetch := fetchMatchingLogs
	originalPoll := pausePointStatusPoll
	pausePointStatusPoll = time.Millisecond
	defer func() {
		queryPausePointStatus = originalQuery
		clearPausePointStatus = originalClear
		fetchMatchingLogs = originalFetch
		pausePointStatusPoll = originalPoll
	}()

	queryPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		return pausePointStatusResponse{
			Id:          id,
			Status:      pausePointStatusEnabled,
			IsEnabled:   true,
			EditorState: pausePointEditorState{IsPlaying: true, CapturedAt: "Current"},
		}, nil
	}
	clearPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		return pausePointStatusResponse{Id: id, Status: pausePointStatusCleared}, nil
	}
	fetchMatchingLogs = func(
		ctx context.Context,
		connection unityipc.Connection,
		searchText string,
		maxCount int,
	) (pausePointMatchingLogsResult, error) {
		return pausePointMatchingLogsResult{
			SearchText:     searchText,
			TotalCount:     3,
			DisplayedCount: 1,
			MaxCount:       maxCount,
			Logs:           []pausePointMatchingLog{{Type: "Log", Message: "[jump] never reached"}},
		}, nil
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runWaitForPausePoint(context.Background(), unityipc.Connection{}, waitForPausePointOptions{
		id:                   "jump",
		timeoutSeconds:       1,
		timeout:              5 * time.Millisecond,
		matchingLogsMaxCount: pausePointDefaultLogsMaxCount,
	}, &stdout, &stderr)

	if code != 1 {
		t.Fatalf("expected timeout failure, got %d with stdout %s", code, stdout.String())
	}
	envelope := parsePausePointErrorEnvelope(t, stderr.Bytes())
	// The detail key mirrors the hit-response field name, so one spelling covers both surfaces.
	matchingLogs, ok := envelope.Error.Details["matchingLogs"].([]any)
	if !ok || len(matchingLogs) != 1 {
		t.Fatalf("MatchingLogs detail mismatch: %#v", envelope.Error.Details)
	}
	evidenceSummary, ok := envelope.Error.Details["evidenceSummary"].(map[string]any)
	if !ok {
		t.Fatalf("EvidenceSummary detail missing: %#v", envelope.Error.Details)
	}
	evidenceLogs, ok := evidenceSummary["matchingLogs"].(map[string]any)
	if !ok || evidenceLogs["mayBeTruncated"] != true || evidenceLogs["matchingLogCount"] != float64(3) {
		t.Fatalf("EvidenceSummary matching logs mismatch: %#v", evidenceSummary)
	}
}

// Verifies timeout errors include a deterministic diagnosis hint for common stuck states.
func TestPausePointTimeoutErrorIncludesDiagnosisHint(t *testing.T) {
	cases := []struct {
		name     string
		response pausePointStatusResponse
		wantHint string
	}{
		{
			name:     "play mode not running",
			response: pausePointStatusResponse{Id: "jump", Status: pausePointStatusEnabled},
			wantHint: "PlayMode is not running. Start PlayMode (or trigger the marker code path in Edit Mode), then wait again.",
		},
		{
			name: "editor already paused",
			response: pausePointStatusResponse{
				Id:          "jump",
				Status:      pausePointStatusEnabled,
				EditorState: pausePointEditorState{IsPlaying: true, IsPaused: true, CapturedAt: "Current"},
			},
			wantHint: "Unity is already paused, so gameplay cannot reach the marker. Resume PlayMode before waiting again.",
		},
		{
			name: "marker never hit",
			response: pausePointStatusResponse{
				Id:          "jump",
				Status:      pausePointStatusEnabled,
				EditorState: pausePointEditorState{IsPlaying: true, CapturedAt: "Current"},
				HitCount:    0,
			},
			wantHint: "Marker was enabled but never hit. Confirm the id matches UloopPausePoint.Pause(\"<id>\") and that the code path was executed. In fast-progressing games the state may have already moved past the marker (for example back to Ready or GameOver), so re-trigger the code path and wait again.",
		},
	}

	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			cliErr := pausePointWaitError("/tmp/MyProject", waitForPausePointOptions{
				id:             "jump",
				timeoutSeconds: 1,
			}, testCase.response, pausePointWaitStateTimeout)

			if cliErr.Details["hint"] != testCase.wantHint {
				t.Fatalf("hint mismatch: %#v", cliErr.Details)
			}
		})
	}
}

// Verifies expired errors include a diagnosis hint, because a marker whose enable
// window ends before the wait deadline surfaces as PAUSE_POINT_EXPIRED, not a timeout.
func TestPausePointExpiredErrorIncludesDiagnosisHint(t *testing.T) {
	cases := []struct {
		name     string
		response pausePointStatusResponse
		wantHint string
	}{
		{
			name:     "play mode not running",
			response: pausePointStatusResponse{Id: "jump", Status: pausePointStatusExpired},
			wantHint: "PlayMode is not running. Start PlayMode (or trigger the marker code path in Edit Mode), then wait again.",
		},
		{
			name: "editor already paused",
			response: pausePointStatusResponse{
				Id:          "jump",
				Status:      pausePointStatusExpired,
				EditorState: pausePointEditorState{IsPlaying: true, IsPaused: true, CapturedAt: "Current"},
			},
			wantHint: "Unity is already paused, so gameplay cannot reach the marker. Resume PlayMode before waiting again.",
		},
		{
			name: "marker expired before hit",
			response: pausePointStatusResponse{
				Id:          "jump",
				Status:      pausePointStatusExpired,
				EditorState: pausePointEditorState{IsPlaying: true, CapturedAt: "Current"},
				HitCount:    0,
			},
			wantHint: "Marker expired before it was hit: the enable-pause-point --timeout-seconds window (measured from enable, not from this wait) ran out. Re-enable the marker with a longer --timeout-seconds and trigger the code path again.",
		},
	}

	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			cliErr := pausePointWaitError("/tmp/MyProject", waitForPausePointOptions{
				id:             "jump",
				timeoutSeconds: 1,
			}, testCase.response, pausePointWaitStateExpired)

			if cliErr.Details["hint"] != testCase.wantHint {
				t.Fatalf("hint mismatch: %#v", cliErr.Details)
			}
		})
	}
}

// Verifies hints stay scoped to diagnosable response states.
func TestPausePointHintIsOmittedOutsideDiagnosableStates(t *testing.T) {
	hitResponse := pausePointStatusResponse{
		Id:          "jump",
		Status:      pausePointStatusHit,
		EditorState: pausePointEditorState{IsPlaying: true, CapturedAt: "PausePointHit"},
		HitCount:    1,
	}
	timeoutErr := pausePointWaitError("/tmp/MyProject", waitForPausePointOptions{
		id:             "jump",
		timeoutSeconds: 1,
	}, hitResponse, pausePointWaitStateTimeout)
	if _, exists := timeoutErr.Details["hint"]; exists {
		t.Fatalf("hint should be omitted when no diagnosis applies: %#v", timeoutErr.Details)
	}

	clearedResponse := pausePointStatusResponse{
		Id:          "jump",
		Status:      pausePointStatusCleared,
		EditorState: pausePointEditorState{IsPlaying: true, CapturedAt: "Current"},
	}
	clearedErr := pausePointWaitError("/tmp/MyProject", waitForPausePointOptions{
		id:             "jump",
		timeoutSeconds: 1,
	}, clearedResponse, pausePointWaitStateCleared)
	if _, exists := clearedErr.Details["hint"]; exists {
		t.Fatalf("hint should be omitted for cleared markers: %#v", clearedErr.Details)
	}
}

// Verifies expired markers report no remaining enabled lifetime.
func TestPausePointExpiredErrorReportsNoRemainingTime(t *testing.T) {
	response := pausePointStatusResponse{
		Id:                              "jump",
		Status:                          pausePointStatusExpired,
		TimeoutSeconds:                  1,
		ElapsedSinceEnabledMilliseconds: 1200,
		EditorState:                     pausePointEditorState{IsPlaying: true, CapturedAt: "Current"},
		Message:                         "Pause point expired before it was hit.",
	}

	cliErr := pausePointWaitError("/tmp/MyProject", waitForPausePointOptions{
		id:             "jump",
		timeoutSeconds: 1,
	}, response, pausePointWaitStateExpired)

	if cliErr.ErrorCode != errorCodePausePointExpired {
		t.Fatalf("error code mismatch: %#v", cliErr)
	}
	if cliErr.Details["remainingMilliseconds"] != int64(0) {
		t.Fatalf("remainingMilliseconds detail mismatch: %#v", cliErr.Details)
	}
}

// Verifies disabled native pause-point commands are rejected before Unity dispatch.
func TestRunProjectLocalWaitForPausePointRespectsToolSettings(t *testing.T) {
	projectRoot := createLaunchTestProject(t)
	writeToolSettings(t, projectRoot, `{"disabledTools":["wait-for-pause-point"]}`)
	t.Chdir(filepath.Dir(projectRoot))

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := RunProjectLocal(
		context.Background(),
		[]string{"--project-path", projectRoot, pausePointWaitCommandName, "--id", "jump"},
		&stdout,
		&stderr)

	if code != 1 {
		t.Fatalf("expected disabled command failure, got %d with stdout %s", code, stdout.String())
	}
	envelope := parsePausePointErrorEnvelope(t, stderr.Bytes())
	if envelope.Error.ErrorCode != errorCodeToolDisabled {
		t.Fatalf("error code mismatch: %#v", envelope.Error)
	}
	if envelope.Error.Command != pausePointWaitCommandName {
		t.Fatalf("command mismatch: %#v", envelope.Error)
	}
}

// Verifies wait-for-pause-point requires a marker id.
func TestParseWaitForPausePointOptionsRequiresID(t *testing.T) {
	_, err := parseWaitForPausePointOptions([]string{"--timeout-seconds", "1"})

	if err == nil {
		t.Fatal("expected missing id error")
	}
	if !strings.Contains(err.Error(), "Missing required option") {
		t.Fatalf("error mismatch: %v", err)
	}
}

// Verifies pause-point-status reports the current marker state without waiting for a hit.
func TestRunPausePointStatusReturnsCurrentStatus(t *testing.T) {
	originalQuery := queryPausePointStatus
	defer func() {
		queryPausePointStatus = originalQuery
	}()

	queryPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		return pausePointStatusResponse{
			Id:                    id,
			Status:                pausePointStatusEnabled,
			IsEnabled:             true,
			EnabledAtUtc:          "2026-06-03T00:00:00.0000000Z",
			RemainingMilliseconds: 30000,
			Generation:            3,
			EditorState:           pausePointEditorState{IsPlaying: true, CapturedAt: "Current"},
			RecommendedNextAction: "Clear this marker, then re-enable it with the same Id and TimeoutSeconds values.",
		}, nil
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runPausePointStatusCommand(
		context.Background(),
		unityipc.Connection{ProjectRoot: "/tmp/MyProject"},
		[]string{"--id", "jump"},
		&stdout,
		&stderr)

	if code != 0 {
		t.Fatalf("expected success, got %d with stderr %s", code, stderr.String())
	}
	var response pausePointStatusResponse
	if err := json.Unmarshal(stdout.Bytes(), &response); err != nil {
		t.Fatalf("stdout is not valid JSON: %v\n%s", err, stdout.String())
	}
	if response.Status != pausePointStatusEnabled {
		t.Fatalf("status mismatch: %#v", response)
	}
	if response.EnabledAtUtc != "2026-06-03T00:00:00.0000000Z" {
		t.Fatalf("enabledAtUtc mismatch: %#v", response)
	}
	if response.RemainingMilliseconds != 30000 {
		t.Fatalf("remaining milliseconds mismatch: %#v", response)
	}
	if response.Generation != 3 {
		t.Fatalf("generation mismatch: %#v", response)
	}
	if response.EditorState.CapturedAt != "Current" || !response.EditorState.IsPlaying {
		t.Fatalf("editor state mismatch: %#v", response)
	}
	if response.RecommendedNextAction != "Clear this marker, then re-enable it with the same Id and TimeoutSeconds values." {
		t.Fatalf("recommendedNextAction mismatch: %#v", response)
	}
}

// Verifies pause-point-status derives Expired from Status when older Unity packages omit the bool field.
func TestRunPausePointStatusDerivesExpiredFromStatus(t *testing.T) {
	originalQuery := queryPausePointStatus
	defer func() {
		queryPausePointStatus = originalQuery
	}()

	queryPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		return pausePointStatusResponse{
			Id:     id,
			Status: pausePointStatusExpired,
		}, nil
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runPausePointStatusCommand(
		context.Background(),
		unityipc.Connection{ProjectRoot: "/tmp/MyProject"},
		[]string{"--id", "jump"},
		&stdout,
		&stderr)

	if code != 0 {
		t.Fatalf("expected success, got %d with stderr %s", code, stderr.String())
	}
	var response pausePointStatusResponse
	if err := json.Unmarshal(stdout.Bytes(), &response); err != nil {
		t.Fatalf("stdout is not valid JSON: %v\n%s", err, stdout.String())
	}
	if !response.Expired {
		t.Fatalf("expired mismatch: %#v", response)
	}
}

// Verifies pause-point-status derives remaining time when older Unity packages omit the field.
func TestRunPausePointStatusDerivesRemainingTimeFromOlderResponse(t *testing.T) {
	originalQuery := queryPausePointStatus
	defer func() {
		queryPausePointStatus = originalQuery
	}()

	queryPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		return pausePointStatusResponse{
			Id:                              id,
			Status:                          pausePointStatusEnabled,
			IsEnabled:                       true,
			TimeoutSeconds:                  30,
			ElapsedSinceEnabledMilliseconds: 1200,
		}, nil
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runPausePointStatusCommand(
		context.Background(),
		unityipc.Connection{ProjectRoot: "/tmp/MyProject"},
		[]string{"--id", "jump"},
		&stdout,
		&stderr)

	if code != 0 {
		t.Fatalf("expected success, got %d with stderr %s", code, stderr.String())
	}
	var response pausePointStatusResponse
	if err := json.Unmarshal(stdout.Bytes(), &response); err != nil {
		t.Fatalf("stdout is not valid JSON: %v\n%s", err, stdout.String())
	}
	if response.RemainingMilliseconds != 28800 {
		t.Fatalf("remaining milliseconds mismatch: %#v", response)
	}
}

// Verifies pause-point-status requires a marker id.
func TestParsePausePointStatusOptionsRequiresID(t *testing.T) {
	_, err := parsePausePointStatusOptions([]string{})

	if err == nil {
		t.Fatal("expected missing id error")
	}
	if !strings.Contains(err.Error(), "Missing required option") {
		t.Fatalf("error mismatch: %v", err)
	}
}

func parsePausePointErrorEnvelope(t *testing.T, payload []byte) cliErrorEnvelope {
	t.Helper()

	var envelope cliErrorEnvelope
	if err := json.Unmarshal(payload, &envelope); err != nil {
		t.Fatalf("stderr is not valid JSON: %v\n%s", err, string(payload))
	}
	return envelope
}
