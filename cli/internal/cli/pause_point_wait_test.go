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
		{Id: "jump", Status: pausePointStatusHit, IsHit: true, IsPaused: true, HitCount: 1},
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
			IsPlaying:                       true,
			IsPaused:                        false,
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
	if envelope.Error.Details["isPlaying"] != true {
		t.Fatalf("isPlaying detail mismatch: %#v", envelope.Error.Details)
	}
	if envelope.Error.Details["isPaused"] != false {
		t.Fatalf("isPaused detail mismatch: %#v", envelope.Error.Details)
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
			Id:       id,
			Status:   pausePointStatusHit,
			IsHit:    true,
			IsPaused: true,
			HitCount: 1,
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
		return pausePointStatusResponse{Id: id, Status: pausePointStatusNotEnabled, IsPlaying: true}, nil
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
			Id:        id,
			Status:    pausePointStatusNotEnabled,
			IsPlaying: true,
			IsPaused:  false,
			Message:   "Pause point is not enabled.",
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
	if envelope.Error.Details["isPlaying"] != true || envelope.Error.Details["isPaused"] != false {
		t.Fatalf("play state details mismatch: %#v", envelope.Error.Details)
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
		return pausePointStatusResponse{Id: id, Status: pausePointStatusHit, IsHit: true, HitCount: 1}, nil
	}

	fetchedSearchText := ""
	fetchedMaxCount := 0
	fetchMatchingLogs = func(
		ctx context.Context,
		connection unityipc.Connection,
		searchText string,
		maxCount int,
	) ([]pausePointMatchingLog, error) {
		fetchedSearchText = searchText
		fetchedMaxCount = maxCount
		return []pausePointMatchingLog{
			{Type: "Log", Message: "[jump] velocity=4.2"},
			{Type: "Log", Message: "[jump] grounded=false"},
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
		return pausePointStatusResponse{Id: id, Status: pausePointStatusHit, IsHit: true, HitCount: 1}, nil
	}
	fetchMatchingLogs = func(
		ctx context.Context,
		connection unityipc.Connection,
		searchText string,
		maxCount int,
	) ([]pausePointMatchingLog, error) {
		return []pausePointMatchingLog{}, nil
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
	if !strings.Contains(stdout.String(), "\"MatchingLogs\": []") {
		t.Fatalf("MatchingLogs must be an explicit empty array: %s", stdout.String())
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
		return pausePointStatusResponse{Id: id, Status: pausePointStatusHit, IsHit: true, HitCount: 1}, nil
	}
	fetchMatchingLogs = func(
		ctx context.Context,
		connection unityipc.Connection,
		searchText string,
		maxCount int,
	) ([]pausePointMatchingLog, error) {
		return nil, context.DeadlineExceeded
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
	if strings.Contains(stdout.String(), "MatchingLogs") {
		t.Fatalf("MatchingLogs must be omitted when the fetch fails: %s", stdout.String())
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
		return pausePointStatusResponse{Id: id, Status: pausePointStatusEnabled, IsEnabled: true, IsPlaying: true}, nil
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
	) ([]pausePointMatchingLog, error) {
		return []pausePointMatchingLog{{Type: "Log", Message: "[jump] never reached"}}, nil
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
	matchingLogs, ok := envelope.Error.Details["MatchingLogs"].([]any)
	if !ok || len(matchingLogs) != 1 {
		t.Fatalf("MatchingLogs detail mismatch: %#v", envelope.Error.Details)
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
			response: pausePointStatusResponse{Id: "jump", Status: pausePointStatusEnabled, IsPlaying: false},
			wantHint: "PlayMode is not running. Start PlayMode (or trigger the marker code path in Edit Mode), then wait again.",
		},
		{
			name:     "editor already paused",
			response: pausePointStatusResponse{Id: "jump", Status: pausePointStatusEnabled, IsPlaying: true, IsPaused: true},
			wantHint: "Unity is already paused, so gameplay cannot reach the marker. Resume PlayMode before waiting again.",
		},
		{
			name:     "marker never hit",
			response: pausePointStatusResponse{Id: "jump", Status: pausePointStatusEnabled, IsPlaying: true, HitCount: 0},
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
			response: pausePointStatusResponse{Id: "jump", Status: pausePointStatusExpired, IsPlaying: false},
			wantHint: "PlayMode is not running. Start PlayMode (or trigger the marker code path in Edit Mode), then wait again.",
		},
		{
			name:     "editor already paused",
			response: pausePointStatusResponse{Id: "jump", Status: pausePointStatusExpired, IsPlaying: true, IsPaused: true},
			wantHint: "Unity is already paused, so gameplay cannot reach the marker. Resume PlayMode before waiting again.",
		},
		{
			name:     "marker expired before hit",
			response: pausePointStatusResponse{Id: "jump", Status: pausePointStatusExpired, IsPlaying: true, HitCount: 0},
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
	hitResponse := pausePointStatusResponse{Id: "jump", Status: pausePointStatusHit, IsPlaying: true, HitCount: 1}
	timeoutErr := pausePointWaitError("/tmp/MyProject", waitForPausePointOptions{
		id:             "jump",
		timeoutSeconds: 1,
	}, hitResponse, pausePointWaitStateTimeout)
	if _, exists := timeoutErr.Details["hint"]; exists {
		t.Fatalf("hint should be omitted when no diagnosis applies: %#v", timeoutErr.Details)
	}

	clearedResponse := pausePointStatusResponse{Id: "jump", Status: pausePointStatusCleared, IsPlaying: true}
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
		IsPlaying:                       true,
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
		return pausePointStatusResponse{Id: id, Status: pausePointStatusEnabled, IsEnabled: true}, nil
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
