package projectrunner

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"os"
	"path/filepath"
	"reflect"
	"strings"
	"testing"
	"time"

	clierrors "github.com/hatayama/unity-cli-loop/common/errors"

	"github.com/hatayama/unity-cli-loop/common/clicore"
	"github.com/hatayama/unity-cli-loop/common/unityipc"
)

// Verifies a successful extend request writes no warning to stderr.
func TestExtendPausePointExpiryBeforeWaitWritesNothingOnSuccess(t *testing.T) {
	originalExtend := extendPausePointExpiry
	defer func() { extendPausePointExpiry = originalExtend }()

	extendPausePointExpiry = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
		minimumRemainingSeconds int,
	) (pausePointStatusResponse, error) {
		return pausePointStatusResponse{Id: id, Status: pausePointStatusEnabled}, nil
	}

	var stderr bytes.Buffer
	extendPausePointExpiryBeforeWait(
		context.Background(),
		unityipc.Connection{},
		waitForPausePointOptions{id: "jump", timeoutSeconds: 30},
		&stderr)

	if stderr.Len() != 0 {
		t.Fatalf("expected no warning, got: %s", stderr.String())
	}
}

// Verifies a failed extend request is best-effort: it writes a warning but never blocks the wait.
func TestExtendPausePointExpiryBeforeWaitWritesWarningOnFailure(t *testing.T) {
	originalExtend := extendPausePointExpiry
	defer func() { extendPausePointExpiry = originalExtend }()

	extendPausePointExpiry = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
		minimumRemainingSeconds int,
	) (pausePointStatusResponse, error) {
		return pausePointStatusResponse{}, errors.New("unknown internal bridge command")
	}

	var stderr bytes.Buffer
	extendPausePointExpiryBeforeWait(
		context.Background(),
		unityipc.Connection{},
		waitForPausePointOptions{id: "jump", timeoutSeconds: 30},
		&stderr)

	if !strings.Contains(stderr.String(), "jump") || !strings.Contains(stderr.String(), "unknown internal bridge command") {
		t.Fatalf("expected a warning mentioning the id and cause, got: %s", stderr.String())
	}
}

// Verifies the await-pause-point command extends the marker's expiry to its own timeout before
// the first status poll, so a slow multi-step CLI round trip cannot let the marker expire first.
func TestRunWaitForPausePointCommandExtendsExpiryBeforePolling(t *testing.T) {
	originalExtend := extendPausePointExpiry
	originalQuery := queryPausePointStatus
	defer func() {
		extendPausePointExpiry = originalExtend
		queryPausePointStatus = originalQuery
	}()

	var callOrder []string
	var extendedID string
	var extendedMinimumRemainingSeconds int
	extendPausePointExpiry = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
		minimumRemainingSeconds int,
	) (pausePointStatusResponse, error) {
		callOrder = append(callOrder, "extend")
		extendedID = id
		extendedMinimumRemainingSeconds = minimumRemainingSeconds
		return pausePointStatusResponse{Id: id, Status: pausePointStatusEnabled}, nil
	}
	queryPausePointStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (pausePointStatusResponse, error) {
		callOrder = append(callOrder, "query")
		return pausePointStatusResponse{
			Id:          id,
			Status:      pausePointStatusHit,
			IsHit:       true,
			HitCount:    1,
			EditorState: pausePointEditorState{IsPlaying: true, IsPaused: true, CapturedAt: "PausePointHit"},
		}, nil
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runWaitForPausePointCommand(
		context.Background(),
		unityipc.Connection{},
		[]string{"--id", "jump", "--timeout-seconds", "7"},
		"",
		&stdout,
		&stderr)

	if code != 0 {
		t.Fatalf("expected success, got %d with stderr %s", code, stderr.String())
	}
	if len(callOrder) == 0 || callOrder[0] != "extend" {
		t.Fatalf("expected extend to run before the first status query, got order: %v", callOrder)
	}
	if extendedID != "jump" {
		t.Fatalf("extended id mismatch: %s", extendedID)
	}
	if extendedMinimumRemainingSeconds != 7 {
		t.Fatalf("extended minimum remaining seconds mismatch: %d", extendedMinimumRemainingSeconds)
	}
}

