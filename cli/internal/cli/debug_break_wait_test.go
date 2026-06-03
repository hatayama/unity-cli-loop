package cli

import (
	"bytes"
	"context"
	"encoding/json"
	"strings"
	"testing"
	"time"

	"github.com/hatayama/unity-cli-loop/cli/internal/unityipc"
)

// Verifies wait-for-debug-break polls until Unity reports the marker hit.
func TestWaitForDebugBreakReturnsHitAfterEnabledStatus(t *testing.T) {
	originalQuery := queryDebugBreakStatus
	originalPoll := debugBreakStatusPoll
	debugBreakStatusPoll = time.Millisecond
	defer func() {
		queryDebugBreakStatus = originalQuery
		debugBreakStatusPoll = originalPoll
	}()

	responses := []debugBreakStatusResponse{
		{Id: "jump", Status: debugBreakStatusEnabled, IsEnabled: true},
		{Id: "jump", Status: debugBreakStatusHit, IsHit: true, IsPaused: true, HitCount: 1},
	}
	requestCount := 0
	queryDebugBreakStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (debugBreakStatusResponse, error) {
		if id != "jump" {
			t.Fatalf("id mismatch: %s", id)
		}
		response := responses[requestCount]
		requestCount++
		return response, nil
	}

	response, state, err := waitForDebugBreak(context.Background(), unityipc.Connection{}, waitForDebugBreakOptions{
		id:             "jump",
		timeoutSeconds: 1,
		timeout:        time.Second,
	})
	if err != nil {
		t.Fatalf("waitForDebugBreak failed: %v", err)
	}
	if state != debugBreakWaitStateHit {
		t.Fatalf("state mismatch: %s", state)
	}
	if response.Status != debugBreakStatusHit || response.HitCount != 1 {
		t.Fatalf("response mismatch: %#v", response)
	}
	if requestCount != 2 {
		t.Fatalf("request count mismatch: %d", requestCount)
	}
}