// Verifies await-pause-point polls until Unity reports the marker hit.
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
			Id:                      "jump",
			Status:                  pausePointStatusHit,
			IsHit:                   true,
			HitCount:                1,
			Mode:                    "continuous",
			MaxHistory:              20,
			MaxPreviewElements:      15,
			CapturedVariableHistory: []pausePointCapturedHistoryFrame{{HitSequence: 1, FrameCount: 42, HitAtUtc: "2026-06-03T00:00:01.0000000Z"}},
			HistoryDroppedCount:     0,
			EditorState:             pausePointEditorState{IsPlaying: true, IsPaused: true, CapturedAt: "PausePointHit"},
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

	response, state, _, _, err := waitForPausePoint(context.Background(), unityipc.Connection{}, waitForPausePointOptions{
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
	if response.Mode != "continuous" || response.MaxHistory != 20 || response.MaxPreviewElements != 15 || len(response.CapturedVariableHistory) != 1 {
		t.Fatalf("capture history mismatch: %#v", response)
	}
	if requestCount != 2 {
		t.Fatalf("request count mismatch: %d", requestCount)
	}
}

// Verifies await-pause-point clears the enabled marker after its own timeout.
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
	if !strings.Contains(stderr.String(), clierrors.ErrorCodePausePointWaitTimeout) {
		t.Fatalf("timeout error missing from stderr: %s", stderr.String())
	}
	envelope := parsePausePointErrorEnvelope(t, stderr.Bytes())
	editorState, ok := envelope.Error.Details["EditorState"].(map[string]any)
	if !ok || editorState["IsPlaying"] != true || editorState["IsPaused"] != false || editorState["CapturedAt"] != "Current" {
		t.Fatalf("editorState detail mismatch: %#v", envelope.Error.Details)
	}
	if envelope.Error.Details["MarkerMessage"] != "Pause point enabled." {
		t.Fatalf("markerMessage detail mismatch: %#v", envelope.Error.Details)
	}
	if envelope.Error.Details["ElapsedSinceEnabledMilliseconds"] != float64(100) {
		t.Fatalf("elapsedSinceEnabledMilliseconds detail mismatch: %#v", envelope.Error.Details)
	}
	if envelope.Error.Details["RemainingMilliseconds"] != float64(900) {
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

	if cliErr.Details["Expired"] != true {
		t.Fatalf("expired detail mismatch: %#v", cliErr.Details)
	}
	if cliErr.Details["EnabledAtUtc"] != "2026-06-03T00:00:00.0000000Z" {
		t.Fatalf("enabledAtUtc detail mismatch: %#v", cliErr.Details)
	}
	if cliErr.Details["Generation"] != 7 {
		t.Fatalf("generation detail mismatch: %#v", cliErr.Details)
	}
	if cliErr.Details["RecommendedNextAction"] != response.RecommendedNextAction {
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

	if cliErr.Details["TimeoutSeconds"] != 30 {
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

	if cliErr.Details["Expired"] != true {
		t.Fatalf("expired detail mismatch: %#v", cliErr.Details)
	}
}

// Verifies await-pause-point does one final status probe before treating timeout as missed.
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

// Verifies await-pause-point rejects calls before the marker is enabled.
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

	response, state, _, _, err := waitForPausePoint(context.Background(), unityipc.Connection{}, waitForPausePointOptions{
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
	if envelope.Error.ErrorCode != clierrors.ErrorCodePausePointNotEnabled {
		t.Fatalf("error code mismatch: %#v", envelope.Error)
	}
	if envelope.Error.Details["Status"] != pausePointStatusNotEnabled {
		t.Fatalf("status detail mismatch: %#v", envelope.Error.Details)
	}
	editorState, ok := envelope.Error.Details["EditorState"].(map[string]any)
	if !ok || editorState["IsPlaying"] != true || editorState["IsPaused"] != false || editorState["CapturedAt"] != "Current" {
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

// Verifies --captured-variables defaults to full, accepts "names", and rejects other values,
// for both await-pause-point and pause-point-status option parsing.
func TestParsePausePointCapturedVariablesModeFlag(t *testing.T) {
	waitDefaults, err := parseWaitForPausePointOptions([]string{"--id", "jump"})
	if err != nil {
		t.Fatalf("default parse failed: %v", err)
	}
	if waitDefaults.capturedVariablesMode != pausePointCapturedVariablesModeFull {
		t.Fatalf("default captured-variables mode mismatch: %#v", waitDefaults)
	}

	waitNames, err := parseWaitForPausePointOptions([]string{"--id", "jump", "--captured-variables", "names"})
	if err != nil {
		t.Fatalf("names parse failed: %v", err)
	}
	if waitNames.capturedVariablesMode != pausePointCapturedVariablesModeNames {
		t.Fatalf("names captured-variables mode mismatch: %#v", waitNames)
	}

	if _, err := parseWaitForPausePointOptions([]string{"--id", "jump", "--captured-variables", "bogus"}); err == nil {
		t.Fatalf("expected error for invalid captured-variables value")
	}

	statusDefaults, err := parsePausePointStatusOptions([]string{"--id", "jump"})
	if err != nil {
		t.Fatalf("default status parse failed: %v", err)
	}
	if statusDefaults.capturedVariablesMode != pausePointCapturedVariablesModeFull {
		t.Fatalf("default status captured-variables mode mismatch: %#v", statusDefaults)
	}

	statusNames, err := parsePausePointStatusOptions([]string{"--id", "jump", "--captured-variables", "names"})
	if err != nil {
		t.Fatalf("names status parse failed: %v", err)
	}
	if statusNames.capturedVariablesMode != pausePointCapturedVariablesModeNames {
		t.Fatalf("names status captured-variables mode mismatch: %#v", statusNames)
	}

	if _, err := parsePausePointStatusOptions([]string{"--id", "jump", "--captured-variables", "bogus"}); err == nil {
		t.Fatalf("expected error for invalid status captured-variables value")
	}
}

// Verifies --expect is parsed repeatably and rejects a value with no "=".
func TestParseWaitForPausePointOptionsParsesExpectFlag(t *testing.T) {
	defaults, err := parseWaitForPausePointOptions([]string{"--id", "jump"})
	if err != nil {
		t.Fatalf("default parse failed: %v", err)
	}
	if defaults.expectations != nil {
		t.Fatalf("expected no expectations by default, got %#v", defaults.expectations)
	}

	options, err := parseWaitForPausePointOptions([]string{
		"--id", "jump", "--expect", "Health=100", "--expect", "Name=Enemy",
	})
	if err != nil {
		t.Fatalf("parse failed: %v", err)
	}
	if len(options.expectations) != 2 {
		t.Fatalf("expectations mismatch: %#v", options.expectations)
	}
	if options.expectations[0] != (pausePointExpectation{Name: "Health", Expected: "100"}) {
		t.Fatalf("expectation[0] mismatch: %#v", options.expectations[0])
	}
	if options.expectations[1] != (pausePointExpectation{Name: "Name", Expected: "Enemy"}) {
		t.Fatalf("expectation[1] mismatch: %#v", options.expectations[1])
	}

	if _, err := parseWaitForPausePointOptions([]string{"--id", "jump", "--expect", "NoEqualsSign"}); err == nil {
		t.Fatalf("expected error for --expect value without '='")
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
			SearchText:        searchText,
			TotalCount:        2,
			DisplayedCount:    2,
			LogType:           "Error",
			MaxCount:          maxCount,
			IncludeStackTrace: true,
			Logs: []pausePointMatchingLog{
				{Type: "Error", Message: "[jump] velocity=4.2", StackTrace: "trace one"},
				{Type: "Error", Message: "[jump] grounded=false", StackTrace: "trace two"},
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
	if result.MatchingLogs[0].StackTrace != "trace one" {
		t.Fatalf("matching log stack trace mismatch: %#v", result.MatchingLogs)
	}
	if result.EditorState.CapturedAt != "PausePointHit" {
		t.Fatalf("editor state mismatch: %#v", result.EditorState)
	}
	if result.Generation != 7 || result.FirstHitSequence != 3 {
		t.Fatalf("pause point fields mismatch: %#v", result)
	}
	if !strings.Contains(result.Warning, "Multiple matching logs") {
		t.Fatalf("warning mismatch: %#v", result.Warning)
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
	if result.Warning != "" {
		t.Fatalf("warning should be empty when there are no matching logs: %#v", result.Warning)
	}
	if strings.Contains(stdout.String(), "Warning") {
		t.Fatalf("Warning must be omitted from JSON when empty: %s", stdout.String())
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
	if strings.Contains(stdout.String(), "MatchingLogs") {
		t.Fatalf("MatchingLogs must be omitted when the fetch fails: %s", stdout.String())
	}
	if strings.Contains(stdout.String(), "Warning") {
		t.Fatalf("Warning must be omitted when the fetch fails: %s", stdout.String())
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
	matchingLogs, ok := envelope.Error.Details["MatchingLogs"].([]any)
	if !ok || len(matchingLogs) != 1 {
		t.Fatalf("MatchingLogs detail mismatch: %#v", envelope.Error.Details)
	}
	warning, ok := envelope.Error.Details["Warning"].(string)
	if !ok || !strings.Contains(warning, "may be truncated") {
		t.Fatalf("Warning detail mismatch: %#v", envelope.Error.Details)
	}
}

// Verifies CapturedVariableHistory never repeats the latest hit: CapturedVariables already
// carries it, so the history must contain only strictly older frames.
func TestFilterPausePointCapturedVariableHistoryExcludesLatestFrame(t *testing.T) {
	cases := []struct {
		name            string
		lastHitSequence int
		history         []pausePointCapturedHistoryFrame
		wantSequences   []int
	}{
		{
			name:            "single-shot leaves history empty",
			lastHitSequence: 1,
			history:         []pausePointCapturedHistoryFrame{{HitSequence: 1, FrameCount: 10}},
			wantSequences:   []int{},
		},
		{
			name:            "continuous with three hits keeps only older frames",
			lastHitSequence: 3,
			history: []pausePointCapturedHistoryFrame{
				{HitSequence: 1, FrameCount: 10},
				{HitSequence: 2, FrameCount: 20},
				{HitSequence: 3, FrameCount: 30},
			},
			wantSequences: []int{1, 2},
		},
		{
			name:            "no history stays empty",
			lastHitSequence: 0,
			history:         nil,
			wantSequences:   []int{},
		},
	}

	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			response := filterPausePointCapturedVariableHistory(pausePointStatusResponse{
				LastHitSequence:         testCase.lastHitSequence,
				CapturedVariableHistory: testCase.history,
			})

			gotSequences := make([]int, len(response.CapturedVariableHistory))
			for index, frame := range response.CapturedVariableHistory {
				gotSequences[index] = frame.HitSequence
			}
			if !reflect.DeepEqual(gotSequences, testCase.wantSequences) {
				t.Fatalf("filtered history mismatch: got %#v, want %#v", gotSequences, testCase.wantSequences)
			}
			if response.CapturedVariableHistory == nil {
				t.Fatalf("CapturedVariableHistory must never be nil so the JSON shape stays constant")
			}
		})
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
			wantHint: "Marker was enabled but never hit. Confirm the id matches UloopPausePoint.Pause(\"<id>\") and that the code path was executed. In fast-progressing games the state may have already moved past the marker (for example back to Ready or GameOver), so re-trigger the code path and wait again. " +
				"If the marker targets a Unity message method such as OnCollisionEnter2D/OnTriggerEnter2D, check whether `enable-pause-point`'s response carried a Warning about cached message dispatch: Unity can resolve a GameObject's message dispatch before the marker patch is installed, so a GameObject that already existed at enable time may never reach the marker even though the method body runs. Recreating the GameObject after enabling, or embedding UloopPausePoint.Pause(\"id\") directly in the method body, avoids this. " +
				"If the target line is inside a very small method, Mono's JIT may have inlined it into callers and the pause point never fires; move the pause point into the calling method. " +
				"If PlayMode kept progressing on its own while you were arranging state (timers, gravity, spawners), the scenario may have already been consumed before this marker could fire; next time, run `control-play-mode --action Pause` before setup and resume with `control-play-mode --action Play` only after `enable-pause-point` succeeds. " +
				"If the target line never hit despite the trigger firing, check the non-firing patterns: (1) the method is a physics/message callback or is called from one on a GameObject that existed before enable — recreate the GameObject or embed UloopPausePoint.Pause; (2) the method was already bound into a delegate/event before enable — the pre-bound invocation path bypasses the patch.",
		},
	}

	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			cliErr := pausePointWaitError("/tmp/MyProject", waitForPausePointOptions{
				id:             "jump",
				timeoutSeconds: 1,
			}, testCase.response, pausePointWaitStateTimeout)

			if cliErr.Details["Hint"] != testCase.wantHint {
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
			wantHint: "Marker expired before it was hit: the enable-pause-point --timeout-seconds window (measured from enable, not from this wait) ran out. Re-enable the marker with a longer --timeout-seconds and trigger the code path again. " +
				"If the target line never hit despite the trigger firing, check the non-firing patterns: (1) the method is a physics/message callback or is called from one on a GameObject that existed before enable — recreate the GameObject or embed UloopPausePoint.Pause; (2) the method was already bound into a delegate/event before enable — the pre-bound invocation path bypasses the patch.",
		},
	}

	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			cliErr := pausePointWaitError("/tmp/MyProject", waitForPausePointOptions{
				id:             "jump",
				timeoutSeconds: 1,
			}, testCase.response, pausePointWaitStateExpired)

			if cliErr.Details["Hint"] != testCase.wantHint {
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
	if _, exists := timeoutErr.Details["Hint"]; exists {
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
	if _, exists := clearedErr.Details["Hint"]; exists {
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

	if cliErr.ErrorCode != clierrors.ErrorCodePausePointExpired {
		t.Fatalf("error code mismatch: %#v", cliErr)
	}
	if cliErr.Details["RemainingMilliseconds"] != int64(0) {
		t.Fatalf("remainingMilliseconds detail mismatch: %#v", cliErr.Details)
	}
}

// Verifies disabled native pause-point commands are rejected before Unity dispatch.
func TestRunProjectLocalWaitForPausePointRespectsToolSettings(t *testing.T) {
	projectRoot := createLaunchTestProject(t)
	writeToolSettings(t, projectRoot, `{"disabledTools":["pause-point"]}`)
	t.Chdir(filepath.Dir(projectRoot))

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := RunProjectLocal(
		context.Background(),
		[]string{"--project-path", projectRoot, clicore.PausePointAwaitCommandName, "--id", "jump"},
		&stdout,
		&stderr)

	if code != 1 {
		t.Fatalf("expected disabled command failure, got %d with stdout %s", code, stdout.String())
	}
	envelope := parsePausePointErrorEnvelope(t, stderr.Bytes())
	if envelope.Error.ErrorCode != clierrors.ErrorCodeToolDisabled {
		t.Fatalf("error code mismatch: %#v", envelope.Error)
	}
	if envelope.Error.Command != clicore.PausePointAwaitCommandName {
		t.Fatalf("command mismatch: %#v", envelope.Error)
	}
}

// Verifies disabled pause-point-status is rejected before Unity dispatch.
func TestRunProjectLocalPausePointStatusRespectsToolSettings(t *testing.T) {
	projectRoot := createLaunchTestProject(t)
	writeToolSettings(t, projectRoot, `{"disabledTools":["pause-point"]}`)
	t.Chdir(filepath.Dir(projectRoot))

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := RunProjectLocal(
		context.Background(),
		[]string{"--project-path", projectRoot, clicore.PausePointStatusUserCommandName, "--id", "jump"},
		&stdout,
		&stderr)

	if code != 1 {
		t.Fatalf("expected disabled command failure, got %d with stdout %s", code, stdout.String())
	}
	envelope := parsePausePointErrorEnvelope(t, stderr.Bytes())
	if envelope.Error.ErrorCode != clierrors.ErrorCodeToolDisabled {
		t.Fatalf("error code mismatch: %#v", envelope.Error)
	}
	if envelope.Error.Command != clicore.PausePointStatusUserCommandName {
		t.Fatalf("command mismatch: %#v", envelope.Error)
	}
}

// Verifies an unrecognized flag on await-pause-point/pause-point-status carries a hint
// that the installed project runner may be older than the skill docs.
func TestParseUnknownOptionErrorsIncludeOutdatedRunnerHint(t *testing.T) {
	wantHint := "Unknown option \"--bogus-flag\" for await-pause-point. If the skill documentation mentions this option, the installed project runner may be older than the docs — check 'uloop --version' and update the CLI."

	_, err := parseWaitForPausePointOptions([]string{"--id", "jump", "--bogus-flag", "value"})
	if err == nil {
		t.Fatal("expected error for unknown flag")
	}
	if err.Error() != wantHint {
		t.Fatalf("await-pause-point hint mismatch: %v", err)
	}

	wantStatusHint := "Unknown option \"--bogus-flag\" for pause-point-status. If the skill documentation mentions this option, the installed project runner may be older than the docs — check 'uloop --version' and update the CLI."

	_, statusErr := parsePausePointStatusOptions([]string{"--id", "jump", "--bogus-flag", "value"})
	if statusErr == nil {
		t.Fatal("expected error for unknown flag")
	}
	if statusErr.Error() != wantStatusHint {
		t.Fatalf("pause-point-status hint mismatch: %v", statusErr)
	}
}

// Verifies await-pause-point requires a marker id.
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

// Verifies pause-point-status passes captured variables and the truncated flag through to stdout.
func TestRunPausePointStatusReturnsCapturedVariables(t *testing.T) {
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
			Status:    pausePointStatusHit,
			IsEnabled: true,
			IsHit:     true,
			CapturedVariables: []pausePointCapturedVariable{
				{
					Name:     "speed",
					Scope:    "Local",
					TypeName: "System.Int32",
					Value:    pausePointVariableValue("5"),
				},
				{
					Name:                  "enemy",
					Scope:                 "InstanceField",
					TypeName:              "UnityEngine.GameObject",
					Value:                 pausePointVariableValue("Enemy"),
					UnityObjectKind:       "SceneObject",
					UnityObjectPath:       "MainScene:/Root/Enemy",
					UnityObjectInstanceId: -1234,
				},
			},
			CapturedVariablesTruncated: true,
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
	if !response.CapturedVariablesTruncated {
		t.Fatalf("expected CapturedVariablesTruncated to be true: %#v", response)
	}
	if len(response.CapturedVariables) != 2 {
		t.Fatalf("expected 2 captured variables, got %#v", response.CapturedVariables)
	}
	first := response.CapturedVariables[0]
	if first.Name != "speed" || first.Value == nil || *first.Value != "5" {
		t.Fatalf("first captured variable mismatch: %#v", first)
	}
	second := response.CapturedVariables[1]
	if second.Name != "enemy" || second.UnityObjectKind != "SceneObject" ||
		second.UnityObjectPath != "MainScene:/Root/Enemy" || second.UnityObjectInstanceId != -1234 {
		t.Fatalf("second captured variable mismatch: %#v", second)
	}
}

// Verifies --captured-variables names strips Value from every captured variable (including
// history frames) while keeping Name/Scope/TypeName, for pause-point-status output.
func TestRunPausePointStatusCapturedVariablesNamesOmitsValues(t *testing.T) {
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
			Id:              id,
			Status:          pausePointStatusHit,
			IsHit:           true,
			Mode:            "continuous",
			LastHitSequence: 2,
			CapturedVariables: []pausePointCapturedVariable{
				{Name: "speed", Scope: "Local", TypeName: "System.Int32", Value: pausePointVariableValue("5")},
			},
			CapturedVariableHistory: []pausePointCapturedHistoryFrame{
				{
					HitSequence: 1,
					CapturedVariables: []pausePointCapturedVariable{
						{Name: "speed", Scope: "Local", TypeName: "System.Int32", Value: pausePointVariableValue("3")},
					},
				},
			},
		}, nil
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runPausePointStatusCommand(
		context.Background(),
		unityipc.Connection{ProjectRoot: "/tmp/MyProject"},
		[]string{"--id", "jump", "--captured-variables", "names"},
		&stdout,
		&stderr)

	if code != 0 {
		t.Fatalf("expected success, got %d with stderr %s", code, stderr.String())
	}
	if strings.Contains(stdout.String(), `"Value"`) {
		t.Fatalf("Value must be omitted in names mode: %s", stdout.String())
	}

	var response pausePointStatusResponse
	if err := json.Unmarshal(stdout.Bytes(), &response); err != nil {
		t.Fatalf("stdout is not valid JSON: %v\n%s", err, stdout.String())
	}
	if len(response.CapturedVariables) != 1 || response.CapturedVariables[0].Name != "speed" ||
		response.CapturedVariables[0].TypeName != "System.Int32" {
		t.Fatalf("CapturedVariables mismatch: %#v", response.CapturedVariables)
	}
	if len(response.CapturedVariableHistory) != 1 ||
		len(response.CapturedVariableHistory[0].CapturedVariables) != 1 ||
		response.CapturedVariableHistory[0].CapturedVariables[0].Name != "speed" {
		t.Fatalf("CapturedVariableHistory mismatch: %#v", response.CapturedVariableHistory)
	}
}

// Verifies pausePointCapturedVariable round-trips through JSON without losing or reordering fields.
func TestPausePointStatusResponseCapturedVariablesJSONRoundTrip(t *testing.T) {
	original := pausePointStatusResponse{
		Id:     "jump",
		Status: pausePointStatusHit,
		CapturedVariables: []pausePointCapturedVariable{
			{
				Name:                  "speed",
				Scope:                 "Local",
				TypeName:              "System.Int32",
				Value:                 pausePointVariableValue("5"),
				UnityObjectKind:       "SceneObject",
				UnityObjectPath:       "MainScene:/Root/Enemy",
				UnityObjectInstanceId: -1234,
			},
		},
		CapturedVariablesTruncated: true,
	}

	marshaled, err := json.Marshal(original)
	if err != nil {
		t.Fatalf("marshal failed: %v", err)
	}

	var roundTripped pausePointStatusResponse
	if err := json.Unmarshal(marshaled, &roundTripped); err != nil {
		t.Fatalf("unmarshal failed: %v", err)
	}

	if !reflect.DeepEqual(original.CapturedVariables, roundTripped.CapturedVariables) {
		t.Fatalf("captured variables mismatch after round trip: got %#v, want %#v",
			roundTripped.CapturedVariables, original.CapturedVariables)
	}
	if original.CapturedVariablesTruncated != roundTripped.CapturedVariablesTruncated {
		t.Fatalf("capturedVariablesTruncated mismatch after round trip: got %v, want %v",
			roundTripped.CapturedVariablesTruncated, original.CapturedVariablesTruncated)
	}
}

// Verifies a non-Unity-object variable omits all three UnityObject* fields from its JSON,
// since Unity always sets them to their zero value ("", "", 0) for such variables.
func TestPausePointCapturedVariableOmitsUnityObjectFieldsWhenNotAUnityObject(t *testing.T) {
	variable := pausePointCapturedVariable{
		Name:     "speed",
		Scope:    "Local",
		TypeName: "System.Int32",
		Value:    pausePointVariableValue("5"),
	}

	marshaled, err := json.Marshal(variable)
	if err != nil {
		t.Fatalf("marshal failed: %v", err)
	}

	for _, field := range []string{"UnityObjectKind", "UnityObjectPath", "UnityObjectInstanceId"} {
		if strings.Contains(string(marshaled), field) {
			t.Fatalf("%s must be omitted for a non-Unity-object variable: %s", field, marshaled)
		}
	}
}

// Verifies UnityObjectKind is the discriminator for "is this a Unity object variable": even
// when UnityObjectInstanceId happens to be its zero value, UnityObjectKind (and Path, if set)
// still appear in the JSON because they are non-zero. This locks in the contract that consumers
// must check UnityObjectKind's presence, not UnityObjectInstanceId's value, to detect a Unity
// object variable.
func TestPausePointCapturedVariableKeepsUnityObjectKindWhenInstanceIdIsZero(t *testing.T) {
	variable := pausePointCapturedVariable{
		Name:                  "destroyedEnemy",
		Scope:                 "Local",
		TypeName:              "UnityEngine.GameObject",
		Value:                 pausePointVariableValue("(destroyed)"),
		UnityObjectKind:       "Destroyed",
		UnityObjectInstanceId: 0,
	}

	marshaled, err := json.Marshal(variable)
	if err != nil {
		t.Fatalf("marshal failed: %v", err)
	}

	if !strings.Contains(string(marshaled), `"UnityObjectKind":"Destroyed"`) {
		t.Fatalf("UnityObjectKind must be present when non-empty: %s", marshaled)
	}
	if strings.Contains(string(marshaled), "UnityObjectInstanceId") {
		t.Fatalf("UnityObjectInstanceId must still be omitted when zero, even alongside a non-empty Kind: %s", marshaled)
	}
}

// Verifies a genuinely empty string Value (e.g. a captured `string s = ""`) still serializes as
// "Value":"" in full mode, distinguishable from names mode omitting Value entirely via a nil pointer.
func TestPausePointCapturedVariableKeepsGenuinelyEmptyValueInFullMode(t *testing.T) {
	variable := pausePointCapturedVariable{
		Name:     "label",
		Scope:    "Local",
		TypeName: "System.String",
		Value:    pausePointVariableValue(""),
	}

	marshaled, err := json.Marshal(variable)
	if err != nil {
		t.Fatalf("marshal failed: %v", err)
	}

	if !strings.Contains(string(marshaled), `"Value":""`) {
		t.Fatalf("a genuinely empty Value must still be serialized in full mode: %s", marshaled)
	}

	stripped := stripPausePointCapturedVariableValues([]pausePointCapturedVariable{variable})
	strippedMarshaled, err := json.Marshal(stripped[0])
	if err != nil {
		t.Fatalf("marshal failed: %v", err)
	}
	if strings.Contains(string(strippedMarshaled), `"Value"`) {
		t.Fatalf("names mode must omit Value entirely, even when the original value was empty: %s", strippedMarshaled)
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

func parsePausePointErrorEnvelope(t *testing.T, payload []byte) clierrors.CLIErrorEnvelope {
	t.Helper()

	var envelope clierrors.CLIErrorEnvelope
	if err := json.Unmarshal(payload, &envelope); err != nil {
		t.Fatalf("stderr is not valid JSON: %v\n%s", err, string(payload))
	}
	return envelope
}

// createLaunchTestProject and writeToolSettings are duplicated from
// internal/cli's test helpers of the same name: test helpers cannot be
// shared across packages, and both packages need a minimal Unity project
// fixture with tool settings for their command-gating tests.
func createLaunchTestProject(t *testing.T) string {
	t.Helper()

	projectRoot := t.TempDir()
	for _, directory := range []string{"Assets", "ProjectSettings"} {
		if err := os.MkdirAll(filepath.Join(projectRoot, directory), 0o755); err != nil {
			t.Fatalf("failed to create %s: %v", directory, err)
		}
	}
	return projectRoot
}

func writeToolSettings(t *testing.T, projectRoot string, content string) {
	t.Helper()
	settingsDir := filepath.Join(projectRoot, ".uloop")
	if err := os.MkdirAll(settingsDir, 0o755); err != nil {
		t.Fatalf("failed to create settings dir: %v", err)
	}
	if err := os.WriteFile(filepath.Join(settingsDir, "settings.tools.json"), []byte(content), 0o644); err != nil {
		t.Fatalf("failed to write tool settings: %v", err)
	}
}