// Verifies wait-for-debug-break clears the enabled marker after its own timeout.
func TestRunWaitForDebugBreakClearsEnabledMarkerAfterTimeout(t *testing.T) {
	originalQuery := queryDebugBreakStatus
	originalClear := clearDebugBreakStatus
	originalPoll := debugBreakStatusPoll
	debugBreakStatusPoll = time.Millisecond
	defer func() {
		queryDebugBreakStatus = originalQuery
		clearDebugBreakStatus = originalClear
		debugBreakStatusPoll = originalPoll
	}()

	queryDebugBreakStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (debugBreakStatusResponse, error) {
		return debugBreakStatusResponse{
			Id:                              id,
			Status:                          debugBreakStatusEnabled,
			IsEnabled:                       true,
			TimeoutSeconds:                  1,
			ElapsedSinceEnabledMilliseconds: 100,
			IsPlaying:                       true,
			IsPaused:                        false,
			Message:                         "Debug break enabled.",
		}, nil
	}

	clearedID := ""
	clearDebugBreakStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (debugBreakStatusResponse, error) {
		clearedID = id
		return debugBreakStatusResponse{Id: id, Status: debugBreakStatusCleared}, nil
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runWaitForDebugBreak(context.Background(), unityipc.Connection{}, waitForDebugBreakOptions{
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
	if !strings.Contains(stderr.String(), errorCodeDebugBreakWaitTimeout) {
		t.Fatalf("timeout error missing from stderr: %s", stderr.String())
	}
	envelope := parseDebugBreakErrorEnvelope(t, stderr.Bytes())
	if envelope.Error.Details["isPlaying"] != true {
		t.Fatalf("isPlaying detail mismatch: %#v", envelope.Error.Details)
	}
	if envelope.Error.Details["isPaused"] != false {
		t.Fatalf("isPaused detail mismatch: %#v", envelope.Error.Details)
	}
	if envelope.Error.Details["markerMessage"] != "Debug break enabled." {
		t.Fatalf("markerMessage detail mismatch: %#v", envelope.Error.Details)
	}
	if envelope.Error.Details["elapsedSinceEnabledMilliseconds"] != float64(100) {
		t.Fatalf("elapsedSinceEnabledMilliseconds detail mismatch: %#v", envelope.Error.Details)
	}
	if envelope.Error.Details["remainingMilliseconds"] != float64(900) {
		t.Fatalf("remainingMilliseconds detail mismatch: %#v", envelope.Error.Details)
	}
}

// Verifies wait-for-debug-break does one final status probe before treating timeout as missed.
func TestRunWaitForDebugBreakReturnsFinalHitAtTimeoutBoundary(t *testing.T) {
	originalQuery := queryDebugBreakStatus
	originalClear := clearDebugBreakStatus
	defer func() {
		queryDebugBreakStatus = originalQuery
		clearDebugBreakStatus = originalClear
	}()

	requestCount := 0
	queryDebugBreakStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (debugBreakStatusResponse, error) {
		requestCount++
		if requestCount == 1 {
			return debugBreakStatusResponse{
				Id:        id,
				Status:    debugBreakStatusEnabled,
				IsEnabled: true,
			}, nil
		}
		return debugBreakStatusResponse{
			Id:       id,
			Status:   debugBreakStatusHit,
			IsHit:    true,
			IsPaused: true,
			HitCount: 1,
		}, nil
	}

	clearedID := ""
	clearDebugBreakStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (debugBreakStatusResponse, error) {
		clearedID = id
		return debugBreakStatusResponse{Id: id, Status: debugBreakStatusCleared}, nil
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runWaitForDebugBreak(context.Background(), unityipc.Connection{}, waitForDebugBreakOptions{
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
	var response debugBreakStatusResponse
	if err := json.Unmarshal(stdout.Bytes(), &response); err != nil {
		t.Fatalf("stdout is not valid JSON: %v\n%s", err, stdout.String())
	}
	if response.Status != debugBreakStatusHit || response.HitCount != 1 {
		t.Fatalf("response mismatch: %#v", response)
	}
	if requestCount != 2 {
		t.Fatalf("request count mismatch: %d", requestCount)
	}
}

// Verifies wait-for-debug-break rejects calls before the marker is enabled.
func TestWaitForDebugBreakReturnsNotEnabledStateImmediately(t *testing.T) {
	originalQuery := queryDebugBreakStatus
	defer func() {
		queryDebugBreakStatus = originalQuery
	}()

	queryDebugBreakStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (debugBreakStatusResponse, error) {
		return debugBreakStatusResponse{Id: id, Status: debugBreakStatusNotEnabled, IsPlaying: true}, nil
	}

	response, state, err := waitForDebugBreak(context.Background(), unityipc.Connection{}, waitForDebugBreakOptions{
		id:             "jump",
		timeoutSeconds: 1,
		timeout:        time.Second,
	})
	if err != nil {
		t.Fatalf("waitForDebugBreak failed: %v", err)
	}
	if state != debugBreakWaitStateNotEnabled {
		t.Fatalf("state mismatch: %s", state)
	}
	if response.Status != debugBreakStatusNotEnabled {
		t.Fatalf("response mismatch: %#v", response)
	}
}

// Verifies not-enabled failures use the user-facing enabled terminology.
func TestRunWaitForDebugBreakReportsNotEnabledError(t *testing.T) {
	originalQuery := queryDebugBreakStatus
	defer func() {
		queryDebugBreakStatus = originalQuery
	}()

	queryDebugBreakStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (debugBreakStatusResponse, error) {
		return debugBreakStatusResponse{
			Id:        id,
			Status:    debugBreakStatusNotEnabled,
			IsPlaying: true,
			IsPaused:  false,
			Message:   "Debug break is not enabled.",
		}, nil
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runWaitForDebugBreak(context.Background(), unityipc.Connection{}, waitForDebugBreakOptions{
		id:             "jump",
		timeoutSeconds: 1,
		timeout:        time.Second,
	}, &stdout, &stderr)

	if code != 1 {
		t.Fatalf("expected failure, got %d with stdout %s", code, stdout.String())
	}
	envelope := parseDebugBreakErrorEnvelope(t, stderr.Bytes())
	if envelope.Error.ErrorCode != errorCodeDebugBreakNotEnabled {
		t.Fatalf("error code mismatch: %#v", envelope.Error)
	}
	if envelope.Error.Details["status"] != debugBreakStatusNotEnabled {
		t.Fatalf("status detail mismatch: %#v", envelope.Error.Details)
	}
	if envelope.Error.Details["isPlaying"] != true || envelope.Error.Details["isPaused"] != false {
		t.Fatalf("play state details mismatch: %#v", envelope.Error.Details)
	}
}

// Verifies expired markers report no remaining enabled lifetime.
func TestDebugBreakExpiredErrorReportsNoRemainingTime(t *testing.T) {
	response := debugBreakStatusResponse{
		Id:                              "jump",
		Status:                          debugBreakStatusExpired,
		TimeoutSeconds:                  1,
		ElapsedSinceEnabledMilliseconds: 1200,
		IsPlaying:                       true,
		Message:                         "Debug break expired before it was hit.",
	}

	cliErr := debugBreakWaitError("/tmp/MyProject", waitForDebugBreakOptions{
		id:             "jump",
		timeoutSeconds: 1,
	}, response, debugBreakWaitStateExpired)

	if cliErr.ErrorCode != errorCodeDebugBreakExpired {
		t.Fatalf("error code mismatch: %#v", cliErr)
	}
	if cliErr.Details["remainingMilliseconds"] != int64(0) {
		t.Fatalf("remainingMilliseconds detail mismatch: %#v", cliErr.Details)
	}
}

// Verifies wait-for-debug-break requires a marker id.
func TestParseWaitForDebugBreakOptionsRequiresID(t *testing.T) {
	_, err := parseWaitForDebugBreakOptions([]string{"--timeout-seconds", "1"})

	if err == nil {
		t.Fatal("expected missing id error")
	}
	if !strings.Contains(err.Error(), "Missing required option") {
		t.Fatalf("error mismatch: %v", err)
	}
}

// Verifies debug-break-status reports the current marker state without waiting for a hit.
func TestRunDebugBreakStatusReturnsCurrentStatus(t *testing.T) {
	originalQuery := queryDebugBreakStatus
	defer func() {
		queryDebugBreakStatus = originalQuery
	}()

	queryDebugBreakStatus = func(
		ctx context.Context,
		connection unityipc.Connection,
		id string,
	) (debugBreakStatusResponse, error) {
		return debugBreakStatusResponse{Id: id, Status: debugBreakStatusEnabled, IsEnabled: true}, nil
	}

	var stdout bytes.Buffer
	var stderr bytes.Buffer
	code := runDebugBreakStatusCommand(
		context.Background(),
		unityipc.Connection{ProjectRoot: "/tmp/MyProject"},
		[]string{"--id", "jump"},
		&stdout,
		&stderr)

	if code != 0 {
		t.Fatalf("expected success, got %d with stderr %s", code, stderr.String())
	}
	var response debugBreakStatusResponse
	if err := json.Unmarshal(stdout.Bytes(), &response); err != nil {
		t.Fatalf("stdout is not valid JSON: %v\n%s", err, stdout.String())
	}
	if response.Status != debugBreakStatusEnabled {
		t.Fatalf("status mismatch: %#v", response)
	}
}

// Verifies debug-break-status requires a marker id.
func TestParseDebugBreakStatusOptionsRequiresID(t *testing.T) {
	_, err := parseDebugBreakStatusOptions([]string{})

	if err == nil {
		t.Fatal("expected missing id error")
	}
	if !strings.Contains(err.Error(), "Missing required option") {
		t.Fatalf("error mismatch: %v", err)
	}
}

func parseDebugBreakErrorEnvelope(t *testing.T, payload []byte) cliErrorEnvelope {
	t.Helper()

	var envelope cliErrorEnvelope
	if err := json.Unmarshal(payload, &envelope); err != nil {
		t.Fatalf("stderr is not valid JSON: %v\n%s", err, string(payload))
	}
	return envelope
}
